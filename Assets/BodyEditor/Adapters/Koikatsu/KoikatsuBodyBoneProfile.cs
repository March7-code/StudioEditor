using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuBodyBoneProfile
    {
        private static readonly BodyBoneRule[] rules =
        {
            Rule("cf_j_hips", null, HumanBodyBones.Hips),
            Rule("cf_j_spine01", "cf_j_hips", HumanBodyBones.Spine),
            Rule("cf_j_spine02", "cf_j_spine01", HumanBodyBones.Chest),
            Rule("cf_j_spine03", "cf_j_spine02", HumanBodyBones.UpperChest),
            Rule("cf_j_neck", "cf_j_spine03", HumanBodyBones.Neck),
            Rule("cf_j_head", "cf_j_neck", HumanBodyBones.Head),
            Rule("cf_j_shoulder_L", "cf_j_spine03", HumanBodyBones.LeftShoulder),
            Rule("cf_j_arm00_L", "cf_j_shoulder_L", HumanBodyBones.LeftUpperArm),
            Rule("cf_j_forearm01_L", "cf_j_arm00_L", HumanBodyBones.LeftLowerArm),
            Rule("cf_j_hand_L", "cf_j_forearm01_L", HumanBodyBones.LeftHand),
            Rule("cf_j_shoulder_R", "cf_j_spine03", HumanBodyBones.RightShoulder),
            Rule("cf_j_arm00_R", "cf_j_shoulder_R", HumanBodyBones.RightUpperArm),
            Rule("cf_j_forearm01_R", "cf_j_arm00_R", HumanBodyBones.RightLowerArm),
            Rule("cf_j_hand_R", "cf_j_forearm01_R", HumanBodyBones.RightHand),
            Rule("cf_j_thigh00_L", "cf_j_hips", HumanBodyBones.LeftUpperLeg),
            Rule("cf_j_leg01_L", "cf_j_thigh00_L", HumanBodyBones.LeftLowerLeg),
            Rule("cf_j_foot_L", "cf_j_leg01_L", HumanBodyBones.LeftFoot),
            Rule("cf_j_toes_L", "cf_j_foot_L", HumanBodyBones.LeftToes),
            Rule("cf_j_thigh00_R", "cf_j_hips", HumanBodyBones.RightUpperLeg),
            Rule("cf_j_leg01_R", "cf_j_thigh00_R", HumanBodyBones.RightLowerLeg),
            Rule("cf_j_foot_R", "cf_j_leg01_R", HumanBodyBones.RightFoot),
            Rule("cf_j_toes_R", "cf_j_foot_R", HumanBodyBones.RightToes),
        };

        public static IReadOnlyList<ReferenceModelBone> Build(Transform bodyRoot)
        {
            var transforms = bodyRoot.GetComponentsInChildren<Transform>(true);
            var indexByTransform = new Dictionary<Transform, int>();
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < transforms.Length; index++)
            {
                indexByTransform[transforms[index]] = index;
                if (!indexByName.ContainsKey(transforms[index].name))
                {
                    indexByName.Add(transforms[index].name, index);
                }
            }

            var ruleByName = new Dictionary<string, BodyBoneRule>(
                StringComparer.Ordinal);
            for (var index = 0; index < rules.Length; index++)
            {
                ruleByName.Add(rules[index].Name, rules[index]);
            }

            var result = new ReferenceModelBone[transforms.Length];
            for (var index = 0; index < transforms.Length; index++)
            {
                var transform = transforms[index];
                var parentIndex = transform.parent != null &&
                                  indexByTransform.TryGetValue(
                                      transform.parent,
                                      out var actualParent)
                    ? actualParent
                    : -1;

                var isBodyBone = ruleByName.TryGetValue(
                    transform.name,
                    out var rule);
                var bodyParentIndex = isBodyBone && rule.ParentName != null &&
                                      indexByName.TryGetValue(
                                          rule.ParentName,
                                          out var bodyParent)
                    ? bodyParent
                    : -1;

                result[index] = new ReferenceModelBone(
                    transform.name,
                    transform,
                    parentIndex,
                    isBodyBone,
                    bodyParentIndex,
                    isBodyBone ? rule.HumanoidBone : null);
            }

            return Array.AsReadOnly(result);
        }

        public static CharacterSkeleton BuildCharacterSkeleton(
            Transform characterRoot,
            IReadOnlyList<ReferenceModelBone> sourceBones)
        {
            if (characterRoot == null)
            {
                throw new ArgumentNullException(nameof(characterRoot));
            }

            if (sourceBones == null)
            {
                throw new ArgumentNullException(nameof(sourceBones));
            }

            var semanticByTransform =
                new Dictionary<Transform, HumanBodyBones>();
            for (var index = 0; index < sourceBones.Count; index++)
            {
                var source = sourceBones[index];
                if (source.Transform != null && source.HumanoidBone.HasValue)
                {
                    semanticByTransform[source.Transform] =
                        source.HumanoidBone.Value;
                }
            }

            var transforms = characterRoot.GetComponentsInChildren<Transform>(true);
            var indicesByTransform = new Dictionary<Transform, int>(
                transforms.Length);
            for (var index = 0; index < transforms.Length; index++)
            {
                indicesByTransform[transforms[index]] = index;
            }

            var result = new CharacterBone[transforms.Length];
            for (var index = 0; index < transforms.Length; index++)
            {
                var transform = transforms[index];
                var parentIndex = transform.parent != null &&
                                  indicesByTransform.TryGetValue(
                                      transform.parent,
                                      out var resolvedParent)
                    ? resolvedParent
                    : -1;
                var semanticBone = semanticByTransform.TryGetValue(
                    transform,
                    out var resolvedSemantic)
                    ? resolvedSemantic
                    : (HumanBodyBones?)null;
                result[index] = new CharacterBone(
                    transform.name,
                    transform,
                    parentIndex,
                    semanticBone);
            }

            return new CharacterSkeleton(result);
        }

        private static BodyBoneRule Rule(
            string name,
            string parentName,
            HumanBodyBones humanoidBone)
        {
            return new BodyBoneRule(name, parentName, humanoidBone);
        }

        private readonly struct BodyBoneRule
        {
            public BodyBoneRule(
                string name,
                string parentName,
                HumanBodyBones humanoidBone)
            {
                Name = name;
                ParentName = parentName;
                HumanoidBone = humanoidBone;
            }

            public string Name { get; }

            public string ParentName { get; }

            public HumanBodyBones HumanoidBone { get; }
        }
    }
}
