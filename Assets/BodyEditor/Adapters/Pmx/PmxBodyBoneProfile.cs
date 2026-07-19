using System;
using System.Collections.Generic;
using System.Text;
using BodyEditor.ReferenceModels;
using UMT;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class PmxBodyBoneProfile
    {
        private static readonly BodyBoneRule[] Rules =
        {
            Rule(BodySlot.Hips, BodySlot.None,
                "下半身", "腰", "LowerBody", "Hips", "Pelvis"),
            Rule(BodySlot.Spine, BodySlot.Hips,
                "上半身", "UpperBody", "Spine"),
            Rule(BodySlot.Chest, new[] { BodySlot.Spine, BodySlot.Hips },
                "上半身2", "UpperBody2", "Chest"),
            Rule(BodySlot.UpperChest, new[] { BodySlot.Chest, BodySlot.Spine },
                "上半身3", "UpperBody3", "UpperChest"),
            Rule(BodySlot.Neck,
                new[] { BodySlot.UpperChest, BodySlot.Chest, BodySlot.Spine },
                "首", "Neck"),
            Rule(BodySlot.Head, BodySlot.Neck,
                "頭", "Head"),

            Rule(BodySlot.LeftShoulder,
                new[] { BodySlot.UpperChest, BodySlot.Chest, BodySlot.Spine },
                "左肩", "LeftShoulder"),
            Rule(BodySlot.LeftUpperArm,
                new[] { BodySlot.LeftShoulder, BodySlot.UpperChest, BodySlot.Chest },
                "左腕", "LeftArm", "LeftUpperArm"),
            Rule(BodySlot.LeftLowerArm, BodySlot.LeftUpperArm,
                "左ひじ", "左肘", "LeftElbow", "LeftLowerArm"),
            Rule(BodySlot.LeftHand, BodySlot.LeftLowerArm,
                "左手首", "LeftWrist", "LeftHand"),

            Rule(BodySlot.RightShoulder,
                new[] { BodySlot.UpperChest, BodySlot.Chest, BodySlot.Spine },
                "右肩", "RightShoulder"),
            Rule(BodySlot.RightUpperArm,
                new[] { BodySlot.RightShoulder, BodySlot.UpperChest, BodySlot.Chest },
                "右腕", "RightArm", "RightUpperArm"),
            Rule(BodySlot.RightLowerArm, BodySlot.RightUpperArm,
                "右ひじ", "右肘", "RightElbow", "RightLowerArm"),
            Rule(BodySlot.RightHand, BodySlot.RightLowerArm,
                "右手首", "RightWrist", "RightHand"),

            Rule(BodySlot.LeftUpperLeg, BodySlot.Hips,
                "左足", "LeftLeg", "LeftUpperLeg"),
            Rule(BodySlot.LeftLowerLeg, BodySlot.LeftUpperLeg,
                "左ひざ", "左膝", "LeftKnee", "LeftLowerLeg"),
            Rule(BodySlot.LeftFoot, BodySlot.LeftLowerLeg,
                "左足首", "LeftAnkle", "LeftFoot"),
            Rule(BodySlot.LeftToes, BodySlot.LeftFoot,
                "左つま先", "左爪先", "LeftToe", "LeftToes"),

            Rule(BodySlot.RightUpperLeg, BodySlot.Hips,
                "右足", "RightLeg", "RightUpperLeg"),
            Rule(BodySlot.RightLowerLeg, BodySlot.RightUpperLeg,
                "右ひざ", "右膝", "RightKnee", "RightLowerLeg"),
            Rule(BodySlot.RightFoot, BodySlot.RightLowerLeg,
                "右足首", "RightAnkle", "RightFoot"),
            Rule(BodySlot.RightToes, BodySlot.RightFoot,
                "右つま先", "右爪先", "RightToe", "RightToes"),
        };

        public static IReadOnlyList<ReferenceModelBone> Build(PMXImportResult result)
        {
            var count = Math.Min(
                result.bones.Length,
                result.model?.bones?.Length ?? 0);
            var sourceIndexByName = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                var name = result.model.bones[index].originalName.ToString();
                var normalized = Normalize(name);
                if (!sourceIndexByName.ContainsKey(normalized))
                {
                    sourceIndexByName.Add(normalized, index);
                }
            }

            var sourceIndexBySlot = new Dictionary<BodySlot, int>();
            for (var ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
            {
                var rule = Rules[ruleIndex];
                for (var aliasIndex = 0; aliasIndex < rule.Aliases.Length; aliasIndex++)
                {
                    if (sourceIndexByName.TryGetValue(
                            Normalize(rule.Aliases[aliasIndex]),
                            out var sourceIndex))
                    {
                        sourceIndexBySlot[rule.Slot] = sourceIndex;
                        break;
                    }
                }
            }

            var humanoidBoneByIndex = new HumanBodyBones?[count];
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.LeftUpperArm,
                "左腕捩",
                "左腕捩1",
                "左腕捩2",
                "左腕捩3");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.LeftLowerArm,
                "左手捩",
                "左手捩1",
                "左手捩2",
                "左手捩3");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.RightUpperArm,
                "右腕捩",
                "右腕捩1",
                "右腕捩2",
                "右腕捩3");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.RightLowerArm,
                "右手捩",
                "右手捩1",
                "右手捩2",
                "右手捩3");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.LeftUpperLeg,
                "左足D");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.LeftLowerLeg,
                "左ひざD",
                "左膝D");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.LeftFoot,
                "左足首D");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.RightUpperLeg,
                "右足D");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.RightLowerLeg,
                "右ひざD",
                "右膝D");
            MapSkinningAliases(
                sourceIndexByName,
                humanoidBoneByIndex,
                HumanBodyBones.RightFoot,
                "右足首D");

            var bodyParentByIndex = new int[count];
            var isBodyBone = new bool[count];
            for (var index = 0; index < bodyParentByIndex.Length; index++)
            {
                bodyParentByIndex[index] = -1;
            }

            for (var ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
            {
                var rule = Rules[ruleIndex];
                if (!sourceIndexBySlot.TryGetValue(rule.Slot, out var sourceIndex))
                {
                    continue;
                }

                isBodyBone[sourceIndex] = true;
                humanoidBoneByIndex[sourceIndex] = ToHumanoidBone(rule.Slot);
                for (var parentIndex = 0;
                     parentIndex < rule.ParentCandidates.Length;
                     parentIndex++)
                {
                    if (sourceIndexBySlot.TryGetValue(
                            rule.ParentCandidates[parentIndex],
                            out var bodyParent))
                    {
                        bodyParentByIndex[sourceIndex] = bodyParent;
                        break;
                    }
                }
            }

            var values = new ReferenceModelBone[count];
            for (var index = 0; index < count; index++)
            {
                var source = result.model.bones[index];
                values[index] = new ReferenceModelBone(
                    source.originalName.ToString(),
                    result.bones[index],
                    source.parentBoneIndex,
                    isBodyBone[index],
                    bodyParentByIndex[index],
                    humanoidBoneByIndex[index]);
            }

            return Array.AsReadOnly(values);
        }

        private static BodyBoneRule Rule(
            BodySlot slot,
            BodySlot parent,
            params string[] aliases)
        {
            return new BodyBoneRule(
                slot,
                parent == BodySlot.None
                    ? Array.Empty<BodySlot>()
                    : new[] { parent },
                aliases);
        }

        private static BodyBoneRule Rule(
            BodySlot slot,
            BodySlot[] parents,
            params string[] aliases)
        {
            return new BodyBoneRule(slot, parents, aliases);
        }

        private static string Normalize(string value)
        {
            var result = new StringBuilder(value?.Length ?? 0);
            if (value == null)
            {
                return string.Empty;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!char.IsWhiteSpace(character) &&
                    character != '_' && character != '-')
                {
                    result.Append(char.ToLowerInvariant(character));
                }
            }

            return result.ToString();
        }

        private static void MapSkinningAliases(
            IReadOnlyDictionary<string, int> sourceIndexByName,
            HumanBodyBones?[] humanoidBoneByIndex,
            HumanBodyBones humanoidBone,
            params string[] aliases)
        {
            for (var index = 0; index < aliases.Length; index++)
            {
                if (sourceIndexByName.TryGetValue(
                        Normalize(aliases[index]),
                        out var sourceIndex))
                {
                    humanoidBoneByIndex[sourceIndex] = humanoidBone;
                }
            }
        }

        private static HumanBodyBones? ToHumanoidBone(BodySlot slot)
        {
            switch (slot)
            {
                case BodySlot.Hips: return HumanBodyBones.Hips;
                case BodySlot.Spine: return HumanBodyBones.Spine;
                case BodySlot.Chest: return HumanBodyBones.Chest;
                case BodySlot.UpperChest: return HumanBodyBones.UpperChest;
                case BodySlot.Neck: return HumanBodyBones.Neck;
                case BodySlot.Head: return HumanBodyBones.Head;
                case BodySlot.LeftShoulder: return HumanBodyBones.LeftShoulder;
                case BodySlot.LeftUpperArm: return HumanBodyBones.LeftUpperArm;
                case BodySlot.LeftLowerArm: return HumanBodyBones.LeftLowerArm;
                case BodySlot.LeftHand: return HumanBodyBones.LeftHand;
                case BodySlot.RightShoulder: return HumanBodyBones.RightShoulder;
                case BodySlot.RightUpperArm: return HumanBodyBones.RightUpperArm;
                case BodySlot.RightLowerArm: return HumanBodyBones.RightLowerArm;
                case BodySlot.RightHand: return HumanBodyBones.RightHand;
                case BodySlot.LeftUpperLeg: return HumanBodyBones.LeftUpperLeg;
                case BodySlot.LeftLowerLeg: return HumanBodyBones.LeftLowerLeg;
                case BodySlot.LeftFoot: return HumanBodyBones.LeftFoot;
                case BodySlot.LeftToes: return HumanBodyBones.LeftToes;
                case BodySlot.RightUpperLeg: return HumanBodyBones.RightUpperLeg;
                case BodySlot.RightLowerLeg: return HumanBodyBones.RightLowerLeg;
                case BodySlot.RightFoot: return HumanBodyBones.RightFoot;
                case BodySlot.RightToes: return HumanBodyBones.RightToes;
                default: return null;
            }
        }

        private readonly struct BodyBoneRule
        {
            public BodyBoneRule(
                BodySlot slot,
                BodySlot[] parentCandidates,
                string[] aliases)
            {
                Slot = slot;
                ParentCandidates = parentCandidates;
                Aliases = aliases;
            }

            public BodySlot Slot { get; }
            public BodySlot[] ParentCandidates { get; }
            public string[] Aliases { get; }
        }

        private enum BodySlot
        {
            None,
            Hips,
            Spine,
            Chest,
            UpperChest,
            Neck,
            Head,
            LeftShoulder,
            LeftUpperArm,
            LeftLowerArm,
            LeftHand,
            RightShoulder,
            RightUpperArm,
            RightLowerArm,
            RightHand,
            LeftUpperLeg,
            LeftLowerLeg,
            LeftFoot,
            LeftToes,
            RightUpperLeg,
            RightLowerLeg,
            RightFoot,
            RightToes,
        }
    }
}
