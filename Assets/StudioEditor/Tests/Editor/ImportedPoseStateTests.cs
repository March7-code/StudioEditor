using NUnit.Framework;
using StudioEditor.Characters;
using StudioEditor.ReferenceModels;
using UnityEngine;

namespace StudioEditor.Tests
{
    public sealed class ImportedPoseStateTests
    {
        [Test]
        public void SavedFingerFkOverridesHandPresetAndCanBeDisabled()
        {
            var root = new GameObject("Hand FK test");
            try
            {
                var finger = new GameObject("Finger").transform;
                finger.SetParent(root.transform, false);
                var skeleton = new CharacterSkeleton(new[] {new CharacterBone("Finger", finger, -1)});
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                var preset = Quaternion.Euler(5, 10, 70);
                var saved = Quaternion.Euler(30, 20, 12);
                var hands = root.AddComponent<CharacterHandPoseController>();
                hands.Configure(pipeline, new[] {new CharacterHandPose("Grip", new[] {new CharacterHandBonePose(0, preset)})}, null);
                hands.SetPose(CharacterHand.Left, 0);
                var fk = pipeline.CreateLayer(CharacterPoseStages.ImportedFk);
                fk.SetLocalRotation(0, saved);
                pipeline.EvaluateNow();
                Assert.That(Quaternion.Angle(finger.localRotation, saved), Is.LessThan(0.01f));
                fk.Enabled = false;
                pipeline.EvaluateNow();
                Assert.That(Quaternion.Angle(finger.localRotation, preset), Is.LessThan(0.01f));
                var animated = Quaternion.Euler(4, 8, 15);
                KoikatsuStudioCharacterPose.ApplyHandPose(hands, CharacterHand.Left, 0);
                finger.localRotation = animated;
                pipeline.EvaluateNow();
                Assert.That(Quaternion.Angle(finger.localRotation, animated), Is.LessThan(0.01f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AttachedSwordKeepsSavedWorldScaleAsHandSizeChanges()
        {
            var scene = new GameObject("Scene");
            try
            {
                var hand = new GameObject("Hand").transform;
                hand.SetParent(scene.transform, false);
                hand.localScale = Vector3.one * 0.935322f;
                var sword = new GameObject("Sword").transform;
                sword.SetParent(hand, false);
                sword.localPosition = new Vector3(0.00373f, -0.02543f, 0.59471f);
                sword.localRotation = Quaternion.Euler(0, 179.282f, 264.729f);
                var scabbard = new GameObject("Scabbard").transform;
                scabbard.SetParent(sword, false);
                var scales = scene.AddComponent<KoikatsuStudioScaleCompensation>();
                scales.Register(sword, Vector3.one);
                scales.Register(scabbard, Vector3.one * 1.2f);
                scales.EvaluateNow();
                AssertVector(sword.lossyScale, Vector3.one);
                AssertVector(scabbard.lossyScale, Vector3.one * 1.2f);
                var savedPosition = sword.localPosition;
                hand.localScale = Vector3.one * 1.16f;
                scales.EvaluateNow();
                scales.EvaluateNow();
                AssertVector(sword.lossyScale, Vector3.one);
                AssertVector(sword.localPosition, savedPosition);
                AssertVector(scabbard.lossyScale, Vector3.one * 1.2f);
                Assert.That(scales.TrySetWorldScale(sword, Vector3.one * 1.4f), Is.True);
                scales.EvaluateNow();
                AssertVector(sword.lossyScale, Vector3.one * 1.4f);
                AssertVector(scabbard.lossyScale, Vector3.one * 1.2f);
                sword.localScale *= 1.1f;
                scales.EvaluateNow();
                AssertVector(sword.lossyScale, Vector3.one * 1.54f);
                var replacement = new GameObject("New sword").transform;
                replacement.SetParent(hand, false);
                scales.Replace(sword, replacement);
                AssertVector(replacement.lossyScale, Vector3.one * 1.54f);
            }
            finally { Object.DestroyImmediate(scene); }
        }

        [Test]
        public void AnimatedHandScaleCorrectionRestoresAttachmentBasisWithoutChangingFk()
        {
            var scene = new GameObject("Animated hand attachment test");
            try
            {
                scene.transform.localScale = Vector3.one * 0.86f;
                var hand = new GameObject("cf_j_hand_L").transform;
                hand.SetParent(scene.transform, false);
                hand.localPosition = new Vector3(-0.2f, 0.9f, 0.4f);
                var point = new GameObject("a_n_hand_L").transform;
                point.SetParent(hand, false);
                point.localPosition = new Vector3(-0.04707f, -0.0148f, 0.00384f);
                point.localRotation = Quaternion.Euler(0, 0, 270);
                point.localScale = Vector3.one * 1.08742f;
                var sword = new GameObject("Sword").transform;
                sword.SetParent(point, false);
                var savedPosition = new Vector3(0.00373f, -0.02543f, 0.59471f);
                sword.localPosition = savedPosition;
                sword.localRotation = Quaternion.Euler(359.934f, 179.282f, 264.729f);
                var clothesHand = new GameObject("cf_j_hand_L").transform;
                clothesHand.SetParent(scene.transform, false);
                var skeleton = new CharacterSkeleton(new[] {
                    new CharacterBone(clothesHand.name, clothesHand, -1),
                    new CharacterBone(hand.name, hand, -1, HumanBodyBones.LeftHand)
                });
                var pipeline = CharacterPoseCoordinator.Attach(scene, skeleton);
                pipeline.RegisterModifier(new KoikatsuHandSizeCorrection(skeleton));
                var fk = pipeline.CreateLayer(CharacterPoseStages.ImportedFk);
                var savedRotation = Quaternion.Euler(32, 51, 28);
                fk.SetLocalRotation(1, savedRotation);
                var scales = scene.AddComponent<KoikatsuStudioScaleCompensation>();
                scales.Register(sword, Vector3.one);
                hand.localRotation = savedRotation;
                var uncorrectedPosition = sword.position;
                pipeline.EvaluateNow();
                scales.EvaluateNow();
                var correctedPosition = sword.position;
                Assert.That(Vector3.Distance(uncorrectedPosition, correctedPosition), Is.GreaterThan(0.05f));
                for (var frame = 0; frame < 4; frame++)
                {
                    // The next Animator sample writes unit scale again.
                    hand.localScale = Vector3.one;
                    pipeline.EvaluateNow();
                    scales.EvaluateNow();
                    AssertVector(sword.position, correctedPosition);
                    AssertVector(sword.localPosition, savedPosition);
                    AssertVector(sword.lossyScale, Vector3.one);
                    AssertVector(clothesHand.localScale, Vector3.one);
                    Assert.That(Quaternion.Angle(hand.localRotation, savedRotation), Is.LessThan(0.01f));
                }
            }
            finally { Object.DestroyImmediate(scene); }
        }

        [Test]
        public void CollapsedParentDoesNotProduceInvalidScale()
        {
            var scene = new GameObject("Collapsed scale test");
            try
            {
                scene.transform.localScale = new Vector3(0, 0.9f, 1.1f);
                var item = new GameObject("Item").transform;
                item.SetParent(scene.transform, false);
                var scales = scene.AddComponent<KoikatsuStudioScaleCompensation>();
                scales.Register(item, Vector3.one);
                scales.EvaluateNow();
                Assert.That(float.IsFinite(item.localScale.x), Is.True);
                scene.transform.localScale = Vector3.one * 0.8f;
                scales.EvaluateNow();
                AssertVector(item.lossyScale, Vector3.one);
            }
            finally { Object.DestroyImmediate(scene); }
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
            => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));
    }
}
