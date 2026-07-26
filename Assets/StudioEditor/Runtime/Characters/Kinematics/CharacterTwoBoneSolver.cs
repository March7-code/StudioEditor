using System;
using UnityEngine;

namespace StudioEditor.Characters.Kinematics
{
    public readonly struct CharacterTwoBoneSettings
    {
        public CharacterTwoBoneSettings(
            float minimumBendDegrees,
            float maximumBendDegrees,
            bool preventBendReversal,
            Vector3 preferredBendDirectionRootLocal)
        {
            MinimumBendDegrees = Mathf.Clamp(
                minimumBendDegrees,
                0f,
                179f);
            MaximumBendDegrees = Mathf.Clamp(
                maximumBendDegrees,
                MinimumBendDegrees,
                179f);
            PreventBendReversal = preventBendReversal;
            PreferredBendDirectionRootLocal =
                preferredBendDirectionRootLocal.sqrMagnitude > 0.00000001f
                    ? preferredBendDirectionRootLocal.normalized
                    : Vector3.zero;
        }

        public float MinimumBendDegrees { get; }

        public float MaximumBendDegrees { get; }

        public bool PreventBendReversal { get; }

        public Vector3 PreferredBendDirectionRootLocal { get; }

        public static CharacterTwoBoneSettings CreateHumanoid(
            CharacterSkeleton skeleton,
            int rootIndex,
            int midIndex,
            int tipIndex,
            HumanBodyBones midBone)
        {
            if (skeleton == null)
            {
                throw new ArgumentNullException(nameof(skeleton));
            }

            var root = skeleton.Bones[rootIndex].Transform;
            var mid = skeleton.Bones[midIndex].Transform;
            var tip = skeleton.Bones[tipIndex].Transform;
            var targetDirection = tip.position - root.position;
            var bendDirection = Vector3.ProjectOnPlane(
                mid.position - root.position,
                targetDirection);
            if (bendDirection.sqrMagnitude < 0.00000001f)
            {
                var fallback = root.root != null
                    ? root.root.forward
                    : Vector3.forward;
                bendDirection = Vector3.ProjectOnPlane(
                    fallback,
                    targetDirection);
            }

            if (bendDirection.sqrMagnitude < 0.00000001f)
            {
                bendDirection = Vector3.ProjectOnPlane(
                    Vector3.up,
                    targetDirection);
            }

            var rootLocalDirection = bendDirection.sqrMagnitude > 0.00000001f
                ? Quaternion.Inverse(root.rotation) * bendDirection.normalized
                : Vector3.zero;
            return new CharacterTwoBoneSettings(
                0f,
                179f,
                false,
                rootLocalDirection);
        }
    }

    public static class CharacterTwoBoneSolver
    {
        public static bool Solve(
            CharacterPoseBuffer pose,
            int rootIndex,
            int midIndex,
            int tipIndex,
            Vector3 requestedTarget,
            Vector3? poleTarget,
            Quaternion tipWorldRotation,
            CharacterTwoBoneSettings settings,
            CharacterPoseLayer outputLayer = null)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }

            var rootPosition = pose.GetWorldPosition(rootIndex);
            var midPosition = pose.GetWorldPosition(midIndex);
            var tipPosition = pose.GetWorldPosition(tipIndex);
            var rootToMid = midPosition - rootPosition;
            var midToTip = tipPosition - midPosition;
            var firstLength = rootToMid.magnitude;
            var secondLength = midToTip.magnitude;
            if (firstLength < 0.00001f || secondLength < 0.00001f)
            {
                return false;
            }

            var targetVector = requestedTarget - rootPosition;
            var requestedDistance = targetVector.magnitude;
            if (requestedDistance < 0.00001f)
            {
                targetVector = rootToMid + midToTip;
                requestedDistance = targetVector.magnitude;
                if (requestedDistance < 0.00001f)
                {
                    return false;
                }
            }

            var targetDirection = targetVector / requestedDistance;
            var minimumDistance = DistanceAtBend(
                firstLength,
                secondLength,
                settings.MaximumBendDegrees);
            var maximumDistance = DistanceAtBend(
                firstLength,
                secondLength,
                settings.MinimumBendDegrees);
            var solvedDistance = Mathf.Clamp(
                requestedDistance,
                minimumDistance,
                maximumDistance);
            solvedDistance = Mathf.Max(solvedDistance, 0.00001f);
            var projection =
                (firstLength * firstLength + solvedDistance * solvedDistance -
                 secondLength * secondLength) /
                (2f * solvedDistance);
            var height = Mathf.Sqrt(Mathf.Max(
                0f,
                firstLength * firstLength - projection * projection));

            var currentRootRotation = pose.GetWorldRotation(rootIndex);
            var preferredDirection = Vector3.ProjectOnPlane(
                currentRootRotation * settings.PreferredBendDirectionRootLocal,
                targetDirection);
            var bendSource = poleTarget.HasValue
                ? poleTarget.Value - rootPosition
                : rootToMid;
            var bendDirection = Vector3.ProjectOnPlane(
                bendSource,
                targetDirection);
            if (settings.PreventBendReversal &&
                preferredDirection.sqrMagnitude > 0.00000001f &&
                (bendDirection.sqrMagnitude < 0.00000001f ||
                 Vector3.Dot(bendDirection, preferredDirection) <= 0f))
            {
                bendDirection = preferredDirection;
            }

            if (bendDirection.sqrMagnitude < 0.00000001f)
            {
                bendDirection = preferredDirection;
            }

            if (bendDirection.sqrMagnitude < 0.00000001f)
            {
                var bendNormal = Vector3.Cross(rootToMid, midToTip);
                bendDirection = Vector3.Cross(
                    bendNormal.sqrMagnitude > 0.00000001f
                        ? bendNormal.normalized
                        : Vector3.up,
                    targetDirection);
            }

            if (bendDirection.sqrMagnitude < 0.00000001f)
            {
                bendDirection = Vector3.Cross(targetDirection, Vector3.forward);
            }

            bendDirection.Normalize();
            var desiredMid = rootPosition + targetDirection * projection +
                             bendDirection * height;
            var rootDelta = Quaternion.FromToRotation(
                rootToMid,
                desiredMid - rootPosition);
            ApplyWorldRotation(
                pose,
                outputLayer,
                rootIndex,
                rootDelta * currentRootRotation);

            midPosition = pose.GetWorldPosition(midIndex);
            tipPosition = pose.GetWorldPosition(tipIndex);
            var solvedTarget = rootPosition + targetDirection * solvedDistance;
            var currentMidToTip = tipPosition - midPosition;
            var desiredMidToTip = solvedTarget - midPosition;
            if (currentMidToTip.sqrMagnitude > 0.00000001f &&
                desiredMidToTip.sqrMagnitude > 0.00000001f)
            {
                var midDelta = Quaternion.FromToRotation(
                    currentMidToTip,
                    desiredMidToTip);
                ApplyWorldRotation(
                    pose,
                    outputLayer,
                    midIndex,
                    midDelta * pose.GetWorldRotation(midIndex));
            }

            ApplyWorldRotation(
                pose,
                outputLayer,
                tipIndex,
                tipWorldRotation);
            return true;
        }

        private static float DistanceAtBend(
            float firstLength,
            float secondLength,
            float bendDegrees)
        {
            var cosine = Mathf.Cos(bendDegrees * Mathf.Deg2Rad);
            return Mathf.Sqrt(Mathf.Max(
                0.0000000001f,
                firstLength * firstLength + secondLength * secondLength +
                2f * firstLength * secondLength * cosine));
        }

        private static void ApplyWorldRotation(
            CharacterPoseBuffer pose,
            CharacterPoseLayer outputLayer,
            int boneIndex,
            Quaternion rotation)
        {
            pose.SetWorldRotation(boneIndex, rotation);
            outputLayer?.SetLocalRotation(
                boneIndex,
                pose.GetLocalRotation(boneIndex));
        }
    }
}
