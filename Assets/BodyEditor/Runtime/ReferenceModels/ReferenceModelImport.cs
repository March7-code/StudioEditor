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

    public interface IReferenceModelPhysicsController
    {
        bool SupportsPhysics { get; }

        bool PhysicsEnabled { get; }

        void SetPhysicsEnabled(bool enabled);
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
        private int loadVersion;

        public event Action StateChanged;

        public ReferenceModelImportStatus Status { get; private set; } =
            ReferenceModelImportStatus.Idle;

        public string Error { get; private set; } = string.Empty;

        public string CurrentPath { get; private set; } = string.Empty;

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

            var adapter = FindAdapter(filePath);
            if (adapter == null)
            {
                SetState(
                    ReferenceModelImportStatus.Failed,
                    $"No model adapter supports '{Path.GetExtension(filePath)}'.");
                return false;
            }

            activeLoad?.Cancel();
            var cancellation = new CancellationTokenSource();
            activeLoad = cancellation;
            var version = ++loadVersion;
            SetState(ReferenceModelImportStatus.Loading);

            try
            {
                var instance = await adapter.ImportAsync(
                    filePath,
                    transform,
                    cancellation.Token);

                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"{adapter.FormatName} adapter returned no model instance.");
                }

                if (version != loadVersion || cancellation.IsCancellationRequested)
                {
                    instance.Dispose();
                    return false;
                }

                current?.Dispose();
                current = instance;
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
            CurrentPath = string.Empty;
            SetState(ReferenceModelImportStatus.Idle);
        }

        private IReferenceModelFormatAdapter FindAdapter(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            for (var adapterIndex = 0; adapterIndex < adapters.Count; adapterIndex++)
            {
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
            StateChanged = null;
        }
    }
}
