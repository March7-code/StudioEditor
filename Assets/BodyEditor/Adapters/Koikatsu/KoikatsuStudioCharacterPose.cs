using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public sealed class KoikatsuStudioCharacterPose : MonoBehaviour
    {
        private const float Epsilon = 0.000001f;

        private static readonly string[] ikTargetNames =
        {
            "cf_t_hips(work)",
            "cf_t_shoulder_L(work)",
            "cf_t_elbo_L(work)",
            "cf_t_hand_L(work)",
            "cf_t_shoulder_R(work)",
            "cf_t_elbo_R(work)",
            "cf_t_hand_R(work)",
            "cf_t_waist_L(work)",
            "cf_t_knee_L(work)",
            "cf_t_leg_L(work)",
            "cf_t_waist_R(work)",
            "cf_t_knee_R(work)",
            "cf_t_leg_R(work)",
        };

        private IReadOnlyDictionary<string, Transform> skeleton;
        private FkOverride[] fkOverrides = Array.Empty<FkOverride>();
        private Transform[] ikTargets;
        private bool[] activeIk;
        private Dictionary<string, Transform> targetsByName;
        private CharacterPoseCoordinator poseCoordinator;
        private ICharacterPoseModifier fkModifier;
        private ICharacterPoseModifier ikModifier;
        private bool ikEnabled;
        private bool initialized;

        internal static void Apply(
            KoikatsuReferenceModelInstance character,
            KoikatsuSceneCharacter source,
            string abdataRoot,
            string modsRoot)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            var characterRoot = character.Root;
            if (characterRoot == null)
            {
                throw new InvalidOperationException(
                    "The Koikatsu character instance has already been disposed.");
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var catalog = KoikatsuListCatalog.Load(abdataRoot, modsRoot);
            var transforms = BuildTransformMap(characterRoot.transform);
            KoikatsuPhysicsRuntime.SetBustAllowed(
                characterRoot,
                !IsActive(source.ActiveFK, 2));
            ApplyBaseAnimation(
                character,
                source,
                abdataRoot,
                catalog,
                transforms);

            var hasFk = source.EnableFK && source.Bones.Count != 0;
            var hasIk = source.EnableIK && source.IkTargets.Count != 0;
            if (hasFk || hasIk)
            {
                var pose = characterRoot.AddComponent<
                    KoikatsuStudioCharacterPose>();
                pose.Initialize(character, source, catalog, transforms);
            }
        }

        public bool TryGetIkTarget(string name, out Transform value)
        {
            if (targetsByName != null)
            {
                return targetsByName.TryGetValue(name, out value);
            }

            value = null;
            return false;
        }

        private void OnDestroy()
        {
            initialized = false;
            if (poseCoordinator != null)
            {
                poseCoordinator.UnregisterModifier(fkModifier);
                poseCoordinator.UnregisterModifier(ikModifier);
            }

            poseCoordinator = null;
            fkModifier = null;
            ikModifier = null;
        }

        private void Initialize(
            KoikatsuReferenceModelInstance character,
            KoikatsuSceneCharacter source,
            KoikatsuListCatalog catalog,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            poseCoordinator = character.PoseCoordinator;
            if (poseCoordinator == null)
            {
                throw new InvalidOperationException(
                    "The character has no pose coordinator.");
            }

            skeleton = transforms;
            fkOverrides = BuildFkOverrides(
                source,
                catalog,
                transforms,
                poseCoordinator.Skeleton);
            activeIk = new bool[source.ActiveIK.Count];
            for (var index = 0; index < activeIk.Length; index++)
            {
                activeIk[index] = source.ActiveIK[index];
            }

            ikTargets = new Transform[ikTargetNames.Length];
            targetsByName = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            for (var id = 0; id < ikTargets.Length; id++)
            {
                if (!source.IkTargets.TryGetValue(id, out var saved))
                {
                    continue;
                }

                var targetObject = new GameObject(ikTargetNames[id]);
                var target = targetObject.transform;
                target.SetParent(transform, false);
                target.localPosition = saved.Position;
                target.localRotation = Quaternion.Euler(saved.Rotation);
                target.localScale = Vector3.one;
                ikTargets[id] = target;
                targetsByName.Add(target.name, target);
            }

            ikEnabled = source.EnableIK && source.IkTargets.Count != 0;
            initialized = true;
            if (fkOverrides.Length > 0)
            {
                fkModifier = new PoseModifier(
                    this,
                    CharacterPoseStages.ImportedFk,
                    false);
                poseCoordinator.RegisterModifier(fkModifier);
            }

            if (ikEnabled)
            {
                ikModifier = new PoseModifier(
                    this,
                    CharacterPoseStages.ImportedIk,
                    true);
                poseCoordinator.RegisterModifier(ikModifier);
            }

            poseCoordinator.EvaluateNow();
        }

        private static FkOverride[] BuildFkOverrides(
            KoikatsuSceneCharacter source,
            KoikatsuListCatalog catalog,
            IReadOnlyDictionary<string, Transform> transforms,
            CharacterSkeleton poseSkeleton)
        {
            if (!source.EnableFK || source.Bones.Count == 0)
            {
                return Array.Empty<FkOverride>();
            }

            var result = new List<FkOverride>(source.Bones.Count);
            foreach (var pair in source.Bones)
            {
                if (!catalog.TryGetStudioBone(pair.Key, out var entry) ||
                    !IsFkGroupActive(entry.Group, source.ActiveFK) ||
                    !transforms.TryGetValue(entry.BoneName, out var target) ||
                    !poseSkeleton.TryGetBoneIndex(target, out var boneIndex))
                {
                    continue;
                }

                result.Add(new FkOverride(
                    boneIndex,
                    Quaternion.Euler(pair.Value.Rotation)));
            }

            return result.ToArray();
        }

        private void ApplyFk(CharacterPoseBuffer pose)
        {
            for (var index = 0; index < fkOverrides.Length; index++)
            {
                pose.SetLocalRotation(
                    fkOverrides[index].BoneIndex,
                    fkOverrides[index].Rotation);
            }
        }

        private static void ApplyBaseAnimation(
            KoikatsuReferenceModelInstance character,
            KoikatsuSceneCharacter source,
            string abdataRoot,
            KoikatsuListCatalog catalog,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            if (!catalog.TryGetStudioAnimation(
                    source.AnimationGroup,
                    source.AnimationCategory,
                    source.AnimationNo,
                    source.AnimationModGuid,
                    out var entry))
            {
                Debug.LogWarning(
                    "Could not restore Koikatsu Studio animation " +
                    $"({source.AnimationGroup}, {source.AnimationCategory}, " +
                    $"{source.AnimationNo}): the animation list entry was not found.",
                    character.Root);
                return;
            }

            if (!transforms.TryGetValue("p_cf_body_bone", out var bodySkeleton))
            {
                Debug.LogWarning(
                    "Could not restore Koikatsu Studio animation: the character " +
                    "body skeleton was not found.",
                    character.Root);
                return;
            }

            var animator = bodySkeleton.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning(
                    "Could not restore Koikatsu Studio animation: the body " +
                    "skeleton has no Animator.",
                    character.Root);
                return;
            }

            KoikatsuAssetBundleLease lease = null;
            try
            {
                var bundleSources = catalog.ResolveBundleCandidates(
                    abdataRoot,
                    entry.BundlePath,
                    entry.Archive);
                lease = KoikatsuVirtualAssetLoader
                    .AcquireAsset<RuntimeAnimatorController>(
                        bundleSources,
                        entry.ControllerName,
                        out var controller,
                        out _);
                if (controller == null)
                {
                    Debug.LogWarning(
                        "Could not restore Koikatsu Studio animation " +
                        $"'{entry.Name}': controller '{entry.ControllerName}' " +
                        $"was not found in '{entry.BundlePath}'.",
                        character.Root);
                    return;
                }

                animator.runtimeAnimatorController = controller;
                // The body skeleton is instantiated before its scene animation
                // controller is known. Rebind the cloned hierarchy so all
                // humanoid/generic animation paths resolve against this copy,
                // matching the binding performed by ChaControl at load time.
                animator.Rebind();
                animator.applyRootMotion = false;
                animator.speed = source.AnimationSpeed;
                animator.Play(
                    entry.StateName,
                    0,
                    source.AnimationNormalizedTime);
                animator.Update(0f);

                character.AddBundleLease(lease);
                lease = null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not restore Koikatsu Studio animation " +
                    $"'{entry.Name}': {exception.Message}",
                    character.Root);
            }
            finally
            {
                lease?.Dispose();
            }
        }

        private void SolveIk(CharacterPoseBuffer pose)
        {
            if (IsActive(activeIk, 0) &&
                TryGetPoseBone("cf_j_hips", out var hips) &&
                TryGetTarget(0, out var bodyTarget))
            {
                pose.SetWorldPosition(hips, bodyTarget.position);
            }

            if (IsActive(activeIk, 4))
            {
                SolveLimb(
                    pose,
                    "cf_j_shoulder_L",
                    "cf_j_arm00_L",
                    "cf_j_forearm01_L",
                    "cf_j_hand_L",
                    1,
                    2,
                    3);
            }

            if (IsActive(activeIk, 3))
            {
                SolveLimb(
                    pose,
                    "cf_j_shoulder_R",
                    "cf_j_arm00_R",
                    "cf_j_forearm01_R",
                    "cf_j_hand_R",
                    4,
                    5,
                    6);
            }

            if (IsActive(activeIk, 2))
            {
                SolveLimb(
                    pose,
                    null,
                    "cf_j_thigh00_L",
                    "cf_j_leg01_L",
                    "cf_j_foot_L",
                    7,
                    8,
                    9);
            }

            if (IsActive(activeIk, 1))
            {
                SolveLimb(
                    pose,
                    null,
                    "cf_j_thigh00_R",
                    "cf_j_leg01_R",
                    "cf_j_foot_R",
                    10,
                    11,
                    12);
            }
        }

        private void SolveLimb(
            CharacterPoseBuffer pose,
            string shoulderName,
            string upperName,
            string lowerName,
            string tipName,
            int baseTargetId,
            int poleTargetId,
            int tipTargetId)
        {
            if (!TryGetPoseBone(upperName, out var upper) ||
                !TryGetPoseBone(lowerName, out var lower) ||
                !TryGetPoseBone(tipName, out var tip) ||
                !TryGetTarget(tipTargetId, out var tipTarget))
            {
                return;
            }

            if (!string.IsNullOrEmpty(shoulderName) &&
                TryGetPoseBone(shoulderName, out var shoulder) &&
                TryGetTarget(baseTargetId, out var shoulderTarget))
            {
                RotateToward(
                    pose,
                    shoulder,
                    upper,
                    shoulderTarget.position);
            }

            var pole = pose.GetWorldPosition(lower);
            if (TryGetTarget(poleTargetId, out var poleTarget))
            {
                pole = poleTarget.position;
            }

            SolveTwoBone(
                pose,
                upper,
                lower,
                tip,
                tipTarget.position,
                pole);
            pose.SetWorldRotation(tip, tipTarget.rotation);
        }

        private bool TryGetPoseBone(string name, out int boneIndex)
        {
            if (TryGetTransform(skeleton, name, out var transform) &&
                poseCoordinator.Skeleton.TryGetBoneIndex(
                    transform,
                    out boneIndex))
            {
                return true;
            }

            boneIndex = -1;
            return false;
        }

        private bool TryGetTarget(int id, out Transform value)
        {
            if (ikTargets != null && id >= 0 && id < ikTargets.Length)
            {
                value = ikTargets[id];
                return value != null;
            }

            value = null;
            return false;
        }

        private static void SolveTwoBone(
            CharacterPoseBuffer pose,
            int upper,
            int lower,
            int tip,
            Vector3 target,
            Vector3 pole)
        {
            var upperPosition = pose.GetWorldPosition(upper);
            var lowerPosition = pose.GetWorldPosition(lower);
            var tipPosition = pose.GetWorldPosition(tip);
            var upperLength = Vector3.Distance(upperPosition, lowerPosition);
            var lowerLength = Vector3.Distance(lowerPosition, tipPosition);
            var targetVector = target - upperPosition;
            if (upperLength < Epsilon || lowerLength < Epsilon ||
                targetVector.sqrMagnitude < Epsilon)
            {
                return;
            }

            var targetDirection = targetVector.normalized;
            var targetDistance = Mathf.Clamp(
                targetVector.magnitude,
                Mathf.Abs(upperLength - lowerLength) + Epsilon,
                upperLength + lowerLength - Epsilon);
            var bendDirection = Vector3.ProjectOnPlane(
                pole - upperPosition,
                targetDirection);
            if (bendDirection.sqrMagnitude < Epsilon)
            {
                bendDirection = Vector3.ProjectOnPlane(
                    lowerPosition - upperPosition,
                    targetDirection);
            }

            if (bendDirection.sqrMagnitude < Epsilon)
            {
                bendDirection = Vector3.Cross(
                    targetDirection,
                    Vector3.up);
                if (bendDirection.sqrMagnitude < Epsilon)
                {
                    bendDirection = Vector3.Cross(
                        targetDirection,
                        Vector3.right);
                }
            }

            bendDirection.Normalize();
            var along =
                (upperLength * upperLength - lowerLength * lowerLength +
                 targetDistance * targetDistance) /
                (2f * targetDistance);
            var height = Mathf.Sqrt(Mathf.Max(
                0f,
                upperLength * upperLength - along * along));
            var desiredLowerPosition = upperPosition +
                                       targetDirection * along +
                                       bendDirection * height;

            RotateToward(pose, upper, lower, desiredLowerPosition);
            RotateToward(pose, lower, tip, target);
        }

        private static void RotateToward(
            CharacterPoseBuffer pose,
            int joint,
            int child,
            Vector3 targetChildPosition)
        {
            var jointPosition = pose.GetWorldPosition(joint);
            var currentDirection =
                pose.GetWorldPosition(child) - jointPosition;
            var targetDirection = targetChildPosition - jointPosition;
            if (currentDirection.sqrMagnitude < Epsilon ||
                targetDirection.sqrMagnitude < Epsilon)
            {
                return;
            }

            pose.SetWorldRotation(
                joint,
                Quaternion.FromToRotation(
                    currentDirection,
                    targetDirection) * pose.GetWorldRotation(joint));
        }

        private static bool IsFkGroupActive(
            int group,
            IReadOnlyList<bool> activeGroups)
        {
            int index;
            switch (group)
            {
                case 7:
                case 8:
                case 9:
                    index = 0;
                    break;
                case 10:
                    index = 1;
                    break;
                case 11:
                case 12:
                    index = 2;
                    break;
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                    index = 3;
                    break;
                case 5:
                    index = 4;
                    break;
                case 6:
                    index = 5;
                    break;
                case 13:
                    index = 6;
                    break;
                default:
                    return false;
            }

            return IsActive(activeGroups, index);
        }

        private static bool IsActive(
            IReadOnlyList<bool> values,
            int index)
        {
            return values != null && index >= 0 && index < values.Count &&
                   values[index];
        }

        private static bool TryGetTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name,
            out Transform value)
        {
            if (!string.IsNullOrEmpty(name))
            {
                return transforms.TryGetValue(name, out value);
            }

            value = null;
            return false;
        }

        private static IReadOnlyDictionary<string, Transform> BuildTransformMap(
            Transform root)
        {
            var values = root.GetComponentsInChildren<Transform>(true);
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            for (var index = 0; index < values.Length; index++)
            {
                if (!result.ContainsKey(values[index].name))
                {
                    result.Add(values[index].name, values[index]);
                }
            }

            return result;
        }

        private readonly struct FkOverride
        {
            public FkOverride(int boneIndex, Quaternion rotation)
            {
                BoneIndex = boneIndex;
                Rotation = rotation;
            }

            public int BoneIndex { get; }

            public Quaternion Rotation { get; }
        }

        private sealed class PoseModifier : ICharacterPoseModifier
        {
            private readonly KoikatsuStudioCharacterPose owner;
            private readonly bool solveIk;

            public PoseModifier(
                KoikatsuStudioCharacterPose owner,
                int order,
                bool solveIk)
            {
                this.owner = owner;
                this.solveIk = solveIk;
                Order = order;
            }

            public int Order { get; }

            public bool Enabled => owner != null && owner.initialized &&
                                   owner.isActiveAndEnabled;

            public void Evaluate(CharacterPoseBuffer pose)
            {
                if (solveIk)
                {
                    owner.SolveIk(pose);
                }
                else
                {
                    owner.ApplyFk(pose);
                }
            }
        }
    }
}
