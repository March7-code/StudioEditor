using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // ChaControl.LateUpdateForce corrects high-poly female hands to (1, .9, .9).
    // Apply this after animation sampling on every frame: attachment positions
    // inherit this basis, while Studio guide objects compensate their own scale.
    internal sealed class KoikatsuHandSizeCorrection : ICharacterPoseModifier
    {
        private readonly CharacterSkeleton skeleton;
        private readonly int leftHand = -1;
        private readonly int rightHand = -1;

        internal KoikatsuHandSizeCorrection(CharacterSkeleton skeleton)
        {
            this.skeleton = skeleton;
            leftHand = FindHand(skeleton, HumanBodyBones.LeftHand, "cf_j_hand_L");
            rightHand = FindHand(skeleton, HumanBodyBones.RightHand, "cf_j_hand_R");
        }

        private static int FindHand(CharacterSkeleton skeleton, HumanBodyBones semantic, string name)
        {
            // Clothes contain bones with the same names. Their proxy follower
            // copies the corrected body pose later; bind the actual body hand.
            if (skeleton.TryGetBoneIndex(semantic, out var bodyHand)) return bodyHand;
            for (var index = 0; index < skeleton.BoneCount; index++)
            {
                if (skeleton.Bones[index].Name == name) return index;
            }
            return -1;
        }

        public int Order => CharacterPoseStages.HandPoses;

        public bool Enabled => true;

        public void Evaluate(CharacterPoseBuffer pose)
        {
            if (!ReferenceEquals(pose.Skeleton, skeleton)) return;
            var scale = new Vector3(1f, 0.9f, 0.9f);
            if (leftHand >= 0) pose.SetLocalScale(leftHand, scale);
            if (rightHand >= 0) pose.SetLocalScale(rightHand, scale);
        }
    }
}
