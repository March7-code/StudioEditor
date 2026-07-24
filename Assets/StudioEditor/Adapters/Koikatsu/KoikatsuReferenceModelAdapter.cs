using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StudioEditor.Characters;
using StudioEditor.Settings;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    public sealed class KoikatsuReferenceModelAdapter :
        IReferenceModelVariantFormatAdapter
    {
        private static readonly IReadOnlyList<string> extensions =
            Array.AsReadOnly(new[] { ".png", ".unity3d" });

        public static string GameRootOverride { get; set; }

        public string FormatName => "Koikatsu character AssetBundle";

        public IReadOnlyList<string> FileExtensions => extensions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAdapter()
        {
            ReferenceModelAdapterRegistry.Register<KoikatsuReferenceModelAdapter>();
        }

        public Task<IReferenceModelInstance> ImportAsync(
            string filePath,
            Transform parent,
            CancellationToken cancellationToken)
        {
            return ImportVariantAsync(
                filePath,
                parent,
                0,
                cancellationToken);
        }

        public Task<IReferenceModelInstance> ImportVariantAsync(
            string filePath,
            Transform parent,
            int variantIndex,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Koikatsu AssetBundle was not found.",
                    filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            KoikatsuReferenceModelInstance instance;
            if (string.Equals(
                    Path.GetExtension(filePath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                var card = KoikatsuCardReader.Read(filePath);
                var installation = KoikatsuInstallation.Find(filePath);
                instance = KoikatsuCharacterAssembler.BuildFromCard(
                    card,
                    installation.AbdataRoot,
                    installation.ModsRoot,
                    parent,
                    cancellationToken,
                    variantIndex);
            }
            else
            {
                if (variantIndex != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(variantIndex),
                        "Direct AssetBundle imports have no outfit slots.");
                }

                if (!string.Equals(
                        Path.GetFileName(filePath),
                        KoikatsuCharacterAssembler.BaseBundleFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Direct Koikatsu bundle import expects " +
                        $"'{KoikatsuCharacterAssembler.BaseBundleFileName}'.");
                }

                instance = KoikatsuCharacterAssembler.BuildFemaleBase(
                    Path.GetFullPath(filePath),
                    parent,
                    cancellationToken);
            }

            return Task.FromResult<IReferenceModelInstance>(instance);
        }
    }

    public sealed class KoikatsuSceneReferenceModelAdapter :
        IReferenceSceneFormatAdapter
    {
        private static readonly IReadOnlyList<string> extensions =
            Array.AsReadOnly(new[] { ".png" });

        public string FormatName => "Koikatsu Studio scene";

        public IReadOnlyList<string> FileExtensions => extensions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAdapter()
        {
            ReferenceModelAdapterRegistry.Register<
                KoikatsuSceneReferenceModelAdapter>();
        }

        public async Task<IReferenceModelInstance> ImportAsync(
            string filePath,
            Transform parent,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Koikatsu Studio scene card was not found.",
                    filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var scene = KoikatsuSceneReader.Read(filePath);
            KoikatsuTimelineScene timeline = null;
            try
            {
                KoikatsuTimelineSceneReader.TryRead(filePath, out timeline);
            }
            catch (InvalidDataException exception)
            {
                Debug.LogWarning(
                    "Could not import Koikatsu Timeline data: " +
                    exception.Message);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var installation = KoikatsuInstallation.Find(filePath);
            var loaded = await KoikatsuStudioSceneLoader.LoadAsync(
                scene,
                installation.AbdataRoot,
                installation.ModsRoot,
                parent,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                loaded.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new KoikatsuSceneReferenceModelInstance(
                filePath,
                loaded,
                timeline,
                installation.AbdataRoot,
                installation.ModsRoot);
        }

    }

    internal sealed class KoikatsuSceneReferenceModelInstance :
        IReferenceModelInstance,
        IReferenceSceneCameraProvider,
        IReferenceModelPhysicsController,
        IReferenceModelTimelineProvider,
        IReferenceSceneHierarchyProvider,
        ICharacterModelCollection,
        IReferenceCharacterReplacementController
    {
        private KoikatsuStudioSceneInstance scene;
        private KoikatsuTimelinePlayer timeline;
        private readonly KoikatsuTimelineScene timelineData;
        private readonly string abdataRoot;
        private readonly string modsRoot;
        private KoikatsuListCatalog catalog;
        private IReferenceSceneNode sceneHierarchy;
        private readonly Dictionary<string, Camera> camerasById =
            new Dictionary<string, Camera>(StringComparer.Ordinal);
        private readonly Dictionary<string, KoikatsuSceneObject>
            cameraObjectsById =
                new Dictionary<string, KoikatsuSceneObject>(
                    StringComparer.Ordinal);
        private Camera activeCamera;
        private Camera freeCamera;
        private ReferenceModelCameraPose freeCameraPose;
        private string activeCameraId = string.Empty;
        private bool physicsEnabled;

        public KoikatsuSceneReferenceModelInstance(
            string sourcePath,
            KoikatsuStudioSceneInstance scene,
            KoikatsuTimelineScene timelineData,
            string abdataRoot,
            string modsRoot)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.timelineData = timelineData;
            this.abdataRoot = abdataRoot ?? string.Empty;
            this.modsRoot = modsRoot ?? string.Empty;
            var missing = scene.MissingItems.Count;
            DisplayName = $"{Path.GetFileNameWithoutExtension(sourcePath)} " +
                          $"({scene.ImportedItemCount} items, " +
                          $"{scene.ImportedCharacterCount} characters" +
                          (missing > 0 ? $", {missing} missing" : string.Empty) +
                          ")";
            sceneHierarchy = BuildSceneHierarchy(
                scene,
                DisplayName,
                camerasById,
                cameraObjectsById,
                out activeCameraId,
                out freeCamera,
                out freeCameraPose);
            if (!string.IsNullOrEmpty(activeCameraId))
            {
                camerasById.TryGetValue(activeCameraId, out activeCamera);
            }
            if (timelineData != null)
            {
                timeline = KoikatsuTimelinePlayer.Attach(
                    scene.Root,
                    timelineData,
                    scene.ObjectsByTimelineIndex,
                    scene.CharacterModels,
                    ResolveTimelineEyePattern);
            }

            SetPhysicsEnabled(false);
        }

        public string DisplayName { get; }

        public GameObject Root => scene?.Root;

        public bool SupportsPhysics => KoikatsuPhysicsRuntime.Supports(Root);

        public bool PhysicsEnabled => SupportsPhysics && physicsEnabled;

        public IReferenceModelTimelineController Timeline => timeline;

        public IReferenceSceneNode SceneHierarchy => sceneHierarchy;

        public string ActiveCameraId => activeCameraId;

        public Camera ActiveCamera => activeCamera;

        public Camera FreeCamera => freeCamera;

        public IReadOnlyList<ICharacterModel> CharacterModels =>
            scene?.CharacterModels ?? Array.Empty<ICharacterModel>();

        public bool TryReplaceCharacter(
            ICharacterModel character,
            IReferenceModelInstance replacement,
            out ICharacterModel result)
        {
            result = null;
            if (scene == null ||
                !(character is KoikatsuReferenceModelInstance current) ||
                !(replacement is KoikatsuReferenceModelInstance next))
            {
                return false;
            }

            var oldRoot = current.Root;
            if (!scene.TryReplaceCharacter(
                    current,
                    next,
                    abdataRoot,
                    modsRoot))
            {
                return false;
            }

            result = next;
            try
            {
                ReplaceNodeRoot(sceneHierarchy, oldRoot, next.Root);
                RebuildTimeline();
                if (physicsEnabled)
                {
                    KoikatsuPhysicsRuntime.SetEnabled(next.Root, true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            return true;
        }

        public void SetPhysicsEnabled(bool enabled)
        {
            physicsEnabled = enabled && SupportsPhysics;
            KoikatsuPhysicsRuntime.SetEnabled(Root, physicsEnabled);
        }

        public bool TryGetCamera(out ReferenceModelCameraPose pose)
        {
            if (activeCamera != null && activeCamera == freeCamera)
            {
                pose = freeCameraPose;
                return true;
            }

            if (activeCamera != null &&
                TryGetCameraPose(activeCamera, out pose))
            {
                return true;
            }

            var camera = scene?.Scene?.Camera;
            if (camera == null)
            {
                pose = default;
                return false;
            }

            pose = new ReferenceModelCameraPose(
                camera.Target,
                camera.EulerAngles,
                camera.Distance,
                camera.FieldOfView);
            return true;
        }

        public bool TryActivateCamera(
            string cameraId,
            out Camera camera)
        {
            if (string.IsNullOrEmpty(cameraId) ||
                !camerasById.TryGetValue(cameraId, out camera) ||
                camera == null)
            {
                camera = null;
                return false;
            }

            foreach (var candidate in camerasById.Values)
            {
                if (candidate != null)
                {
                    candidate.enabled = false;
                }
            }

            foreach (var candidate in cameraObjectsById.Values)
            {
                candidate.Active = false;
            }

            if (cameraObjectsById.TryGetValue(cameraId, out var source))
            {
                source.Active = true;
            }

            activeCameraId = cameraId;
            activeCamera = camera;
            activeCamera.enabled = true;
            return true;
        }

        public void Dispose()
        {
            timeline = null;
            sceneHierarchy = null;
            activeCamera = null;
            freeCamera = null;
            camerasById.Clear();
            cameraObjectsById.Clear();
            activeCameraId = string.Empty;
            scene?.Dispose();
            scene = null;
            physicsEnabled = false;
        }

        private void RebuildTimeline()
        {
            if (timelineData == null || scene?.Root == null)
            {
                return;
            }

            if (timeline != null)
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(timeline);
            }

            timeline = KoikatsuTimelinePlayer.Attach(
                scene.Root,
                timelineData,
                scene.ObjectsByTimelineIndex,
                scene.CharacterModels,
                ResolveTimelineEyePattern);
        }

        private int ResolveTimelineEyePattern(
            ICharacterModel character,
            int eyeSetId)
        {
            catalog ??= KoikatsuListCatalog.Load(abdataRoot, modsRoot);
            return KoikatsuCharacterAssembler.ResolveEyeMorphPattern(
                catalog,
                eyeSetId);
        }

        private static bool ReplaceNodeRoot(
            IReferenceSceneNode node,
            GameObject oldRoot,
            GameObject newRoot)
        {
            if (node == null)
            {
                return false;
            }

            if (ReferenceEquals(node.Root, oldRoot) &&
                node is ReferenceSceneNode mutable)
            {
                mutable.ReplaceRoot(newRoot);
                return true;
            }

            for (var index = 0; index < node.Children.Count; index++)
            {
                if (ReplaceNodeRoot(node.Children[index], oldRoot, newRoot))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReferenceSceneNode BuildSceneHierarchy(
            KoikatsuStudioSceneInstance instance,
            string displayName,
            IDictionary<string, Camera> camerasById,
            IDictionary<string, KoikatsuSceneObject> cameraObjectsById,
            out string activeCameraId,
            out Camera freeCamera,
            out ReferenceModelCameraPose freeCameraPose)
        {
            var source = instance.Scene;
            var children = new List<IReferenceSceneNode>(
                source.CameraSlots.Count + source.Objects.Count + 1);
            const string freeCameraId = "scene/free-camera";
            freeCamera = CreateFreeCamera(
                instance.Root.transform,
                source.Camera,
                out freeCameraPose);
            camerasById[freeCameraId] = freeCamera;
            children.Add(new ReferenceSceneNode(
                freeCameraId,
                "Free Camera",
                ReferenceSceneObjectKind.Camera,
                freeCamera.gameObject));
            activeCameraId = freeCameraId;
            for (var index = 0; index < source.CameraSlots.Count; index++)
            {
                var slot = source.CameraSlots[index];
                var id = $"scene/camera-slot/{slot.SlotNumber}";
                var camera = CreateCamera(
                    instance.Root.transform,
                    slot,
                    $"Camera {slot.SlotNumber}");
                camerasById[id] = camera;
                children.Add(new ReferenceSceneNode(
                    id,
                    $"Camera {slot.SlotNumber}",
                    ReferenceSceneObjectKind.Camera,
                    camera.gameObject));
            }

            for (var index = 0; index < source.Objects.Count; index++)
            {
                children.Add(BuildSceneNode(
                    instance,
                    source.Objects[index],
                    $"scene/{index}",
                    camerasById,
                    cameraObjectsById,
                    ref activeCameraId));
            }

            // Opening a scene starts from its saved Studio view while keeping
            // camera objects and slots available for explicit selection.
            activeCameraId = freeCameraId;

            return new ReferenceSceneNode(
                "scene",
                displayName,
                ReferenceSceneObjectKind.Scene,
                instance.Root,
                children.AsReadOnly());
        }

        private static IReferenceSceneNode BuildSceneNode(
            KoikatsuStudioSceneInstance instance,
            KoikatsuSceneObject source,
            string id,
            IDictionary<string, Camera> camerasById,
            IDictionary<string, KoikatsuSceneObject> cameraObjectsById,
            ref string activeCameraId)
        {
            instance.TryGetObject(source.Base.DicKey, out var root);
            if (root == null &&
                source.DictionaryKey != source.Base.DicKey)
            {
                instance.TryGetObject(source.DictionaryKey, out root);
            }

            var sourceChildren = source.Children ??
                                 Array.Empty<KoikatsuSceneObject>();
            var children = new List<IReferenceSceneNode>(
                sourceChildren.Count);
            for (var index = 0; index < sourceChildren.Count; index++)
            {
                children.Add(BuildSceneNode(
                    instance,
                    sourceChildren[index],
                    $"{id}/{index}",
                    camerasById,
                    cameraObjectsById,
                    ref activeCameraId));
            }

            if (source.Kind == KoikatsuSceneObjectKind.Camera)
            {
                var camera = EnsureCamera(
                    root,
                    instance.Scene.Camera?.FieldOfView ?? 23f);
                if (camera != null)
                {
                    camerasById[id] = camera;
                    cameraObjectsById[id] = source;
                    if (source.Active)
                    {
                        activeCameraId = id;
                    }
                }
            }

            var displayName = !string.IsNullOrWhiteSpace(source.Name)
                ? source.Name
                : root != null
                    ? root.name
                    : GetFallbackName(source);
            return new ReferenceSceneNode(
                id,
                displayName,
                MapObjectKind(source.Kind),
                root,
                children.AsReadOnly());
        }

        private static bool TryGetCameraPose(
            Camera camera,
            out ReferenceModelCameraPose pose)
        {
            if (camera == null)
            {
                pose = default;
                return false;
            }

            const float lookDistance = 1f;
            var rotation = camera.transform.rotation;
            pose = new ReferenceModelCameraPose(
                camera.transform.position + rotation * Vector3.forward *
                lookDistance,
                rotation.eulerAngles,
                new Vector3(0f, 0f, -lookDistance),
                camera.fieldOfView);
            return true;
        }

        private static Camera CreateCamera(
            Transform parent,
            KoikatsuSceneCamera source,
            string name)
        {
            var cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent, false);
            var rotation = Quaternion.Euler(source.EulerAngles);
            cameraObject.transform.localPosition =
                source.Target + rotation * source.Distance;
            cameraObject.transform.localRotation = rotation;
            return ConfigureCamera(
                cameraObject.AddComponent<Camera>(),
                source.FieldOfView);
        }

        private static Camera CreateFreeCamera(
            Transform sceneRoot,
            KoikatsuSceneCamera source,
            out ReferenceModelCameraPose pose)
        {
            var cameraObject = new GameObject("Free Camera");
            cameraObject.transform.SetParent(sceneRoot, false);
            var camera = ConfigureCamera(
                cameraObject.AddComponent<Camera>(),
                source?.FieldOfView ?? 50f);
            if (source != null)
            {
                var localRotation = Quaternion.Euler(source.EulerAngles);
                cameraObject.transform.localPosition =
                    source.Target + localRotation * source.Distance;
                cameraObject.transform.localRotation = localRotation;

                var worldRotation = cameraObject.transform.rotation;
                var worldTarget = sceneRoot.TransformPoint(source.Target);
                var cameraDistance = Quaternion.Inverse(worldRotation) *
                                     (cameraObject.transform.position -
                                      worldTarget);
                pose = new ReferenceModelCameraPose(
                    worldTarget,
                    worldRotation.eulerAngles,
                    cameraDistance,
                    camera.fieldOfView);
                return camera;
            }

            var renderers = sceneRoot.GetComponentsInChildren<Renderer>(true);
            var target = sceneRoot.position + Vector3.up;
            var distance = 8f;
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++)
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }

                target = bounds.center;
                var visibleHalfSize = Mathf.Max(
                    bounds.extents.y,
                    bounds.extents.x,
                    bounds.extents.z * 0.5f);
                distance = Mathf.Max(
                    1f,
                    visibleHalfSize * 1.25f /
                    Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) +
                    bounds.extents.z);
            }

            var rotation = Quaternion.Euler(12f, 25f, 0f);
            cameraObject.transform.SetPositionAndRotation(
                target - rotation * Vector3.forward * distance,
                rotation);
            pose = new ReferenceModelCameraPose(
                target,
                rotation.eulerAngles,
                new Vector3(0f, 0f, -distance),
                camera.fieldOfView);
            return camera;
        }

        private static Camera EnsureCamera(GameObject root, float fieldOfView)
        {
            if (root == null)
            {
                return null;
            }

            root.SetActive(true);
            return ConfigureCamera(
                root.GetComponent<Camera>() ?? root.AddComponent<Camera>(),
                fieldOfView);
        }

        private static Camera ConfigureCamera(Camera camera, float fieldOfView)
        {
            camera.fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 2000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.105f, 0.115f, 0.125f, 1f);
            camera.enabled = false;
            return camera;
        }

        private static ReferenceSceneObjectKind MapObjectKind(
            KoikatsuSceneObjectKind kind)
        {
            switch (kind)
            {
                case KoikatsuSceneObjectKind.Character:
                    return ReferenceSceneObjectKind.Character;
                case KoikatsuSceneObjectKind.Item:
                    return ReferenceSceneObjectKind.Object;
                case KoikatsuSceneObjectKind.Light:
                    return ReferenceSceneObjectKind.Light;
                case KoikatsuSceneObjectKind.Camera:
                    return ReferenceSceneObjectKind.Camera;
                case KoikatsuSceneObjectKind.Folder:
                case KoikatsuSceneObjectKind.Route:
                    return ReferenceSceneObjectKind.Collection;
                default:
                    return ReferenceSceneObjectKind.Object;
            }
        }

        private static string GetFallbackName(KoikatsuSceneObject source)
        {
            var label = MapObjectKind(source.Kind).ToString();
            var key = source.Base.DicKey >= 0
                ? source.Base.DicKey
                : source.DictionaryKey;
            return key >= 0 ? $"{label} {key}" : label;
        }
    }

    internal sealed class KoikatsuInstallation
    {
        private KoikatsuInstallation(string gameRoot)
        {
            GameRoot = gameRoot;
            AbdataRoot = Path.Combine(gameRoot, "abdata");
            ModsRoot = Path.Combine(gameRoot, "mods");
        }

        public string GameRoot { get; }

        public string AbdataRoot { get; }

        public string ModsRoot { get; }

        public static KoikatsuInstallation Find(string cardPath)
        {
            var configuredRoot = KoikatsuReferenceModelAdapter.GameRootOverride;
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                return Validate(configuredRoot);
            }

            if (StudioEditorSettings.HasKoikatsuGameRootOverride)
            {
                configuredRoot = StudioEditorSettings.KoikatsuGameRoot;
                if (!string.IsNullOrWhiteSpace(configuredRoot))
                {
                    return Validate(configuredRoot);
                }
            }
            else
            {
                var configuredRoots = KoikatsuAdapterConfiguration.GameRoots;
                for (var index = 0; index < configuredRoots.Count; index++)
                {
                    configuredRoot = configuredRoots[index];
                    if (string.IsNullOrWhiteSpace(configuredRoot))
                    {
                        continue;
                    }

                    configuredRoot = Path.GetFullPath(configuredRoot.Trim());
                    if (Directory.Exists(Path.Combine(configuredRoot, "abdata")))
                    {
                        return new KoikatsuInstallation(configuredRoot);
                    }
                }
            }

            var directory = new DirectoryInfo(
                Path.GetDirectoryName(Path.GetFullPath(cardPath)) ??
                string.Empty);
            while (directory != null)
            {
                if (string.Equals(
                        directory.Name,
                        "UserData",
                        StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent != null)
                {
                    var candidate = directory.Parent.FullName;
                    if (Directory.Exists(Path.Combine(candidate, "abdata")))
                    {
                        return new KoikatsuInstallation(candidate);
                    }
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Koikatsu installation for this card. " +
                "Configure the directory containing abdata, mods, and " +
                "UserData in Editor Settings.");
        }

        private static KoikatsuInstallation Validate(string gameRoot)
        {
            gameRoot = Path.GetFullPath(gameRoot.Trim());
            var abdataRoot = Path.Combine(gameRoot, "abdata");
            if (!Directory.Exists(abdataRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Koikatsu abdata directory was not found: {abdataRoot}");
            }

            return new KoikatsuInstallation(gameRoot);
        }
    }

    internal static class KoikatsuAdapterConfiguration
    {
        private const string ResourceName = "KoikatsuAdapterConfig";
        private static IReadOnlyList<string> gameRoots;

        public static IReadOnlyList<string> GameRoots
        {
            get
            {
                if (gameRoots != null)
                {
                    return gameRoots;
                }

                var asset = Resources.Load<TextAsset>(ResourceName);
                if (asset == null)
                {
                    gameRoots = Array.Empty<string>();
                    return gameRoots;
                }

                var table = JsonUtility.FromJson<ConfigurationTable>(asset.text);
                gameRoots = Array.AsReadOnly(
                    table?.gameRoots ?? Array.Empty<string>());
                return gameRoots;
            }
        }

        [Serializable]
        private sealed class ConfigurationTable
        {
            public string[] gameRoots = Array.Empty<string>();
        }
    }
}
