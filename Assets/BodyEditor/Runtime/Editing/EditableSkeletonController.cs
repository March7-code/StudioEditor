using System;
using System.Collections.Generic;
using BodyEditor.Viewport;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.Editing
{
    [DisallowMultipleComponent]
    public sealed class EditableSkeletonController : MonoBehaviour
    {
        public const int HistoryCapacity = 100;
        private static readonly IReadOnlyList<HumanoidBoneReference> EmptyBones =
            Array.Empty<HumanoidBoneReference>();

        private readonly List<PoseEdit> undoHistory = new List<PoseEdit>();
        private readonly List<PoseEdit> redoHistory = new List<PoseEdit>();

        private HumanoidSkeleton skeleton;
        private EditableSkeletonOverlay overlay;
        private Vector3[] basePose = Array.Empty<Vector3>();
        private HumanBodyBones? selectedBone;
        private bool symmetryEnabled = true;
        private bool visible;
        private bool dragging;
        private HumanBodyBones dragBone;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private Vector3[] dragStartPose;

        public event Action StateChanged;
        public event Action PoseChanged;

        public HumanoidSkeleton Skeleton => skeleton;
        public GameObject Root => skeleton != null ? skeleton.gameObject : null;
        public IReadOnlyList<HumanoidBoneReference> Bones =>
            skeleton != null ? skeleton.Bones : EmptyBones;
        public HumanBodyBones? SelectedBone => selectedBone;
        public bool SymmetryEnabled => symmetryEnabled;
        public bool Visible => visible;
        public bool CanUndo => undoHistory.Count > 0;
        public bool CanRedo => redoHistory.Count > 0;
        public int UndoCount => undoHistory.Count;
        public int RedoCount => redoHistory.Count;
        public string UndoDescription => CanUndo
            ? undoHistory[undoHistory.Count - 1].Description
            : string.Empty;
        public string RedoDescription => CanRedo
            ? redoHistory[redoHistory.Count - 1].Description
            : string.Empty;
        public bool CanReset => skeleton != null && !PoseEquals(basePose, CapturePose());

        private void OnEnable()
        {
            CreateDefaultSkeleton();
        }

        private void Start()
        {
            if (Root != null)
            {
                GetComponent<BodyEditorViewport>()?.Frame(Root);
            }
        }

        public bool TryGetJointRootPosition(
            HumanBodyBones bone,
            out Vector3 position)
        {
            if (skeleton != null && skeleton.TryGetBone(bone, out var boneTransform))
            {
                position = skeleton.transform.InverseTransformPoint(
                    boneTransform.position);
                return true;
            }

            position = default;
            return false;
        }

        public void SelectBone(HumanBodyBones? bone)
        {
            if (bone.HasValue &&
                (skeleton == null || !skeleton.TryGetBone(bone.Value, out _)))
            {
                return;
            }

            if (selectedBone == bone)
            {
                return;
            }

            selectedBone = bone;
            NotifyStateChanged(false);
        }

        public void SetSymmetryEnabled(bool enabled)
        {
            if (symmetryEnabled == enabled)
            {
                return;
            }

            symmetryEnabled = enabled;
            NotifyStateChanged(false);
        }

        public void SetVisible(bool enabled)
        {
            visible = enabled;
            if (Root != null)
            {
                Root.SetActive(enabled);
            }
        }

        public void SetJointRootPosition(
            HumanBodyBones bone,
            Vector3 rootPosition)
        {
            if (!IsFinite(rootPosition) || skeleton == null ||
                !skeleton.TryGetBone(bone, out _))
            {
                return;
            }

            var before = CapturePose();
            ApplyJointPosition(bone, rootPosition);
            CommitEdit(before, $"Move {FormatBoneName(bone)}");
        }

        public void ResetPose()
        {
            if (skeleton == null)
            {
                return;
            }

            var before = CapturePose();
            ApplyPose(basePose);
            CommitEdit(before, "Reset pose");
        }

        public void Undo()
        {
            if (!CanUndo)
            {
                return;
            }

            var editIndex = undoHistory.Count - 1;
            var edit = undoHistory[editIndex];
            undoHistory.RemoveAt(editIndex);
            ApplyPose(edit.Before);
            redoHistory.Add(edit);
            NotifyStateChanged(true);
        }

        public void Redo()
        {
            if (!CanRedo)
            {
                return;
            }

            var editIndex = redoHistory.Count - 1;
            var edit = redoHistory[editIndex];
            redoHistory.RemoveAt(editIndex);
            ApplyPose(edit.After);
            undoHistory.Add(edit);
            NotifyStateChanged(true);
        }

        public bool BeginPointerDrag(Ray ray, Vector3 viewNormal)
        {
            if (overlay == null || !overlay.TryPick(ray, out var bone))
            {
                SelectBone(null);
                return false;
            }

            SelectBone(bone);
            if (!skeleton.TryGetBone(bone, out var boneTransform))
            {
                return false;
            }

            if (viewNormal.sqrMagnitude < 0.0001f)
            {
                viewNormal = Vector3.forward;
            }

            dragPlane = new Plane(viewNormal.normalized, boneTransform.position);
            if (!dragPlane.Raycast(ray, out var distance))
            {
                return false;
            }

            dragBone = bone;
            dragOffset = boneTransform.position - ray.GetPoint(distance);
            dragStartPose = CapturePose();
            dragging = true;
            return true;
        }

        public bool UpdatePointerDrag(Ray ray)
        {
            if (!dragging || !dragPlane.Raycast(ray, out var distance))
            {
                return false;
            }

            var worldPosition = ray.GetPoint(distance) + dragOffset;
            var rootPosition = skeleton.transform.InverseTransformPoint(worldPosition);
            ApplyJointPosition(dragBone, rootPosition);
            NotifyStateChanged(true);
            return true;
        }

        public void EndPointerDrag()
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            CommitEdit(
                dragStartPose,
                $"Move {FormatBoneName(dragBone)}",
                false);
            dragStartPose = null;
        }

        private void CreateDefaultSkeleton()
        {
            DisposeSkeleton();

            skeleton = HumanoidSkeletonFactory.CreateDefault(
                transform,
                "Editable Default Skeleton");
            skeleton.gameObject.SetActive(visible);
            basePose = CapturePose();
            overlay = new EditableSkeletonOverlay(skeleton.transform, skeleton);
            overlay.Refresh(selectedBone);
            undoHistory.Clear();
            redoHistory.Clear();
            StateChanged?.Invoke();
        }

        private void ApplyJointPosition(
            HumanBodyBones bone,
            Vector3 rootPosition)
        {
            if (symmetryEnabled && IsMidlineBone(bone))
            {
                rootPosition.x = 0f;
            }

            SetJointPosition(bone, rootPosition);

            if (symmetryEnabled && TryGetMirrorBone(bone, out var mirrorBone))
            {
                SetJointPosition(
                    mirrorBone,
                    new Vector3(-rootPosition.x, rootPosition.y, rootPosition.z));
            }
        }

        private void SetJointPosition(
            HumanBodyBones bone,
            Vector3 rootPosition)
        {
            if (skeleton.TryGetBone(bone, out var boneTransform))
            {
                boneTransform.position = skeleton.transform.TransformPoint(rootPosition);
            }
        }

        private Vector3[] CapturePose()
        {
            if (skeleton == null)
            {
                return Array.Empty<Vector3>();
            }

            var pose = new Vector3[HumanoidSkeletonSchema.DefaultDefinitions.Count];
            for (var index = 0; index < pose.Length; index++)
            {
                var bone = HumanoidSkeletonSchema.DefaultDefinitions[index].Bone;
                if (skeleton.TryGetBone(bone, out var boneTransform))
                {
                    pose[index] = skeleton.transform.InverseTransformPoint(
                        boneTransform.position);
                }
            }

            return pose;
        }

        private void ApplyPose(IReadOnlyList<Vector3> pose)
        {
            var count = Mathf.Min(
                pose.Count,
                HumanoidSkeletonSchema.DefaultDefinitions.Count);
            for (var index = 0; index < count; index++)
            {
                var bone = HumanoidSkeletonSchema.DefaultDefinitions[index].Bone;
                SetJointPosition(bone, pose[index]);
            }
        }

        private void CommitEdit(
            Vector3[] before,
            string description,
            bool notify = true)
        {
            var after = CapturePose();
            if (!PoseEquals(before, after))
            {
                undoHistory.Add(new PoseEdit(description, before, after));
                if (undoHistory.Count > HistoryCapacity)
                {
                    undoHistory.RemoveAt(0);
                }

                redoHistory.Clear();
            }

            if (notify)
            {
                NotifyStateChanged(true);
            }
            else
            {
                StateChanged?.Invoke();
            }
        }

        private void NotifyStateChanged(bool poseChanged)
        {
            overlay?.Refresh(selectedBone);
            if (poseChanged)
            {
                PoseChanged?.Invoke();
            }

            StateChanged?.Invoke();
        }

        private void OnDisable()
        {
            DisposeSkeleton();
            StateChanged = null;
            PoseChanged = null;
        }

        private void DisposeSkeleton()
        {
            overlay?.Dispose();
            overlay = null;
            if (skeleton != null)
            {
                DestroyRuntimeObject(skeleton.gameObject);
                skeleton = null;
            }

            basePose = Array.Empty<Vector3>();
            selectedBone = null;
            dragging = false;
            dragStartPose = null;
        }

        private static bool PoseEquals(
            IReadOnlyList<Vector3> first,
            IReadOnlyList<Vector3> second)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                return false;
            }

            for (var index = 0; index < first.Count; index++)
            {
                if ((first[index] - second[index]).sqrMagnitude > 0.0000000001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        private static string FormatBoneName(HumanBodyBones bone)
        {
            var value = bone.ToString();
            var result = new System.Text.StringBuilder(value.Length + 4);
            result.Append(value[0]);
            for (var index = 1; index < value.Length; index++)
            {
                if (char.IsUpper(value[index]) && !char.IsUpper(value[index - 1]))
                {
                    result.Append(' ');
                }

                result.Append(value[index]);
            }

            return result.ToString();
        }

        private static bool IsMidlineBone(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.Hips ||
                   bone == HumanBodyBones.Spine ||
                   bone == HumanBodyBones.Chest ||
                   bone == HumanBodyBones.UpperChest ||
                   bone == HumanBodyBones.Neck ||
                   bone == HumanBodyBones.Head;
        }

        private static bool TryGetMirrorBone(
            HumanBodyBones bone,
            out HumanBodyBones mirror)
        {
            switch (bone)
            {
                case HumanBodyBones.LeftShoulder:
                    mirror = HumanBodyBones.RightShoulder;
                    return true;
                case HumanBodyBones.RightShoulder:
                    mirror = HumanBodyBones.LeftShoulder;
                    return true;
                case HumanBodyBones.LeftUpperArm:
                    mirror = HumanBodyBones.RightUpperArm;
                    return true;
                case HumanBodyBones.RightUpperArm:
                    mirror = HumanBodyBones.LeftUpperArm;
                    return true;
                case HumanBodyBones.LeftLowerArm:
                    mirror = HumanBodyBones.RightLowerArm;
                    return true;
                case HumanBodyBones.RightLowerArm:
                    mirror = HumanBodyBones.LeftLowerArm;
                    return true;
                case HumanBodyBones.LeftHand:
                    mirror = HumanBodyBones.RightHand;
                    return true;
                case HumanBodyBones.RightHand:
                    mirror = HumanBodyBones.LeftHand;
                    return true;
                case HumanBodyBones.LeftUpperLeg:
                    mirror = HumanBodyBones.RightUpperLeg;
                    return true;
                case HumanBodyBones.RightUpperLeg:
                    mirror = HumanBodyBones.LeftUpperLeg;
                    return true;
                case HumanBodyBones.LeftLowerLeg:
                    mirror = HumanBodyBones.RightLowerLeg;
                    return true;
                case HumanBodyBones.RightLowerLeg:
                    mirror = HumanBodyBones.LeftLowerLeg;
                    return true;
                case HumanBodyBones.LeftFoot:
                    mirror = HumanBodyBones.RightFoot;
                    return true;
                case HumanBodyBones.RightFoot:
                    mirror = HumanBodyBones.LeftFoot;
                    return true;
                case HumanBodyBones.LeftToes:
                    mirror = HumanBodyBones.RightToes;
                    return true;
                case HumanBodyBones.RightToes:
                    mirror = HumanBodyBones.LeftToes;
                    return true;
                default:
                    mirror = default;
                    return false;
            }
        }

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private readonly struct PoseEdit
        {
            public PoseEdit(
                string description,
                Vector3[] before,
                Vector3[] after)
            {
                Description = description ?? string.Empty;
                Before = before;
                After = after;
            }

            public string Description { get; }
            public Vector3[] Before { get; }
            public Vector3[] After { get; }
        }
    }

    internal sealed class EditableSkeletonOverlay : IDisposable
    {
        private const float HandleDiameter = 0.045f;
        private const float SelectedHandleDiameter = 0.065f;

        private readonly HumanoidSkeleton skeleton;
        private readonly GameObject root;
        private readonly Mesh lineMesh;
        private readonly Material lineMaterial;
        private readonly Material handleMaterial;
        private readonly Material selectedHandleMaterial;
        private readonly List<Vector3> lineVertices = new List<Vector3>();
        private readonly Dictionary<HumanBodyBones, HandleEntry> handles =
            new Dictionary<HumanBodyBones, HandleEntry>();
        private readonly Dictionary<Collider, HumanBodyBones> bonesByCollider =
            new Dictionary<Collider, HumanBodyBones>();

        public EditableSkeletonOverlay(
            Transform parent,
            HumanoidSkeleton skeleton)
        {
            this.skeleton = skeleton;
            root = new GameObject("Editable Skeleton Overlay")
            {
                hideFlags = HideFlags.DontSave,
            };
            root.transform.SetParent(parent, false);

            var shader = Resources.Load<Shader>("BodyEditorOverlay") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("Editable skeleton overlay shader was not found.");
                return;
            }

            lineMaterial = CreateMaterial(
                shader,
                "Editable Skeleton Lines",
                new Color(0.25f, 0.72f, 0.92f, 0.95f),
                4100);
            handleMaterial = CreateMaterial(
                shader,
                "Editable Skeleton Joints",
                new Color(0.2f, 0.78f, 0.62f, 1f),
                4101);
            selectedHandleMaterial = CreateMaterial(
                shader,
                "Editable Skeleton Selected Joint",
                new Color(1f, 0.67f, 0.16f, 1f),
                4102);

            var lineObject = new GameObject("Editable Skeleton Lines");
            lineObject.transform.SetParent(root.transform, false);
            lineMesh = new Mesh
            {
                name = "Editable Skeleton Line Mesh",
                hideFlags = HideFlags.DontSave,
            };
            lineObject.AddComponent<MeshFilter>().sharedMesh = lineMesh;
            ConfigureRenderer(
                lineObject.AddComponent<MeshRenderer>(),
                lineMaterial);

            for (var index = 0;
                 index < HumanoidSkeletonSchema.DefaultDefinitions.Count;
                 index++)
            {
                var bone = HumanoidSkeletonSchema.DefaultDefinitions[index].Bone;
                var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handle.name = bone + " Handle";
                handle.hideFlags = HideFlags.DontSave;
                handle.transform.SetParent(root.transform, false);
                var renderer = handle.GetComponent<MeshRenderer>();
                ConfigureRenderer(renderer, handleMaterial);
                var collider = handle.GetComponent<Collider>();
                handles.Add(bone, new HandleEntry(handle.transform, renderer));
                bonesByCollider.Add(collider, bone);
            }
        }

        public bool TryPick(Ray ray, out HumanBodyBones bone)
        {
            var hits = Physics.RaycastAll(ray, float.PositiveInfinity);
            var closestDistance = float.PositiveInfinity;
            var found = false;
            bone = default;
            for (var index = 0; index < hits.Length; index++)
            {
                if (hits[index].distance >= closestDistance ||
                    !bonesByCollider.TryGetValue(hits[index].collider, out var hitBone))
                {
                    continue;
                }

                closestDistance = hits[index].distance;
                bone = hitBone;
                found = true;
            }

            return found;
        }

        public void Refresh(HumanBodyBones? selectedBone)
        {
            if (root == null || skeleton == null)
            {
                return;
            }

            lineVertices.Clear();
            for (var index = 0;
                 index < HumanoidSkeletonSchema.DefaultDefinitions.Count;
                 index++)
            {
                var definition = HumanoidSkeletonSchema.DefaultDefinitions[index];
                if (!skeleton.TryGetBone(definition.Bone, out var boneTransform))
                {
                    continue;
                }

                if (handles.TryGetValue(definition.Bone, out var handle))
                {
                    handle.Transform.position = boneTransform.position;
                    var isSelected = selectedBone == definition.Bone;
                    handle.Transform.localScale = Vector3.one *
                                                  (isSelected
                                                      ? SelectedHandleDiameter
                                                      : HandleDiameter);
                    handle.Renderer.sharedMaterial = isSelected
                        ? selectedHandleMaterial
                        : handleMaterial;
                }

                if (!definition.Parent.HasValue ||
                    !skeleton.TryGetBone(definition.Parent.Value, out var parentTransform))
                {
                    continue;
                }

                lineVertices.Add(root.transform.InverseTransformPoint(
                    parentTransform.position));
                lineVertices.Add(root.transform.InverseTransformPoint(
                    boneTransform.position));
            }

            if (lineMesh != null)
            {
                lineMesh.Clear(false);
                lineMesh.SetVertices(lineVertices);
                var indices = new int[lineVertices.Count];
                for (var index = 0; index < indices.Length; index++)
                {
                    indices[index] = index;
                }

                lineMesh.SetIndices(indices, MeshTopology.Lines, 0);
            }
        }

        public void Dispose()
        {
            DestroyObject(root);
            DestroyObject(lineMesh);
            DestroyObject(lineMaterial);
            DestroyObject(handleMaterial);
            DestroyObject(selectedHandleMaterial);
        }

        private static Material CreateMaterial(
            Shader shader,
            string name,
            Color color,
            int renderQueue)
        {
            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                renderQueue = renderQueue,
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static void ConfigureRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        private readonly struct HandleEntry
        {
            public HandleEntry(Transform transform, MeshRenderer renderer)
            {
                Transform = transform;
                Renderer = renderer;
            }

            public Transform Transform { get; }
            public MeshRenderer Renderer { get; }
        }
    }
}
