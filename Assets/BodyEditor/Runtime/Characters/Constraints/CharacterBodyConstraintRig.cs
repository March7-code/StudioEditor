using System;
using System.Collections.Generic;
using BodyEditor.Characters.Kinematics;
using UnityEngine;

namespace BodyEditor.Characters.Constraints
{
    public sealed class CharacterBodyConstraintSettings
    {
        private int solverIterations = 2;
        private float margin = 0.004f;
        private float maxCorrectionRatio = 0.3f;

        public int SolverIterations
        {
            get => solverIterations;
            set => solverIterations = Mathf.Clamp(value, 1, 6);
        }

        public float Margin
        {
            get => margin;
            set => margin = Mathf.Max(0f, value);
        }

        public float MaxCorrectionRatio
        {
            get => maxCorrectionRatio;
            set => maxCorrectionRatio = Mathf.Clamp(value, 0.05f, 1f);
        }
    }

    public sealed class CharacterBodyConstraintRig :
        ICharacterPoseModifier,
        IDisposable
    {
        private readonly ICharacterModel model;
        private readonly CharacterPoseCoordinator coordinator;
        private readonly BodyCollisionProfile profile;
        private readonly IReadOnlyList<LimbChain> limbs;
        private readonly List<CapsuleVolume> volumes =
            new List<CapsuleVolume>(3);
        private bool disposed;

        private CharacterBodyConstraintRig(
            ICharacterModel model,
            BodyCollisionProfile profile,
            IReadOnlyList<LimbChain> limbs)
        {
            this.model = model;
            this.profile = profile;
            this.limbs = limbs;
            coordinator = model.PoseCoordinator;
            Settings = new CharacterBodyConstraintSettings();
            coordinator.RegisterModifier(this);
        }

        public int Order => CharacterPoseStages.BodyConstraints;

        public bool Enabled { get; set; } = true;

        public CharacterBodyConstraintSettings Settings { get; }

        public ICharacterModel Model => model;

        public static bool TryCreate(
            ICharacterModel model,
            out CharacterBodyConstraintRig rig)
        {
            rig = null;
            if (model == null || model.Root == null ||
                model.Skeleton == null || model.PoseCoordinator == null ||
                !model.PoseCoordinator.IsInitialized ||
                !BodyCollisionProfile.TryCreate(model, out var profile))
            {
                return false;
            }

            var limbs = BuildLimbs(model.Skeleton, profile.ReferenceScale);
            if (limbs.Count == 0)
            {
                return false;
            }

            rig = new CharacterBodyConstraintRig(model, profile, limbs);
            return true;
        }

        public void Evaluate(CharacterPoseBuffer pose)
        {
            if (!Enabled || disposed ||
                !ReferenceEquals(pose.Skeleton, model.Skeleton))
            {
                return;
            }

            BuildVolumes(pose);
            for (var index = 0; index < limbs.Count; index++)
            {
                ConstrainLimb(pose, limbs[index]);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (coordinator != null)
            {
                coordinator.UnregisterModifier(this);
            }

            volumes.Clear();
        }

        private void BuildVolumes(CharacterPoseBuffer pose)
        {
            volumes.Clear();
            volumes.Add(new CapsuleVolume(
                pose.GetWorldPosition(profile.SpineIndex),
                pose.GetWorldPosition(profile.UpperTorsoIndex),
                profile.TorsoRadius));

            var hips = pose.GetWorldPosition(profile.HipsIndex);
            volumes.Add(new CapsuleVolume(
                hips,
                hips,
                profile.PelvisRadius));

            if (profile.HeadIndex >= 0)
            {
                var headCenter = pose.GetWorldMatrix(profile.HeadIndex)
                    .MultiplyPoint3x4(profile.HeadCenterLocal);
                volumes.Add(new CapsuleVolume(
                    headCenter,
                    headCenter,
                    profile.HeadRadius));
            }
        }

        private void ConstrainLimb(
            CharacterPoseBuffer pose,
            LimbChain limb)
        {
            var root = pose.GetWorldPosition(limb.RootIndex);
            var mid = pose.GetWorldPosition(limb.MidIndex);
            var tip = pose.GetWorldPosition(limb.TipIndex);
            var originalMid = mid;
            var originalTip = tip;

            for (var iteration = 0;
                 iteration < Settings.SolverIterations;
                 iteration++)
            {
                mid = ResolvePoint(mid, limb.JointRadius);
                tip = ResolvePoint(tip, limb.EndpointRadius);

                var upperSample = Vector3.Lerp(root, mid, 0.72f);
                var upperCorrection = ResolvePoint(
                                          upperSample,
                                          limb.SegmentRadius) -
                                      upperSample;
                mid += upperCorrection * 1.35f;

                var lowerSample = Vector3.Lerp(mid, tip, 0.55f);
                var lowerCorrection = ResolvePoint(
                                          lowerSample,
                                          limb.SegmentRadius) -
                                      lowerSample;
                tip += lowerCorrection * 1.45f;
            }

            var correctionLimit = profile.ReferenceScale *
                                  Settings.MaxCorrectionRatio;
            mid = originalMid + Vector3.ClampMagnitude(
                mid - originalMid,
                correctionLimit);
            tip = originalTip + Vector3.ClampMagnitude(
                tip - originalTip,
                correctionLimit);
            if ((mid - originalMid).sqrMagnitude < 0.0000000001f &&
                (tip - originalTip).sqrMagnitude < 0.0000000001f)
            {
                return;
            }

            CharacterTwoBoneSolver.Solve(
                pose,
                limb.RootIndex,
                limb.MidIndex,
                limb.TipIndex,
                tip,
                mid,
                pose.GetWorldRotation(limb.TipIndex),
                limb.TwoBoneSettings);
        }

        private Vector3 ResolvePoint(Vector3 point, float radius)
        {
            for (var index = 0; index < volumes.Count; index++)
            {
                point += CalculateCorrection(
                    point,
                    radius + Settings.Margin,
                    volumes[index]);
            }

            return point;
        }

        private Vector3 CalculateCorrection(
            Vector3 point,
            float pointRadius,
            CapsuleVolume volume)
        {
            var segment = volume.End - volume.Start;
            var squareLength = segment.sqrMagnitude;
            var closest = squareLength > 0.00000001f
                ? volume.Start + segment * Mathf.Clamp01(
                    Vector3.Dot(point - volume.Start, segment) /
                    squareLength)
                : volume.Start;
            var offset = point - closest;
            var requiredDistance = volume.Radius + pointRadius;
            var squareDistance = offset.sqrMagnitude;
            if (squareDistance >= requiredDistance * requiredDistance)
            {
                return Vector3.zero;
            }

            if (squareDistance < 0.00000001f)
            {
                var fallback = model.Root != null
                    ? model.Root.transform.forward
                    : Vector3.forward;
                return fallback.normalized * requiredDistance;
            }

            var distance = Mathf.Sqrt(squareDistance);
            return offset / distance * (requiredDistance - distance);
        }

        private static IReadOnlyList<LimbChain> BuildLimbs(
            CharacterSkeleton skeleton,
            float referenceScale)
        {
            var result = new List<LimbChain>(4);
            AddLimb(
                result,
                skeleton,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                referenceScale * 0.055f);
            AddLimb(
                result,
                skeleton,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                referenceScale * 0.055f);
            AddLimb(
                result,
                skeleton,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot,
                referenceScale * 0.075f);
            AddLimb(
                result,
                skeleton,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot,
                referenceScale * 0.075f);
            return result.AsReadOnly();
        }

        private static void AddLimb(
            ICollection<LimbChain> destination,
            CharacterSkeleton skeleton,
            HumanBodyBones rootBone,
            HumanBodyBones midBone,
            HumanBodyBones tipBone,
            float radius)
        {
            if (skeleton.TryGetBoneIndex(rootBone, out var root) &&
                skeleton.TryGetBoneIndex(midBone, out var mid) &&
                skeleton.TryGetBoneIndex(tipBone, out var tip))
            {
                destination.Add(new LimbChain(
                    root,
                    mid,
                    tip,
                    radius,
                    radius * 1.25f,
                    radius,
                    CharacterTwoBoneSettings.CreateHumanoid(
                        skeleton,
                        root,
                        mid,
                        tip,
                        midBone)));
            }
        }

        private readonly struct LimbChain
        {
            public LimbChain(
                int rootIndex,
                int midIndex,
                int tipIndex,
                float jointRadius,
                float endpointRadius,
                float segmentRadius,
                CharacterTwoBoneSettings twoBoneSettings)
            {
                RootIndex = rootIndex;
                MidIndex = midIndex;
                TipIndex = tipIndex;
                JointRadius = jointRadius;
                EndpointRadius = endpointRadius;
                SegmentRadius = segmentRadius;
                TwoBoneSettings = twoBoneSettings;
            }

            public int RootIndex { get; }
            public int MidIndex { get; }
            public int TipIndex { get; }
            public float JointRadius { get; }
            public float EndpointRadius { get; }
            public float SegmentRadius { get; }
            public CharacterTwoBoneSettings TwoBoneSettings { get; }
        }

        private readonly struct CapsuleVolume
        {
            public CapsuleVolume(Vector3 start, Vector3 end, float radius)
            {
                Start = start;
                End = end;
                Radius = radius;
            }

            public Vector3 Start { get; }
            public Vector3 End { get; }
            public float Radius { get; }
        }

        private readonly struct BodyCollisionProfile
        {
            private BodyCollisionProfile(
                int hipsIndex,
                int spineIndex,
                int upperTorsoIndex,
                int headIndex,
                Vector3 headCenterLocal,
                float torsoRadius,
                float pelvisRadius,
                float headRadius,
                float referenceScale)
            {
                HipsIndex = hipsIndex;
                SpineIndex = spineIndex;
                UpperTorsoIndex = upperTorsoIndex;
                HeadIndex = headIndex;
                HeadCenterLocal = headCenterLocal;
                TorsoRadius = torsoRadius;
                PelvisRadius = pelvisRadius;
                HeadRadius = headRadius;
                ReferenceScale = referenceScale;
            }

            public int HipsIndex { get; }
            public int SpineIndex { get; }
            public int UpperTorsoIndex { get; }
            public int HeadIndex { get; }
            public Vector3 HeadCenterLocal { get; }
            public float TorsoRadius { get; }
            public float PelvisRadius { get; }
            public float HeadRadius { get; }
            public float ReferenceScale { get; }

            public static bool TryCreate(
                ICharacterModel model,
                out BodyCollisionProfile profile)
            {
                var skeleton = model.Skeleton;
                if (!skeleton.TryGetBoneIndex(
                        HumanBodyBones.Hips,
                        out var hips) ||
                    !skeleton.TryGetBoneIndex(
                        HumanBodyBones.Spine,
                        out var spine))
                {
                    profile = default;
                    return false;
                }

                var upperTorso = skeleton.TryGetBoneIndex(
                    HumanBodyBones.UpperChest,
                    out var upperChest)
                    ? upperChest
                    : skeleton.TryGetBoneIndex(
                        HumanBodyBones.Chest,
                        out var chest)
                        ? chest
                        : -1;
                if (upperTorso < 0)
                {
                    profile = default;
                    return false;
                }

                var shoulderWidth = Distance(
                    skeleton,
                    HumanBodyBones.LeftUpperArm,
                    HumanBodyBones.RightUpperArm,
                    0.38f);
                var hipWidth = Distance(
                    skeleton,
                    HumanBodyBones.LeftUpperLeg,
                    HumanBodyBones.RightUpperLeg,
                    shoulderWidth * 0.65f);
                var referenceScale = Mathf.Max(0.1f, shoulderWidth);
                var headIndex = skeleton.TryGetBoneIndex(
                    HumanBodyBones.Head,
                    out var resolvedHead)
                    ? resolvedHead
                    : -1;
                var headCenterLocal = Vector3.zero;
                var headRadius = referenceScale * 0.28f;
                if (headIndex >= 0 &&
                    TryGetHeadBounds(model.Geometry, out var headBounds))
                {
                    var headTransform = skeleton.Bones[headIndex].Transform;
                    headCenterLocal = headTransform.InverseTransformPoint(
                        headBounds.center);
                    var measuredRadius = Mathf.Max(
                        headBounds.extents.x,
                        headBounds.extents.z,
                        headBounds.extents.y * 0.72f);
                    headRadius = Mathf.Clamp(
                        measuredRadius,
                        referenceScale * 0.2f,
                        referenceScale * 0.58f);
                }

                profile = new BodyCollisionProfile(
                    hips,
                    spine,
                    upperTorso,
                    headIndex,
                    headCenterLocal,
                    referenceScale * 0.29f,
                    Mathf.Max(hipWidth * 0.46f, referenceScale * 0.2f),
                    headRadius,
                    referenceScale);
                return true;
            }

            private static float Distance(
                CharacterSkeleton skeleton,
                HumanBodyBones first,
                HumanBodyBones second,
                float fallback)
            {
                return skeleton.TryGetTransform(first, out var firstTransform) &&
                       skeleton.TryGetTransform(second, out var secondTransform)
                    ? Vector3.Distance(
                        firstTransform.position,
                        secondTransform.position)
                    : fallback;
            }

            private static bool TryGetHeadBounds(
                CharacterGeometry geometry,
                out Bounds bounds)
            {
                bounds = default;
                if (geometry == null || geometry.HeadRenderers.Count == 0)
                {
                    return false;
                }

                var found = false;
                for (var index = 0; index < geometry.HeadRenderers.Count; index++)
                {
                    var renderer = geometry.HeadRenderers[index];
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        bounds = renderer.bounds;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                return found;
            }
        }
    }
}
