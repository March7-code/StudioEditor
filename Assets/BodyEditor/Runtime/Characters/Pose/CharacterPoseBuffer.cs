using System;
using UnityEngine;

namespace BodyEditor.Characters
{
    [Flags]
    public enum CharacterPoseChannels
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Scale = 1 << 2,
        All = Position | Rotation | Scale,
    }

    public sealed class CharacterPoseBuffer
    {
        private readonly Vector3[] localPositions;
        private readonly Quaternion[] localRotations;
        private readonly Vector3[] localScales;
        private readonly CharacterPoseChannels[] dirtyChannels;
        private readonly Matrix4x4[] rootParentMatrices;
        private readonly Matrix4x4[] worldMatrices;
        private readonly int[] worldMatrixVersions;
        private int poseVersion = 1;

        public CharacterPoseBuffer(CharacterSkeleton skeleton)
        {
            Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            localPositions = new Vector3[skeleton.BoneCount];
            localRotations = new Quaternion[skeleton.BoneCount];
            localScales = new Vector3[skeleton.BoneCount];
            dirtyChannels = new CharacterPoseChannels[skeleton.BoneCount];
            rootParentMatrices = new Matrix4x4[skeleton.BoneCount];
            worldMatrices = new Matrix4x4[skeleton.BoneCount];
            worldMatrixVersions = new int[skeleton.BoneCount];
        }

        public CharacterSkeleton Skeleton { get; }

        public int BoneCount => Skeleton.BoneCount;

        public void Capture()
        {
            for (var index = 0; index < Skeleton.BoneCount; index++)
            {
                var bone = Skeleton.Bones[index];
                var transform = bone.Transform;
                localPositions[index] = transform.localPosition;
                localRotations[index] = transform.localRotation;
                localScales[index] = transform.localScale;
                dirtyChannels[index] = CharacterPoseChannels.None;
                if (bone.ParentIndex < 0)
                {
                    rootParentMatrices[index] = transform.parent != null
                        ? transform.parent.localToWorldMatrix
                        : Matrix4x4.identity;
                }
            }

            InvalidateWorldMatrices();
        }

        public void Apply()
        {
            for (var index = 0; index < Skeleton.BoneCount; index++)
            {
                var channels = dirtyChannels[index];
                if (channels == CharacterPoseChannels.None)
                {
                    continue;
                }

                var transform = Skeleton.Bones[index].Transform;
                if ((channels & CharacterPoseChannels.Position) != 0)
                {
                    transform.localPosition = localPositions[index];
                }

                if ((channels & CharacterPoseChannels.Rotation) != 0)
                {
                    transform.localRotation = localRotations[index];
                }

                if ((channels & CharacterPoseChannels.Scale) != 0)
                {
                    transform.localScale = localScales[index];
                }
            }
        }

        public CharacterPoseChannels GetDirtyChannels(int boneIndex)
        {
            ValidateIndex(boneIndex);
            return dirtyChannels[boneIndex];
        }

        public Vector3 GetLocalPosition(int boneIndex)
        {
            ValidateIndex(boneIndex);
            return localPositions[boneIndex];
        }

        public Quaternion GetLocalRotation(int boneIndex)
        {
            ValidateIndex(boneIndex);
            return localRotations[boneIndex];
        }

        public Vector3 GetLocalScale(int boneIndex)
        {
            ValidateIndex(boneIndex);
            return localScales[boneIndex];
        }

        public void SetLocalPosition(int boneIndex, Vector3 value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            localPositions[boneIndex] = value;
            dirtyChannels[boneIndex] |= CharacterPoseChannels.Position;
            InvalidateWorldMatrices();
        }

        public void SetLocalRotation(int boneIndex, Quaternion value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            localRotations[boneIndex] = value.normalized;
            dirtyChannels[boneIndex] |= CharacterPoseChannels.Rotation;
            InvalidateWorldMatrices();
        }

        public void SetLocalScale(int boneIndex, Vector3 value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            localScales[boneIndex] = value;
            dirtyChannels[boneIndex] |= CharacterPoseChannels.Scale;
            InvalidateWorldMatrices();
        }

        public Matrix4x4 GetWorldMatrix(int boneIndex)
        {
            ValidateIndex(boneIndex);
            if (worldMatrixVersions[boneIndex] == poseVersion)
            {
                return worldMatrices[boneIndex];
            }

            var localMatrix = Matrix4x4.TRS(
                localPositions[boneIndex],
                localRotations[boneIndex],
                localScales[boneIndex]);
            var parentIndex = Skeleton.Bones[boneIndex].ParentIndex;
            worldMatrices[boneIndex] = parentIndex >= 0
                ? GetWorldMatrix(parentIndex) * localMatrix
                : rootParentMatrices[boneIndex] * localMatrix;
            worldMatrixVersions[boneIndex] = poseVersion;
            return worldMatrices[boneIndex];
        }

        public Vector3 GetWorldPosition(int boneIndex)
        {
            return GetWorldMatrix(boneIndex).MultiplyPoint3x4(Vector3.zero);
        }

        public Quaternion GetWorldRotation(int boneIndex)
        {
            return GetWorldMatrix(boneIndex).rotation;
        }

        public void SetWorldPosition(int boneIndex, Vector3 value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            var parentIndex = Skeleton.Bones[boneIndex].ParentIndex;
            var parentWorld = parentIndex >= 0
                ? GetWorldMatrix(parentIndex)
                : rootParentMatrices[boneIndex];
            SetLocalPosition(boneIndex, parentWorld.inverse.MultiplyPoint3x4(value));
        }

        public void SetWorldRotation(int boneIndex, Quaternion value)
        {
            ValidateIndex(boneIndex);
            ValidateFinite(value, nameof(value));
            var parentIndex = Skeleton.Bones[boneIndex].ParentIndex;
            var parentRotation = parentIndex >= 0
                ? GetWorldRotation(parentIndex)
                : rootParentMatrices[boneIndex].rotation;
            SetLocalRotation(
                boneIndex,
                Quaternion.Inverse(parentRotation) * value);
        }

        private void InvalidateWorldMatrices()
        {
            if (poseVersion == int.MaxValue)
            {
                Array.Clear(worldMatrixVersions, 0, worldMatrixVersions.Length);
                poseVersion = 1;
                return;
            }

            poseVersion++;
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
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) ||
                !float.IsFinite(value.z) || !float.IsFinite(value.w) ||
                value.x * value.x + value.y * value.y +
                value.z * value.z + value.w * value.w < 0.00000001f)
            {
                throw new ArgumentException(
                    "Pose rotation must be finite and non-zero.",
                    parameterName);
            }
        }
    }
}
