using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.Characters
{
    [Flags]
    public enum CharacterModelFeatures
    {
        None = 0,
        SemanticSkeleton = 1 << 0,
        AnatomyGeometry = 1 << 1,
        BodyConstraints = 1 << 2,
    }

    public enum CharacterKinematicMode
    {
        None,
        ForwardKinematics,
        InverseKinematics,
    }

    [Flags]
    public enum CharacterKinematicModes
    {
        None = 0,
        ForwardKinematics = 1 << 0,
        InverseKinematics = 1 << 1,
    }

    [Flags]
    public enum CharacterKinematicGroups
    {
        None = 0,
        Body = 1 << 0,
        RightLeg = 1 << 1,
        LeftLeg = 1 << 2,
        RightHand = 1 << 3,
        LeftHand = 1 << 4,
        Hair = 1 << 5,
        Neck = 1 << 6,
        Breast = 1 << 7,
        Skirt = 1 << 8,
    }

    public enum CharacterFullBodyIkTarget
    {
        LeftHand,
        LeftElbow,
        RightHand,
        RightElbow,
        LeftFoot,
        LeftKnee,
        RightFoot,
        RightKnee,
    }

    public interface ICharacterModel : IDisposable
    {
        string DisplayName { get; }

        GameObject Root { get; }

        CharacterSkeleton Skeleton { get; }

        CharacterGeometry Geometry { get; }

        ICharacterControls Controls { get; }

        CharacterModelFeatures Features { get; }
    }

    public interface ICharacterModelSource
    {
        event Action CharactersChanged;

        IReadOnlyList<ICharacterModel> CharacterModels { get; }
    }

    public interface ICharacterKinematicController
    {
        CharacterKinematicModes SupportedKinematicModes { get; }

        CharacterKinematicMode KinematicMode { get; }

        void SetKinematicMode(CharacterKinematicMode mode);
    }

    public interface ICharacterKinematicGroupController :
        ICharacterKinematicController
    {
        CharacterKinematicModes ActiveKinematicModes { get; }

        void SetKinematicModeActive(
            CharacterKinematicMode mode,
            bool active);

        CharacterKinematicGroups GetSupportedGroups(
            CharacterKinematicMode mode);

        CharacterKinematicGroups GetActiveGroups(
            CharacterKinematicMode mode);

        void SetGroupActive(
            CharacterKinematicMode mode,
            CharacterKinematicGroups group,
            bool active);
    }

    public interface ICharacterFullBodyIkTargetController
    {
        bool SupportsTarget(CharacterFullBodyIkTarget target);

        bool SetTarget(
            CharacterFullBodyIkTarget target,
            Vector3 worldPosition,
            Quaternion worldRotation);

        bool ClearTarget(CharacterFullBodyIkTarget target);
    }

    public interface ICharacterPatternController
    {
        int PatternCount { get; }

        int Pattern { get; }

        float OpenRate { get; }

        float OpenMax { get; }

        string GetPatternName(int pattern);

        void SetPattern(int pattern, bool blend = true);

        void SetOpenRate(float value);

        void SetOpenMax(float value);
    }

    public interface ICharacterMouthController : ICharacterPatternController
    {
        void SetFixedOpenRate(float value);
    }

    public interface ICharacterEyeOpenController : ICharacterPatternController
    {
    }

    public interface ICharacterEyebrowController : ICharacterPatternController
    {
    }

    public enum CharacterHand
    {
        Left,
        Right,
    }

    public interface ICharacterHandPoseController
    {
        int GetPoseCount(CharacterHand hand);

        int GetPose(CharacterHand hand);

        float GetWeight(CharacterHand hand);

        string GetPoseName(CharacterHand hand, int pose);

        void SetPose(CharacterHand hand, int pose, float weight = 1f);

        void ClearPose(CharacterHand hand);
    }

    public interface ICharacterEyeLookController
    {
        bool IsFollowingTarget { get; }

        Transform Target { get; }

        Transform ManualTarget { get; }

        void SetTarget(Transform target);

        void SetManualTarget(Transform target);

        void SetFollowTarget(bool enabled);
    }

    public enum CharacterEye
    {
        Left,
        Right,
    }

    public interface ICharacterEyeControls
    {
        ICharacterEyeOpenController Open { get; }

        ICharacterEyeLookController Look { get; }
    }

    public interface ICharacterPoseControls
    {
        ICharacterPosePipeline Pipeline { get; }

        ICharacterKinematicController Kinematics { get; }
    }

    public interface ICharacterControls
    {
        ICharacterPoseControls Pose { get; }

        ICharacterMouthController Mouth { get; }

        ICharacterEyebrowController Eyebrows { get; }

        ICharacterEyeControls Eyes { get; }

        ICharacterHandPoseController Hands { get; }
    }

    public sealed class CharacterControlSet : ICharacterControls
    {
        public CharacterControlSet(
            ICharacterPosePipeline posePipeline,
            ICharacterKinematicController kinematics = null,
            ICharacterMouthController mouth = null,
            ICharacterEyeOpenController eyeOpen = null,
            ICharacterEyeLookController eyeLook = null,
            ICharacterHandPoseController hands = null,
            ICharacterEyebrowController eyebrows = null)
        {
            Pose = new CharacterPoseControlSet(posePipeline, kinematics);
            Mouth = mouth;
            Eyebrows = eyebrows;
            Eyes = new CharacterEyeControlSet(eyeOpen, eyeLook);
            Hands = hands;
        }

        public ICharacterPoseControls Pose { get; }

        public ICharacterMouthController Mouth { get; }

        public ICharacterEyebrowController Eyebrows { get; }

        public ICharacterEyeControls Eyes { get; }

        public ICharacterHandPoseController Hands { get; }
    }

    public sealed class CharacterEyeControlSet : ICharacterEyeControls
    {
        public CharacterEyeControlSet(
            ICharacterEyeOpenController open = null,
            ICharacterEyeLookController look = null)
        {
            Open = open;
            Look = look;
        }

        public ICharacterEyeOpenController Open { get; }

        public ICharacterEyeLookController Look { get; }
    }

    public sealed class CharacterPoseControlSet : ICharacterPoseControls
    {
        public CharacterPoseControlSet(
            ICharacterPosePipeline pipeline,
            ICharacterKinematicController kinematics = null)
        {
            Pipeline = pipeline ??
                throw new ArgumentNullException(nameof(pipeline));
            Kinematics = kinematics;
        }

        public ICharacterPosePipeline Pipeline { get; }

        public ICharacterKinematicController Kinematics { get; }
    }

    public interface ICharacterModelCollection
    {
        IReadOnlyList<ICharacterModel> CharacterModels { get; }
    }
}
