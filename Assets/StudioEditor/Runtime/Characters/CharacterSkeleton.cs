using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.Characters
{
    public sealed class CharacterBone
    {
        public CharacterBone(
            string name,
            Transform transform,
            int parentIndex,
            HumanBodyBones? semanticBone = null)
        {
            Name = name ?? string.Empty;
            Transform = transform;
            ParentIndex = parentIndex;
            SemanticBone = semanticBone;
        }

        public string Name { get; }

        public Transform Transform { get; }

        public int ParentIndex { get; }

        public HumanBodyBones? SemanticBone { get; }
    }

    public sealed class CharacterSkeleton
    {
        private static readonly HumanBodyBones[] bodyConstraintBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.Neck,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
        };

        private readonly IReadOnlyList<CharacterBone> bones;
        private readonly Dictionary<HumanBodyBones, CharacterBone> bonesBySemantic =
            new Dictionary<HumanBodyBones, CharacterBone>();
        private readonly Dictionary<Transform, int> indicesByTransform =
            new Dictionary<Transform, int>();

        public CharacterSkeleton(IEnumerable<CharacterBone> bones)
        {
            if (bones == null)
            {
                throw new ArgumentNullException(nameof(bones));
            }

            var values = new List<CharacterBone>();
            foreach (var bone in bones)
            {
                if (bone == null)
                {
                    throw new ArgumentException(
                        "Character skeleton contains a null bone.",
                        nameof(bones));
                }

                if (bone.Transform == null)
                {
                    throw new ArgumentException(
                        $"Character bone '{bone.Name}' has no transform.",
                        nameof(bones));
                }

                if (indicesByTransform.ContainsKey(bone.Transform))
                {
                    throw new ArgumentException(
                        $"Character transform '{bone.Transform.name}' is mapped " +
                        "more than once.",
                        nameof(bones));
                }

                if (bone.SemanticBone == HumanBodyBones.LastBone)
                {
                    throw new ArgumentException(
                        $"Character bone '{bone.Name}' uses LastBone.",
                        nameof(bones));
                }

                if (bone.SemanticBone.HasValue &&
                    bonesBySemantic.ContainsKey(bone.SemanticBone.Value))
                {
                    throw new ArgumentException(
                        $"Character skeleton maps {bone.SemanticBone.Value} " +
                        "more than once.",
                        nameof(bones));
                }

                var index = values.Count;
                values.Add(bone);
                indicesByTransform.Add(bone.Transform, index);
                if (bone.SemanticBone.HasValue)
                {
                    bonesBySemantic.Add(bone.SemanticBone.Value, bone);
                }
            }

            for (var index = 0; index < values.Count; index++)
            {
                var parentIndex = values[index].ParentIndex;
                if (parentIndex < -1 || parentIndex >= values.Count ||
                    parentIndex == index)
                {
                    throw new ArgumentException(
                        $"Character bone '{values[index].Name}' has invalid parent " +
                        $"index {parentIndex}.",
                        nameof(bones));
                }

                var ancestor = parentIndex;
                var remaining = values.Count;
                while (ancestor >= 0 && remaining > 0)
                {
                    ancestor = values[ancestor].ParentIndex;
                    remaining--;
                }

                if (ancestor >= 0)
                {
                    throw new ArgumentException(
                        $"Character bone '{values[index].Name}' has a cyclic " +
                        "parent chain.",
                        nameof(bones));
                }
            }

            this.bones = values.AsReadOnly();
        }

        public static CharacterSkeleton Empty { get; } =
            new CharacterSkeleton(Array.Empty<CharacterBone>());

        public IReadOnlyList<CharacterBone> Bones => bones;

        public int BoneCount => bones.Count;

        public int SemanticBoneCount => bonesBySemantic.Count;

        public bool SupportsBodyConstraints
        {
            get
            {
                for (var index = 0; index < bodyConstraintBones.Length; index++)
                {
                    if (!bonesBySemantic.ContainsKey(bodyConstraintBones[index]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool TryGetBone(
            HumanBodyBones semanticBone,
            out CharacterBone bone)
        {
            return bonesBySemantic.TryGetValue(semanticBone, out bone);
        }

        public bool TryGetBoneIndex(Transform transform, out int index)
        {
            if (transform != null)
            {
                return indicesByTransform.TryGetValue(transform, out index);
            }

            index = -1;
            return false;
        }

        public bool TryGetBoneIndex(
            HumanBodyBones semanticBone,
            out int index)
        {
            if (TryGetBone(semanticBone, out var bone))
            {
                return indicesByTransform.TryGetValue(bone.Transform, out index);
            }

            index = -1;
            return false;
        }

        public bool TryGetTransform(
            HumanBodyBones semanticBone,
            out Transform transform)
        {
            if (TryGetBone(semanticBone, out var bone))
            {
                transform = bone.Transform;
                return transform != null;
            }

            transform = null;
            return false;
        }
    }
}
