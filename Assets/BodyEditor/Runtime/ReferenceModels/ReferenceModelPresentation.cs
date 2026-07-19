using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public enum ReferenceTopologyDisplayMode
    {
        Edges,
        Rings,
        Both,
    }

    public sealed class ReferenceModelPartState
    {
        internal ReferenceModelPartState(
            string name,
            string path,
            bool isBodyBone = false)
        {
            Name = name;
            Path = path;
            IsBodyBone = isBodyBone;
        }

        public string Name { get; }
        public string Path { get; }
        public bool Visible { get; internal set; } = true;
        public bool Highlighted { get; internal set; }
        public bool IsBodyBone { get; }
    }

    [RequireComponent(typeof(ReferenceModelImportController))]
    public sealed class ReferenceModelPresentationController : MonoBehaviour
    {
        internal static readonly Color SkeletonColor =
            new Color(0.28f, 0.72f, 0.92f, 0.9f);
        internal static readonly Color SkeletonHighlightColor =
            new Color(1f, 0.67f, 0.16f, 1f);
        internal static readonly Color MeshHighlightColor =
            new Color(0.2f, 0.78f, 0.62f, 0.24f);
        internal static readonly Color MeshTopologyColor =
            new Color(0.66f, 0.88f, 0.92f, 0.92f);
        internal static readonly Color SectionRingColor =
            new Color(1f, 0.56f, 0.16f, 1f);
        internal static readonly Color SectionFieldColor =
            new Color(0.24f, 0.88f, 0.62f, 0.82f);

        private readonly List<ReferenceModelPartState> meshItems =
            new List<ReferenceModelPartState>();
        private readonly List<ReferenceModelPartState> boneItems =
            new List<ReferenceModelPartState>();
        private ReferenceModelImportController importController;
        private GameObject currentRoot;
        private SourceRenderer[] sourceRenderers = Array.Empty<SourceRenderer>();
        private bool[] sourceInitiallyEnabled = Array.Empty<bool>();
        private MeshHighlightOverlay meshOverlay;
        private ReferenceMeshTopologyOverlay topologyOverlay;
        private ReferenceSectionRingOverlay sectionRingOverlay;
        private BodySectionFieldOverlay sectionFieldOverlay;
        private SkeletonOverlay skeletonOverlay;
        private BodySectionField sectionField = BodySectionField.Empty;

        public event Action StateChanged;

        public bool HasModel => currentRoot != null;
        public bool SupportsBodyBoneView { get; private set; }
        public bool BodyBonesOnly { get; private set; }
        public bool TopologyMode { get; private set; }
        public ReferenceTopologyDisplayMode TopologyDisplayMode { get; private set; } =
            ReferenceTopologyDisplayMode.Edges;
        public int SectionRingCount { get; private set; } =
            ReferenceSectionRingOverlay.DefaultRingCount;
        public bool SupportsTopologyMode => HasModel && topologyOverlay != null;
        public bool SupportsSectionRings => sectionRingOverlay?.IsSupported == true;
        public IReadOnlyList<ReferenceSectionRing> SectionRings =>
            sectionRingOverlay?.Rings ?? Array.Empty<ReferenceSectionRing>();
        public IReadOnlyList<ReferenceJointPatch> JointPatches =>
            sectionRingOverlay?.JointPatches ?? Array.Empty<ReferenceJointPatch>();
        public BodySectionField SectionField => sectionField;
        public int Revision { get; private set; }
        public IReadOnlyList<ReferenceModelPartState> MeshItems => meshItems;
        public IReadOnlyList<ReferenceModelPartState> BoneItems => boneItems;
        public bool MeshVisible => All(meshItems, item => item.Visible, true);
        public bool MeshVisibilityMixed => Mixed(meshItems, item => item.Visible);
        public bool MeshHighlighted => All(meshItems, item => item.Highlighted, false);
        public bool MeshHighlightMixed => Mixed(meshItems, item => item.Highlighted);
        public bool SkeletonVisible => AllBones(item => item.Visible, true);
        public bool SkeletonVisibilityMixed => MixedBones(item => item.Visible);
        public bool SkeletonHighlighted => All(
            VisibleBoneItems(),
            item => item.Highlighted,
            false);
        public bool SkeletonHighlightMixed => Mixed(
            VisibleBoneItems(),
            item => item.Highlighted);
        public int MeshCount => meshItems.Count;
        public int BoneCount => boneItems.Count;

        private void OnEnable()
        {
            importController = GetComponent<ReferenceModelImportController>();
            importController.StateChanged += HandleImportStateChanged;
            HandleImportStateChanged();
        }

        private void LateUpdate()
        {
            skeletonOverlay?.Refresh();
            if (Any(meshItems, item => item.Visible && item.Highlighted))
            {
                meshOverlay?.Refresh();
            }
        }

        public void SetMeshVisible(bool visible)
        {
            for (var index = 0; index < meshItems.Count; index++)
            {
                SetMeshItemVisible(index, visible, false);
            }

            RefreshSectionRings();
            StateChanged?.Invoke();
        }

        public void SetMeshHighlighted(bool highlighted)
        {
            for (var index = 0; index < meshItems.Count; index++)
            {
                SetMeshItemHighlighted(index, highlighted, false);
            }

            StateChanged?.Invoke();
        }

        public void SetSkeletonVisible(bool visible)
        {
            for (var index = 0; index < boneItems.Count; index++)
            {
                if (!BodyBonesOnly || boneItems[index].IsBodyBone)
                {
                    boneItems[index].Visible = visible;
                }
            }

            skeletonOverlay?.Refresh();
            StateChanged?.Invoke();
        }

        public void SetSkeletonHighlighted(bool highlighted)
        {
            for (var index = 0; index < boneItems.Count; index++)
            {
                if (!BodyBonesOnly || boneItems[index].IsBodyBone)
                {
                    boneItems[index].Highlighted = highlighted;
                }
            }

            skeletonOverlay?.Refresh();
            StateChanged?.Invoke();
        }

        public void SetMeshItemVisible(int index, bool visible)
        {
            SetMeshItemVisible(index, visible, true);
        }

        public void SetMeshItemHighlighted(int index, bool highlighted)
        {
            SetMeshItemHighlighted(index, highlighted, true);
        }

        public void SetBoneItemVisible(int index, bool visible)
        {
            if (!IsValid(index, boneItems.Count) ||
                (BodyBonesOnly && !boneItems[index].IsBodyBone) ||
                boneItems[index].Visible == visible)
            {
                return;
            }

            boneItems[index].Visible = visible;
            skeletonOverlay?.Refresh();
            StateChanged?.Invoke();
        }

        public void SetBoneItemHighlighted(int index, bool highlighted)
        {
            if (!IsValid(index, boneItems.Count) ||
                (BodyBonesOnly && !boneItems[index].IsBodyBone) ||
                boneItems[index].Highlighted == highlighted)
            {
                return;
            }

            boneItems[index].Highlighted = highlighted;
            skeletonOverlay?.Refresh();
            StateChanged?.Invoke();
        }

        public void SetBodyBonesOnly(bool enabled)
        {
            enabled = enabled && SupportsBodyBoneView;
            if (BodyBonesOnly == enabled)
            {
                return;
            }

            BodyBonesOnly = enabled;
            if (enabled)
            {
                for (var index = 0; index < boneItems.Count; index++)
                {
                    if (!boneItems[index].IsBodyBone)
                    {
                        boneItems[index].Highlighted = false;
                    }
                }
            }

            skeletonOverlay?.SetBodyBonesOnly(enabled);
            StateChanged?.Invoke();
        }

        public void SetTopologyMode(bool enabled)
        {
            enabled = enabled && SupportsTopologyMode;
            if (enabled &&
                importController.Current is IReferenceModelPhysicsController physics &&
                physics.PhysicsEnabled)
            {
                physics.SetPhysicsEnabled(false);
            }

            if (TopologyMode == enabled)
            {
                return;
            }

            TopologyMode = enabled;
            ApplyTopologyMode();
            StateChanged?.Invoke();
        }

        public void SetTopologyDisplayMode(ReferenceTopologyDisplayMode mode)
        {
            if ((mode == ReferenceTopologyDisplayMode.Rings ||
                 mode == ReferenceTopologyDisplayMode.Both) &&
                !SupportsSectionRings)
            {
                mode = ReferenceTopologyDisplayMode.Edges;
            }

            if (TopologyDisplayMode == mode)
            {
                return;
            }

            TopologyDisplayMode = mode;
            RefreshSectionRings();
            ApplyTopologyMode();
            StateChanged?.Invoke();
        }

        public void SetSectionRingCount(int count)
        {
            count = Mathf.Clamp(
                count,
                1,
                ReferenceSectionRingOverlay.MaximumRingCount);
            if (SectionRingCount == count)
            {
                return;
            }

            SectionRingCount = count;
            RefreshSectionRings();
            StateChanged?.Invoke();
        }

        private void SetMeshItemVisible(int index, bool visible, bool notify)
        {
            if (!IsValid(index, meshItems.Count) || meshItems[index].Visible == visible)
            {
                return;
            }

            meshItems[index].Visible = visible;
            sourceRenderers[index].ApplyVisibility(visible && !TopologyMode);
            meshOverlay?.SetVisible(
                index,
                !TopologyMode && visible && meshItems[index].Highlighted);
            topologyOverlay?.SetVisible(
                index,
                TopologyMode &&
                TopologyDisplayMode != ReferenceTopologyDisplayMode.Rings &&
                visible &&
                sourceRenderers[index].InitiallyEnabled);
            if (notify)
            {
                RefreshSectionRings();
                StateChanged?.Invoke();
            }
        }

        private void SetMeshItemHighlighted(int index, bool highlighted, bool notify)
        {
            if (!IsValid(index, meshItems.Count) ||
                meshItems[index].Highlighted == highlighted)
            {
                return;
            }

            meshItems[index].Highlighted = highlighted;
            meshOverlay?.SetVisible(
                index,
                !TopologyMode && highlighted && meshItems[index].Visible);
            if (notify)
            {
                StateChanged?.Invoke();
            }
        }

        private void HandleImportStateChanged()
        {
            if (importController.Status == ReferenceModelImportStatus.Idle)
            {
                ClearPresentation();
                Revision++;
                StateChanged?.Invoke();
                return;
            }

            if (importController.Status != ReferenceModelImportStatus.Ready ||
                importController.Current?.Root == null ||
                importController.Current.Root == currentRoot)
            {
                return;
            }

            BuildPresentation(importController.Current.Root);
        }

        private void BuildPresentation(GameObject root)
        {
            ClearPresentation();
            currentRoot = root;

            var renderers = CollectMeshRenderers(root);
            sourceRenderers = new SourceRenderer[renderers.Count];
            sourceInitiallyEnabled = new bool[renderers.Count];
            for (var index = 0; index < renderers.Count; index++)
            {
                var renderer = renderers[index];
                sourceRenderers[index] = new SourceRenderer(renderer);
                sourceInitiallyEnabled[index] =
                    sourceRenderers[index].InitiallyEnabled;
                meshItems.Add(new ReferenceModelPartState(
                    renderer.name,
                    BuildPath(root.transform, renderer.transform)));
            }

            var skeletonProvider =
                importController.Current as IReferenceModelSkeletonProvider;
            CollectBones(
                root,
                renderers,
                skeletonProvider,
                out var bones,
                out var parentIndices,
                out var bodyParentIndices,
                out var boneNames,
                out var bodyBoneFlags);
            for (var index = 0; index < bones.Length; index++)
            {
                boneItems.Add(new ReferenceModelPartState(
                    boneNames[index],
                    BuildPath(root.transform, bones[index]),
                    bodyBoneFlags[index]));
            }

            SupportsBodyBoneView = Any(boneItems, item => item.IsBodyBone);
            BodyBonesOnly = SupportsBodyBoneView;

            var shader = Resources.Load<Shader>("BodyEditorOverlay");
            if (shader == null)
            {
                Debug.LogWarning("Body Editor overlay shader was not found.", this);
            }
            else
            {
                meshOverlay = new MeshHighlightOverlay(
                    renderers,
                    shader,
                    MeshHighlightColor);
                topologyOverlay = new ReferenceMeshTopologyOverlay(
                    renderers,
                    shader,
                    MeshTopologyColor);
                sectionRingOverlay = new ReferenceSectionRingOverlay(
                    transform,
                    renderers,
                    skeletonProvider,
                    shader,
                    SectionRingColor);
                sectionFieldOverlay = new BodySectionFieldOverlay(
                    transform,
                    shader,
                    SectionFieldColor);
                skeletonOverlay = new SkeletonOverlay(
                    transform,
                    bones,
                    parentIndices,
                    bodyParentIndices,
                    boneItems,
                    shader,
                    SkeletonColor,
                    SkeletonHighlightColor);
                skeletonOverlay.SetBodyBonesOnly(BodyBonesOnly);
                ApplyTopologyMode();
            }

            Revision++;
            StateChanged?.Invoke();
        }

        private void ClearPresentation()
        {
            for (var index = 0; index < sourceRenderers.Length; index++)
            {
                sourceRenderers[index].Restore();
            }

            meshOverlay?.Dispose();
            topologyOverlay?.Dispose();
            sectionRingOverlay?.Dispose();
            sectionFieldOverlay?.Dispose();
            skeletonOverlay?.Dispose();
            meshOverlay = null;
            topologyOverlay = null;
            sectionRingOverlay = null;
            sectionFieldOverlay = null;
            skeletonOverlay = null;
            sectionField = BodySectionField.Empty;
            sourceRenderers = Array.Empty<SourceRenderer>();
            sourceInitiallyEnabled = Array.Empty<bool>();
            meshItems.Clear();
            boneItems.Clear();
            SupportsBodyBoneView = false;
            BodyBonesOnly = false;
            TopologyMode = false;
            TopologyDisplayMode = ReferenceTopologyDisplayMode.Edges;
            currentRoot = null;
        }

        private void ApplyTopologyMode()
        {
            for (var index = 0; index < meshItems.Count; index++)
            {
                var visible = meshItems[index].Visible &&
                              sourceRenderers[index].InitiallyEnabled;
                sourceRenderers[index].ApplyVisibility(
                    !TopologyMode && meshItems[index].Visible);
                meshOverlay?.SetVisible(
                    index,
                    !TopologyMode && visible && meshItems[index].Highlighted);
                topologyOverlay?.SetVisible(
                    index,
                    TopologyMode &&
                    TopologyDisplayMode != ReferenceTopologyDisplayMode.Rings &&
                    visible);
            }

            sectionRingOverlay?.SetVisible(
                TopologyMode &&
                TopologyDisplayMode != ReferenceTopologyDisplayMode.Edges);
            sectionFieldOverlay?.SetVisible(
                TopologyMode &&
                TopologyDisplayMode != ReferenceTopologyDisplayMode.Edges);
            skeletonOverlay?.SetVisible(!TopologyMode);
        }

        private void RefreshSectionRings()
        {
            if (sectionRingOverlay == null ||
                TopologyDisplayMode == ReferenceTopologyDisplayMode.Edges)
            {
                return;
            }

            sectionRingOverlay.Rebuild(
                meshItems,
                sourceInitiallyEnabled,
                SectionRingCount);
            sectionField = BodySectionFieldBuilder.Build(
                sectionRingOverlay.Rings,
                sectionRingOverlay.JointPatches);
            sectionFieldOverlay?.Rebuild(sectionField);
        }

        private static List<Renderer> CollectMeshRenderers(GameObject root)
        {
            var result = new List<Renderer>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer is SkinnedMeshRenderer skinned &&
                    skinned.sharedMesh != null)
                {
                    result.Add(renderer);
                }
                else if (renderer is MeshRenderer &&
                         renderer.GetComponent<MeshFilter>()?.sharedMesh != null)
                {
                    result.Add(renderer);
                }
            }

            return result;
        }

        private static void CollectBones(
            GameObject root,
            IReadOnlyList<Renderer> renderers,
            IReferenceModelSkeletonProvider provider,
            out Transform[] bones,
            out int[] parentIndices,
            out int[] bodyParentIndices,
            out string[] names,
            out bool[] bodyBoneFlags)
        {
            if (provider?.Bones != null && provider.Bones.Count > 0)
            {
                var sourceBones = provider.Bones;
                bones = new Transform[sourceBones.Count];
                parentIndices = new int[sourceBones.Count];
                bodyParentIndices = new int[sourceBones.Count];
                names = new string[sourceBones.Count];
                bodyBoneFlags = new bool[sourceBones.Count];
                for (var index = 0; index < sourceBones.Count; index++)
                {
                    var source = sourceBones[index];
                    bones[index] = source.Transform;
                    names[index] = string.IsNullOrEmpty(source.Name)
                        ? source.Transform?.name ?? "Bone"
                        : source.Name;
                    parentIndices[index] = source.ParentIndex >= 0 &&
                                           source.ParentIndex < sourceBones.Count
                        ? source.ParentIndex
                        : -1;
                    bodyParentIndices[index] = source.BodyParentIndex >= 0 &&
                                               source.BodyParentIndex < sourceBones.Count
                        ? source.BodyParentIndex
                        : -1;
                    bodyBoneFlags[index] = source.IsBodyBone;
                }

                return;
            }

            var boneSet = new HashSet<Transform>();
            for (var rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                if (!(renderers[rendererIndex] is SkinnedMeshRenderer skinned))
                {
                    continue;
                }

                var rendererBones = skinned.bones;
                for (var boneIndex = 0; boneIndex < rendererBones.Length; boneIndex++)
                {
                    if (rendererBones[boneIndex] != null)
                    {
                        boneSet.Add(rendererBones[boneIndex]);
                    }
                }
            }

            var ordered = new List<Transform>(boneSet.Count);
            var hierarchy = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < hierarchy.Length; index++)
            {
                if (boneSet.Contains(hierarchy[index]))
                {
                    ordered.Add(hierarchy[index]);
                }
            }

            bones = ordered.ToArray();
            parentIndices = new int[bones.Length];
            bodyParentIndices = new int[bones.Length];
            names = new string[bones.Length];
            bodyBoneFlags = new bool[bones.Length];
            var indexByTransform = new Dictionary<Transform, int>(bones.Length);
            for (var index = 0; index < bones.Length; index++)
            {
                indexByTransform[bones[index]] = index;
                names[index] = bones[index].name;
            }

            for (var index = 0; index < bones.Length; index++)
            {
                var parent = bones[index].parent;
                while (parent != null && !indexByTransform.ContainsKey(parent))
                {
                    parent = parent.parent;
                }

                parentIndices[index] = parent != null
                    ? indexByTransform[parent]
                    : -1;
                bodyParentIndices[index] = -1;
            }
        }

        private static string BuildPath(Transform root, Transform item)
        {
            var names = new Stack<string>();
            var current = item;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static bool IsValid(int index, int count)
        {
            return index >= 0 && index < count;
        }

        private static bool All(
            IReadOnlyList<ReferenceModelPartState> items,
            Func<ReferenceModelPartState, bool> predicate,
            bool emptyValue)
        {
            if (items.Count == 0)
            {
                return emptyValue;
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (!predicate(items[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Any(
            IReadOnlyList<ReferenceModelPartState> items,
            Func<ReferenceModelPartState, bool> predicate)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (predicate(items[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Mixed(
            IReadOnlyList<ReferenceModelPartState> items,
            Func<ReferenceModelPartState, bool> selector)
        {
            if (items.Count < 2)
            {
                return false;
            }

            var first = selector(items[0]);
            for (var index = 1; index < items.Count; index++)
            {
                if (selector(items[index]) != first)
                {
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<ReferenceModelPartState> VisibleBoneItems()
        {
            if (!BodyBonesOnly)
            {
                return boneItems;
            }

            var result = new List<ReferenceModelPartState>();
            for (var index = 0; index < boneItems.Count; index++)
            {
                if (boneItems[index].IsBodyBone)
                {
                    result.Add(boneItems[index]);
                }
            }

            return result;
        }

        private bool AllBones(
            Func<ReferenceModelPartState, bool> predicate,
            bool emptyValue)
        {
            return All(VisibleBoneItems(), predicate, emptyValue);
        }

        private bool MixedBones(Func<ReferenceModelPartState, bool> selector)
        {
            return Mixed(VisibleBoneItems(), selector);
        }

        private void OnDisable()
        {
            if (importController != null)
            {
                importController.StateChanged -= HandleImportStateChanged;
            }

            ClearPresentation();
        }

        private sealed class SourceRenderer
        {
            private readonly Renderer renderer;
            private readonly bool initiallyEnabled;

            public SourceRenderer(Renderer renderer)
            {
                this.renderer = renderer;
                initiallyEnabled = renderer.enabled;
            }

            public bool InitiallyEnabled => initiallyEnabled;

            public void ApplyVisibility(bool visible)
            {
                if (renderer != null)
                {
                    renderer.enabled = visible && initiallyEnabled;
                }
            }

            public void Restore()
            {
                if (renderer != null)
                {
                    renderer.enabled = initiallyEnabled;
                }
            }
        }
    }
}
