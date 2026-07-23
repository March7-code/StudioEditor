using System;
using System.Collections.Generic;
using BodyEditor.Characters.Kinematics;
using UnityEngine;

namespace BodyEditor.Characters.Controls
{
    public enum CharacterControlPoint
    {
        Hips,
        Chest,
        Head,
        LeftShoulder,
        RightShoulder,
        LeftHand,
        LeftElbow,
        RightHand,
        RightElbow,
        LeftFoot,
        LeftKnee,
        RightFoot,
        RightKnee,
    }

    public sealed class CharacterControlRig : IDisposable
    {
        private static readonly CharacterControlPoint[] evaluationOrder =
        {
            CharacterControlPoint.Hips,
            CharacterControlPoint.Chest,
            CharacterControlPoint.Head,
            CharacterControlPoint.LeftShoulder,
            CharacterControlPoint.RightShoulder,
            CharacterControlPoint.LeftHand,
            CharacterControlPoint.LeftElbow,
            CharacterControlPoint.RightHand,
            CharacterControlPoint.RightElbow,
            CharacterControlPoint.LeftFoot,
            CharacterControlPoint.LeftKnee,
            CharacterControlPoint.RightFoot,
            CharacterControlPoint.RightKnee,
        };

        private readonly ICharacterModel model;
        private readonly ICharacterPosePipeline coordinator;
        private readonly CharacterPoseLayer poseLayer;
        private readonly ControlPoseModifier modifier;
        private readonly Dictionary<CharacterControlPoint, ControlDefinition>
            definitions =
                new Dictionary<CharacterControlPoint, ControlDefinition>();
        private readonly Dictionary<CharacterControlPoint, ControlState> states =
            new Dictionary<CharacterControlPoint, ControlState>();
        private readonly Dictionary<int, BoneBaseline> baselines =
            new Dictionary<int, BoneBaseline>();
        private readonly IReadOnlyList<CharacterControlPoint> controlPoints;
        private bool enabled = true;
        private bool disposed;

        public CharacterControlRig(ICharacterModel model)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            coordinator = model.Controls?.Pose?.Pipeline ??
                          throw new ArgumentException(
                              "Character model has no pose pipeline.",
                              nameof(model));
            if (!coordinator.IsInitialized ||
                !ReferenceEquals(coordinator.Skeleton, model.Skeleton))
            {
                throw new ArgumentException(
                    "Character pose coordinator is not initialized for its skeleton.",
                    nameof(model));
            }

            BuildDefinitions(model.Skeleton);
            var values = new List<CharacterControlPoint>();
            for (var index = 0; index < evaluationOrder.Length; index++)
            {
                var point = evaluationOrder[index];
                if (!definitions.ContainsKey(point))
                {
                    continue;
                }

                values.Add(point);
                states.Add(point, default);
            }

            controlPoints = values.AsReadOnly();
            EstimatedHeight = CalculateHeight(model.Skeleton);

            poseLayer = new CharacterPoseLayer(
                model.Skeleton,
                CharacterPoseStages.ActionEditing,
                $"{model.DisplayName} Control Points");
            modifier = new ControlPoseModifier(this);
            coordinator.RegisterModifier(modifier);
            coordinator.RegisterModifier(poseLayer);
        }

        public ICharacterModel Model => model;

        public CharacterPoseLayer PoseLayer => poseLayer;

        public IReadOnlyList<CharacterControlPoint> ControlPoints => controlPoints;

        public float EstimatedHeight { get; }

        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value)
                {
                    return;
                }

                enabled = value;
                if (!enabled)
                {
                    RestoreAllBaselinesToTransforms();
                    poseLayer.Clear();
                }

                coordinator.EvaluateNow();
            }
        }

        public bool IsActive(CharacterControlPoint point)
        {
            return states.TryGetValue(point, out var state) && state.Active;
        }

        public bool TryGetControlPosition(
            CharacterControlPoint point,
            out Vector3 position)
        {
            if (!definitions.TryGetValue(point, out var definition))
            {
                position = default;
                return false;
            }

            var state = states[point];
            if (state.Active)
            {
                position = state.Target;
                return true;
            }

            position = GetBoneWorldPosition(definition.AnchorIndex);
            return true;
        }

        public bool SupportsRotation(CharacterControlPoint point)
        {
            return definitions.TryGetValue(point, out var definition) &&
                   definition.RotationIndex >= 0;
        }

        public bool TryGetControlRotation(
            CharacterControlPoint point,
            out Quaternion rotation)
        {
            if (!definitions.TryGetValue(point, out var definition) ||
                definition.RotationIndex < 0)
            {
                rotation = Quaternion.identity;
                return false;
            }

            var state = states[point];
            if (state.Active && state.RotationActive)
            {
                rotation = state.TargetRotation;
                return true;
            }

            rotation = GetBoneWorldRotation(definition.RotationIndex);
            return true;
        }

        public bool TryGetAnchorPosition(
            CharacterControlPoint point,
            out Vector3 position)
        {
            if (!definitions.TryGetValue(point, out var definition))
            {
                position = default;
                return false;
            }

            position = GetBoneWorldPosition(definition.AnchorIndex);
            return true;
        }

        public bool SetTarget(CharacterControlPoint point, Vector3 worldPosition)
        {
            if (!IsFinite(worldPosition) ||
                !definitions.TryGetValue(point, out var definition))
            {
                return false;
            }

            var state = states[point];
            if (!state.Active)
            {
                CaptureBaselines(definition);
                state.Active = true;
                state.TargetRotation = definition.RotationIndex >= 0
                    ? GetBoneWorldRotation(definition.RotationIndex)
                    : Quaternion.identity;
            }

            state.Target = worldPosition;
            states[point] = state;
            coordinator.EvaluateNow();
            return true;
        }

        public bool SetTargetRotation(
            CharacterControlPoint point,
            Quaternion worldRotation)
        {
            if (!IsFinite(worldRotation) ||
                !definitions.TryGetValue(point, out var definition) ||
                definition.RotationIndex < 0)
            {
                return false;
            }

            var state = states[point];
            if (!state.Active)
            {
                CaptureBaselines(definition);
                state.Active = true;
                state.Target = GetBoneWorldPosition(definition.AnchorIndex);
            }

            CaptureBaseline(
                definition.RotationIndex,
                CharacterPoseChannels.Rotation);
            state.RotationActive = true;
            state.TargetRotation = worldRotation.normalized;
            states[point] = state;
            coordinator.EvaluateNow();
            return true;
        }

        public bool ClearTarget(CharacterControlPoint point)
        {
            if (!states.TryGetValue(point, out var state) || !state.Active)
            {
                return false;
            }

            states[point] = default;
            RestoreReleasedBaselines();
            coordinator.EvaluateNow();
            return true;
        }

        public void ClearTargets()
        {
            var changed = false;
            for (var index = 0; index < controlPoints.Count; index++)
            {
                var point = controlPoints[index];
                var state = states[point];
                if (!state.Active)
                {
                    continue;
                }

                states[point] = default;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            RestoreReleasedBaselines();
            coordinator.EvaluateNow();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            RestoreAllBaselinesToTransforms();
            poseLayer.Clear();
            if (coordinator != null)
            {
                coordinator.UnregisterModifier(modifier);
                coordinator.UnregisterModifier(poseLayer);
                coordinator.EvaluateNow();
            }

            baselines.Clear();
            states.Clear();
            definitions.Clear();
        }

        private void Evaluate(CharacterPoseBuffer pose)
        {
            poseLayer.Clear();
            if (!enabled || disposed)
            {
                return;
            }

            RestoreBaselines(pose);
            if (TryGetActiveState(
                    CharacterControlPoint.Hips,
                    out var hipsDefinition,
                    out var hipsState))
            {
                ApplyWorldPosition(
                    pose,
                    hipsDefinition.RootIndex,
                    hipsState.Target);
                if (hipsState.RotationActive)
                {
                    ApplyWorldRotation(
                        pose,
                        hipsDefinition.RotationIndex,
                        hipsState.TargetRotation);
                }
            }

            SolveEndpoint(pose, CharacterControlPoint.Chest);
            SolveEndpoint(pose, CharacterControlPoint.Head);
            SolveShoulder(pose, CharacterControlPoint.LeftShoulder);
            SolveShoulder(pose, CharacterControlPoint.RightShoulder);
            SolveLimb(
                pose,
                CharacterControlPoint.LeftHand,
                CharacterControlPoint.LeftElbow);
            SolveLimb(
                pose,
                CharacterControlPoint.RightHand,
                CharacterControlPoint.RightElbow);
            SolveLimb(
                pose,
                CharacterControlPoint.LeftFoot,
                CharacterControlPoint.LeftKnee);
            SolveLimb(
                pose,
                CharacterControlPoint.RightFoot,
                CharacterControlPoint.RightKnee);
        }

        private void SolveEndpoint(
            CharacterPoseBuffer pose,
            CharacterControlPoint point)
        {
            if (!TryGetActiveState(point, out var definition, out var state))
            {
                return;
            }

            var rotation = state.RotationActive
                ? state.TargetRotation
                : pose.GetWorldRotation(definition.TipIndex);
            CharacterTwoBoneSolver.Solve(
                pose,
                definition.RootIndex,
                definition.MidIndex,
                definition.TipIndex,
                state.Target,
                null,
                rotation,
                definition.TwoBoneSettings,
                poseLayer);
        }

        private void SolveShoulder(
            CharacterPoseBuffer pose,
            CharacterControlPoint point)
        {
            if (!TryGetActiveState(point, out var definition, out var state))
            {
                return;
            }

            var rootPosition = pose.GetWorldPosition(definition.RootIndex);
            var tipPosition = pose.GetWorldPosition(definition.TipIndex);
            var currentDirection = tipPosition - rootPosition;
            var desiredDirection = state.Target - rootPosition;
            if (currentDirection.sqrMagnitude < 0.00000001f ||
                desiredDirection.sqrMagnitude < 0.00000001f)
            {
                return;
            }

            var tipRotation = state.RotationActive
                ? state.TargetRotation
                : pose.GetWorldRotation(definition.TipIndex);
            var delta = Quaternion.FromToRotation(
                currentDirection,
                desiredDirection);
            ApplyWorldRotation(
                pose,
                definition.RootIndex,
                delta * pose.GetWorldRotation(definition.RootIndex));
            ApplyWorldRotation(pose, definition.TipIndex, tipRotation);
        }

        private void SolveLimb(
            CharacterPoseBuffer pose,
            CharacterControlPoint endpoint,
            CharacterControlPoint pole)
        {
            if (!definitions.TryGetValue(endpoint, out var definition))
            {
                return;
            }

            states.TryGetValue(endpoint, out var endpointState);
            states.TryGetValue(pole, out var poleState);
            if (!endpointState.Active && !poleState.Active)
            {
                return;
            }

            var target = endpointState.Active
                ? endpointState.Target
                : pose.GetWorldPosition(definition.TipIndex);
            var rotation = endpointState.Active && endpointState.RotationActive
                ? endpointState.TargetRotation
                : pose.GetWorldRotation(definition.TipIndex);
            CharacterTwoBoneSolver.Solve(
                pose,
                definition.RootIndex,
                definition.MidIndex,
                definition.TipIndex,
                target,
                poleState.Active ? poleState.Target : (Vector3?)null,
                rotation,
                definition.TwoBoneSettings,
                poseLayer);
        }

        private bool TryGetActiveState(
            CharacterControlPoint point,
            out ControlDefinition definition,
            out ControlState state)
        {
            if (definitions.TryGetValue(point, out definition) &&
                states.TryGetValue(point, out state) && state.Active)
            {
                return true;
            }

            definition = default;
            state = default;
            return false;
        }

        private void RestoreBaselines(CharacterPoseBuffer pose)
        {
            foreach (var pair in baselines)
            {
                var baseline = pair.Value;
                if ((baseline.Channels & CharacterPoseChannels.Position) != 0)
                {
                    pose.SetLocalPosition(pair.Key, baseline.LocalPosition);
                }

                if ((baseline.Channels & CharacterPoseChannels.Rotation) != 0)
                {
                    pose.SetLocalRotation(pair.Key, baseline.LocalRotation);
                }
            }
        }

        private void ApplyWorldPosition(
            CharacterPoseBuffer pose,
            int boneIndex,
            Vector3 value)
        {
            pose.SetWorldPosition(boneIndex, value);
            poseLayer.SetLocalPosition(boneIndex, pose.GetLocalPosition(boneIndex));
        }

        private void ApplyWorldRotation(
            CharacterPoseBuffer pose,
            int boneIndex,
            Quaternion value)
        {
            pose.SetWorldRotation(boneIndex, value);
            poseLayer.SetLocalRotation(boneIndex, pose.GetLocalRotation(boneIndex));
        }

        private void CaptureBaselines(ControlDefinition definition)
        {
            CaptureBaseline(definition.RootIndex, definition.RootChannels);
            CaptureBaseline(definition.MidIndex, definition.MidChannels);
            CaptureBaseline(definition.TipIndex, definition.TipChannels);
        }

        private void CaptureBaseline(int boneIndex, CharacterPoseChannels channels)
        {
            if (boneIndex < 0 || channels == CharacterPoseChannels.None)
            {
                return;
            }

            baselines.TryGetValue(boneIndex, out var baseline);
            var transform = model.Skeleton.Bones[boneIndex].Transform;
            var addedChannels = channels & ~baseline.Channels;
            if ((addedChannels & CharacterPoseChannels.Position) != 0)
            {
                baseline.LocalPosition = transform.localPosition;
            }

            if ((addedChannels & CharacterPoseChannels.Rotation) != 0)
            {
                baseline.LocalRotation = transform.localRotation;
            }

            baseline.Channels |= channels;
            baselines[boneIndex] = baseline;
        }

        private void RestoreReleasedBaselines()
        {
            var boneIndices = new List<int>(baselines.Keys);
            for (var index = 0; index < boneIndices.Count; index++)
            {
                var boneIndex = boneIndices[index];
                var baseline = baselines[boneIndex];
                var required = GetRequiredChannels(boneIndex);
                var released = baseline.Channels & ~required;
                RestoreTransformChannels(boneIndex, baseline, released);
                baseline.Channels &= required;
                if (baseline.Channels == CharacterPoseChannels.None)
                {
                    baselines.Remove(boneIndex);
                }
                else
                {
                    baselines[boneIndex] = baseline;
                }
            }
        }

        private CharacterPoseChannels GetRequiredChannels(int boneIndex)
        {
            var result = CharacterPoseChannels.None;
            for (var index = 0; index < controlPoints.Count; index++)
            {
                var point = controlPoints[index];
                if (!states[point].Active)
                {
                    continue;
                }

                var definition = definitions[point];
                if (definition.RootIndex == boneIndex)
                {
                    result |= definition.RootChannels;
                }

                if (definition.MidIndex == boneIndex)
                {
                    result |= definition.MidChannels;
                }

                if (definition.TipIndex == boneIndex)
                {
                    result |= definition.TipChannels;
                }

                if (states[point].RotationActive &&
                    definition.RotationIndex == boneIndex)
                {
                    result |= CharacterPoseChannels.Rotation;
                }
            }

            return result;
        }

        private void RestoreAllBaselinesToTransforms()
        {
            foreach (var pair in baselines)
            {
                RestoreTransformChannels(pair.Key, pair.Value, pair.Value.Channels);
            }
        }

        private void RestoreTransformChannels(
            int boneIndex,
            BoneBaseline baseline,
            CharacterPoseChannels channels)
        {
            if (model.Skeleton == null ||
                boneIndex < 0 || boneIndex >= model.Skeleton.BoneCount)
            {
                return;
            }

            var transform = model.Skeleton.Bones[boneIndex].Transform;
            if (transform == null)
            {
                return;
            }

            if ((channels & CharacterPoseChannels.Position) != 0)
            {
                transform.localPosition = baseline.LocalPosition;
            }

            if ((channels & CharacterPoseChannels.Rotation) != 0)
            {
                transform.localRotation = baseline.LocalRotation;
            }
        }

        private Vector3 GetBoneWorldPosition(int boneIndex)
        {
            var transform = model.Skeleton.Bones[boneIndex].Transform;
            return transform != null ? transform.position : Vector3.zero;
        }

        private Quaternion GetBoneWorldRotation(int boneIndex)
        {
            var transform = model.Skeleton.Bones[boneIndex].Transform;
            return transform != null ? transform.rotation : Quaternion.identity;
        }

        private void BuildDefinitions(CharacterSkeleton skeleton)
        {
            if (TryIndex(skeleton, HumanBodyBones.Hips, out var hips))
            {
                definitions.Add(
                    CharacterControlPoint.Hips,
                    ControlDefinition.Hips(hips));
            }

            if (TryChain(
                    skeleton,
                    HumanBodyBones.Spine,
                    HumanBodyBones.Chest,
                    HumanBodyBones.UpperChest,
                    out var spine,
                    out var chest,
                    out var upperChest))
            {
                definitions.Add(
                    CharacterControlPoint.Chest,
                    ControlDefinition.TwoBone(
                        spine,
                        chest,
                        upperChest,
                        CharacterTwoBoneSettings.CreateHumanoid(
                            skeleton,
                            spine,
                            chest,
                            upperChest,
                            HumanBodyBones.Chest)));
            }
            else if (TryChain(
                         skeleton,
                         HumanBodyBones.Hips,
                         HumanBodyBones.Spine,
                         HumanBodyBones.Chest,
                         out spine,
                         out chest,
                         out upperChest))
            {
                definitions.Add(
                    CharacterControlPoint.Chest,
                    ControlDefinition.TwoBone(
                        spine,
                        chest,
                        upperChest,
                        CharacterTwoBoneSettings.CreateHumanoid(
                            skeleton,
                            spine,
                            chest,
                            upperChest,
                            HumanBodyBones.Spine)));
            }

            var torso = TryIndex(
                skeleton,
                HumanBodyBones.UpperChest,
                out var resolvedTorso)
                ? resolvedTorso
                : TryIndex(skeleton, HumanBodyBones.Chest, out resolvedTorso)
                    ? resolvedTorso
                    : -1;
            if (torso >= 0 &&
                TryIndex(skeleton, HumanBodyBones.Neck, out var neck) &&
                TryIndex(skeleton, HumanBodyBones.Head, out var head))
            {
                definitions.Add(
                    CharacterControlPoint.Head,
                    ControlDefinition.TwoBone(
                        torso,
                        neck,
                        head,
                        CharacterTwoBoneSettings.CreateHumanoid(
                            skeleton,
                            torso,
                            neck,
                            head,
                            HumanBodyBones.Neck)));
            }

            // Shoulder targets stay unavailable until the inferred pose layer can
            // enforce clavicle limits without stretching the imported rig.

            AddLimb(
                skeleton,
                CharacterControlPoint.LeftHand,
                CharacterControlPoint.LeftElbow,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand);
            AddLimb(
                skeleton,
                CharacterControlPoint.RightHand,
                CharacterControlPoint.RightElbow,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand);
            AddLimb(
                skeleton,
                CharacterControlPoint.LeftFoot,
                CharacterControlPoint.LeftKnee,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot);
            AddLimb(
                skeleton,
                CharacterControlPoint.RightFoot,
                CharacterControlPoint.RightKnee,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot);
        }

        private void AddLimb(
            CharacterSkeleton skeleton,
            CharacterControlPoint endpoint,
            CharacterControlPoint pole,
            HumanBodyBones rootBone,
            HumanBodyBones midBone,
            HumanBodyBones tipBone)
        {
            if (TryChain(
                    skeleton,
                    rootBone,
                    midBone,
                    tipBone,
                    out var root,
                    out var mid,
                    out var tip))
            {
                var settings = CharacterTwoBoneSettings.CreateHumanoid(
                    skeleton,
                    root,
                    mid,
                    tip,
                    midBone);
                definitions.Add(
                    endpoint,
                    ControlDefinition.TwoBone(root, mid, tip, settings));
                definitions.Add(
                    pole,
                    ControlDefinition.Pole(root, mid, tip, settings));
            }
        }

        private void AddShoulder(
            CharacterSkeleton skeleton,
            CharacterControlPoint point,
            HumanBodyBones shoulderBone,
            HumanBodyBones upperArmBone)
        {
            if (TryIndex(skeleton, shoulderBone, out var shoulder) &&
                TryIndex(skeleton, upperArmBone, out var upperArm))
            {
                definitions.Add(
                    point,
                    ControlDefinition.OneBone(shoulder, upperArm));
            }
        }

        private static bool TryChain(
            CharacterSkeleton skeleton,
            HumanBodyBones rootBone,
            HumanBodyBones midBone,
            HumanBodyBones tipBone,
            out int root,
            out int mid,
            out int tip)
        {
            root = -1;
            mid = -1;
            tip = -1;
            return TryIndex(skeleton, rootBone, out root) &&
                   TryIndex(skeleton, midBone, out mid) &&
                   TryIndex(skeleton, tipBone, out tip);
        }

        private static bool TryIndex(
            CharacterSkeleton skeleton,
            HumanBodyBones bone,
            out int index)
        {
            return skeleton.TryGetBoneIndex(bone, out index);
        }

        private static float CalculateHeight(CharacterSkeleton skeleton)
        {
            if (!skeleton.TryGetTransform(HumanBodyBones.Head, out var head))
            {
                return 1.6f;
            }

            var footPosition = head.position - Vector3.up * 1.6f;
            var count = 0;
            if (skeleton.TryGetTransform(HumanBodyBones.LeftFoot, out var leftFoot))
            {
                footPosition = leftFoot.position;
                count++;
            }

            if (skeleton.TryGetTransform(HumanBodyBones.RightFoot, out var rightFoot))
            {
                footPosition = count == 0
                    ? rightFoot.position
                    : (footPosition + rightFoot.position) * 0.5f;
                count++;
            }

            return Mathf.Max(0.25f, Vector3.Distance(head.position, footPosition));
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            var squareMagnitude = value.x * value.x + value.y * value.y +
                                  value.z * value.z + value.w * value.w;
            return float.IsFinite(squareMagnitude) &&
                   squareMagnitude > 0.00000001f;
        }

        private sealed class ControlPoseModifier : ICharacterPoseModifier
        {
            private readonly CharacterControlRig owner;

            public ControlPoseModifier(CharacterControlRig owner)
            {
                this.owner = owner;
            }

            public int Order => CharacterPoseStages.ActionEditing;

            public bool Enabled => true;

            public void Evaluate(CharacterPoseBuffer pose)
            {
                owner.Evaluate(pose);
            }
        }

        private enum ControlSolver
        {
            Hips,
            OneBone,
            TwoBone,
            Pole,
        }

        private readonly struct ControlDefinition
        {
            private ControlDefinition(
                ControlSolver solver,
                int anchorIndex,
                int rootIndex,
                int midIndex,
                int tipIndex,
                int rotationIndex,
                CharacterPoseChannels rootChannels,
                CharacterPoseChannels midChannels,
                CharacterPoseChannels tipChannels,
                CharacterTwoBoneSettings twoBoneSettings = default)
            {
                Solver = solver;
                AnchorIndex = anchorIndex;
                RootIndex = rootIndex;
                MidIndex = midIndex;
                TipIndex = tipIndex;
                RotationIndex = rotationIndex;
                RootChannels = rootChannels;
                MidChannels = midChannels;
                TipChannels = tipChannels;
                TwoBoneSettings = twoBoneSettings;
            }

            public ControlSolver Solver { get; }
            public int AnchorIndex { get; }
            public int RootIndex { get; }
            public int MidIndex { get; }
            public int TipIndex { get; }
            public int RotationIndex { get; }
            public CharacterPoseChannels RootChannels { get; }
            public CharacterPoseChannels MidChannels { get; }
            public CharacterPoseChannels TipChannels { get; }
            public CharacterTwoBoneSettings TwoBoneSettings { get; }

            public static ControlDefinition Hips(int index)
            {
                return new ControlDefinition(
                    ControlSolver.Hips,
                    index,
                    index,
                    -1,
                    -1,
                    index,
                    CharacterPoseChannels.Position,
                    CharacterPoseChannels.None,
                    CharacterPoseChannels.None);
            }

            public static ControlDefinition TwoBone(
                int root,
                int mid,
                int tip,
                CharacterTwoBoneSettings settings)
            {
                return new ControlDefinition(
                    ControlSolver.TwoBone,
                    tip,
                    root,
                    mid,
                    tip,
                    tip,
                    CharacterPoseChannels.Rotation,
                    CharacterPoseChannels.Rotation,
                    CharacterPoseChannels.Rotation,
                    settings);
            }

            public static ControlDefinition OneBone(int root, int tip)
            {
                return new ControlDefinition(
                    ControlSolver.OneBone,
                    tip,
                    root,
                    -1,
                    tip,
                    tip,
                    CharacterPoseChannels.Rotation,
                    CharacterPoseChannels.None,
                    CharacterPoseChannels.Rotation);
            }

            public static ControlDefinition Pole(
                int root,
                int mid,
                int tip,
                CharacterTwoBoneSettings settings)
            {
                return new ControlDefinition(
                    ControlSolver.Pole,
                    mid,
                    root,
                    mid,
                    tip,
                    -1,
                    CharacterPoseChannels.Rotation,
                    CharacterPoseChannels.Rotation,
                    CharacterPoseChannels.Rotation,
                    settings);
            }
        }

        private struct ControlState
        {
            public bool Active;
            public Vector3 Target;
            public bool RotationActive;
            public Quaternion TargetRotation;
        }

        private struct BoneBaseline
        {
            public CharacterPoseChannels Channels;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }
    }
}
