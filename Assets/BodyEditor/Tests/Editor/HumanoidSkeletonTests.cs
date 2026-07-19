using System.Collections.Generic;
using BodyEditor.Editing;
using BodyEditor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BodyEditor.Tests
{
    public sealed class HumanoidSkeletonTests
    {
        [Test]
        public void DefaultDefinitionsHaveUniqueBonesAndOrderedParents()
        {
            var seen = new HashSet<HumanBodyBones>();

            for (var index = 0; index < HumanoidSkeletonSchema.DefaultDefinitions.Count; index++)
            {
                var definition = HumanoidSkeletonSchema.DefaultDefinitions[index];
                Assert.That(seen.Add(definition.Bone), Is.True, $"Duplicate bone: {definition.Bone}");

                if (definition.Parent.HasValue)
                {
                    Assert.That(
                        seen.Contains(definition.Parent.Value),
                        Is.True,
                        $"Parent {definition.Parent.Value} must precede {definition.Bone}.");
                }
            }
        }

        [Test]
        public void DefaultTemplateContainsAValidSkeleton()
        {
            DefaultHumanoidSkeletonBuilder.BuildDefaultTemplate();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                DefaultHumanoidSkeletonBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            var skeleton = prefab.GetComponent<HumanoidSkeleton>();
            Assert.That(skeleton, Is.Not.Null);
            Assert.That(
                skeleton.BoneCount,
                Is.EqualTo(HumanoidSkeletonSchema.DefaultDefinitions.Count));

            var errors = new List<string>();
            Assert.That(skeleton.Validate(errors), Is.True, string.Join("\n", errors));
        }

        [Test]
        public void EditableSkeletonMirrorsAndRestoresJointPositions()
        {
            var root = new GameObject("Editable Skeleton Test");
            EditableSkeletonController controller = null;

            try
            {
                controller = root.AddComponent<EditableSkeletonController>();
                Assert.That(
                    controller.Bones.Count,
                    Is.EqualTo(HumanoidSkeletonSchema.DefaultDefinitions.Count));
                Assert.That(
                    controller.TryGetJointRootPosition(
                        HumanBodyBones.LeftLowerArm,
                        out var originalLeft),
                    Is.True);
                Assert.That(
                    controller.TryGetJointRootPosition(
                        HumanBodyBones.RightLowerArm,
                        out var originalRight),
                    Is.True);

                var editedLeft = originalLeft + new Vector3(-0.04f, 0.03f, 0.02f);
                controller.SetJointRootPosition(
                    HumanBodyBones.LeftLowerArm,
                    editedLeft);

                Assert.That(
                    controller.TryGetJointRootPosition(
                        HumanBodyBones.RightLowerArm,
                        out var editedRight),
                    Is.True);
                AssertVector(
                    editedRight,
                    new Vector3(-editedLeft.x, editedLeft.y, editedLeft.z));
                Assert.That(controller.CanUndo, Is.True);
                Assert.That(controller.UndoCount, Is.EqualTo(1));
                Assert.That(controller.UndoDescription, Is.EqualTo("Move Left Lower Arm"));

                controller.Undo();
                Assert.That(controller.CanRedo, Is.True);
                Assert.That(controller.RedoDescription, Is.EqualTo("Move Left Lower Arm"));
                Assert.That(
                    controller.TryGetJointRootPosition(
                        HumanBodyBones.LeftLowerArm,
                        out var undoneLeft),
                    Is.True);
                Assert.That(
                    controller.TryGetJointRootPosition(
                        HumanBodyBones.RightLowerArm,
                        out var undoneRight),
                    Is.True);
                AssertVector(undoneLeft, originalLeft);
                AssertVector(undoneRight, originalRight);

                controller.Redo();
                controller.ResetPose();
                Assert.That(
                    controller.TryGetJointRootPosition(
                        HumanBodyBones.LeftLowerArm,
                        out var resetLeft),
                    Is.True);
                AssertVector(resetLeft, originalLeft);
            }
            finally
            {
                if (controller != null)
                {
                    controller.enabled = false;
                }

                Object.DestroyImmediate(root);
            }
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.00001f));
        }
    }
}
