using System;
using System.Collections.Generic;
using System.IO;
using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    [DefaultExecutionOrder(31000)]
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
        private bool[] activeFk;
        private Dictionary<string, Transform> targetsByName;
        private ICharacterPosePipeline poseCoordinator;
        private ICharacterPoseModifier fkModifier;
        private ICharacterPoseModifier ikModifier;
        private KoikatsuStudioFinalIkRig finalIkRig;
        private CharacterKinematicModes supportedKinematicModes;
        private CharacterKinematicModes activeKinematicModes;
        private bool initialized;

        public CharacterKinematicModes SupportedKinematicModes =>
            supportedKinematicModes;

        public CharacterKinematicMode KinematicMode
        {
            get
            {
                if (activeKinematicModes ==
                    CharacterKinematicModes.ForwardKinematics)
                {
                    return CharacterKinematicMode.ForwardKinematics;
                }

                if (activeKinematicModes ==
                    CharacterKinematicModes.InverseKinematics)
                {
                    return CharacterKinematicMode.InverseKinematics;
                }

                return CharacterKinematicMode.None;
            }
        }

        public CharacterKinematicModes ActiveKinematicModes =>
            activeKinematicModes;

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
            ApplyBaseAnimation(
                character,
                source,
                abdataRoot,
                catalog,
                transforms);
            KoikatsuCharacterAssembler.ApplyImportedExpression(
                character,
                source.Card?.Status,
                catalog);
            ApplySceneExpression(character, source, transforms);
            ApplyHandPoses(character, source);

            var hasFk = source.Bones.Count != 0;
            var hasIk = source.IkTargets.Count != 0;
            if (hasFk || hasIk)
            {
                var pose = characterRoot.AddComponent<
                    KoikatsuStudioCharacterPose>();
                pose.Initialize(character, source, catalog, transforms);
            }
        }

        private static void ApplySceneExpression(
            KoikatsuReferenceModelInstance character,
            KoikatsuSceneCharacter source,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            var mouth = character.Controls?.Mouth;
            if (mouth != null)
            {
                mouth.SetFixedOpenRate(source.MouthOpen);
            }

            var eyeLook = character.Controls?.Eyes?.Look;
            var koikatsuEyeLook = eyeLook as CharacterEyeLookController;
            var data = source.EyeLookData;
            if (koikatsuEyeLook != null &&
                data != null &&
                data.Length >= 32)
            {
                try
                {
                    using (var stream = new MemoryStream(data, false))
                    using (var reader = new BinaryReader(stream))
                    {
                        var left = ReadQuaternion(reader);
                        var right = ReadQuaternion(reader);
                        koikatsuEyeLook.SetFixedLocalRotations(left, right);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is ArgumentException)
                {
                    Debug.LogWarning(
                        "Could not restore the Koikatsu Studio eye pose: " +
                        exception.Message,
                        character.Root);
                }
            }

            var lookAtTarget = RestoreLookAtTarget(
                character.Root,
                source.LookAtTarget,
                transforms);
            if (eyeLook != null &&
                source.Card?.Status?.EyesLookPattern == 4)
            {
                eyeLook.SetManualTarget(lookAtTarget);
            }
        }

        private static Transform RestoreLookAtTarget(
            GameObject characterRoot,
            KoikatsuSceneBone source,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            if (characterRoot == null || source == null ||
                transforms == null ||
                !transforms.TryGetValue("cf_j_head", out var head) ||
                head == null)
            {
                return null;
            }

            var target = head.Find("Look At Target");
            if (target == null)
            {
                var targetObject = new GameObject("Look At Target");
                target = targetObject.transform;
                target.SetParent(head, false);
            }

            target.localPosition = source.Position;
            target.localRotation = Quaternion.Euler(source.Rotation);
            target.localScale = source.Scale;
            return target;
        }

        private static Quaternion ReadQuaternion(BinaryReader reader)
        {
            return new Quaternion(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
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

        public void SetKinematicMode(CharacterKinematicMode mode)
        {
            if (mode != CharacterKinematicMode.None && !SupportsMode(mode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "The imported character does not support this kinematic mode.");
            }

            var modes = ModeFlag(mode);
            if (activeKinematicModes == modes)
            {
                return;
            }

            activeKinematicModes = modes;
            UpdateKinematicState();
            poseCoordinator?.EvaluateNow();
        }

        public void SetKinematicModeActive(
            CharacterKinematicMode mode,
            bool active)
        {
            var modeFlag = ModeFlag(mode);
            if (modeFlag == CharacterKinematicModes.None)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (active && (supportedKinematicModes & modeFlag) == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "The imported character does not support this " +
                    "kinematic mode.");
            }

            var next = active
                ? modeFlag
                : activeKinematicModes & ~modeFlag;
            if (next == activeKinematicModes)
            {
                return;
            }

            activeKinematicModes = next;
            UpdateKinematicState();
            poseCoordinator?.EvaluateNow();
        }

        public CharacterKinematicGroups GetSupportedGroups(
            CharacterKinematicMode mode)
        {
            if (mode == CharacterKinematicMode.ForwardKinematics)
            {
                var result = CharacterKinematicGroups.None;
                for (var index = 0; index < fkOverrides.Length; index++)
                {
                    result |= FkGroupAt(fkOverrides[index].GroupIndex);
                }

                return result;
            }

            if (mode == CharacterKinematicMode.InverseKinematics)
            {
                var result = CharacterKinematicGroups.None;
                for (var index = 0; index < 5; index++)
                {
                    if (HasIkGroup(index))
                    {
                        result |= IkGroupAt(index);
                    }
                }

                return result;
            }

            return CharacterKinematicGroups.None;
        }

        public CharacterKinematicGroups GetActiveGroups(
            CharacterKinematicMode mode)
        {
            var values = mode == CharacterKinematicMode.ForwardKinematics
                ? activeFk
                : mode == CharacterKinematicMode.InverseKinematics
                    ? activeIk
                    : null;
            var count = mode == CharacterKinematicMode.ForwardKinematics
                ? 7
                : mode == CharacterKinematicMode.InverseKinematics
                    ? 5
                    : 0;
            var result = CharacterKinematicGroups.None;
            for (var index = 0; index < count; index++)
            {
                if (IsActive(values, index))
                {
                    result |= mode == CharacterKinematicMode.ForwardKinematics
                        ? FkGroupAt(index)
                        : IkGroupAt(index);
                }
            }

            return result;
        }

        public void SetGroupActive(
            CharacterKinematicMode mode,
            CharacterKinematicGroups group,
            bool active)
        {
            var values = mode == CharacterKinematicMode.ForwardKinematics
                ? activeFk
                : mode == CharacterKinematicMode.InverseKinematics
                    ? activeIk
                    : null;
            var count = mode == CharacterKinematicMode.ForwardKinematics
                ? 7
                : mode == CharacterKinematicMode.InverseKinematics
                    ? 5
                    : 0;
            if (values == null || group == CharacterKinematicGroups.None)
            {
                return;
            }

            for (var index = 0; index < count && index < values.Length; index++)
            {
                var candidate = mode == CharacterKinematicMode.ForwardKinematics
                    ? FkGroupAt(index)
                    : IkGroupAt(index);
                if ((group & candidate) != 0)
                {
                    values[index] = active;
                }
            }

            UpdateKinematicState();
            poseCoordinator?.EvaluateNow();
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
            finalIkRig?.Disable();
            finalIkRig = null;
        }

        private void OnEnable()
        {
            if (initialized)
            {
                UpdateKinematicState();
            }
        }

        private void Update()
        {
            if (initialized)
            {
                // Match SolverManager's order: clear the previous IK result in
                // Update, then let Animator and Timeline establish this frame's
                // pose before Final IK runs at the end of the pose pipeline.
                finalIkRig?.FixTransforms();
            }
        }

        private void LateUpdate()
        {
            SolveFinalIk();
        }

        private void OnDisable()
        {
            finalIkRig?.Disable();
        }

        private void Initialize(
            KoikatsuReferenceModelInstance character,
            KoikatsuSceneCharacter source,
            KoikatsuListCatalog catalog,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            poseCoordinator = character.Controls?.Pose?.Pipeline;
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
            activeFk = new bool[source.ActiveFK.Count];
            for (var index = 0; index < activeFk.Length; index++)
            {
                activeFk[index] = source.ActiveFK[index];
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

            supportedKinematicModes = CharacterKinematicModes.None;
            if (fkOverrides.Length > 0)
            {
                supportedKinematicModes |=
                    CharacterKinematicModes.ForwardKinematics;
            }

            var ikAvailable = source.IkTargets.Count != 0;
            if (ikAvailable)
            {
                supportedKinematicModes |=
                    CharacterKinematicModes.InverseKinematics;
            }

            activeKinematicModes = ResolveInitialModes(source);
            var useFallbackIk = ikAvailable;
            if (ikAvailable)
            {
                if (KoikatsuStudioFinalIkRig.TryCreate(
                        gameObject,
                        transforms,
                        ikTargets,
                        out finalIkRig,
                        out var finalIkError))
                {
                    useFallbackIk = false;
                }
                else
                {
                    Debug.LogWarning(
                        "Could not initialize Final IK for the imported " +
                        $"Koikatsu character; using the legacy limb solver: " +
                        finalIkError,
                        gameObject);
                }
            }

            initialized = true;
            UpdateKinematicState();
            if (fkOverrides.Length > 0)
            {
                fkModifier = new PoseModifier(
                    this,
                    CharacterPoseStages.ImportedFk,
                    false);
                poseCoordinator.RegisterModifier(fkModifier);
            }

            if (useFallbackIk)
            {
                ikModifier = new PoseModifier(
                    this,
                    CharacterPoseStages.ImportedIk,
                    true);
                poseCoordinator.RegisterModifier(ikModifier);
            }

            poseCoordinator.EvaluateNow();
            SolveFinalIk();
        }

        private void SolveFinalIk()
        {
            if (initialized)
            {
                finalIkRig?.Solve();
            }
        }

        private CharacterKinematicModes ResolveInitialModes(
            KoikatsuSceneCharacter source)
        {
            if (source.EnableIK &&
                SupportsMode(CharacterKinematicMode.InverseKinematics))
            {
                return CharacterKinematicModes.InverseKinematics;
            }

            if (source.EnableFK &&
                SupportsMode(CharacterKinematicMode.ForwardKinematics))
            {
                return CharacterKinematicModes.ForwardKinematics;
            }

            return CharacterKinematicModes.None;
        }

        private bool SupportsMode(CharacterKinematicMode mode)
        {
            var required = ModeFlag(mode);
            return required == CharacterKinematicModes.None ||
                   (supportedKinematicModes & required) != 0;
        }

        private static CharacterKinematicModes ModeFlag(
            CharacterKinematicMode mode)
        {
            return mode == CharacterKinematicMode.ForwardKinematics
                ? CharacterKinematicModes.ForwardKinematics
                : mode == CharacterKinematicMode.InverseKinematics
                    ? CharacterKinematicModes.InverseKinematics
                    : CharacterKinematicModes.None;
        }

        private static FkOverride[] BuildFkOverrides(
            KoikatsuSceneCharacter source,
            KoikatsuListCatalog catalog,
            IReadOnlyDictionary<string, Transform> transforms,
            CharacterSkeleton poseSkeleton)
        {
            if (source.Bones.Count == 0)
            {
                return Array.Empty<FkOverride>();
            }

            var result = new List<FkOverride>(source.Bones.Count);
            foreach (var pair in source.Bones)
            {
                if (!catalog.TryGetStudioBone(pair.Key, out var entry) ||
                    !TryGetFkGroupIndex(entry.Group, out var groupIndex) ||
                    !transforms.TryGetValue(entry.BoneName, out var target) ||
                    !poseSkeleton.TryGetBoneIndex(target, out var boneIndex))
                {
                    continue;
                }

                result.Add(new FkOverride(
                    boneIndex,
                    Quaternion.Euler(pair.Value.Rotation),
                    groupIndex));
            }

            return result.ToArray();
        }

        private void ApplyFk(CharacterPoseBuffer pose)
        {
            for (var index = 0; index < fkOverrides.Length; index++)
            {
                if (!IsActive(activeFk, fkOverrides[index].GroupIndex))
                {
                    continue;
                }

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
            KoikatsuAssetBundleLease overrideLease = null;
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

                RuntimeAnimatorController appliedController = controller;
                if (entry.IsHAnimation &&
                    !string.IsNullOrWhiteSpace(entry.OverrideBundlePath) &&
                    entry.OverrideBundlePath != "0" &&
                    !string.IsNullOrWhiteSpace(entry.OverrideControllerName) &&
                    entry.OverrideControllerName != "0")
                {
                    var overrideSources = catalog.ResolveBundleCandidates(
                        abdataRoot,
                        entry.OverrideBundlePath,
                        entry.Archive);
                    overrideLease = KoikatsuVirtualAssetLoader
                        .AcquireAsset<RuntimeAnimatorController>(
                            overrideSources,
                            entry.OverrideControllerName,
                            out var overrideController,
                            out _);
                    if (overrideController != null)
                    {
                        var combined = CreateAnimatorOverrideController(
                            controller,
                            overrideController);
                        character.AddRuntimeObject(combined);
                        appliedController = combined;
                    }
                    else
                    {
                        Debug.LogWarning(
                            "Could not restore Koikatsu Studio H animation " +
                            $"'{entry.Name}': override controller " +
                            $"'{entry.OverrideControllerName}' was not found.",
                            character.Root);
                    }
                }

                animator.runtimeAnimatorController = appliedController;
                // The body skeleton is instantiated before its scene animation
                // controller is known. Rebind the cloned hierarchy so all
                // humanoid/generic animation paths resolve against this copy,
                // matching the binding performed by ChaControl at load time.
                animator.Rebind();
                animator.applyRootMotion = false;
                animator.speed = source.AnimationSpeed;
                ApplyAnimatorParameters(animator, source, entry);
                animator.Play(
                    entry.StateName,
                    0,
                    source.AnimationNormalizedTime);
                animator.Update(0f);

                character.AddBundleLease(lease);
                lease = null;
                if (overrideLease != null)
                {
                    character.AddBundleLease(overrideLease);
                    overrideLease = null;
                }
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
                overrideLease?.Dispose();
            }
        }

        private static AnimatorOverrideController
            CreateAnimatorOverrideController(
                RuntimeAnimatorController source,
                RuntimeAnimatorController overrides)
        {
            var result = new AnimatorOverrideController(source)
            {
                name = overrides.name,
            };
            var clips = overrides.animationClips;
            for (var index = 0; index < clips.Length; index++)
            {
                if (clips[index] != null)
                {
                    result[clips[index].name] = clips[index];
                }
            }

            return result;
        }

        private static void ApplyAnimatorParameters(
            Animator animator,
            KoikatsuSceneCharacter source,
            KoikatsuStudioAnimationEntry entry)
        {
            var bodyShape = source.Card?.Body?.ShapeValues;
            if (bodyShape != null && bodyShape.Count > 0)
            {
                SetFloatIfPresent(animator, "height", bodyShape[0]);
            }

            if (!entry.IsHAnimation)
            {
                return;
            }

            if (entry.IsMotion)
            {
                SetFloatIfPresent(
                    animator,
                    "motion",
                    source.AnimationPattern);
            }

            if (bodyShape != null && bodyShape.Count > 4)
            {
                SetFloatIfPresent(animator, "Breast", bodyShape[4]);
            }

            var heightParameter = HasFloatParameter(animator, "height1")
                ? "height1"
                : "height";
            SetFloatIfPresent(
                animator,
                heightParameter,
                source.AnimationOptionParam1);
            var breastParameter = HasFloatParameter(animator, "Breast1")
                ? "Breast1"
                : "Breast";
            SetFloatIfPresent(
                animator,
                breastParameter,
                source.AnimationOptionParam2);
        }

        private static bool SetFloatIfPresent(
            Animator animator,
            string name,
            float value)
        {
            if (!HasFloatParameter(animator, name))
            {
                return false;
            }

            animator.SetFloat(Animator.StringToHash(name), value);
            return true;
        }

        private static bool HasFloatParameter(Animator animator, string name)
        {
            var hash = Animator.StringToHash(name);
            var parameters = animator.parameters;
            for (var index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].nameHash == hash &&
                    parameters[index].type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyHandPoses(
            KoikatsuReferenceModelInstance character,
            KoikatsuSceneCharacter source)
        {
            var hands = character.Controls?.Hands;
            if (hands == null)
            {
                return;
            }

            ApplyHandPose(
                hands,
                CharacterHand.Left,
                source.LeftHandPattern);
            ApplyHandPose(
                hands,
                CharacterHand.Right,
                source.RightHandPattern);
        }

        private static void ApplyHandPose(
            ICharacterHandPoseController controller,
            CharacterHand hand,
            int pattern)
        {
            if (pattern >= 0 && pattern < controller.GetPoseCount(hand))
            {
                controller.SetPose(hand, pattern);
            }
        }

        private void SolveIk(CharacterPoseBuffer pose)
        {
            if (IsActive(activeIk, 0) &&
                TryGetPoseBone("cf_j_hips", out var hips) &&
                TryGetTarget(0, out var bodyTarget))
            {
                pose.SetWorldPosition(hips, bodyTarget.position);
                AlignLowerBodyFrame(pose, hips, bodyTarget.position);
                AlignUpperBodyFrame(pose, hips, bodyTarget.position);
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

        private void AlignLowerBodyFrame(
            CharacterPoseBuffer pose,
            int hips,
            Vector3 desiredHipsPosition)
        {
            if (!IsActive(activeIk, 1) || !IsActive(activeIk, 2) ||
                !TryGetPoseBone("cf_j_thigh00_L", out var leftThigh) ||
                !TryGetPoseBone("cf_j_thigh00_R", out var rightThigh) ||
                !TryGetTarget(7, out var leftTarget) ||
                !TryGetTarget(10, out var rightTarget))
            {
                return;
            }

            var currentLeft = pose.GetWorldPosition(leftThigh);
            var currentRight = pose.GetWorldPosition(rightThigh);
            var currentCenter = (currentLeft + currentRight) * 0.5f;
            var desiredCenter =
                (leftTarget.position + rightTarget.position) * 0.5f;
            if (!TryCreateBodyFrame(
                    currentRight - currentLeft,
                    pose.GetWorldPosition(hips) - currentCenter,
                    out var currentFrame) ||
                !TryCreateBodyFrame(
                    rightTarget.position - leftTarget.position,
                    desiredHipsPosition - desiredCenter,
                    out var desiredFrame))
            {
                return;
            }

            var correction = desiredFrame * Quaternion.Inverse(currentFrame);
            pose.SetWorldRotation(
                hips,
                correction * pose.GetWorldRotation(hips));
        }

        private void AlignUpperBodyFrame(
            CharacterPoseBuffer pose,
            int hips,
            Vector3 desiredHipsPosition)
        {
            if (!IsActive(activeIk, 3) || !IsActive(activeIk, 4) ||
                !TryGetPoseBone("cf_j_arm00_L", out var leftArm) ||
                !TryGetPoseBone("cf_j_arm00_R", out var rightArm) ||
                !TryGetTarget(1, out var leftTarget) ||
                !TryGetTarget(4, out var rightTarget))
            {
                return;
            }

            var currentLeft = pose.GetWorldPosition(leftArm);
            var currentRight = pose.GetWorldPosition(rightArm);
            var currentCenter = (currentLeft + currentRight) * 0.5f;
            var desiredCenter =
                (leftTarget.position + rightTarget.position) * 0.5f;
            if (!TryCreateBodyFrame(
                    currentRight - currentLeft,
                    currentCenter - pose.GetWorldPosition(hips),
                    out var currentFrame) ||
                !TryCreateBodyFrame(
                    rightTarget.position - leftTarget.position,
                    desiredCenter - desiredHipsPosition,
                    out var desiredFrame))
            {
                return;
            }

            var correction = desiredFrame * Quaternion.Inverse(currentFrame);
            ApplyDistributedWorldRotation(
                pose,
                correction,
                "cf_j_spine01",
                "cf_j_spine02",
                "cf_j_spine03");
        }

        private void ApplyDistributedWorldRotation(
            CharacterPoseBuffer pose,
            Quaternion correction,
            params string[] boneNames)
        {
            var available = 0;
            for (var index = 0; index < boneNames.Length; index++)
            {
                if (TryGetPoseBone(boneNames[index], out _))
                {
                    available++;
                }
            }

            if (available == 0)
            {
                return;
            }

            var step = Quaternion.Slerp(
                Quaternion.identity,
                correction,
                1f / available);
            for (var index = 0; index < boneNames.Length; index++)
            {
                if (TryGetPoseBone(boneNames[index], out var bone))
                {
                    pose.SetWorldRotation(
                        bone,
                        step * pose.GetWorldRotation(bone));
                }
            }
        }

        private static bool TryCreateBodyFrame(
            Vector3 side,
            Vector3 up,
            out Quaternion frame)
        {
            if (side.sqrMagnitude < Epsilon || up.sqrMagnitude < Epsilon)
            {
                frame = Quaternion.identity;
                return false;
            }

            side.Normalize();
            up = Vector3.ProjectOnPlane(up, side);
            if (up.sqrMagnitude < Epsilon)
            {
                frame = Quaternion.identity;
                return false;
            }

            up.Normalize();
            var forward = Vector3.Cross(side, up);
            if (forward.sqrMagnitude < Epsilon)
            {
                frame = Quaternion.identity;
                return false;
            }

            frame = Quaternion.LookRotation(forward.normalized, up);
            return true;
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

        private void UpdateFkPhysicsState()
        {
            KoikatsuPhysicsRuntime.SetBustAllowed(
                gameObject,
                (activeKinematicModes &
                 CharacterKinematicModes.ForwardKinematics) == 0 ||
                !IsActive(activeFk, 2));
        }

        private void UpdateKinematicState()
        {
            UpdateFkPhysicsState();
            finalIkRig?.SetState(
                (activeKinematicModes &
                 CharacterKinematicModes.InverseKinematics) != 0,
                activeIk);
        }

        private static bool TryGetFkGroupIndex(int group, out int index)
        {
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
                    index = -1;
                    return false;
            }

            return true;
        }

        private bool HasIkGroup(int index)
        {
            switch (index)
            {
                case 0:
                    return TryGetTarget(0, out _);
                case 1:
                    return TryGetTarget(12, out _);
                case 2:
                    return TryGetTarget(9, out _);
                case 3:
                    return TryGetTarget(6, out _);
                case 4:
                    return TryGetTarget(3, out _);
                default:
                    return false;
            }
        }

        private static CharacterKinematicGroups IkGroupAt(int index)
        {
            switch (index)
            {
                case 0: return CharacterKinematicGroups.Body;
                case 1: return CharacterKinematicGroups.RightLeg;
                case 2: return CharacterKinematicGroups.LeftLeg;
                case 3: return CharacterKinematicGroups.RightHand;
                case 4: return CharacterKinematicGroups.LeftHand;
                default: return CharacterKinematicGroups.None;
            }
        }

        private static CharacterKinematicGroups FkGroupAt(int index)
        {
            switch (index)
            {
                case 0: return CharacterKinematicGroups.Hair;
                case 1: return CharacterKinematicGroups.Neck;
                case 2: return CharacterKinematicGroups.Breast;
                case 3: return CharacterKinematicGroups.Body;
                case 4: return CharacterKinematicGroups.RightHand;
                case 5: return CharacterKinematicGroups.LeftHand;
                case 6: return CharacterKinematicGroups.Skirt;
                default: return CharacterKinematicGroups.None;
            }
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
            public FkOverride(
                int boneIndex,
                Quaternion rotation,
                int groupIndex)
            {
                BoneIndex = boneIndex;
                Rotation = rotation;
                GroupIndex = groupIndex;
            }

            public int BoneIndex { get; }

            public Quaternion Rotation { get; }

            public int GroupIndex { get; }
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
                                   owner.isActiveAndEnabled &&
                                   (owner.activeKinematicModes & (solveIk
                                       ? CharacterKinematicModes.InverseKinematics
                                       : CharacterKinematicModes.ForwardKinematics)) != 0;

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
