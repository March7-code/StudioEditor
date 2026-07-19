using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor
{
    public static class HumanoidSkeletonFactory
    {
        public static HumanoidSkeleton CreateDefault(
            Transform parent = null,
            string name = "DefaultHumanoidSkeleton")
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var skeleton = root.AddComponent<HumanoidSkeleton>();
            var transforms = new Dictionary<HumanBodyBones, Transform>();
            var references = new List<HumanoidBoneReference>(
                HumanoidSkeletonSchema.DefaultDefinitions.Count);

            for (var index = 0;
                 index < HumanoidSkeletonSchema.DefaultDefinitions.Count;
                 index++)
            {
                var definition = HumanoidSkeletonSchema.DefaultDefinitions[index];
                var boneParent = definition.Parent.HasValue
                    ? transforms[definition.Parent.Value]
                    : root.transform;

                var boneObject = new GameObject(definition.Bone.ToString());
                var boneTransform = boneObject.transform;
                boneTransform.SetParent(boneParent, false);
                boneTransform.localPosition = definition.LocalPosition;
                boneTransform.localRotation = Quaternion.identity;
                boneTransform.localScale = Vector3.one;

                transforms.Add(definition.Bone, boneTransform);
                references.Add(new HumanoidBoneReference(
                    definition.Bone,
                    boneTransform));
            }

            skeleton.SetBones(references);
            return skeleton;
        }
    }
}
