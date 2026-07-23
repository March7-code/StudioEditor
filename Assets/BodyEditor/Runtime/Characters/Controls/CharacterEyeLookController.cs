using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.Characters
{
    public sealed class CharacterPupilMaterialTarget
    {
        public CharacterPupilMaterialTarget(
            Material material,
            string textureProperty,
            CharacterEye eye)
        {
            Material = material != null
                ? material
                : throw new ArgumentNullException(nameof(material));
            TextureProperty = !string.IsNullOrWhiteSpace(textureProperty)
                ? textureProperty
                : throw new ArgumentException(
                    "A pupil texture property is required.",
                    nameof(textureProperty));
            if (!material.HasProperty(TextureProperty))
            {
                throw new ArgumentException(
                    $"Material '{material.name}' has no texture property " +
                    $"'{TextureProperty}'.",
                    nameof(textureProperty));
            }

            Eye = eye;
            BaseScale = material.GetTextureScale(textureProperty);
            BaseOffset = material.GetTextureOffset(textureProperty);
        }

        public Material Material { get; }

        public string TextureProperty { get; }

        public CharacterEye Eye { get; }

        public Vector2 BaseScale { get; }

        public Vector2 BaseOffset { get; }
    }

    [DisallowMultipleComponent]
    public sealed class CharacterEyeLookController :
        MonoBehaviour,
        ICharacterEyeLookController,
        ICharacterPoseModifier
    {
        private readonly EyeState[] eyes = new EyeState[2];
        private CharacterPupilMaterialTarget[] pupils =
            Array.Empty<CharacterPupilMaterialTarget>();
        private ICharacterPosePipeline coordinator;
        private Transform target;
        private bool isFollowingTarget;
        private bool configured;

        public bool IsFollowingTarget => isFollowingTarget;

        public Transform Target => target;

        public float LeftMinHorizontalAngle { get; set; } = -18f;

        public float LeftMaxHorizontalAngle { get; set; } = 18f;

        public float RightMinHorizontalAngle { get; set; } = -18f;

        public float RightMaxHorizontalAngle { get; set; } = 18f;

        public float UpAngleLimit { get; set; } = 12f;

        public float DownAngleLimit { get; set; } = 12f;

        public float Response { get; set; } = 12f;

        public float PupilHorizontalTravel { get; set; } = 0.1f;

        public float PupilVerticalTravel { get; set; } = 0.1f;

        public float BendingThreshold { get; set; }

        public float MaxAngleDifference { get; set; }

        public float BendingMultiplier { get; set; } = 1f;

        public int Order => CharacterPoseStages.EyeLook;

        public bool Enabled => isActiveAndEnabled;

        public void Configure(
            ICharacterPosePipeline requestedCoordinator,
            Transform reference,
            Transform leftEye,
            Transform rightEye,
            Vector3 referenceForward,
            Vector3 referenceUp)
        {
            if (requestedCoordinator == null)
            {
                throw new ArgumentNullException(nameof(requestedCoordinator));
            }

            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (leftEye == null)
            {
                throw new ArgumentNullException(nameof(leftEye));
            }

            if (rightEye == null)
            {
                throw new ArgumentNullException(nameof(rightEye));
            }

            if (referenceForward.sqrMagnitude < 0.000001f ||
                referenceUp.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "Eye reference axes must be non-zero.");
            }

            referenceForward.Normalize();
            referenceUp = Vector3.ProjectOnPlane(
                referenceUp,
                referenceForward);
            if (referenceUp.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "Eye reference axes must not be parallel.");
            }

            referenceUp.Normalize();
            RestoreAppliedRotations();
            coordinator?.UnregisterModifier(this);
            coordinator = requestedCoordinator;
            eyes[0] = CreateEyeState(
                coordinator.Skeleton,
                reference,
                leftEye,
                CharacterEye.Left,
                referenceForward,
                referenceUp);
            eyes[1] = CreateEyeState(
                coordinator.Skeleton,
                reference,
                rightEye,
                CharacterEye.Right,
                referenceForward,
                referenceUp);
            configured = true;
            coordinator.RegisterModifier(this);
        }

        public void ConfigureLimits(
            float leftMinHorizontal,
            float leftMaxHorizontal,
            float rightMinHorizontal,
            float rightMaxHorizontal,
            float up,
            float down,
            float bendingThreshold,
            float maxAngleDifference,
            float bendingMultiplier,
            float response)
        {
            if (!float.IsFinite(leftMinHorizontal) ||
                !float.IsFinite(leftMaxHorizontal) ||
                !float.IsFinite(rightMinHorizontal) ||
                !float.IsFinite(rightMaxHorizontal) ||
                !float.IsFinite(up) ||
                !float.IsFinite(down) ||
                !float.IsFinite(bendingThreshold) ||
                !float.IsFinite(maxAngleDifference) ||
                !float.IsFinite(bendingMultiplier) ||
                !float.IsFinite(response))
            {
                throw new ArgumentException("Eye-look limits must be finite.");
            }

            LeftMinHorizontalAngle = Mathf.Min(0f, leftMinHorizontal);
            LeftMaxHorizontalAngle = Mathf.Max(0f, leftMaxHorizontal);
            RightMinHorizontalAngle = Mathf.Min(0f, rightMinHorizontal);
            RightMaxHorizontalAngle = Mathf.Max(0f, rightMaxHorizontal);
            UpAngleLimit = Mathf.Abs(up);
            DownAngleLimit = Mathf.Abs(down);
            BendingThreshold = Mathf.Max(0f, bendingThreshold);
            MaxAngleDifference = Mathf.Max(0f, maxAngleDifference);
            BendingMultiplier = bendingMultiplier;
            Response = Mathf.Max(0f, response);
        }

        public void SetTarget(Transform value)
        {
            target = value;
        }

        public void ConfigurePupils(
            IReadOnlyList<CharacterPupilMaterialTarget> targets)
        {
            RestorePupilOffsets();
            if (targets == null || targets.Count == 0)
            {
                pupils = Array.Empty<CharacterPupilMaterialTarget>();
                return;
            }

            pupils = new CharacterPupilMaterialTarget[targets.Count];
            for (var index = 0; index < pupils.Length; index++)
            {
                pupils[index] = targets[index] ??
                    throw new ArgumentException(
                        "A pupil material target cannot be null.",
                        nameof(targets));
            }

            ApplyPupilOffsets();
        }

        public void SetFollowTarget(bool enabled)
        {
            isFollowingTarget = enabled;
        }

        public void SetFixedLocalRotations(
            Quaternion left,
            Quaternion right)
        {
            ValidateRotation(left, nameof(left));
            ValidateRotation(right, nameof(right));
            eyes[0].FixedLocalRotation = left.normalized;
            eyes[0].HasFixedLocalRotation = true;
            eyes[1].FixedLocalRotation = right.normalized;
            eyes[1].HasFixedLocalRotation = true;
            isFollowingTarget = false;
            coordinator?.EvaluateNow();
        }

        public void ClearFixedLocalRotations()
        {
            for (var index = 0; index < eyes.Length; index++)
            {
                eyes[index].HasFixedLocalRotation = false;
            }

            coordinator?.EvaluateNow();
        }

        public void Evaluate(CharacterPoseBuffer pose)
        {
            if (!configured)
            {
                return;
            }

            var followsTarget = isFollowingTarget && target != null;
            for (var index = 0; index < eyes.Length; index++)
            {
                ApplyEye(pose, eyes[index], followsTarget);
            }

            ApplyPupilOffsets();
        }

        private void ApplyEye(
            CharacterPoseBuffer pose,
            EyeState eye,
            bool followsTarget)
        {
            var capturedRotation = pose.GetLocalRotation(eye.BoneIndex);
            var hasFreshRotation =
                (pose.GetDirtyChannels(eye.BoneIndex) &
                 CharacterPoseChannels.Rotation) != 0;
            var baseRotation = !hasFreshRotation && eye.HasOutput &&
                               Quaternion.Angle(
                                   capturedRotation,
                                   eye.LastOutputRotation) < 0.01f
                ? Quaternion.Inverse(eye.LastAppliedDelta) * capturedRotation
                : capturedRotation;

            var usesFixedRotation = !followsTarget &&
                                    eye.HasFixedLocalRotation;
            var horizontalRate = 0f;
            var verticalRate = 0f;
            var desiredDelta = followsTarget
                ? CalculateTargetDelta(
                    pose,
                    eye,
                    target.position,
                    out horizontalRate,
                    out verticalRate)
                : usesFixedRotation
                    ? eye.FixedLocalRotation *
                      Quaternion.Inverse(baseRotation)
                    : Quaternion.identity;
            Quaternion appliedDelta;
            float appliedHorizontalRate;
            float appliedVerticalRate;
            if (!followsTarget)
            {
                appliedDelta = desiredDelta;
                appliedHorizontalRate = 0f;
                appliedVerticalRate = 0f;
            }
            else if (Response <= 0f || Time.deltaTime <= 0f)
            {
                appliedDelta = desiredDelta;
                appliedHorizontalRate = horizontalRate;
                appliedVerticalRate = verticalRate;
            }
            else
            {
                var blend = 1f - Mathf.Exp(-Response * Time.deltaTime);
                appliedDelta = Quaternion.Slerp(
                    eye.LastAppliedDelta,
                    desiredDelta,
                    blend);
                appliedHorizontalRate = Mathf.Lerp(
                    eye.HorizontalRate,
                    horizontalRate,
                    blend);
                appliedVerticalRate = Mathf.Lerp(
                    eye.VerticalRate,
                    verticalRate,
                    blend);
            }

            var outputRotation = appliedDelta * baseRotation;
            pose.SetLocalRotation(eye.BoneIndex, outputRotation);
            eye.LastAppliedDelta = appliedDelta;
            eye.LastOutputRotation = outputRotation;
            eye.HorizontalRate = appliedHorizontalRate;
            eye.VerticalRate = appliedVerticalRate;
            eye.HasOutput = true;
        }

        private Quaternion CalculateTargetDelta(
            CharacterPoseBuffer pose,
            EyeState eye,
            Vector3 targetPosition,
            out float horizontalRate,
            out float verticalRate)
        {
            horizontalRate = 0f;
            verticalRate = 0f;
            var direction = targetPosition -
                            pose.GetWorldPosition(eye.BoneIndex);
            if (direction.sqrMagnitude < 0.000001f)
            {
                return Quaternion.identity;
            }

            var bone = pose.Skeleton.Bones[eye.BoneIndex];
            var parentRotation = bone.ParentIndex >= 0
                ? pose.GetWorldRotation(bone.ParentIndex)
                : eye.Transform.parent != null
                    ? eye.Transform.parent.rotation
                    : Quaternion.identity;
            direction = Quaternion.Inverse(parentRotation) *
                        direction.normalized;
            var horizontal = Vector3.ProjectOnPlane(
                direction,
                eye.ReferenceUp);
            var rawYaw = horizontal.sqrMagnitude > 0.000001f
                ? Vector3.SignedAngle(
                    eye.ReferenceForward,
                    horizontal.normalized,
                    eye.ReferenceUp)
                : 0f;
            var minYaw = eye.Eye == CharacterEye.Left
                ? LeftMinHorizontalAngle
                : RightMinHorizontalAngle;
            var maxYaw = eye.Eye == CharacterEye.Left
                ? LeftMaxHorizontalAngle
                : RightMaxHorizontalAngle;
            var yaw = Mathf.Clamp(ApplyBending(rawYaw), minYaw, maxYaw);
            horizontalRate = NormalizeAngle(yaw, minYaw, maxYaw);
            var yawForward = Quaternion.AngleAxis(
                yaw,
                eye.ReferenceUp) * eye.ReferenceForward;
            var right = Vector3.Cross(eye.ReferenceUp, yawForward).normalized;
            var verticalDot = Mathf.Clamp(
                Vector3.Dot(direction, eye.ReferenceUp),
                -1f,
                1f);
            var planarLength = Mathf.Sqrt(Mathf.Max(
                0f,
                1f - verticalDot * verticalDot));
            var rawPitch = -Mathf.Atan2(verticalDot, planarLength) *
                           Mathf.Rad2Deg;
            var pitch = Mathf.Clamp(
                ApplyBending(rawPitch),
                -Mathf.Abs(UpAngleLimit),
                Mathf.Abs(DownAngleLimit));
            var verticalLimit = pitch < 0f
                ? Mathf.Abs(UpAngleLimit)
                : Mathf.Abs(DownAngleLimit);
            verticalRate = verticalLimit > 0.0001f
                ? pitch / verticalLimit
                : 0f;
            var pitchRotation = Quaternion.AngleAxis(pitch, right);
            var desiredForward = pitchRotation * yawForward;
            var desiredUp = pitchRotation * eye.ReferenceUp;
            var referenceBasis = Quaternion.LookRotation(
                eye.ReferenceForward,
                eye.ReferenceUp);
            var desiredBasis = Quaternion.LookRotation(
                desiredForward,
                desiredUp);
            return desiredBasis * Quaternion.Inverse(referenceBasis);
        }

        private float ApplyBending(float angle)
        {
            var magnitude = Mathf.Abs(angle);
            var thresholded = Mathf.Max(
                0f,
                magnitude - BendingThreshold) *
                Mathf.Abs(BendingMultiplier);
            var corrected = Mathf.Max(
                thresholded,
                magnitude - MaxAngleDifference);
            return corrected * Mathf.Sign(angle) *
                   Mathf.Sign(BendingMultiplier);
        }

        private static float NormalizeAngle(
            float angle,
            float minimum,
            float maximum)
        {
            if (angle < 0f)
            {
                return minimum < -0.0001f ? -angle / minimum : 0f;
            }

            return maximum > 0.0001f ? angle / maximum : 0f;
        }

        private static void ValidateRotation(
            Quaternion rotation,
            string parameterName)
        {
            if (!float.IsFinite(rotation.x) ||
                !float.IsFinite(rotation.y) ||
                !float.IsFinite(rotation.z) ||
                !float.IsFinite(rotation.w) ||
                rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w < 0.000001f)
            {
                throw new ArgumentException(
                    "Eye rotation must be a finite, non-zero quaternion.",
                    parameterName);
            }
        }

        private void ApplyPupilOffsets()
        {
            if (!configured)
            {
                return;
            }

            for (var index = 0; index < pupils.Length; index++)
            {
                var pupil = pupils[index];
                if (pupil?.Material == null ||
                    !pupil.Material.HasProperty(pupil.TextureProperty))
                {
                    continue;
                }

                var eye = eyes[EyeIndex(pupil.Eye)];
                var scaleAdjustment = pupil.BaseScale - Vector2.one;
                var centerCompensation = scaleAdjustment * -0.5f;
                var baseMovement = pupil.BaseOffset - centerCompensation;
                var movementScale = new Vector2(
                    Mathf.Lerp(1f, 5f, scaleAdjustment.x),
                    Mathf.Lerp(1f, 5f, scaleAdjustment.y));
                var horizontalTravel = Mathf.Abs(PupilHorizontalTravel);
                var verticalTravel = Mathf.Abs(PupilVerticalTravel);
                var lookInput = new Vector2(
                    DivideMovement(
                        baseMovement.x,
                        horizontalTravel * movementScale.x) +
                    eye.HorizontalRate,
                    DivideMovement(
                        -baseMovement.y,
                        verticalTravel * movementScale.y) -
                    eye.VerticalRate);
                if (lookInput.sqrMagnitude > 1f)
                {
                    lookInput.Normalize();
                }

                var movement = new Vector2(
                    lookInput.x * horizontalTravel * movementScale.x,
                    -lookInput.y * verticalTravel * movementScale.y);
                pupil.Material.SetTextureScale(
                    pupil.TextureProperty,
                    pupil.BaseScale);
                pupil.Material.SetTextureOffset(
                    pupil.TextureProperty,
                    centerCompensation + movement);
            }
        }

        private static float DivideMovement(float value, float scale)
        {
            return Mathf.Abs(scale) > 0.000001f ? value / scale : 0f;
        }

        private void RestorePupilOffsets()
        {
            for (var index = 0; index < pupils.Length; index++)
            {
                var pupil = pupils[index];
                if (pupil?.Material == null ||
                    !pupil.Material.HasProperty(pupil.TextureProperty))
                {
                    continue;
                }

                pupil.Material.SetTextureScale(
                    pupil.TextureProperty,
                    pupil.BaseScale);
                pupil.Material.SetTextureOffset(
                    pupil.TextureProperty,
                    pupil.BaseOffset);
            }
        }

        private static int EyeIndex(CharacterEye eye)
        {
            switch (eye)
            {
                case CharacterEye.Left:
                    return 0;
                case CharacterEye.Right:
                    return 1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(eye));
            }
        }

        private static EyeState CreateEyeState(
            CharacterSkeleton skeleton,
            Transform reference,
            Transform eye,
            CharacterEye side,
            Vector3 referenceForward,
            Vector3 referenceUp)
        {
            if (!skeleton.TryGetBoneIndex(eye, out var boneIndex))
            {
                throw new ArgumentException(
                    $"Eye transform '{eye.name}' is not part of the character " +
                    "skeleton.",
                    nameof(eye));
            }

            var worldForward = reference.TransformDirection(referenceForward);
            var worldUp = reference.TransformDirection(referenceUp);
            var parentRotation = eye.parent != null
                ? eye.parent.rotation
                : Quaternion.identity;
            var localForward = Quaternion.Inverse(parentRotation) *
                               worldForward.normalized;
            var localUp = Quaternion.Inverse(parentRotation) *
                          worldUp.normalized;
            localUp = Vector3.ProjectOnPlane(localUp, localForward).normalized;
            return new EyeState(
                boneIndex,
                eye,
                side,
                localForward,
                localUp);
        }

        private void RestoreAppliedRotations()
        {
            if (!configured)
            {
                return;
            }

            for (var index = 0; index < eyes.Length; index++)
            {
                var eye = eyes[index];
                if (eye?.Transform == null || !eye.HasOutput ||
                    Quaternion.Angle(
                        eye.Transform.localRotation,
                        eye.LastOutputRotation) >= 0.01f)
                {
                    continue;
                }

                eye.Transform.localRotation =
                    Quaternion.Inverse(eye.LastAppliedDelta) *
                    eye.Transform.localRotation;
                eye.LastAppliedDelta = Quaternion.identity;
                eye.HasOutput = false;
            }
        }

        private void OnDisable()
        {
            RestoreAppliedRotations();
            RestorePupilOffsets();
        }

        private void OnDestroy()
        {
            RestoreAppliedRotations();
            RestorePupilOffsets();
            coordinator?.UnregisterModifier(this);
            coordinator = null;
        }

        private sealed class EyeState
        {
            public EyeState(
                int boneIndex,
                Transform transform,
                CharacterEye eye,
                Vector3 referenceForward,
                Vector3 referenceUp)
            {
                BoneIndex = boneIndex;
                Transform = transform;
                Eye = eye;
                ReferenceForward = referenceForward;
                ReferenceUp = referenceUp;
                LastAppliedDelta = Quaternion.identity;
                LastOutputRotation = transform.localRotation;
                FixedLocalRotation = transform.localRotation;
            }

            public int BoneIndex { get; }

            public Transform Transform { get; }

            public CharacterEye Eye { get; }

            public Vector3 ReferenceForward { get; }

            public Vector3 ReferenceUp { get; }

            public Quaternion LastAppliedDelta { get; set; }

            public Quaternion LastOutputRotation { get; set; }

            public bool HasOutput { get; set; }

            public Quaternion FixedLocalRotation { get; set; }

            public bool HasFixedLocalRotation { get; set; }

            public float HorizontalRate { get; set; }

            public float VerticalRate { get; set; }
        }
    }
}
