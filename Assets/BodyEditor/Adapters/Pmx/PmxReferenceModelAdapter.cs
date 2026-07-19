using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UMT;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public sealed class PmxReferenceModelAdapter : IReferenceModelFormatAdapter
    {
        private static readonly IReadOnlyList<string> extensions =
            Array.AsReadOnly(new[] { ".pmx" });

        public string FormatName => "MikuMikuDance PMX";

        public IReadOnlyList<string> FileExtensions => extensions;

        public async Task<IReferenceModelInstance> ImportAsync(
            string filePath,
            Transform parent,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PMX file was not found.", filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            PMXModel model;
            var frameBudget = new UMTFrameBudget(4d);
            using (var stream = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                model = await PMXReader.ReadAsync(frameBudget, stream, false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            UseOriginalNames(model);

            var container = new GameObject(
                Path.GetFileNameWithoutExtension(filePath) + " (Imported)");
            container.transform.SetParent(parent, false);

            PMXImportResult result = null;
            try
            {
                var options = new PMXImportOptions
                {
                    sourcePath = filePath,
                    sourceName = Path.GetFileNameWithoutExtension(filePath),
                    textureBaseDirectory = Path.GetDirectoryName(filePath),
                    parent = container.transform,
                    umtResources = Resources.Load<UMTResources>("UMTResources"),
                    applyRenames = false,
                    createAvatar = false,
                    strictVersion = false,
                };

                result = await PMXImporter.BuildUnityObjectsAsync(
                    frameBudget,
                    model,
                    options);
                cancellationToken.ThrowIfCancellationRequested();

                return new PmxReferenceModelInstance(result, container);
            }
            catch
            {
                if (result != null)
                {
                    new PmxReferenceModelInstance(result, container).Dispose();
                }
                else
                {
                    PMXUtilities.DestroyRuntimeObject(container);
                    PMXUtilities.DestroyRuntimeObject(model);
                }

                throw;
            }
        }

        private static void UseOriginalNames(PMXModel model)
        {
            for (var index = 0; index < model.materials.Length; index++)
            {
                var value = model.materials[index];
                value.renamedName = value.originalName;
                model.materials[index] = value;
            }

            for (var index = 0; index < model.bones.Length; index++)
            {
                var value = model.bones[index];
                value.renamedName = value.originalName;
                model.bones[index] = value;
            }

            for (var index = 0; index < model.morphs.Length; index++)
            {
                var value = model.morphs[index];
                value.renamedName = value.originalName;
                model.morphs[index] = value;
            }

            for (var index = 0; index < model.displayFrames.Length; index++)
            {
                var value = model.displayFrames[index];
                value.renamedName = value.originalName;
                model.displayFrames[index] = value;
            }

            for (var index = 0; index < model.rigidBodies.Length; index++)
            {
                var value = model.rigidBodies[index];
                value.renamedName = value.originalName;
                model.rigidBodies[index] = value;
            }

            for (var index = 0; index < model.joints.Length; index++)
            {
                var value = model.joints[index];
                value.renamedName = value.originalName;
                model.joints[index] = value;
            }
        }
    }

    internal sealed class PmxReferenceModelInstance :
        IReferenceModelInstance,
        IReferenceModelPhysicsController,
        IReferenceModelSkeletonProvider
    {
        private PMXImportResult result;
        private GameObject container;
        private MMDTransformManager transformManager;
        private IReadOnlyList<ReferenceModelBone> bones;

        public PmxReferenceModelInstance(
            PMXImportResult result,
            GameObject container)
        {
            this.result = result ?? throw new ArgumentNullException(nameof(result));
            this.container = container ?? throw new ArgumentNullException(nameof(container));
            transformManager = result.root != null
                ? result.root.GetComponent<MMDTransformManager>()
                : null;
            bones = PmxBodyBoneProfile.Build(result);
            SetPhysicsEnabled(false);
        }

        public string DisplayName => result?.root != null
            ? result.root.name
            : string.Empty;

        public GameObject Root => result?.root;

        public IReadOnlyList<ReferenceModelBone> Bones => bones;

        public bool SupportsPhysics => transformManager?.physicsManager != null;

        public bool PhysicsEnabled => SupportsPhysics && transformManager.livePhysics;

        public void SetPhysicsEnabled(bool enabled)
        {
            var manager = transformManager;
            if (manager?.physicsManager == null)
            {
                return;
            }

            manager.livePhysics = enabled;
            if (enabled)
            {
                manager.ResetPhysics();
            }
            else
            {
                manager.SolveTransforms(true);
            }
        }

        public void Dispose()
        {
            if (result == null)
            {
                return;
            }

            for (var index = 0; index < result.meshes.Count; index++)
            {
                PMXUtilities.DestroyRuntimeObject(result.meshes[index].mesh);
            }

            for (var index = 0; index < result.materials.Count; index++)
            {
                PMXUtilities.DestroyRuntimeObject(result.materials[index]);
            }

            for (var index = 0; index < result.textures.Count; index++)
            {
                PMXUtilities.DestroyRuntimeObject(result.textures[index]);
            }

            if (result.avatarResult != null)
            {
                PMXUtilities.DestroyRuntimeObject(result.avatarResult.avatar);
            }

            PMXUtilities.DestroyRuntimeObject(container);
            PMXUtilities.DestroyRuntimeObject(result.model);
            result = null;
            container = null;
            transformManager = null;
            bones = Array.Empty<ReferenceModelBone>();
        }

    }
}
