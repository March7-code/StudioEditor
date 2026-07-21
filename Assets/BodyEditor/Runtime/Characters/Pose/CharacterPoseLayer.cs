using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.Characters
{
    public sealed class CharacterPoseLayer : ICharacterPoseModifier
    {
        private readonly Dictionary<int, BoneOverride> overrides =
            new Dictionary<int, BoneOverride>();

        public CharacterPoseLayer(
            CharacterSkeleton skeleton,
            int order = CharacterPoseStages.ActionEditing,
            string name = null)
        {
            Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            Order = order;
            Name = name ?? "Pose Layer";
        }

        public CharacterSkeleton Skeleton { get; }

        public string Name { get; }

        public int Order { get; }

        public bool Enabled { get; set; } = true;

        public int OverrideCount => overrides.Count;

        public CharacterPoseChannels GetChannels(int boneIndex)
        {
            ValidateIndex(boneIndex);
            return overrides.TryGetValue(boneIndex, out var value)
                ? value.Channels
                : CharacterPoseChannels.None;
        }

        public void SetLocalPosition(int boneIndex, Vector3 value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            overrides.TryGetValue(boneIndex, out var current);
            current.Position = value;
            current.Channels |= CharacterPoseChannels.Position;
            overrides[boneIndex] = current;
        }

        public void SetLocalRotation(int boneIndex, Quaternion value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            overrides.TryGetValue(boneIndex, out var current);
            current.Rotation = value.normalized;
            current.Channels |= CharacterPoseChannels.Rotation;
            overrides[boneIndex] = current;
        }

        public void SetLocalScale(int boneIndex, Vector3 value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            overrides.TryGetValue(boneIndex, out var current);
            current.Scale = value;
            current.Channels |= CharacterPoseChannels.Scale;
            overrides[boneIndex] = current;
        }

        public bool TrySetLocalRotation(
            HumanBodyBones semanticBone,
            Quaternion value)
        {
            if (!Skeleton.TryGetBoneIndex(semanticBone, out var boneIndex))
            {
                return false;
            }

            SetLocalRotation(boneIndex, value);
            return true;
        }

        public bool TrySetLocalPosition(
            HumanBodyBones semanticBone,
            Vector3 value)
        {
            if (!Skeleton.TryGetBoneIndex(semanticBone, out var boneIndex))
            {
                return false;
            }

            SetLocalPosition(boneIndex, value);
            return true;
        }

        public void ClearChannels(
            int boneIndex,
            CharacterPoseChannels channels)
        {
            ValidateIndex(boneIndex);
            if (!overrides.TryGetValue(boneIndex, out var current))
            {
                return;
            }

            current.Channels &= ~channels;
            if (current.Channels == CharacterPoseChannels.None)
            {
                overrides.Remove(boneIndex);
            }
            else
            {
                overrides[boneIndex] = current;
            }
        }

        public void ClearBone(int boneIndex)
        {
            ValidateIndex(boneIndex);
            overrides.Remove(boneIndex);
        }

        public void Clear()
        {
            overrides.Clear();
        }

        public void Evaluate(CharacterPoseBuffer pose)
        {
            if (!ReferenceEquals(pose.Skeleton, Skeleton))
            {
                throw new InvalidOperationException(
                    "Pose layer and pose buffer use different skeletons.");
            }

            foreach (var pair in overrides)
            {
                var value = pair.Value;
                if ((value.Channels & CharacterPoseChannels.Position) != 0)
                {
                    pose.SetLocalPosition(pair.Key, value.Position);
                }

                if ((value.Channels & CharacterPoseChannels.Rotation) != 0)
                {
                    pose.SetLocalRotation(pair.Key, value.Rotation);
                }

                if ((value.Channels & CharacterPoseChannels.Scale) != 0)
                {
                    pose.SetLocalScale(pair.Key, value.Scale);
                }
            }
        }

        private void ValidateIndex(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= Skeleton.BoneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(boneIndex));
            }
        }

        private static void ValidateFinite(Vector3 value, string parameterName)
        {
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) ||
                !float.IsFinite(value.z))
            {
                throw new ArgumentException(
                    "Pose value must be finite.",
                    parameterName);
            }
        }

        private static void ValidateFinite(
            Quaternion value,
            string parameterName)
        {
            var squareMagnitude = value.x * value.x + value.y * value.y +
                                  value.z * value.z + value.w * value.w;
            if (!float.IsFinite(squareMagnitude) ||
                squareMagnitude < 0.00000001f)
            {
                throw new ArgumentException(
                    "Pose rotation must be finite and non-zero.",
                    parameterName);
            }
        }

        private struct BoneOverride
        {
            public CharacterPoseChannels Channels;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }
    }
}
