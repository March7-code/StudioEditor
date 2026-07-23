using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BodyEditor.ReferenceModels
{
    public static class KoikatsuStudioItemLoader
    {
        public static KoikatsuStudioItemInstance Load(
            string abdataRoot,
            int group,
            int category,
            int no,
            string modsRoot = null,
            string modGuid = null,
            Transform parent = null,
            Vector3? localPosition = null,
            Vector3? localEulerAngles = null,
            Vector3? localScale = null,
            bool visible = true,
            KoikatsuSceneItem appearance = null)
        {
            return LoadInternal(
                abdataRoot,
                group,
                category,
                no,
                modsRoot,
                modGuid,
                parent,
                localPosition,
                localEulerAngles,
                localScale,
                visible,
                appearance,
                null,
                null);
        }

        private static KoikatsuStudioItemInstance LoadInternal(
            string abdataRoot,
            int group,
            int category,
            int no,
            string modsRoot,
            string modGuid,
            Transform parent,
            Vector3? localPosition,
            Vector3? localEulerAngles,
            Vector3? localScale,
            bool visible,
            KoikatsuSceneItem appearance,
            KoikatsuScene scene,
            KoikatsuSceneObject itemObject)
        {
            if (string.IsNullOrWhiteSpace(abdataRoot))
            {
                throw new ArgumentException(
                    "Koikatsu abdata root is required.",
                    nameof(abdataRoot));
            }

            var catalog = KoikatsuListCatalog.Load(abdataRoot, modsRoot);
            if (!catalog.TryGetStudio(
                    group,
                    category,
                    no,
                    modGuid,
                    out var entry))
            {
                throw new InvalidDataException(
                    "Koikatsu Studio item list entry was not found for " +
                    $"group {group}, category {category}, slot {no}" +
                    (string.IsNullOrWhiteSpace(modGuid)
                        ? "."
                        : $", zipmod GUID '{modGuid}'."));
            }

            if (string.IsNullOrWhiteSpace(entry.BundlePath) ||
                string.IsNullOrWhiteSpace(entry.AssetName))
            {
                throw new InvalidDataException(
                    $"Koikatsu Studio item '{entry.Name}' has no prefab source.");
            }

            var leases = new List<KoikatsuAssetBundleLease>();
            GameObject instance = null;
            var materials = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                KoikatsuStudioBundleDependencies.Acquire(
                    abdataRoot,
                    catalog,
                    entry,
                    leases);
                var bundleSources = catalog.ResolveBundleCandidates(
                    abdataRoot,
                    entry.BundlePath,
                    entry.Archive);
                var lease = KoikatsuVirtualAssetLoader.AcquireAsset<GameObject>(
                    bundleSources,
                    entry.AssetName,
                    out var prefab,
                    out var bundleSource);
                if (lease == null || prefab == null)
                {
                    throw new InvalidDataException(
                        $"No Koikatsu Sideloader candidate for virtual bundle " +
                        $"'{entry.BundlePath}' contains prefab " +
                        $"'{entry.AssetName}'.");
                }

                leases.Add(lease);

                instance = Object.Instantiate(prefab, parent, false);
                instance.name = string.IsNullOrWhiteSpace(entry.Name)
                    ? entry.AssetName
                    : entry.Name;
                instance.transform.localPosition = localPosition ?? Vector3.zero;
                instance.transform.localRotation = Quaternion.Euler(
                    localEulerAngles ?? Vector3.zero);
                instance.transform.localScale = localScale ?? Vector3.one;
                var rendererMap = KoikatsuStudioItemMetadataLoader.TryCreate(
                    bundleSource,
                    entry.AssetName,
                    instance);
                KoikatsuMaterialConverter.Convert(instance, materials);
                var patterns = KoikatsuStudioPatternLoader.Load(
                    abdataRoot,
                    catalog,
                    entry,
                    appearance,
                    scene,
                    itemObject,
                    leases,
                    textures);
                KoikatsuStudioItemAppearance.Apply(
                    instance,
                    appearance,
                    entry,
                    rendererMap,
                    patterns);
                KoikatsuSpringBoneMetadataLoader.Attach(
                    bundleSource,
                    entry.AssetName,
                    instance,
                    appearance?.EnableDynamicBone ?? true);
                KoikatsuStudioFinalIkMetadataLoader.Attach(
                    bundleSource,
                    entry.AssetName,
                    instance);
                var animator = instance.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = entry.IsAnime;
                    if (appearance != null)
                    {
                        animator.speed = appearance.AnimeSpeed;
                    }
                }

                KoikatsuStudioItemPose.Attach(
                    instance,
                    animator,
                    appearance);

                var childRoot = FindByName(
                                    instance.transform,
                                    entry.ChildRoot) ??
                                instance.transform;
                instance.SetActive(visible);

                var result = new KoikatsuStudioItemInstance(
                    instance,
                    childRoot,
                    leases,
                    materials,
                    textures,
                    group,
                    category,
                    no,
                    entry.ModGuid);
                instance = null;
                return result;
            }
            catch
            {
                Destroy(instance);
                for (var index = 0; index < materials.Count; index++)
                {
                    Destroy(materials[index]);
                }

                for (var index = 0; index < textures.Count; index++)
                {
                    Destroy(textures[index]);
                }

                for (var index = leases.Count - 1; index >= 0; index--)
                {
                    leases[index].Dispose();
                }

                throw;
            }
        }

        public static KoikatsuStudioItemInstance Load(
            string abdataRoot,
            KoikatsuScene scene,
            KoikatsuSceneObject itemObject,
            string modsRoot = null,
            Transform parent = null)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (itemObject?.Item == null)
            {
                throw new ArgumentException(
                    "Scene object is not a Koikatsu Studio item.",
                    nameof(itemObject));
            }

            var hasResolution = scene.TryResolveItem(
                itemObject,
                out var resolution);
            var guid = hasResolution ? resolution.Guid : null;
            var resolvedSlot = hasResolution
                ? resolution.Slot
                : itemObject.Item.No;
            return LoadInternal(
                abdataRoot,
                itemObject.Item.Group,
                itemObject.Item.Category,
                resolvedSlot,
                modsRoot,
                guid,
                parent,
                itemObject.Base.Position,
                itemObject.Base.Rotation,
                itemObject.Base.Scale,
                itemObject.Base.Visible,
                itemObject.Item,
                scene,
                itemObject);
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (string.Equals(
                        transforms[index].name,
                        name,
                        StringComparison.Ordinal))
                {
                    return transforms[index];
                }
            }

            return null;
        }

        internal static void Destroy(Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }
    }

    [DefaultExecutionOrder(31000)]
    internal sealed class KoikatsuStudioItemPose : MonoBehaviour
    {
        private readonly struct FkOverride
        {
            public FkOverride(Transform target, Quaternion rotation)
            {
                Target = target;
                Rotation = rotation;
            }

            public Transform Target { get; }

            public Quaternion Rotation { get; }
        }

        private Animator animator;
        private KoikatsuFinalIkComponent[] finalIk =
            Array.Empty<KoikatsuFinalIkComponent>();
        private bool[] activeFinalIk = Array.Empty<bool>();
        private FkOverride[] fkOverrides = Array.Empty<FkOverride>();
        private bool fkEnabled;
        private int timelineEvaluationFrame = -1;

        internal static KoikatsuStudioItemPose Attach(
            GameObject root,
            Animator animator,
            KoikatsuSceneItem source)
        {
            if (root == null || source == null)
            {
                return null;
            }

            var finalIk =
                KoikatsuFinalIkRuntime.GetComponentsInChildren(root);
            var fkOverrides = BuildFkOverrides(root.transform, source.Bones);
            if (animator == null && finalIk.Length == 0 &&
                fkOverrides.Length == 0)
            {
                return null;
            }

            var pose = root.AddComponent<KoikatsuStudioItemPose>();
            pose.Initialize(
                animator,
                source,
                finalIk,
                fkOverrides);
            return pose;
        }

        internal void EvaluateNow()
        {
            FixFinalIkTransforms();
            ApplyFk();
            SolveFinalIk();
        }

        internal void EvaluateAfterTimeline()
        {
            EvaluateNow();
            timelineEvaluationFrame = Time.frameCount;
        }

        internal void SuppressFkPhysics()
        {
            if (!fkEnabled)
            {
                return;
            }

            var springs = GetComponentsInChildren<KoikatsuSpringBone>(true);
            for (var index = 0; index < springs.Length; index++)
            {
                springs[index].SetSimulationEnabled(false);
            }

            var ver02 = GetComponentsInChildren<KoikatsuVer02SpringBone>(true);
            for (var index = 0; index < ver02.Length; index++)
            {
                ver02[index].SetSimulationEnabled(false);
            }
        }

        private void Initialize(
            Animator itemAnimator,
            KoikatsuSceneItem source,
            KoikatsuFinalIkComponent[] itemFinalIk,
            FkOverride[] itemFkOverrides)
        {
            animator = itemAnimator;
            finalIk = itemFinalIk ?? Array.Empty<KoikatsuFinalIkComponent>();
            activeFinalIk = new bool[finalIk.Length];
            fkOverrides = itemFkOverrides ?? Array.Empty<FkOverride>();
            fkEnabled = source.EnableFK && fkOverrides.Length != 0;

            RestoreAnimationTime(source.AnimeNormalizedTime);
            for (var index = 0; index < finalIk.Length; index++)
            {
                var component = finalIk[index];
                activeFinalIk[index] = component != null &&
                                       component.IsAlive &&
                                       component.Enabled;
                if (activeFinalIk[index])
                {
                    component.Enabled = false;
                }
            }

            SuppressFkPhysics();
            EvaluateNow();
        }

        private void RestoreAnimationTime(float normalizedTime)
        {
            if (animator == null || !animator.enabled ||
                animator.runtimeAnimatorController == null ||
                animator.layerCount == 0)
            {
                return;
            }

            if (Mathf.Approximately(normalizedTime, 0f))
            {
                animator.Update(0f);
                return;
            }

            // Studio first enters the controller so it can discover the active
            // state, then seeks that state to the time saved in OIItemInfo.
            animator.Update(1f);
            var state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.shortNameHash, 0, normalizedTime);
            animator.Update(0f);
        }

        private void LateUpdate()
        {
            if (timelineEvaluationFrame != Time.frameCount)
            {
                EvaluateNow();
            }
        }

        private void SolveFinalIk()
        {
            for (var index = 0; index < finalIk.Length; index++)
            {
                var component = finalIk[index];
                if (!activeFinalIk[index] || component == null ||
                    !component.IsAlive)
                {
                    continue;
                }

                if (!component.SolverInitiated)
                {
                    component.Initiate();
                }

                if (component.SolverInitiated)
                {
                    component.UpdateSolver();
                }
            }
        }

        private void FixFinalIkTransforms()
        {
            for (var index = 0; index < finalIk.Length; index++)
            {
                var component = finalIk[index];
                if (!activeFinalIk[index] || component == null ||
                    !component.IsAlive || !component.FixTransforms ||
                    !component.SolverInitiated)
                {
                    continue;
                }

                component.FixSolverTransforms();
            }
        }

        private void ApplyFk()
        {
            if (!fkEnabled)
            {
                return;
            }

            for (var index = 0; index < fkOverrides.Length; index++)
            {
                var target = fkOverrides[index].Target;
                if (target != null)
                {
                    target.localRotation = fkOverrides[index].Rotation;
                }
            }
        }

        private static FkOverride[] BuildFkOverrides(
            Transform root,
            IReadOnlyDictionary<string, KoikatsuSceneBone> bones)
        {
            if (root == null || bones == null || bones.Count == 0)
            {
                return Array.Empty<FkOverride>();
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            var transformsByName = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (!transformsByName.ContainsKey(transforms[index].name))
                {
                    transformsByName.Add(transforms[index].name, transforms[index]);
                }
            }

            var result = new List<FkOverride>(bones.Count);
            foreach (var pair in bones)
            {
                if (transformsByName.TryGetValue(pair.Key, out var target))
                {
                    result.Add(new FkOverride(
                        target,
                        Quaternion.Euler(pair.Value.Rotation)));
                }
            }

            return result.ToArray();
        }
    }

    public sealed class KoikatsuStudioItemInstance : IDisposable
    {
        private GameObject root;
        private List<KoikatsuAssetBundleLease> leases;
        private List<Material> runtimeMaterials;
        private List<Texture2D> runtimeTextures;

        internal KoikatsuStudioItemInstance(
            GameObject root,
            Transform childRoot,
            List<KoikatsuAssetBundleLease> leases,
            List<Material> runtimeMaterials,
            List<Texture2D> runtimeTextures,
            int group,
            int category,
            int no,
            string modGuid)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            ChildRoot = childRoot != null ? childRoot : root.transform;
            this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
            this.runtimeMaterials = runtimeMaterials ??
                throw new ArgumentNullException(nameof(runtimeMaterials));
            this.runtimeTextures = runtimeTextures ??
                throw new ArgumentNullException(nameof(runtimeTextures));
            Group = group;
            Category = category;
            No = no;
            ModGuid = modGuid ?? string.Empty;
        }

        public GameObject Root => root;

        public Transform ChildRoot { get; }

        public int Group { get; }

        public int Category { get; }

        public int No { get; }

        public string ModGuid { get; }

        public void Dispose()
        {
            ReleaseResources(true);
        }

        internal void ReleaseResources(bool destroyRoot)
        {
            if (root == null)
            {
                return;
            }

            if (destroyRoot)
            {
                KoikatsuStudioItemLoader.Destroy(root);
            }

            root = null;
            for (var index = 0; index < runtimeMaterials.Count; index++)
            {
                KoikatsuStudioItemLoader.Destroy(runtimeMaterials[index]);
            }

            runtimeMaterials.Clear();
            for (var index = 0; index < runtimeTextures.Count; index++)
            {
                KoikatsuStudioItemLoader.Destroy(runtimeTextures[index]);
            }

            runtimeTextures.Clear();
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }

            leases.Clear();
        }
    }
}
