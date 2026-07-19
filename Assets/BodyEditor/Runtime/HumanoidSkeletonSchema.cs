using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor
{
    public readonly struct HumanoidBoneDefinition
    {
        public HumanoidBoneDefinition(
            HumanBodyBones bone,
            HumanBodyBones? parent,
            Vector3 localPosition)
        {
            Bone = bone;
            Parent = parent;
            LocalPosition = localPosition;
        }

        public HumanBodyBones Bone { get; }

        public HumanBodyBones? Parent { get; }

        public Vector3 LocalPosition { get; }
    }

    public static class HumanoidSkeletonSchema
    {
        private static readonly IReadOnlyList<HumanoidBoneDefinition> definitions =
            Array.AsReadOnly(new[]
            {
                new HumanoidBoneDefinition(HumanBodyBones.Hips, null, new Vector3(0f, 1f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.Spine, HumanBodyBones.Hips, new Vector3(0f, 0.12f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.Chest, HumanBodyBones.Spine, new Vector3(0f, 0.16f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.UpperChest, HumanBodyBones.Chest, new Vector3(0f, 0.18f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.Neck, HumanBodyBones.UpperChest, new Vector3(0f, 0.16f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.Head, HumanBodyBones.Neck, new Vector3(0f, 0.1f, 0f)),

                new HumanoidBoneDefinition(HumanBodyBones.LeftShoulder, HumanBodyBones.UpperChest, new Vector3(-0.1f, 0.1f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftShoulder, new Vector3(-0.12f, 0f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, new Vector3(-0.28f, 0f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, new Vector3(-0.25f, 0f, 0f)),

                new HumanoidBoneDefinition(HumanBodyBones.RightShoulder, HumanBodyBones.UpperChest, new Vector3(0.1f, 0.1f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.RightUpperArm, HumanBodyBones.RightShoulder, new Vector3(0.12f, 0f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, new Vector3(0.28f, 0f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, new Vector3(0.25f, 0f, 0f)),

                new HumanoidBoneDefinition(HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, new Vector3(-0.09f, -0.1f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, new Vector3(0f, -0.42f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, new Vector3(0f, -0.42f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot, new Vector3(0f, -0.06f, 0.16f)),

                new HumanoidBoneDefinition(HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, new Vector3(0.09f, -0.1f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, new Vector3(0f, -0.42f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, new Vector3(0f, -0.42f, 0f)),
                new HumanoidBoneDefinition(HumanBodyBones.RightToes, HumanBodyBones.RightFoot, new Vector3(0f, -0.06f, 0.16f)),
            });

        private static readonly IReadOnlyList<HumanBodyBones> requiredBones =
            Array.AsReadOnly(new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
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
            });

        public static IReadOnlyList<HumanoidBoneDefinition> DefaultDefinitions => definitions;

        public static IReadOnlyList<HumanBodyBones> RequiredBones => requiredBones;

        public static bool TryGetDefinition(
            HumanBodyBones bone,
            out HumanoidBoneDefinition definition)
        {
            for (var index = 0; index < definitions.Count; index++)
            {
                if (definitions[index].Bone != bone)
                {
                    continue;
                }

                definition = definitions[index];
                return true;
            }

            definition = default;
            return false;
        }
    }
}
