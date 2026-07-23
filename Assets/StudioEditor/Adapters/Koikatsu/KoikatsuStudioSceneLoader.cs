using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using StudioEditor.Characters;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StudioEditor.ReferenceModels
{
    public static class KoikatsuStudioSceneLoader
    {
        public static async Task<KoikatsuStudioSceneInstance> LoadAsync(
            KoikatsuScene scene,
            string abdataRoot,
            string modsRoot = null,
            Transform parent = null,
            CancellationToken cancellationToken = default)
        {
            var warningStackTrace = Application.GetStackTraceLogType(
                LogType.Warning);
            Application.SetStackTraceLogType(
                LogType.Warning,
                StackTraceLogType.None);
            try
            {
                // Leave the UI event dispatch stack before synchronous prefab work.
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                var result = LoadItems(
                    scene,
                    abdataRoot,
                    modsRoot,
                    parent);
                try
                {
                    // Let Unity integrate instantiated objects before additive scene
                    // loading. Calling LoadSceneAsync from the original pointer event
                    // can block the editor's main thread on legacy scene bundles.
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    await result.LoadMapAsync(
                        abdataRoot,
                        modsRoot,
                        cancellationToken);
                    return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }
            finally
            {
                Application.SetStackTraceLogType(
                    LogType.Warning,
                    warningStackTrace);
            }
        }

        public static KoikatsuStudioSceneInstance LoadItems(
            string scenePath,
            string abdataRoot,
            string modsRoot = null,
            Transform parent = null)
        {
            return LoadItems(
                KoikatsuSceneReader.Read(scenePath),
                abdataRoot,
                modsRoot,
                parent);
        }

        public static KoikatsuStudioSceneInstance LoadItems(
            KoikatsuScene scene,
            string abdataRoot,
            string modsRoot = null,
            Transform parent = null)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            var root = new GameObject(
                string.IsNullOrWhiteSpace(scene.SourcePath)
                    ? "Koikatsu Studio Scene"
                    : Path.GetFileNameWithoutExtension(scene.SourcePath));
            root.transform.SetParent(parent, false);
            var items = new List<KoikatsuStudioItemInstance>();
            var characters = new List<KoikatsuReferenceModelInstance>();
            var characterSources =
                new Dictionary<KoikatsuReferenceModelInstance,
                    KoikatsuSceneObject>();
            var missing = new List<KoikatsuMissingStudioItem>();
            var objectsByKey = new Dictionary<int, GameObject>();
            try
            {
                for (var index = 0; index < scene.Objects.Count; index++)
                {
                    LoadObject(
                        scene,
                        scene.Objects[index],
                        abdataRoot,
                        modsRoot,
                        root.transform,
                        items,
                        characters,
                        characterSources,
                        missing,
                        objectsByKey);
                }

                var objectsByTimelineIndex = objectsByKey
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Value)
                    .ToList();
                var nodeConstraints = KoikatsuStudioNodeConstraints.TryAttach(
                    root,
                    scene.SourcePath,
                    objectsByTimelineIndex);
                nodeConstraints?.EvaluateNow();
                var itemPoses = root.GetComponentsInChildren<
                    KoikatsuStudioItemPose>(true);
                for (var index = 0; index < itemPoses.Length; index++)
                {
                    itemPoses[index].EvaluateNow();
                }

                return new KoikatsuStudioSceneInstance(
                    root,
                    scene,
                    items,
                    characters,
                    characterSources,
                    missing,
                    objectsByKey,
                    objectsByTimelineIndex);
            }
            catch
            {
                for (var index = characters.Count - 1; index >= 0; index--)
                {
                    characters[index].Dispose();
                }

                for (var index = items.Count - 1; index >= 0; index--)
                {
                    items[index].ReleaseResources(false);
                }

                KoikatsuStudioItemLoader.Destroy(root);
                throw;
            }
        }

        private static void LoadObject(
            KoikatsuScene scene,
            KoikatsuSceneObject source,
            string abdataRoot,
            string modsRoot,
            Transform parent,
            ICollection<KoikatsuStudioItemInstance> items,
            ICollection<KoikatsuReferenceModelInstance> characters,
            IDictionary<KoikatsuReferenceModelInstance, KoikatsuSceneObject>
                characterSources,
            ICollection<KoikatsuMissingStudioItem> missing,
            IDictionary<int, GameObject> objectsByKey)
        {
            Transform childParent;
            GameObject loadedObject;
            if (source.Character != null)
            {
                try
                {
                    if (scene.TryResolveCharacterAnimation(
                            source,
                            out var animationResolution))
                    {
                        source.Character.AnimationModGuid =
                            animationResolution.Guid;
                        source.Character.AnimationNo =
                            animationResolution.Slot;
                    }

                    var character = KoikatsuCharacterAssembler.BuildFromCard(
                        source.Character.Card,
                        abdataRoot,
                        modsRoot,
                        parent,
                        CancellationToken.None,
                        source.Character.ActiveCoordinateIndex);
                    ApplyTransform(character.Root.transform, source.Base, parent);
                    KoikatsuStudioCharacterPose.Apply(
                        character,
                        source.Character,
                        abdataRoot,
                        modsRoot);
                    character.Root.SetActive(source.Base.Visible);
                    characters.Add(character);
                    characterSources[character] = source;
                    childParent = character.Root.transform;
                    loadedObject = character.Root;
                }
                catch (Exception exception) when (
                    IsMissingCharacterFailure(exception))
                {
                    loadedObject = CreateContainer(source, parent);
                    childParent = loadedObject.transform;
                    Debug.LogWarning(
                        "Could not load Koikatsu Studio character " +
                        $"{source.Base.DicKey}: {exception.Message}");
                }
            }
            else if (source.Item != null)
            {
                try
                {
                    var item = KoikatsuStudioItemLoader.Load(
                        abdataRoot,
                        scene,
                        source,
                        modsRoot,
                        parent);
                    items.Add(item);
                    childParent = item.ChildRoot;
                    loadedObject = item.Root;
                }
                catch (Exception exception) when (IsMissingItemFailure(exception))
                {
                    var placeholder = CreatePlaceholder(source, parent);
                    childParent = placeholder.transform;
                    loadedObject = placeholder;
                    var guid = scene.TryResolveItem(source, out var resolution)
                        ? resolution.Guid
                        : string.Empty;
                    missing.Add(new KoikatsuMissingStudioItem(
                        source.Item.Group,
                        source.Item.Category,
                        source.Item.No,
                        guid,
                        exception.Message));
                    Debug.LogWarning(
                        "Could not load Koikatsu Studio item " +
                        $"{source.Item.Group}/{source.Item.Category}/" +
                        $"{source.Item.No}" +
                        (string.IsNullOrEmpty(guid)
                            ? string.Empty
                            : $" from zipmod '{guid}'") +
                        $": {exception.Message}");
                }
            }
            else if (source.Light != null)
            {
                loadedObject = CreateLight(
                    source,
                    abdataRoot,
                    modsRoot,
                    parent);
                childParent = loadedObject.transform;
            }
            else
            {
                loadedObject = CreateContainer(source, parent);
                childParent = loadedObject.transform;
            }

            if (source.Base.DicKey >= 0 &&
                !objectsByKey.ContainsKey(source.Base.DicKey))
            {
                objectsByKey.Add(source.Base.DicKey, loadedObject);
            }

            for (var index = 0; index < source.Children.Count; index++)
            {
                LoadObject(
                    scene,
                    source.Children[index],
                    abdataRoot,
                    modsRoot,
                    childParent,
                    items,
                    characters,
                    characterSources,
                    missing,
                    objectsByKey);
            }
        }

        private static bool IsMissingCharacterFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is InvalidDataException ||
                   exception is NotSupportedException ||
                   exception is UnityException;
        }

        private static bool IsMissingItemFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is InvalidDataException ||
                   exception is UnityException;
        }

        private static GameObject CreateContainer(
            KoikatsuSceneObject source,
            Transform parent)
        {
            var name = !string.IsNullOrWhiteSpace(source.Name)
                ? source.Name
                : source.Kind == KoikatsuSceneObjectKind.Character
                    ? $"Character {source.Base.DicKey}"
                    : source.Kind.ToString();
            var container = new GameObject(name);
            ApplyTransform(container.transform, source.Base, parent);
            container.SetActive(source.Base.Visible);
            return container;
        }

        private static GameObject CreateLight(
            KoikatsuSceneObject source,
            string abdataRoot,
            string modsRoot,
            Transform parent)
        {
            var catalog = KoikatsuListCatalog.Load(abdataRoot, modsRoot);
            catalog.TryGetStudioLight(source.Light.No, out var entry);
            var name = !string.IsNullOrWhiteSpace(source.Name)
                ? source.Name
                : !string.IsNullOrWhiteSpace(entry?.Name)
                    ? entry.Name
                    : $"Light {source.Light.No}";
            var result = new GameObject(name);
            ApplyTransform(result.transform, source.Base, parent);

            var light = result.AddComponent<Light>();
            light.type = ResolveLightType(source.Light.No, entry);
            light.color = source.Light.Color;
            light.intensity = Mathf.Max(0f, source.Light.Intensity);
            light.range = Mathf.Max(0.01f, source.Light.Range);
            light.spotAngle = Mathf.Clamp(source.Light.SpotAngle, 1f, 179f);
            light.shadows = source.Light.Shadow
                ? LightShadows.Soft
                : LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = source.Light.Enable;
            result.SetActive(source.Base.Visible);
            return result;
        }

        private static LightType ResolveLightType(
            int id,
            KoikatsuStudioLightEntry entry)
        {
            var descriptor = ((entry?.AssetName ?? string.Empty) + " " +
                              (entry?.Name ?? string.Empty)).ToLowerInvariant();
            if (descriptor.Contains("spot"))
            {
                return LightType.Spot;
            }

            if (descriptor.Contains("point"))
            {
                return LightType.Point;
            }

            if (descriptor.Contains("direction"))
            {
                return LightType.Directional;
            }

            switch ((id % 3 + 3) % 3)
            {
                case 0:
                    return LightType.Directional;
                case 2:
                    return LightType.Spot;
                default:
                    return LightType.Point;
            }
        }

        private static GameObject CreatePlaceholder(
            KoikatsuSceneObject source,
            Transform parent)
        {
            var item = source.Item;
            var placeholder = new GameObject(
                $"Missing Item {item.Group}-{item.Category}-{item.No}");
            ApplyTransform(placeholder.transform, source.Base, parent);
            placeholder.SetActive(source.Base.Visible);
            return placeholder;
        }

        private static void ApplyTransform(
            Transform target,
            KoikatsuSceneObjectBase source,
            Transform parent)
        {
            target.SetParent(parent, false);
            target.localPosition = source.Position;
            target.localRotation = Quaternion.Euler(source.Rotation);
            target.localScale = source.Scale;
        }
    }

    [DefaultExecutionOrder(29000)]
    internal sealed class KoikatsuStudioNodeConstraints : MonoBehaviour
    {
        private const string PluginId = "nodesConstraints";
        private const string DataKey = "constraints";
        private const float Epsilon = 0.000001f;

        private readonly List<ConstraintBinding> bindings =
            new List<ConstraintBinding>();

        internal static KoikatsuStudioNodeConstraints TryAttach(
            GameObject host,
            string scenePath,
            IReadOnlyList<GameObject> objectsByTimelineIndex)
        {
            if (host == null || string.IsNullOrWhiteSpace(scenePath) ||
                objectsByTimelineIndex == null ||
                !KoikatsuTimelineSceneReader.TryReadPluginString(
                    scenePath,
                    PluginId,
                    DataKey,
                    out var xml))
            {
                return null;
            }

            var values = Parse(xml, objectsByTimelineIndex);
            if (values.Count == 0)
            {
                return null;
            }

            var component = host.AddComponent<KoikatsuStudioNodeConstraints>();
            component.bindings.AddRange(values);
            return component;
        }

        internal void EvaluateNow()
        {
            for (var index = 0; index < bindings.Count; index++)
            {
                bindings[index].Evaluate();
            }
        }

        private void LateUpdate()
        {
            EvaluateNow();
        }

        private static List<ConstraintBinding> Parse(
            string xml,
            IReadOnlyList<GameObject> objectsByTimelineIndex)
        {
            var document = new XmlDocument
            {
                XmlResolver = null,
            };
            using (var text = new StringReader(xml))
            using (var reader = XmlReader.Create(
                       text,
                       new XmlReaderSettings
                       {
                           DtdProcessing = DtdProcessing.Prohibit,
                           XmlResolver = null,
                       }))
            {
                document.Load(reader);
            }

            var result = new List<ConstraintBinding>();
            var root = document.DocumentElement;
            if (root == null ||
                !string.Equals(root.Name, "constraints", StringComparison.Ordinal))
            {
                return result;
            }

            for (var index = 0; index < root.ChildNodes.Count; index++)
            {
                var node = root.ChildNodes[index];
                if (!string.Equals(
                        node.Name,
                        "constraint",
                        StringComparison.Ordinal) ||
                    !ReadBoolean(node, "enabled", true))
                {
                    continue;
                }

                var parent = ResolveTransform(
                    objectsByTimelineIndex,
                    ReadInt(node, "parentObjectIndex", -1),
                    ReadString(node, "parentPath"));
                var child = ResolveTransform(
                    objectsByTimelineIndex,
                    ReadInt(node, "childObjectIndex", -1),
                    ReadString(node, "childPath"));
                if (parent == null || child == null)
                {
                    continue;
                }

                result.Add(new ConstraintBinding(
                    parent,
                    child,
                    ReadBoolean(node, "position", false),
                    ReadVector3(node, "positionOffset", Vector3.zero),
                    ReadBoolean(node, "rotation", false),
                    ReadQuaternion(node, "rotationOffset", Quaternion.identity),
                    ReadBoolean(node, "scale", false),
                    ReadVector3(node, "scaleOffset", Vector3.one),
                    ReadBoolean(node, "mirrorPosition", false),
                    ReadBoolean(node, "mirrorRotation", false),
                    ReadBoolean(node, "mirrorScale", false),
                    ReadBoolean(node, "lookAt", false),
                    ReadSingle(node, "positionChangeFactor", 1f),
                    ReadSingle(node, "rotationChangeFactor", 1f),
                    ReadSingle(node, "scaleChangeFactor", 1f),
                    ReadLocks(node, "positionLocks"),
                    ReadLocks(node, "rotationLocks"),
                    ReadLocks(node, "scaleLocks")));
            }

            return result;
        }

        private static Transform ResolveTransform(
            IReadOnlyList<GameObject> objects,
            int objectIndex,
            string path)
        {
            if (objectIndex < 0 || objectIndex >= objects.Count ||
                objects[objectIndex] == null)
            {
                return null;
            }

            var root = objects[objectIndex].transform;
            if (string.IsNullOrEmpty(path))
            {
                return root;
            }

            var result = root.Find(path);
            if (result != null || path.IndexOf('/') >= 0)
            {
                return result;
            }

            var descendants = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < descendants.Length; index++)
            {
                if (string.Equals(
                        descendants[index].name,
                        path,
                        StringComparison.Ordinal))
                {
                    return descendants[index];
                }
            }

            return null;
        }

        private static string ReadString(XmlNode node, string name)
        {
            return node.Attributes?[name]?.Value ?? string.Empty;
        }

        private static bool ReadBoolean(
            XmlNode node,
            string name,
            bool fallback)
        {
            var value = node.Attributes?[name]?.Value;
            try
            {
                return string.IsNullOrEmpty(value)
                    ? fallback
                    : XmlConvert.ToBoolean(value);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        private static int ReadInt(XmlNode node, string name, int fallback)
        {
            var value = node.Attributes?[name]?.Value;
            try
            {
                return string.IsNullOrEmpty(value)
                    ? fallback
                    : XmlConvert.ToInt32(value);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        private static float ReadSingle(
            XmlNode node,
            string name,
            float fallback)
        {
            var value = node.Attributes?[name]?.Value;
            try
            {
                return string.IsNullOrEmpty(value)
                    ? fallback
                    : XmlConvert.ToSingle(value);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        private static Vector3 ReadVector3(
            XmlNode node,
            string prefix,
            Vector3 fallback)
        {
            return new Vector3(
                ReadSingle(node, prefix + "X", fallback.x),
                ReadSingle(node, prefix + "Y", fallback.y),
                ReadSingle(node, prefix + "Z", fallback.z));
        }

        private static Quaternion ReadQuaternion(
            XmlNode node,
            string prefix,
            Quaternion fallback)
        {
            return new Quaternion(
                ReadSingle(node, prefix + "X", fallback.x),
                ReadSingle(node, prefix + "Y", fallback.y),
                ReadSingle(node, prefix + "Z", fallback.z),
                ReadSingle(node, prefix + "W", fallback.w));
        }

        private static AxisLocks ReadLocks(XmlNode node, string prefix)
        {
            return new AxisLocks(
                ReadBoolean(node, prefix + "X", true),
                ReadBoolean(node, prefix + "Y", true),
                ReadBoolean(node, prefix + "Z", true));
        }

        private readonly struct AxisLocks
        {
            public AxisLocks(bool x, bool y, bool z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool X { get; }
            public bool Y { get; }
            public bool Z { get; }
        }

        private sealed class ConstraintBinding
        {
            private readonly Transform parent;
            private readonly Transform child;
            private readonly bool position;
            private readonly Vector3 positionOffset;
            private readonly bool rotation;
            private readonly Quaternion rotationOffset;
            private readonly bool scale;
            private readonly Vector3 scaleOffset;
            private readonly bool mirrorPosition;
            private readonly bool mirrorRotation;
            private readonly bool mirrorScale;
            private readonly bool lookAt;
            private readonly float positionFactor;
            private readonly float rotationFactor;
            private readonly float scaleFactor;
            private readonly AxisLocks positionLocks;
            private readonly AxisLocks rotationLocks;
            private readonly AxisLocks scaleLocks;
            private readonly Vector3 originalParentPosition;
            private readonly Quaternion originalParentRotation;
            private readonly Vector3 originalParentScale;

            public ConstraintBinding(
                Transform parent,
                Transform child,
                bool position,
                Vector3 positionOffset,
                bool rotation,
                Quaternion rotationOffset,
                bool scale,
                Vector3 scaleOffset,
                bool mirrorPosition,
                bool mirrorRotation,
                bool mirrorScale,
                bool lookAt,
                float positionFactor,
                float rotationFactor,
                float scaleFactor,
                AxisLocks positionLocks,
                AxisLocks rotationLocks,
                AxisLocks scaleLocks)
            {
                this.parent = parent;
                this.child = child;
                this.position = position;
                this.positionOffset = positionOffset;
                this.rotation = rotation;
                this.rotationOffset = rotationOffset;
                this.scale = scale;
                this.scaleOffset = scaleOffset;
                this.mirrorPosition = mirrorPosition;
                this.mirrorRotation = mirrorRotation;
                this.mirrorScale = mirrorScale;
                this.lookAt = lookAt;
                this.positionFactor = positionFactor;
                this.rotationFactor = rotationFactor;
                this.scaleFactor = scaleFactor;
                this.positionLocks = positionLocks;
                this.rotationLocks = rotationLocks;
                this.scaleLocks = scaleLocks;
                originalParentPosition = parent.position;
                originalParentRotation = parent.rotation;
                originalParentScale = parent.lossyScale;
            }

            public void Evaluate()
            {
                if (parent == null || child == null)
                {
                    return;
                }

                if (position)
                {
                    ApplyPosition();
                }

                if (rotation)
                {
                    ApplyRotation();
                }

                if (scale)
                {
                    ApplyScale();
                }
            }

            private void ApplyPosition()
            {
                var movement = parent.position - originalParentPosition;
                var target = mirrorPosition
                    ? -movement * (positionFactor + 1f)
                    : movement * (positionFactor - 1f);
                target = parent.TransformPoint(target + positionOffset);
                var current = child.position;
                child.position = new Vector3(
                    positionLocks.X ? target.x : current.x,
                    positionLocks.Y ? target.y : current.y,
                    positionLocks.Z ? target.z : current.z);
            }

            private void ApplyRotation()
            {
                Quaternion target;
                if (lookAt)
                {
                    target = Quaternion.LookRotation(
                        parent.position - child.position);
                    if (mirrorRotation)
                    {
                        target = new Quaternion(
                            -target.x,
                            -target.y,
                            target.z,
                            target.w);
                    }
                }
                else
                {
                    var change = Quaternion.Inverse(originalParentRotation) *
                                 parent.rotation;
                    target = originalParentRotation *
                             (mirrorRotation
                                 ? Quaternion.Inverse(change)
                                 : change);
                }

                target *= rotationOffset;
                target = Quaternion.SlerpUnclamped(
                    Quaternion.identity,
                    target,
                    rotationFactor);
                var current = child.rotation.eulerAngles;
                var euler = target.eulerAngles;
                child.rotation = Quaternion.Euler(
                    rotationLocks.X ? euler.x : current.x,
                    rotationLocks.Y ? euler.y : current.y,
                    rotationLocks.Z ? euler.z : current.z);
            }

            private void ApplyScale()
            {
                var parentScale = parent.lossyScale;
                var childParentScale = child.parent != null
                    ? child.parent.lossyScale
                    : Vector3.one;
                var target = new Vector3(
                    ScaleAxis(
                        originalParentScale.x,
                        parentScale.x,
                        childParentScale.x,
                        scaleOffset.x),
                    ScaleAxis(
                        originalParentScale.y,
                        parentScale.y,
                        childParentScale.y,
                        scaleOffset.y),
                    ScaleAxis(
                        originalParentScale.z,
                        parentScale.z,
                        childParentScale.z,
                        scaleOffset.z));
                var current = child.localScale;
                child.localScale = new Vector3(
                    scaleLocks.X ? target.x : current.x,
                    scaleLocks.Y ? target.y : current.y,
                    scaleLocks.Z ? target.z : current.z);
            }

            private float ScaleAxis(
                float original,
                float current,
                float childParent,
                float offset)
            {
                if (Mathf.Abs(original) < Epsilon ||
                    Mathf.Abs(childParent) < Epsilon)
                {
                    return offset;
                }

                var exponent = mirrorScale ? -scaleFactor : scaleFactor;
                return original * offset / childParent *
                       Mathf.Pow(current / original, exponent);
            }
        }
    }

    public sealed class KoikatsuStudioSceneInstance : IDisposable
    {
        private GameObject root;
        private List<KoikatsuStudioItemInstance> items;
        private List<KoikatsuReferenceModelInstance> characters;
        private IReadOnlyList<ICharacterModel> characterModels;
        private Dictionary<KoikatsuReferenceModelInstance,
            KoikatsuSceneObject> characterSources;
        private Dictionary<int, GameObject> objectsByDictionaryKey;
        private List<GameObject> objectsByTimelineIndex;
        private IReadOnlyList<GameObject> readOnlyObjectsByTimelineIndex;
        private KoikatsuStudioMapInstance map;

        internal KoikatsuStudioSceneInstance(
            GameObject root,
            KoikatsuScene scene,
            List<KoikatsuStudioItemInstance> items,
            List<KoikatsuReferenceModelInstance> characters,
            Dictionary<KoikatsuReferenceModelInstance, KoikatsuSceneObject>
                characterSources,
            List<KoikatsuMissingStudioItem> missingItems,
            Dictionary<int, GameObject> objectsByDictionaryKey,
            List<GameObject> objectsByTimelineIndex)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.items = items ?? throw new ArgumentNullException(nameof(items));
            this.characters = characters ??
                throw new ArgumentNullException(nameof(characters));
            characterModels = characters.AsReadOnly();
            this.characterSources = characterSources ??
                throw new ArgumentNullException(nameof(characterSources));
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            MissingItems = missingItems.AsReadOnly();
            this.objectsByDictionaryKey = objectsByDictionaryKey;
            this.objectsByTimelineIndex = objectsByTimelineIndex;
            readOnlyObjectsByTimelineIndex = objectsByTimelineIndex.AsReadOnly();
        }

        public GameObject Root => root;

        public KoikatsuScene Scene { get; }

        public int ImportedItemCount => items?.Count ?? 0;

        public int ImportedCharacterCount => characters?.Count ?? 0;

        public IReadOnlyList<ICharacterModel> CharacterModels =>
            characterModels ?? Array.Empty<ICharacterModel>();

        public bool HasMap => map != null;

        public string MapError { get; private set; } = string.Empty;

        public IReadOnlyList<KoikatsuMissingStudioItem> MissingItems { get; }

        public IReadOnlyDictionary<int, GameObject> ObjectsByDictionaryKey =>
            objectsByDictionaryKey;

        public IReadOnlyList<GameObject> ObjectsByTimelineIndex =>
            readOnlyObjectsByTimelineIndex;

        public bool TryGetObject(int dictionaryKey, out GameObject value)
        {
            return ObjectsByDictionaryKey.TryGetValue(dictionaryKey, out value);
        }

        internal bool TryReplaceCharacter(
            KoikatsuReferenceModelInstance character,
            KoikatsuReferenceModelInstance replacement,
            string abdataRoot,
            string modsRoot)
        {
            if (character == null || replacement == null ||
                replacement.SourceCard == null ||
                !characterSources.TryGetValue(character, out var source))
            {
                return false;
            }

            var characterIndex = characters.IndexOf(character);
            var oldRoot = character.Root;
            var newRoot = replacement.Root;
            if (characterIndex < 0 || oldRoot == null || newRoot == null)
            {
                return false;
            }

            var oldTransform = oldRoot.transform;
            var newTransform = newRoot.transform;
            newTransform.SetParent(oldTransform.parent, false);
            newTransform.SetSiblingIndex(oldTransform.GetSiblingIndex());
            newTransform.localPosition = oldTransform.localPosition;
            newTransform.localRotation = oldTransform.localRotation;
            newTransform.localScale = oldTransform.localScale;
            newRoot.SetActive(oldRoot.activeSelf);

            var previousCard = source.Character.Card;
            source.Character.Card = replacement.SourceCard;
            try
            {
                KoikatsuStudioCharacterPose.Apply(
                    replacement,
                    source.Character,
                    abdataRoot,
                    modsRoot);
            }
            catch
            {
                source.Character.Card = previousCard;
                throw;
            }

            for (var index = 0; index < source.Children.Count; index++)
            {
                if (TryGetObject(
                        source.Children[index].Base.DicKey,
                        out var child) &&
                    child != null && child.transform.IsChildOf(oldTransform))
                {
                    child.transform.SetParent(newTransform, false);
                }
            }

            characters[characterIndex] = replacement;
            characterSources.Remove(character);
            characterSources[replacement] = source;
            ReplaceObjectReferences(oldRoot, newRoot);
            try
            {
                character.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            return true;
        }

        private void ReplaceObjectReferences(
            GameObject oldRoot,
            GameObject newRoot)
        {
            var keys = objectsByDictionaryKey
                .Where(pair => ReferenceEquals(pair.Value, oldRoot))
                .Select(pair => pair.Key)
                .ToArray();
            for (var index = 0; index < keys.Length; index++)
            {
                objectsByDictionaryKey[keys[index]] = newRoot;
            }

            for (var index = 0; index < objectsByTimelineIndex.Count; index++)
            {
                if (ReferenceEquals(objectsByTimelineIndex[index], oldRoot))
                {
                    objectsByTimelineIndex[index] = newRoot;
                }
            }
        }

        internal async Task LoadMapAsync(
            string abdataRoot,
            string modsRoot,
            CancellationToken cancellationToken)
        {
            if (Scene.Map == null || Scene.Map.Id < 0)
            {
                return;
            }

            try
            {
                map = await KoikatsuStudioMapInstance.LoadAsync(
                    Scene.Map,
                    abdataRoot,
                    modsRoot,
                    root.transform,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsMapLoadFailure(exception))
            {
                MapError = exception.Message;
                Debug.LogWarning(
                    $"Could not load Koikatsu Studio map {Scene.Map.Id}: " +
                    exception.Message);
            }
        }

        private static bool IsMapLoadFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is InvalidDataException ||
                   exception is ArgumentException ||
                   exception is UnityException;
        }

        public void Dispose()
        {
            if (root == null)
            {
                return;
            }

            map?.Dispose();
            map = null;

            for (var index = characters.Count - 1; index >= 0; index--)
            {
                characters[index].Dispose();
            }

            characters.Clear();
            characterModels = Array.Empty<ICharacterModel>();
            characterSources.Clear();

            for (var index = items.Count - 1; index >= 0; index--)
            {
                items[index].ReleaseResources(false);
            }

            items.Clear();
            objectsByDictionaryKey.Clear();
            objectsByTimelineIndex.Clear();
            readOnlyObjectsByTimelineIndex = Array.Empty<GameObject>();
            KoikatsuStudioItemLoader.Destroy(root);
            root = null;
        }
    }

    internal sealed class KoikatsuStudioMapInstance : IDisposable
    {
        private GameObject root;
        private List<KoikatsuAssetBundleLease> leases;
        private List<Material> runtimeMaterials;

        private KoikatsuStudioMapInstance(
            GameObject root,
            List<KoikatsuAssetBundleLease> leases,
            List<Material> runtimeMaterials)
        {
            this.root = root;
            this.leases = leases;
            this.runtimeMaterials = runtimeMaterials;
        }

        public static async Task<KoikatsuStudioMapInstance> LoadAsync(
            KoikatsuSceneMap source,
            string abdataRoot,
            string modsRoot,
            Transform parent,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var catalog = KoikatsuListCatalog.Load(abdataRoot, modsRoot);
            if (!catalog.TryGetMap(source.Id, source.ModGuid, out var entry))
            {
                throw new InvalidDataException(
                    $"Map ID {source.Id}" +
                    (string.IsNullOrEmpty(source.ModGuid)
                        ? string.Empty
                        : $" from zipmod '{source.ModGuid}'") +
                    " was not found in the Koikatsu map lists.");
            }

            var leases = new List<KoikatsuAssetBundleLease>();
            var runtimeMaterials = new List<Material>();
            GameObject mapRoot = null;
            Scene loadedScene = default;
            try
            {
                KoikatsuStudioBundleDependencies.Acquire(
                    abdataRoot,
                    catalog,
                    entry,
                    leases);
                var mapSources = catalog.ResolveBundleCandidates(
                    abdataRoot,
                    entry.BundlePath,
                    entry.Archive);
                var mapLease = KoikatsuVirtualAssetLoader.AcquireFirst(
                    mapSources,
                    out _);
                if (mapLease == null)
                {
                    throw new InvalidDataException(
                        "No Koikatsu Sideloader candidate can provide map " +
                        $"bundle '{entry.BundlePath}'.");
                }

                leases.Add(mapLease);

                var scenePath = FindScenePath(
                    mapLease.Bundle.GetAllScenePaths(),
                    entry.SceneName);
                var existingScenes = new HashSet<ulong>();
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    existingScenes.Add(
                        SceneManager.GetSceneAt(index).handle.GetRawData());
                }

                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                var operation = SceneManager.LoadSceneAsync(
                    scenePath,
                    LoadSceneMode.Additive);
                if (operation == null)
                {
                    throw new InvalidDataException(
                        $"Unity could not start loading map scene '{scenePath}'.");
                }

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                loadedScene = FindLoadedScene(existingScenes, scenePath);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    throw new InvalidDataException(
                        $"Map scene '{scenePath}' did not finish loading.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var roots = loadedScene.GetRootGameObjects();
                for (var index = 0; index < roots.Length; index++)
                {
                    if (string.Equals(
                            roots[index].name,
                            "Map",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        mapRoot = roots[index];
                        break;
                    }
                }

                if (mapRoot == null && roots.Length != 0)
                {
                    mapRoot = roots[0];
                }

                if (mapRoot == null)
                {
                    throw new InvalidDataException(
                        $"Map scene '{scenePath}' has no root object.");
                }

                SceneManager.MoveGameObjectToScene(
                    mapRoot,
                    parent.gameObject.scene);
                mapRoot.name = string.IsNullOrWhiteSpace(entry.Name)
                    ? $"Map {source.Id}"
                    : entry.Name;
                mapRoot.transform.SetParent(parent, false);
                mapRoot.transform.localPosition = source.Position;
                mapRoot.transform.localRotation = Quaternion.Euler(
                    source.EulerAngles);
                mapRoot.transform.localScale = Vector3.one;
                DisableMapCameras(mapRoot);
                KoikatsuMaterialConverter.Convert(
                    mapRoot,
                    runtimeMaterials);

                var unload = SceneManager.UnloadSceneAsync(loadedScene);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        await Task.Yield();
                    }
                }

                loadedScene = default;
                return new KoikatsuStudioMapInstance(
                    mapRoot,
                    leases,
                    runtimeMaterials);
            }
            catch
            {
                if (mapRoot != null)
                {
                    KoikatsuCharacterAssembler.DestroyRuntimeObject(mapRoot);
                }

                if (loadedScene.IsValid() && loadedScene.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(loadedScene);
                    if (unload != null)
                    {
                        while (!unload.isDone)
                        {
                            await Task.Yield();
                        }
                    }
                }

                for (var index = 0; index < runtimeMaterials.Count; index++)
                {
                    KoikatsuCharacterAssembler.DestroyRuntimeObject(
                        runtimeMaterials[index]);
                }

                for (var index = leases.Count - 1; index >= 0; index--)
                {
                    leases[index].Dispose();
                }

                throw;
            }
        }

        private static string FindScenePath(
            IReadOnlyList<string> scenePaths,
            string sceneName)
        {
            for (var index = 0; index < scenePaths.Count; index++)
            {
                if (string.Equals(
                        Path.GetFileNameWithoutExtension(scenePaths[index]),
                        sceneName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return scenePaths[index];
                }
            }

            if (scenePaths.Count == 1)
            {
                return scenePaths[0];
            }

            throw new InvalidDataException(
                $"Map AssetBundle does not contain scene '{sceneName}'.");
        }

        private static Scene FindLoadedScene(
            ISet<ulong> existingScenes,
            string scenePath)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!existingScenes.Contains(scene.handle.GetRawData()) &&
                    scene.isLoaded &&
                    (string.Equals(
                         scene.path,
                         scenePath,
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         scene.name,
                         Path.GetFileNameWithoutExtension(scenePath),
                         StringComparison.OrdinalIgnoreCase)))
                {
                    return scene;
                }
            }

            return default;
        }

        private static void DisableMapCameras(GameObject mapRoot)
        {
            var cameras = mapRoot.GetComponentsInChildren<Camera>(true);
            for (var index = 0; index < cameras.Length; index++)
            {
                cameras[index].enabled = false;
            }

            var listeners = mapRoot.GetComponentsInChildren<AudioListener>(true);
            for (var index = 0; index < listeners.Length; index++)
            {
                listeners[index].enabled = false;
            }
        }

        public void Dispose()
        {
            if (root == null)
            {
                return;
            }

            KoikatsuCharacterAssembler.DestroyRuntimeObject(root);
            root = null;
            for (var index = 0; index < runtimeMaterials.Count; index++)
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(
                    runtimeMaterials[index]);
            }

            runtimeMaterials.Clear();
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }

            leases.Clear();
        }
    }

    public sealed class KoikatsuMissingStudioItem
    {
        internal KoikatsuMissingStudioItem(
            int group,
            int category,
            int no,
            string modGuid,
            string error)
        {
            Group = group;
            Category = category;
            No = no;
            ModGuid = modGuid ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public int Group { get; }

        public int Category { get; }

        public int No { get; }

        public string ModGuid { get; }

        public string Error { get; }
    }
}
