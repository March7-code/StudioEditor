using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public interface IReferenceModelInstance : IDisposable
    {
        string DisplayName { get; }

        GameObject Root { get; }
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
            int keyframeCount,
            bool enabled,
            bool supported,
            string status = null)
        {
            Index = index;
            Name = name ?? string.Empty;
            Target = target ?? string.Empty;
            Kind = kind;
            KeyframeCount = keyframeCount;
            Enabled = enabled;
            Supported = supported;
            Status = status ?? string.Empty;
        }

        public int Index { get; }

        public string Name { get; }

        public string Target { get; }

        public ReferenceTimelineTrackKind Kind { get; }

        public int KeyframeCount { get; }

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

    public sealed class ReferenceModelImportController : MonoBehaviour
    {
        private readonly List<IReferenceModelFormatAdapter> adapters =
            new List<IReferenceModelFormatAdapter>();

        private CancellationTokenSource activeLoad;
        private IReferenceModelInstance current;
        private IReferenceModelFormatAdapter currentAdapter;
        private int loadVersion;

        public event Action StateChanged;

        public ReferenceModelImportStatus Status { get; private set; } =
            ReferenceModelImportStatus.Idle;

        public string Error { get; private set; } = string.Empty;

        public string CurrentPath { get; private set; } = string.Empty;

        public string CurrentFormatName => currentAdapter?.FormatName ??
                                           string.Empty;

        public IReferenceModelInstance Current => current;

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
                cancellationToken => adapter.ImportVariantAsync(
                    filePath,
                    transform,
                    variantIndex,
                    cancellationToken));
        }

        private async Task<bool> ImportInternalAsync(
            string filePath,
            IReferenceModelFormatAdapter adapter,
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

                current?.Dispose();
                current = instance;
                currentAdapter = adapter;
                CurrentPath = filePath;
                SetState(ReferenceModelImportStatus.Ready);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (version == loadVersion)
                {
                    SetState(current == null
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

        public void Clear()
        {
            loadVersion++;
            activeLoad?.Cancel();
            current?.Dispose();
            current = null;
            currentAdapter = null;
            CurrentPath = string.Empty;
            SetState(ReferenceModelImportStatus.Idle);
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
            current?.Dispose();
            current = null;
            currentAdapter = null;
            StateChanged = null;
        }
    }
}
