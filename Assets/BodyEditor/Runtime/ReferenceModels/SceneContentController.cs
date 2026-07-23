using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BodyEditor.Characters;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public interface IReferenceModelInstance : IDisposable
    {
        string DisplayName { get; }

        GameObject Root { get; }
    }

    public enum ReferenceSceneObjectKind
    {
        Scene,
        Character,
        Object,
        Light,
        Camera,
        Collection,
    }

    public interface IReferenceSceneNode
    {
        string Id { get; }

        string DisplayName { get; }

        ReferenceSceneObjectKind Kind { get; }

        GameObject Root { get; }

        bool IsVisible { get; }

        IReadOnlyList<IReferenceSceneNode> Children { get; }

        void SetVisible(bool visible);
    }

    public interface IReferenceSceneHierarchyProvider
    {
        IReferenceSceneNode SceneHierarchy { get; }
    }

    public interface IReferenceCharacterReplacementController
    {
        bool TryReplaceCharacter(
            ICharacterModel character,
            IReferenceModelInstance replacement,
            out ICharacterModel result);
    }

    public sealed class ReferenceSceneNode : IReferenceSceneNode
    {
        public ReferenceSceneNode(
            string id,
            string displayName,
            ReferenceSceneObjectKind kind,
            GameObject root,
            IReadOnlyList<IReferenceSceneNode> children = null)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Kind = kind;
            Root = root;
            Children = children ?? Array.Empty<IReferenceSceneNode>();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public ReferenceSceneObjectKind Kind { get; }

        public GameObject Root { get; private set; }

        public bool IsVisible => Root != null && Root.activeSelf;

        public IReadOnlyList<IReferenceSceneNode> Children { get; }

        public void SetVisible(bool visible)
        {
            if (Root != null)
            {
                Root.SetActive(visible);
            }
        }

        public void ReplaceRoot(GameObject root)
        {
            Root = root;
        }
    }

    public readonly struct ReferenceModelCameraPose
    {
        public ReferenceModelCameraPose(
            Vector3 target,
            Vector3 eulerAngles,
            Vector3 distance,
            float fieldOfView)
        {
            Target = target;
            EulerAngles = eulerAngles;
            Distance = distance;
            FieldOfView = fieldOfView;
        }

        public Vector3 Target { get; }

        public Vector3 EulerAngles { get; }

        public Vector3 Distance { get; }

        public float FieldOfView { get; }
    }

    public interface IReferenceModelCameraProvider
    {
        bool TryGetCamera(out ReferenceModelCameraPose pose);
    }

    public interface IReferenceSceneCameraProvider :
        IReferenceModelCameraProvider
    {
        string ActiveCameraId { get; }

        Camera ActiveCamera { get; }

        Camera FreeCamera { get; }

        bool TryActivateCamera(
            string cameraId,
            out Camera camera);
    }

    public interface IReferenceModelVariantProvider
    {
        string VariantLabel { get; }

        IReadOnlyList<string> VariantNames { get; }

        int ActiveVariantIndex { get; }
    }

    public interface IReferenceModelPhysicsController
    {
        bool SupportsPhysics { get; }

        bool PhysicsEnabled { get; }

        void SetPhysicsEnabled(bool enabled);
    }

    public enum ReferenceTimelineTrackKind
    {
        Position,
        Rotation,
        Scale,
        Value,
        Unsupported,
    }

    public sealed class ReferenceTimelineTrack
    {
        public ReferenceTimelineTrack(
            int index,
            string name,
            string target,
            ReferenceTimelineTrackKind kind,
            IReadOnlyList<float> keyframeTimes,
            bool enabled,
            bool supported,
            string status = null)
        {
            Index = index;
            Name = name ?? string.Empty;
            Target = target ?? string.Empty;
            Kind = kind;
            var times = keyframeTimes == null
                ? Array.Empty<float>()
                : new List<float>(keyframeTimes).ToArray();
            Array.Sort(times);
            KeyframeTimes = Array.AsReadOnly(times);
            Enabled = enabled;
            Supported = supported;
            Status = status ?? string.Empty;
        }

        public int Index { get; }

        public string Name { get; }

        public string Target { get; }

        public ReferenceTimelineTrackKind Kind { get; }

        public int KeyframeCount => KeyframeTimes.Count;

        public IReadOnlyList<float> KeyframeTimes { get; }

        public bool Enabled { get; private set; }

        public bool Supported { get; }

        public string Status { get; }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
        }
    }

    public interface IReferenceModelTimelineController
    {
        event Action StateChanged;

        float Duration { get; }

        float CurrentTime { get; }

        float PlaybackSpeed { get; set; }

        bool IsPlaying { get; }

        bool Loop { get; set; }

        IReadOnlyList<ReferenceTimelineTrack> Tracks { get; }

        void Play();

        void Pause();

        void Stop();

        void Seek(float time);

        void SetTrackEnabled(int trackIndex, bool enabled);
    }

    public interface IReferenceModelTimelineProvider
    {
        IReferenceModelTimelineController Timeline { get; }
    }

    public interface IReferenceModelSkeletonProvider
    {
        IReadOnlyList<ReferenceModelBone> Bones { get; }
    }

    public sealed class ReferenceModelBone
    {
        public ReferenceModelBone(
            string name,
            Transform transform,
            int parentIndex,
            bool isBodyBone = false,
            int bodyParentIndex = -1,
            HumanBodyBones? humanoidBone = null)
        {
            Name = name ?? string.Empty;
            Transform = transform;
            ParentIndex = parentIndex;
            IsBodyBone = isBodyBone;
            BodyParentIndex = bodyParentIndex;
            HumanoidBone = humanoidBone;
        }

        public string Name { get; }

        public Transform Transform { get; }

        public int ParentIndex { get; }

        public bool IsBodyBone { get; }

        public int BodyParentIndex { get; }

        public HumanBodyBones? HumanoidBone { get; }
    }

    public interface IReferenceModelFormatAdapter
    {
        string FormatName { get; }

        IReadOnlyList<string> FileExtensions { get; }

        Task<IReferenceModelInstance> ImportAsync(
            string filePath,
            Transform parent,
            CancellationToken cancellationToken);
    }

    public interface IReferenceSceneFormatAdapter :
        IReferenceModelFormatAdapter
    {
    }

    public interface IReferenceModelVariantFormatAdapter :
        IReferenceModelFormatAdapter
    {
        Task<IReferenceModelInstance> ImportVariantAsync(
            string filePath,
            Transform parent,
            int variantIndex,
            CancellationToken cancellationToken);
    }

    public enum ReferenceModelImportStatus
    {
        Idle,
        Loading,
        Ready,
        Failed,
    }

    public sealed class SceneContentController : MonoBehaviour
    {
        private readonly List<IReferenceModelFormatAdapter> adapters =
            new List<IReferenceModelFormatAdapter>();
        private readonly List<IReferenceModelInstance> additionalImports =
            new List<IReferenceModelInstance>();
        private readonly List<IReferenceModelInstance> managedImports =
            new List<IReferenceModelInstance>();
        private readonly List<ICharacterModel> characterModels =
            new List<ICharacterModel>();

        private CancellationTokenSource activeLoad;
        private IReferenceModelInstance current;
        private IReferenceModelFormatAdapter currentAdapter;
        private IReferenceSceneNode sceneHierarchy;
        private int loadVersion;

        public event Action StateChanged;

        public ReferenceModelImportStatus Status { get; private set; } =
            ReferenceModelImportStatus.Idle;

        public string Error { get; private set; } = string.Empty;

        public string CurrentPath { get; private set; } = string.Empty;

        public string CurrentFormatName => currentAdapter?.FormatName ??
                                           string.Empty;

        public IReferenceModelInstance Current => current;

        public IReadOnlyList<IReferenceModelInstance> ManagedImports =>
            managedImports;

        public IReadOnlyList<ICharacterModel> CharacterModels =>
            characterModels;

        public IReferenceSceneNode SceneHierarchy => sceneHierarchy;

        public IReadOnlyList<IReferenceModelFormatAdapter> Adapters => adapters;

        public void RegisterAdapter(IReferenceModelFormatAdapter adapter)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            adapters.Add(adapter);
        }

        public async Task<bool> ImportAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var adapter = FindAdapter(filePath, false);
            if (adapter == null)
            {
                SetState(
                    ReferenceModelImportStatus.Failed,
                    $"No model adapter supports '{Path.GetExtension(filePath)}'.");
                return false;
            }

            return await ImportInternalAsync(
                filePath,
                adapter,
                false,
                false,
                cancellationToken => adapter.ImportAsync(
                    filePath,
                    transform,
                    cancellationToken));
        }

        public async Task<bool> ImportSceneAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var adapter = FindAdapter(filePath, true);
            if (adapter == null)
            {
                SetState(
                    ReferenceModelImportStatus.Failed,
                    $"No scene adapter supports '{Path.GetExtension(filePath)}'.");
                return false;
            }

            return await ImportInternalAsync(
                filePath,
                adapter,
                true,
                false,
                cancellationToken => adapter.ImportAsync(
                    filePath,
                    transform,
                    cancellationToken));
        }

        public async Task<bool> SelectVariantAsync(int variantIndex)
        {
            if (!(current is IReferenceModelVariantProvider variants) ||
                !(currentAdapter is IReferenceModelVariantFormatAdapter adapter) ||
                variantIndex < 0 ||
                variantIndex >= variants.VariantNames.Count)
            {
                return false;
            }

            if (variantIndex == variants.ActiveVariantIndex)
            {
                return true;
            }

            var filePath = CurrentPath;
            return await ImportInternalAsync(
                filePath,
                adapter,
                false,
                true,
                cancellationToken => adapter.ImportVariantAsync(
                    filePath,
                    transform,
                    variantIndex,
                    cancellationToken));
        }

        private async Task<bool> ImportInternalAsync(
            string filePath,
            IReferenceModelFormatAdapter adapter,
            bool replaceAll,
            bool replacePrimary,
            Func<CancellationToken, Task<IReferenceModelInstance>> import)
        {

            activeLoad?.Cancel();
            var cancellation = new CancellationTokenSource();
            activeLoad = cancellation;
            var version = ++loadVersion;
            SetState(ReferenceModelImportStatus.Loading);

            try
            {
                var instance = await import(cancellation.Token);

                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"{adapter.FormatName} adapter returned no imported instance.");
                }

                if (version != loadVersion || cancellation.IsCancellationRequested)
                {
                    instance.Dispose();
                    return false;
                }

                if (replaceAll)
                {
                    DisposeManagedImports();
                }
                else if (replacePrimary)
                {
                    current?.Dispose();
                    current = null;
                }

                if (current == null)
                {
                    current = instance;
                    currentAdapter = adapter;
                    CurrentPath = filePath;
                }
                else
                {
                    additionalImports.Add(instance);
                }

                RefreshManagedScene();
                SetState(ReferenceModelImportStatus.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (version == loadVersion)
                {
                    SetState(managedImports.Count == 0
                        ? ReferenceModelImportStatus.Idle
                        : ReferenceModelImportStatus.Ready);
                }

                return false;
            }
            catch (Exception exception)
            {
                if (version == loadVersion)
                {
                    SetState(ReferenceModelImportStatus.Failed, exception.Message);
                }

                Debug.LogException(exception, this);
                return false;
            }
            finally
            {
                if (ReferenceEquals(activeLoad, cancellation))
                {
                    activeLoad = null;
                }

                cancellation.Dispose();
            }
        }

        public async Task<bool> ReplaceCharacterAsync(
            ICharacterModel character,
            string filePath)
        {
            if (character == null || character.Root == null ||
                string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var adapter = FindAdapter(filePath, false);
            if (adapter == null)
            {
                SetState(
                    ReferenceModelImportStatus.Failed,
                    $"No character adapter supports " +
                    $"'{Path.GetExtension(filePath)}'.");
                return false;
            }

            activeLoad?.Cancel();
            var cancellation = new CancellationTokenSource();
            activeLoad = cancellation;
            var version = ++loadVersion;
            SetState(ReferenceModelImportStatus.Loading);
            IReferenceModelInstance replacement = null;
            try
            {
                var parent = character.Root.transform.parent ?? transform;
                replacement = await adapter.ImportAsync(
                    filePath,
                    parent,
                    cancellation.Token);
                if (replacement == null)
                {
                    throw new InvalidOperationException(
                        $"{adapter.FormatName} adapter returned no " +
                        "replacement character.");
                }

                if (version != loadVersion || cancellation.IsCancellationRequested)
                {
                    replacement.Dispose();
                    replacement = null;
                    return false;
                }

                if (!TryAdoptReplacement(
                        character,
                        replacement,
                        adapter,
                        filePath))
                {
                    throw new InvalidOperationException(
                        "The selected character cannot be replaced by this " +
                        "imported model.");
                }

                replacement = null;
                RefreshManagedScene();
                SetState(ReferenceModelImportStatus.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (version == loadVersion)
                {
                    SetState(managedImports.Count == 0
                        ? ReferenceModelImportStatus.Idle
                        : ReferenceModelImportStatus.Ready);
                }

                return false;
            }
            catch (Exception exception)
            {
                if (version == loadVersion)
                {
                    SetState(ReferenceModelImportStatus.Failed, exception.Message);
                }

                Debug.LogException(exception, this);
                return false;
            }
            finally
            {
                replacement?.Dispose();
                if (ReferenceEquals(activeLoad, cancellation))
                {
                    activeLoad = null;
                }

                cancellation.Dispose();
            }
        }

        private bool TryAdoptReplacement(
            ICharacterModel character,
            IReferenceModelInstance replacement,
            IReferenceModelFormatAdapter adapter,
            string filePath)
        {
            for (var index = 0; index < managedImports.Count; index++)
            {
                if (managedImports[index] is
                        IReferenceCharacterReplacementController controller &&
                    controller.TryReplaceCharacter(
                        character,
                        replacement,
                        out _))
                {
                    return true;
                }
            }

            if (!(replacement is ICharacterModel replacementCharacter))
            {
                return false;
            }

            if (ReferenceEquals(current, character))
            {
                CopyRootTransform(character.Root, replacementCharacter.Root);
                var previous = current;
                current = replacement;
                currentAdapter = adapter;
                CurrentPath = filePath;
                DisposeReplacedImport(previous);
                return true;
            }

            for (var index = 0; index < additionalImports.Count; index++)
            {
                if (!ReferenceEquals(additionalImports[index], character))
                {
                    continue;
                }

                CopyRootTransform(character.Root, replacementCharacter.Root);
                var previous = additionalImports[index];
                additionalImports[index] = replacement;
                DisposeReplacedImport(previous);
                return true;
            }

            return false;
        }

        private static void CopyRootTransform(
            GameObject source,
            GameObject destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            var sourceTransform = source.transform;
            var destinationTransform = destination.transform;
            destinationTransform.SetParent(sourceTransform.parent, false);
            destinationTransform.SetSiblingIndex(sourceTransform.GetSiblingIndex());
            destinationTransform.localPosition = sourceTransform.localPosition;
            destinationTransform.localRotation = sourceTransform.localRotation;
            destinationTransform.localScale = sourceTransform.localScale;
            destination.SetActive(source.activeSelf);
        }

        private static void DisposeReplacedImport(
            IReferenceModelInstance instance)
        {
            try
            {
                instance?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public void Clear()
        {
            loadVersion++;
            activeLoad?.Cancel();
            DisposeManagedImports();
            RefreshManagedScene();
            SetState(ReferenceModelImportStatus.Idle);
        }

        private void RefreshManagedScene()
        {
            managedImports.Clear();
            characterModels.Clear();
            if (current != null)
            {
                managedImports.Add(current);
            }

            managedImports.AddRange(additionalImports);
            for (var index = 0; index < managedImports.Count; index++)
            {
                AddCharacters(managedImports[index], characterModels);
            }

            sceneHierarchy = BuildManagedHierarchy(managedImports);
        }

        private static void AddCharacters(
            IReferenceModelInstance instance,
            ICollection<ICharacterModel> result)
        {
            if (instance is ICharacterModelCollection collection)
            {
                var models = collection.CharacterModels;
                for (var index = 0; index < models.Count; index++)
                {
                    if (models[index] != null)
                    {
                        result.Add(models[index]);
                    }
                }
            }
            else if (instance is ICharacterModel character)
            {
                result.Add(character);
            }
        }

        private static IReferenceSceneNode BuildManagedHierarchy(
            IReadOnlyList<IReferenceModelInstance> imports)
        {
            if (imports.Count == 0)
            {
                return null;
            }

            var children = new List<IReferenceSceneNode>();
            GameObject root = null;
            var displayName = "Scene";
            for (var index = 0; index < imports.Count; index++)
            {
                var instance = imports[index];
                if (index == 0 && instance is
                        IReferenceSceneHierarchyProvider sceneProvider &&
                    sceneProvider.SceneHierarchy != null)
                {
                    var sourceRoot = sceneProvider.SceneHierarchy;
                    root = sourceRoot.Root;
                    displayName = sourceRoot.DisplayName;
                    for (var childIndex = 0;
                         childIndex < sourceRoot.Children.Count;
                         childIndex++)
                    {
                        children.Add(sourceRoot.Children[childIndex]);
                    }

                    continue;
                }

                var kind = instance is ICharacterModel
                    ? ReferenceSceneObjectKind.Character
                    : ReferenceSceneObjectKind.Object;
                children.Add(new ReferenceSceneNode(
                    $"scene/runtime-import/{index}",
                    instance.DisplayName,
                    kind,
                    instance.Root));
            }

            return new ReferenceSceneNode(
                "scene",
                displayName,
                ReferenceSceneObjectKind.Scene,
                root,
                children.AsReadOnly());
        }

        private void DisposeManagedImports()
        {
            for (var index = additionalImports.Count - 1;
                 index >= 0;
                 index--)
            {
                additionalImports[index].Dispose();
            }

            additionalImports.Clear();
            current?.Dispose();
            current = null;
            currentAdapter = null;
            CurrentPath = string.Empty;
        }

        private IReferenceModelFormatAdapter FindAdapter(
            string filePath,
            bool sceneAdapter)
        {
            var extension = Path.GetExtension(filePath);
            for (var adapterIndex = 0; adapterIndex < adapters.Count; adapterIndex++)
            {
                if ((adapters[adapterIndex] is IReferenceSceneFormatAdapter) !=
                    sceneAdapter)
                {
                    continue;
                }

                var extensions = adapters[adapterIndex].FileExtensions;
                for (var extensionIndex = 0;
                     extensionIndex < extensions.Count;
                     extensionIndex++)
                {
                    if (string.Equals(
                            extension,
                            extensions[extensionIndex],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return adapters[adapterIndex];
                    }
                }
            }

            return null;
        }

        private void SetState(
            ReferenceModelImportStatus status,
            string error = null)
        {
            Status = status;
            Error = error ?? string.Empty;
            StateChanged?.Invoke();
        }

        private void OnDestroy()
        {
            activeLoad?.Cancel();
            DisposeManagedImports();
            managedImports.Clear();
            characterModels.Clear();
            sceneHierarchy = null;
            StateChanged = null;
        }
    }
}
