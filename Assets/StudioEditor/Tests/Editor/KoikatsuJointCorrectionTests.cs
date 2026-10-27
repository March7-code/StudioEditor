using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StudioEditor.Characters;
using StudioEditor.ReferenceModels;
using UnityEngine;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuJointCorrectionTests
    {
        [Test]
        public void FinalSpinePoseAndBodyDeformationMoveTheClothingColliders()
        {
            var root = new GameObject("Torso collider correction test");
            try
            {
                var spine = Child(root.transform, "cf_j_spine03");
                var chest = Child(spine, "cf_d_bust00");
                chest.localPosition = new Vector3(.012f, 0, 0);
                var body = Child(chest, "cf_j_bust02_L");
                var collider = Child(body, "cf_d_hit_bust_L");
                body.localPosition = new Vector3(.02f, -.04f, .08f);
                body.localScale = new Vector3(.8f, 1.2f, .6f);
                var skeleton = Skeleton(root);
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                var correction = new KoikatsuJointCorrection(skeleton, root.transform);
                correction.Configure(new bool[8]); // These flags only control limbs.
                pipeline.RegisterModifier(correction);
                skeleton.TryGetBoneIndex(spine, out var index);
                var finalPose = pipeline.CreateLayer(CharacterPoseStages.FullBodyIk);
                finalPose.SetLocalRotation(index, KoikatsuJointRotation.FromDegrees(new Vector3(30, 10, -20)));
                pipeline.EvaluateNow();
                AssertVector(chest.localPosition, new Vector3(.012f, .0009f, .012f));
                AssertRotation(chest.localRotation, KoikatsuJointRotation.FromDegrees(new Vector3(-10.5f, -3.5f, 7)));
                AssertVector(collider.localPosition, new Vector3(-.01f, .02f, -.02f));
                AssertVector(collider.localScale, new Vector3(1.1f, .9f, 1.2f));
                AssertVector(body.localPosition, new Vector3(.02f, -.04f, .08f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("L", -90f, -.021465914f, -.034346913f, .439394586f)]
        [TestCase("R", 90f, .021465914f, -.034161649f, -.439394586f)]
        public void ElbowMatchesSourceAfterFinalIkAndLeavesDriversAndDuplicateClothesAlone(
            string side, float angle, float frontX, float frontZ, float backX)
        {
            var root = new GameObject("Joint correction test");
            try
            {
                var body = Child(root.transform, "Body");
                var source = Child(body, "cf_j_forearm01_" + side);
                var front = Child(source, "cf_s_elbo_" + side);
                var back = Child(source, "cf_s_elboback_" + side);
                front.localPosition = new Vector3(0, .031f, 0);
                back.localPosition = new Vector3(0, .017f, 0);
                var duplicate = Child(root.transform, front.name);
                duplicate.localPosition = Vector3.one;
                var skeleton = Skeleton(root);
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                var correction = new KoikatsuJointCorrection(skeleton, body);
                pipeline.RegisterModifier(correction);
                skeleton.TryGetBoneIndex(source, out var sourceIndex);
                var finalIk = pipeline.CreateLayer(CharacterPoseStages.FullBodyIk);
                var saved = Quaternion.AngleAxis(angle, Vector3.up);
                finalIk.SetLocalRotation(sourceIndex, saved);
                for (var frame = 0; frame < 4; frame++)
                {
                    source.localRotation = Quaternion.identity; // Animator sample.
                    pipeline.EvaluateNow();
                    AssertVector(front.localPosition, new Vector3(frontX, .031f, frontZ));
                    AssertVector(back.localPosition, new Vector3(backX, .017f, -.199452342f));
                    AssertRotation(front.localRotation, Quaternion.AngleAxis(-angle * .5f, Vector3.up));
                    AssertRotation(source.localRotation, saved);
                    AssertVector(duplicate.localPosition, Vector3.one);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("L", 9f)]
        [TestCase("R", -9f)]
        public void BentKneeMatchesSourcePositionsAndMirroredBackRotation(string side, float backRoll)
        {
            var root = new GameObject("Knee correction test");
            try
            {
                var source = Child(root.transform, "cf_j_leg01_" + side);
                var front = Child(source, "cf_d_kneeF_" + side);
                var back = Child(source, "cf_s_kneeB_" + side);
                source.localRotation = Quaternion.AngleAxis(90, Vector3.right);
                var skeleton = Skeleton(root);
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                pipeline.RegisterModifier(new KoikatsuJointCorrection(skeleton, root.transform));
                pipeline.EvaluateNow();
                AssertVector(front.localPosition, new Vector3(0, .018811159f, .019193808f));
                AssertVector(back.localPosition, new Vector3(0, -.34583404f, -.255826951f));
                AssertRotation(back.localRotation, Quaternion.AngleAxis(backRoll, Vector3.forward) * Quaternion.AngleAxis(-45, Vector3.right));
                AssertRotation(source.localRotation, Quaternion.AngleAxis(90, Vector3.right));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SavedCategoriesIndependentlyDisableArmAndThighCorrections()
        {
            var root = new GameObject("Correction categories test");
            try
            {
                var left = Child(root.transform, "cf_j_arm00_L");
                var right = Child(root.transform, "cf_j_arm00_R");
                var leftHelper = Child(left, "cf_d_arm01_L");
                var rightHelper = Child(right, "cf_d_arm01_R");
                var thigh = Child(root.transform, "cf_j_thigh00_L");
                var thighHelper = Child(thigh, "cf_d_thigh01_L");
                left.localRotation = right.localRotation = Quaternion.AngleAxis(60, Vector3.right);
                thigh.localRotation = Quaternion.AngleAxis(40, Vector3.up);
                var skeleton = Skeleton(root);
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                var correction = new KoikatsuJointCorrection(skeleton, root.transform);
                correction.Configure(new[] { false, true, true, true, true, true, false, true });
                pipeline.RegisterModifier(correction);
                pipeline.EvaluateNow();
                AssertRotation(leftHelper.localRotation, Quaternion.identity);
                AssertRotation(rightHelper.localRotation, Quaternion.AngleAxis(-36, Vector3.right));
                AssertRotation(thighHelper.localRotation, Quaternion.identity);
                correction.Configure(null);
                pipeline.EvaluateNow();
                AssertRotation(leftHelper.localRotation, Quaternion.AngleAxis(-36, Vector3.right));
                AssertRotation(thighHelper.localRotation, Quaternion.AngleAxis(-30, Vector3.up));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RotationOrdersRoundTripMixedAnglesAcrossTheEulerSeam(int orderValue)
        {
            var order = (KoikatsuJointRotation.Order)orderValue;
            foreach (var input in new[] { new Vector3(-17, 359, 44), new Vector3(61, -42, -29), new Vector3(8, 23, 77) })
            {
                var rotation = KoikatsuJointRotation.FromDegrees(input, order);
                AssertRotation(KoikatsuJointRotation.FromDegrees(KoikatsuJointRotation.ToDegrees(rotation, order), order), rotation);
            }
        }

        [Test]
        public void CorrectedBodyHelpersPropagateToClothingProxies()
        {
            var root = new GameObject("Joint clothing proxy test");
            try
            {
                var body = Child(root.transform, "Body");
                var source = Child(body, "cf_j_forearm01_L");
                var front = Child(source, "cf_s_elbo_L");
                var clothes = Child(root.transform, "Clothes");
                var proxySource = Child(clothes, source.name);
                var proxyFront = Child(proxySource, front.name);
                var follower = clothes.gameObject.AddComponent<KoikatsuBoneProxyFollower>();
                follower.Configure(clothes, new Dictionary<string, Transform> { [source.name] = source, [front.name] = front });
                var skeleton = Skeleton(root);
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                pipeline.RegisterModifier(new KoikatsuJointCorrection(skeleton, body));
                skeleton.TryGetBoneIndex(source, out var index);
                var fk = pipeline.CreateLayer(CharacterPoseStages.ImportedFk);
                fk.SetLocalRotation(index, Quaternion.AngleAxis(-120, Vector3.up));
                pipeline.EvaluateNow();
                typeof(KoikatsuBoneProxyFollower).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(follower, null);
                AssertVector(proxyFront.localPosition, front.localPosition);
                AssertRotation(proxyFront.localRotation, front.localRotation);
                AssertVector(proxyFront.position, front.position);
                Assert.That(Vector3.Distance(proxyFront.localPosition, Vector3.zero), Is.GreaterThan(.01f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Child(Transform parent, string name)
        {
            var value = new GameObject(name).transform;
            value.SetParent(parent, false);
            return value;
        }

        private static CharacterSkeleton Skeleton(GameObject root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            var indices = transforms.Select((t, i) => (t, i)).ToDictionary(pair => pair.t, pair => pair.i);
            return new CharacterSkeleton(transforms.Select(t => new CharacterBone(t.name, t, t.parent != null && indices.TryGetValue(t.parent, out var parent) ? parent : -1)));
        }

        private static void AssertVector(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00001f));
        private static void AssertRotation(Quaternion actual, Quaternion expected) => Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(.06f));
    }
}
