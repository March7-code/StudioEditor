using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.Characters
{
    public readonly struct CharacterMorphPair
    {
        public CharacterMorphPair(int closedIndex, int openIndex)
        {
            ClosedIndex = closedIndex;
            OpenIndex = openIndex;
        }

        public int ClosedIndex { get; }

        public int OpenIndex { get; }
    }

    public sealed class CharacterMorphTarget
    {
        public CharacterMorphTarget(
            SkinnedMeshRenderer renderer,
            IReadOnlyList<CharacterMorphPair> patterns)
        {
            Renderer = renderer != null
                ? renderer
                : throw new ArgumentNullException(nameof(renderer));
            if (patterns == null || patterns.Count == 0)
            {
                throw new ArgumentException(
                    "A mouth target requires at least one pattern.",
                    nameof(patterns));
            }

            var values = new CharacterMorphPair[patterns.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = patterns[index];
            }

            Patterns = Array.AsReadOnly(values);
            var affected = new HashSet<int>();
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index].ClosedIndex >= 0)
                {
                    affected.Add(values[index].ClosedIndex);
                }

                if (values[index].OpenIndex >= 0)
                {
                    affected.Add(values[index].OpenIndex);
                }
            }

            var affectedValues = new int[affected.Count];
            affected.CopyTo(affectedValues);
            AffectedIndices = Array.AsReadOnly(affectedValues);
        }

        public SkinnedMeshRenderer Renderer { get; }

        public IReadOnlyList<CharacterMorphPair> Patterns { get; }

        public IReadOnlyList<int> AffectedIndices { get; }
    }

    public abstract class CharacterPairedMorphController : MonoBehaviour
    {
        private CharacterMorphTarget[] targets =
            Array.Empty<CharacterMorphTarget>();
        private string[] patternNames = Array.Empty<string>();
        private int previousPattern;
        private int pattern;
        private float openRate;
        private float openMin;
        private float openMax = 1f;
        private float maximumOpenMax = 1f;
        private float fixedOpenRate = -1f;
        private float transition = 1f;

        public int PatternCount => patternNames.Length;

        public int Pattern => pattern;

        public float OpenRate => fixedOpenRate >= 0f
            ? fixedOpenRate
            : openRate;

        public float OpenMax => openMax;

        public float BlendDuration { get; set; } = 0.15f;

        protected void ConfigureMorphs(
            IReadOnlyList<CharacterMorphTarget> requestedTargets,
            IReadOnlyList<string> requestedPatternNames,
            float initialOpenRate,
            string patternLabel,
            float requestedOpenMin,
            float requestedOpenMax)
        {
            if (requestedTargets == null || requestedTargets.Count == 0)
            {
                throw new ArgumentException(
                    "At least one paired morph target is required.",
                    nameof(requestedTargets));
            }

            var count = int.MaxValue;
            targets = new CharacterMorphTarget[requestedTargets.Count];
            for (var index = 0; index < targets.Length; index++)
            {
                targets[index] = requestedTargets[index] ??
                    throw new ArgumentException(
                        "A paired morph target cannot be null.",
                        nameof(requestedTargets));
                count = Mathf.Min(count, targets[index].Patterns.Count);
            }

            patternNames = new string[count];
            for (var index = 0; index < count; index++)
            {
                var requestedName = requestedPatternNames != null &&
                                    index < requestedPatternNames.Count
                    ? requestedPatternNames[index]
                    : null;
                patternNames[index] = string.IsNullOrWhiteSpace(requestedName)
                    ? $"{patternLabel} {index}"
                    : requestedName;
            }

            pattern = 0;
            previousPattern = 0;
            openRate = Mathf.Clamp01(initialOpenRate);
            if (!float.IsFinite(requestedOpenMin) ||
                !float.IsFinite(requestedOpenMax))
            {
                throw new ArgumentException(
                    "Morph open limits must be finite.");
            }

            openMin = Mathf.Clamp01(requestedOpenMin);
            maximumOpenMax = Mathf.Clamp01(requestedOpenMax);
            openMax = maximumOpenMax;
            fixedOpenRate = -1f;
            transition = 1f;
            Apply();
        }

        public string GetPatternName(int requestedPattern)
        {
            ValidatePattern(requestedPattern);
            return patternNames[requestedPattern];
        }

        public void SetPattern(int requestedPattern, bool blend = true)
        {
            ValidatePattern(requestedPattern);
            if (pattern == requestedPattern)
            {
                return;
            }

            previousPattern = pattern;
            pattern = requestedPattern;
            transition = blend && BlendDuration > 0f ? 0f : 1f;
            Apply();
        }

        public void SetOpenRate(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException(
                    "Morph open rate must be finite.",
                    nameof(value));
            }

            openRate = Mathf.Clamp01(value);
            fixedOpenRate = -1f;
            Apply();
        }

        protected void SetFixedOpenRateInternal(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException(
                    "Morph fixed open rate must be finite.",
                    nameof(value));
            }

            fixedOpenRate = Mathf.Clamp01(value);
            Apply();
        }

        public void SetOpenMax(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException(
                    "Morph open maximum must be finite.",
                    nameof(value));
            }

            openMax = Mathf.Clamp(value, 0f, maximumOpenMax);
            Apply();
        }

        protected virtual void LateUpdate()
        {
            if (transition < 1f)
            {
                transition = BlendDuration <= 0f
                    ? 1f
                    : Mathf.Min(1f, transition + Time.deltaTime / BlendDuration);
            }

            Apply();
        }

        private void Apply()
        {
            if (targets.Length == 0)
            {
                return;
            }

            for (var targetIndex = 0;
                 targetIndex < targets.Length;
                 targetIndex++)
            {
                var target = targets[targetIndex];
                if (target.Renderer == null)
                {
                    continue;
                }

                for (var index = 0;
                     index < target.AffectedIndices.Count;
                     index++)
                {
                    target.Renderer.SetBlendShapeWeight(
                        target.AffectedIndices[index],
                        0f);
                }

                var currentWeight = Mathf.Clamp01(transition);
                var effectiveOpenRate = fixedOpenRate >= 0f
                    ? fixedOpenRate
                    : Mathf.Lerp(openMin, openMax, openRate);
                ApplyPattern(
                    target,
                    pattern,
                    currentWeight,
                    effectiveOpenRate);
                if (currentWeight < 1f)
                {
                    ApplyPattern(
                        target,
                        previousPattern,
                        1f - currentWeight,
                        effectiveOpenRate);
                }
            }
        }

        private void ApplyPattern(
            CharacterMorphTarget target,
            int requestedPattern,
            float patternWeight,
            float effectiveOpenRate)
        {
            if (requestedPattern < 0 ||
                requestedPattern >= target.Patterns.Count ||
                patternWeight <= 0f)
            {
                return;
            }

            var pair = target.Patterns[requestedPattern];
            AddWeight(
                target.Renderer,
                pair.ClosedIndex,
                (1f - effectiveOpenRate) * patternWeight * 100f);
            AddWeight(
                target.Renderer,
                pair.OpenIndex,
                effectiveOpenRate * patternWeight * 100f);
        }

        private static void AddWeight(
            SkinnedMeshRenderer renderer,
            int index,
            float value)
        {
            if (index < 0 || renderer.sharedMesh == null ||
                index >= renderer.sharedMesh.blendShapeCount)
            {
                return;
            }

            renderer.SetBlendShapeWeight(
                index,
                renderer.GetBlendShapeWeight(index) + value);
        }

        private void ValidatePattern(int requestedPattern)
        {
            if (requestedPattern < 0 || requestedPattern >= PatternCount)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedPattern));
            }
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30100)]
    public sealed class CharacterEyebrowController :
        CharacterPairedMorphController,
        ICharacterEyebrowController
    {
        public void Configure(
            IReadOnlyList<CharacterMorphTarget> requestedTargets,
            IReadOnlyList<string> requestedPatternNames = null,
            float openMin = 0f,
            float openMax = 1f)
        {
            ConfigureMorphs(
                requestedTargets,
                requestedPatternNames,
                1f,
                "Eyebrow",
                openMin,
                openMax);
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30100)]
    public sealed class CharacterMouthController :
        CharacterPairedMorphController,
        ICharacterMouthController
    {
        public void Configure(
            IReadOnlyList<CharacterMorphTarget> requestedTargets,
            IReadOnlyList<string> requestedPatternNames = null,
            float openMin = 0f,
            float openMax = 1f)
        {
            ConfigureMorphs(
                requestedTargets,
                requestedPatternNames,
                0f,
                "Mouth",
                openMin,
                openMax);
        }

        public void SetFixedOpenRate(float value)
        {
            SetFixedOpenRateInternal(value);
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30100)]
    public sealed class CharacterEyeOpenController :
        CharacterPairedMorphController,
        ICharacterEyeOpenController
    {
        public void Configure(
            IReadOnlyList<CharacterMorphTarget> requestedTargets,
            IReadOnlyList<string> requestedPatternNames = null,
            float openMin = 0f,
            float openMax = 1f)
        {
            ConfigureMorphs(
                requestedTargets,
                requestedPatternNames,
                1f,
                "Eyes",
                openMin,
                openMax);
        }
    }

    public readonly struct CharacterHandBonePose
    {
        public CharacterHandBonePose(int boneIndex, Quaternion localRotation)
        {
            BoneIndex = boneIndex;
            LocalRotation = localRotation;
        }

        public int BoneIndex { get; }

        public Quaternion LocalRotation { get; }
    }

    public sealed class CharacterHandPose
    {
        public CharacterHandPose(
            string name,
            IReadOnlyList<CharacterHandBonePose> bones)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Hand pose" : name;
            if (bones == null)
            {
                throw new ArgumentNullException(nameof(bones));
            }

            var values = new CharacterHandBonePose[bones.Count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = bones[index];
            }

            Bones = Array.AsReadOnly(values);
        }

        public string Name { get; }

        public IReadOnlyList<CharacterHandBonePose> Bones { get; }
    }

    [DisallowMultipleComponent]
    public sealed class CharacterHandPoseController :
        MonoBehaviour,
        ICharacterHandPoseController,
        ICharacterPoseModifier
    {
        private IReadOnlyList<CharacterHandPose>[] poses =
            new IReadOnlyList<CharacterHandPose>[2];
        private readonly int[] selected = { -1, -1 };
        private readonly float[] weights = { 1f, 1f };
        private ICharacterPosePipeline coordinator;

        public void Configure(
            ICharacterPosePipeline requestedCoordinator,
            IReadOnlyList<CharacterHandPose> left,
            IReadOnlyList<CharacterHandPose> right)
        {
            if (requestedCoordinator == null)
            {
                throw new ArgumentNullException(nameof(requestedCoordinator));
            }

            coordinator?.UnregisterModifier(this);
            coordinator = requestedCoordinator;
            poses[0] = left ?? Array.Empty<CharacterHandPose>();
            poses[1] = right ?? Array.Empty<CharacterHandPose>();
            selected[0] = -1;
            selected[1] = -1;
            weights[0] = 1f;
            weights[1] = 1f;
            coordinator.RegisterModifier(this);
        }

        public int Order => CharacterPoseStages.ActionEditing;

        public bool Enabled { get; set; } = true;

        public int GetPoseCount(CharacterHand hand)
        {
            return poses[Index(hand)].Count;
        }

        public int GetPose(CharacterHand hand)
        {
            return selected[Index(hand)];
        }

        public float GetWeight(CharacterHand hand)
        {
            return weights[Index(hand)];
        }

        public string GetPoseName(CharacterHand hand, int pose)
        {
            var handIndex = Index(hand);
            ValidatePose(handIndex, pose);
            return poses[handIndex][pose].Name;
        }

        public void SetPose(CharacterHand hand, int pose, float weight = 1f)
        {
            var handIndex = Index(hand);
            if (pose < -1 || pose >= poses[handIndex].Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pose));
            }

            if (!float.IsFinite(weight))
            {
                throw new ArgumentException(
                    "Hand pose weight must be finite.",
                    nameof(weight));
            }

            selected[handIndex] = pose;
            weights[handIndex] = Mathf.Clamp01(weight);
        }

        public void ClearPose(CharacterHand hand)
        {
            selected[Index(hand)] = -1;
        }

        public void Evaluate(CharacterPoseBuffer pose)
        {
            for (var handIndex = 0; handIndex < poses.Length; handIndex++)
            {
                var poseIndex = selected[handIndex];
                if (poseIndex < 0 || weights[handIndex] <= 0f)
                {
                    continue;
                }

                var handPose = poses[handIndex][poseIndex];
                for (var boneIndex = 0;
                     boneIndex < handPose.Bones.Count;
                     boneIndex++)
                {
                    var bone = handPose.Bones[boneIndex];
                    if (bone.BoneIndex < 0 || bone.BoneIndex >= pose.BoneCount)
                    {
                        continue;
                    }

                    pose.SetLocalRotation(
                        bone.BoneIndex,
                        Quaternion.Slerp(
                            pose.GetLocalRotation(bone.BoneIndex),
                            bone.LocalRotation,
                            weights[handIndex]));
                }
            }
        }

        private static int Index(CharacterHand hand)
        {
            switch (hand)
            {
                case CharacterHand.Left:
                    return 0;
                case CharacterHand.Right:
                    return 1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(hand));
            }
        }

        private void ValidatePose(int handIndex, int pose)
        {
            if (pose < 0 || pose >= poses[handIndex].Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pose));
            }
        }

        private void OnDestroy()
        {
            coordinator?.UnregisterModifier(this);
            coordinator = null;
        }
    }
}
