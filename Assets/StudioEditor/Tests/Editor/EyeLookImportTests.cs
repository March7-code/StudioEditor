using System;
using NUnit.Framework;
using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.Tests
{
    public sealed class EyeLookImportTests
    {
        [TestCase(0.6f, -0.555f)]
        [TestCase(-0.6f, -0.147f)]
        public void FixedGazeMatchesSourceIrisPlacement(float verticalRate, float expectedY)
        {
            using (var fixture = new EyeFixture())
            {
                var scale = new Vector2(1.8f, 1.6f);
                var offset = new Vector2(-0.484f, -0.351f);
                fixture.Material.SetTextureScale("_BaseMap", scale);
                fixture.Material.SetTextureOffset("_BaseMap", offset);
                fixture.Look.ConfigurePupils(new[] {
                    new CharacterPupilMaterialTarget(fixture.Material, "_BaseMap", CharacterEye.Left) });
                var rotation = Quaternion.Euler(-8, 12, 0);
                fixture.Look.SetFixedLocalRotations(rotation, Quaternion.identity,
                    new Vector2(0.35f, verticalRate), Vector2.zero);
                fixture.Pipeline.EvaluateNow();

                // EyeLookMaterialControll: custom offset (-.2, .15), scale (.8, .6),
                // waits -100..100 / 100..-100 and power .001.
                var actual = fixture.Material.GetTextureOffset("_BaseMap");
                Assert.That(actual.x, Is.EqualTo(-0.337f).Within(0.00001f));
                Assert.That(actual.y, Is.EqualTo(expectedY).Within(0.00001f));
                Assert.That(Quaternion.Angle(fixture.Left.localRotation, rotation), Is.LessThan(0.001f));
                Assert.That(fixture.Material.GetTextureScale("_BaseMap"), Is.EqualTo(scale));

                fixture.Look.enabled = false;
                // EditMode does not dispatch the runtime component's OnDisable.
                typeof(CharacterEyeLookController).GetMethod("OnDisable",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(fixture.Look, null);
                Assert.That(fixture.Material.GetTextureOffset("_BaseMap"), Is.EqualTo(offset));
            }
        }

        [Test]
        public void SharedVerticalGazeUsesLeftRateAndKeepsIndependentHorizontalRates()
        {
            using (var fixture = new EyeFixture())
            {
                fixture.Look.ConfigurePupils(new[] {
                    new CharacterPupilMaterialTarget(fixture.Material, "_BaseMap", CharacterEye.Right) });
                fixture.Look.UseSharedVerticalLookRate = true;
                fixture.Look.SetFixedLocalRotations(Quaternion.identity, Quaternion.identity,
                    new Vector2(0.35f, 0.6f), new Vector2(0.05f, -0.2f));
                Assert.That(fixture.Look.GetLookRate(CharacterEye.Right), Is.EqualTo(new Vector2(0.05f, 0.6f)));
                var actual = fixture.Material.GetTextureOffset("_BaseMap");
                Assert.That(actual.x, Is.EqualTo(0.005f).Within(0.00001f));
                Assert.That(actual.y, Is.EqualTo(-0.06f).Within(0.00001f));

                fixture.Look.UseSharedVerticalLookRate = false;
                fixture.Pipeline.EvaluateNow();
                Assert.That(fixture.Look.GetLookRate(CharacterEye.Right).y, Is.EqualTo(-0.2f));
                Assert.That(fixture.Material.GetTextureOffset("_BaseMap").y, Is.EqualTo(0.02f).Within(0.00001f));
            }
        }

        [Test]
        public void FollowingAnUpperTargetUsesTheSameUpwardRateAsSavedGaze()
        {
            using (var fixture = new EyeFixture())
            {
                fixture.Look.Response = 0;
                fixture.Look.ConfigurePupils(new[] {
                    new CharacterPupilMaterialTarget(fixture.Material, "_BaseMap", CharacterEye.Left) });
                var target = new GameObject("Upper target").transform;
                target.SetParent(fixture.Root.transform);
                target.position = new Vector3(0, 2, 10);
                fixture.Look.SetManualTarget(target);
                fixture.Pipeline.EvaluateNow();

                var upwardRate = Mathf.Atan2(2, 10) * Mathf.Rad2Deg / fixture.Look.UpAngleLimit;
                Assert.That(fixture.Look.GetLookRate(CharacterEye.Left).y, Is.EqualTo(upwardRate).Within(0.00001f));
                Assert.That(fixture.Material.GetTextureOffset("_BaseMap").y, Is.EqualTo(-0.1f * upwardRate).Within(0.00001f));
                Assert.That((fixture.Left.rotation * Vector3.forward).y, Is.GreaterThan(0));
            }
        }

        private sealed class EyeFixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("Imported eye test");
            public readonly Material Material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            public readonly Transform Left;
            public readonly CharacterPoseCoordinator Pipeline;
            public readonly CharacterEyeLookController Look;

            public EyeFixture()
            {
                Left = new GameObject("Left eye").transform; Left.SetParent(Root.transform);
                var right = new GameObject("Right eye").transform; right.SetParent(Root.transform);
                var skeleton = new CharacterSkeleton(new[] {
                    new CharacterBone("Root", Root.transform, -1),
                    new CharacterBone("Left", Left, 0), new CharacterBone("Right", right, 0) });
                Pipeline = CharacterPoseCoordinator.Attach(Root, skeleton);
                Look = Root.AddComponent<CharacterEyeLookController>();
                Look.Configure(Pipeline, Root.transform, Left, right, Vector3.forward, Vector3.up);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
                UnityEngine.Object.DestroyImmediate(Material);
            }
        }
    }
}
