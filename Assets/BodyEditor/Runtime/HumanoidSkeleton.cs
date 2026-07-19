using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor
{
    [Serializable]
    public struct HumanoidBoneReference
    {
        [SerializeField]
        private HumanBodyBones bone;

        [SerializeField]
        private Transform boneTransform;

        public HumanoidBoneReference(HumanBodyBones bone, Transform boneTransform)
        {
            this.bone = bone;
            this.boneTransform = boneTransform;
        }

        public HumanBodyBones Bone => bone;

        public Transform Transform => boneTransform;
    }

    [DisallowMultipleComponent]
    public sealed class HumanoidSkeleton : MonoBehaviour
    {
        [SerializeField]
        private List<HumanoidBoneReference> bones = new List<HumanoidBoneReference>();

        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField, Min(0.001f)]
        private float jointGizmoRadius = 0.012f;

        private Dictionary<HumanBodyBones, Transform> boneLookup;

        public IReadOnlyList<HumanoidBoneReference> Bones => bones;

        public int BoneCount => bones.Count;

        public void SetBones(IEnumerable<HumanoidBoneReference> boneReferences)
        {
            if (boneReferences == null)
            {
                throw new ArgumentNullException(nameof(boneReferences));
            }

            bones.Clear();
            bones.AddRange(boneReferences);
            RebuildLookup();
        }

        public bool TryGetBone(HumanBodyBones bone, out Transform boneTransform)
        {
            EnsureLookup();
            return boneLookup.TryGetValue(bone, out boneTransform);
        }

        public Transform GetBone(HumanBodyBones bone)
        {
            if (TryGetBone(bone, out var boneTransform))
            {
                return boneTransform;
            }

            throw new KeyNotFoundException($"The skeleton does not contain {bone}.");
        }

        public bool Validate(List<string> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            errors.Clear();

            var seenBones = new HashSet<HumanBodyBones>();
            var seenTransforms = new HashSet<Transform>();

            for (var index = 0; index < bones.Count; index++)
            {
                var reference = bones[index];

                if (reference.Bone == HumanBodyBones.LastBone)
                {
                    errors.Add($"Entry {index} uses the LastBone sentinel.");
                    continue;
                }

                if (!seenBones.Add(reference.Bone))
                {
                    errors.Add($"Bone {reference.Bone} is mapped more than once.");
                }

                if (reference.Transform == null)
                {
                    errors.Add($"Bone {reference.Bone} has no Transform.");
                    continue;
                }

                if (!seenTransforms.Add(reference.Transform))
                {
                    errors.Add($"Transform {reference.Transform.name} is mapped more than once.");
                }

                if (!reference.Transform.IsChildOf(transform))
                {
                    errors.Add($"Bone {reference.Bone} is outside the skeleton hierarchy.");
                }
            }

            for (var index = 0; index < HumanoidSkeletonSchema.RequiredBones.Count; index++)
            {
                var requiredBone = HumanoidSkeletonSchema.RequiredBones[index];
                if (!seenBones.Contains(requiredBone))
                {
                    errors.Add($"Required bone {requiredBone} is missing.");
                }
            }

            EnsureLookup();

            for (var index = 0; index < HumanoidSkeletonSchema.DefaultDefinitions.Count; index++)
            {
                var definition = HumanoidSkeletonSchema.DefaultDefinitions[index];
                if (!definition.Parent.HasValue ||
                    !boneLookup.TryGetValue(definition.Bone, out var child) ||
                    !boneLookup.TryGetValue(definition.Parent.Value, out var parent))
                {
                    continue;
                }

                if (child == parent || !child.IsChildOf(parent))
                {
                    errors.Add($"Bone {definition.Bone} is not below {definition.Parent.Value}.");
                }
            }

            return errors.Count == 0;
        }

        [ContextMenu("Validate Skeleton")]
        private void ValidateAndLog()
        {
            var errors = new List<string>();
            if (Validate(errors))
            {
                Debug.Log($"{name} is a valid humanoid skeleton.", this);
                return;
            }

            Debug.LogError(string.Join("\n", errors), this);
        }

        private void OnValidate()
        {
            jointGizmoRadius = Mathf.Max(0.001f, jointGizmoRadius);
            RebuildLookup();
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
            {
                return;
            }

            EnsureLookup();
            Gizmos.color = new Color(0.1f, 0.75f, 0.85f, 1f);

            for (var index = 0; index < HumanoidSkeletonSchema.DefaultDefinitions.Count; index++)
            {
                var definition = HumanoidSkeletonSchema.DefaultDefinitions[index];
                if (!boneLookup.TryGetValue(definition.Bone, out var boneTransform) || boneTransform == null)
                {
                    continue;
                }

                Gizmos.DrawSphere(boneTransform.position, jointGizmoRadius);

                if (definition.Parent.HasValue &&
                    boneLookup.TryGetValue(definition.Parent.Value, out var parentTransform) &&
                    parentTransform != null)
                {
                    Gizmos.DrawLine(parentTransform.position, boneTransform.position);
                }
            }
        }

        private void EnsureLookup()
        {
            if (boneLookup == null || boneLookup.Count != bones.Count)
            {
                RebuildLookup();
            }
        }

        private void RebuildLookup()
        {
            if (boneLookup == null)
            {
                boneLookup = new Dictionary<HumanBodyBones, Transform>(bones.Count);
            }
            else
            {
                boneLookup.Clear();
            }

            for (var index = 0; index < bones.Count; index++)
            {
                var reference = bones[index];
                if (reference.Bone == HumanBodyBones.LastBone ||
                    reference.Transform == null ||
                    boneLookup.ContainsKey(reference.Bone))
                {
                    continue;
                }

                boneLookup.Add(reference.Bone, reference.Transform);
            }
        }
    }
}
