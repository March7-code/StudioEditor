using System.Collections.Generic;
using BodyEditor.ReferenceModels;
using NUnit.Framework;
using UnityEngine;

namespace BodyEditor.Tests
{
    public sealed class ReferenceSectionRingTests
    {
        [Test]
        public void WeightedCylinderSlicesSnapToSourceVertexRows()
        {
            var root = new GameObject("Section Ring Test");
            var mesh = BuildCylinderMesh(8);
            ReferenceSectionRingOverlay overlay = null;

            try
            {
                var start = CreateBone(root.transform, "Upper Arm", Vector3.zero);
                var end = CreateBone(root.transform, "Lower Arm", Vector3.up);
                var meshObject = new GameObject("Body");
                meshObject.transform.SetParent(root.transform, false);
                var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { start, end };
                renderer.rootBone = start;

                var shader = Resources.Load<Shader>("BodyEditorOverlay");
                Assert.That(shader, Is.Not.Null);

                var provider = new TestSkeletonProvider(new[]
                {
                    new ReferenceModelBone(
                        "Upper Arm",
                        start,
                        -1,
                        true,
                        -1,
                        HumanBodyBones.LeftUpperArm),
                    new ReferenceModelBone(
                        "Lower Arm",
                        end,
                        0,
                        true,
                        0,
                        HumanBodyBones.LeftLowerArm),
                });
                overlay = new ReferenceSectionRingOverlay(
                    root.transform,
                    new Renderer[] { renderer },
                    provider,
                    shader,
                    Color.yellow);

                overlay.Rebuild(
                    new[] { new ReferenceModelPartState("Body", "Body") },
                    new[] { true },
                    2);

                Assert.That(overlay.SupportedSegmentCount, Is.EqualTo(1));
                Assert.That(overlay.Rings.Count, Is.EqualTo(2));
                for (var ringIndex = 0; ringIndex < overlay.Rings.Count; ringIndex++)
                {
                    var ring = overlay.Rings[ringIndex];
                    Assert.That(ring.Segment, Is.EqualTo(
                        ReferenceBodySegment.LeftUpperArm));
                    Assert.That(ring.SourceVertices.Count,
                        Is.GreaterThanOrEqualTo(8));
                    var expectedHeight = ringIndex;
                    Assert.That(ring.NormalizedPosition,
                        Is.EqualTo(expectedHeight).Within(1e-5f));
                    for (var index = 0;
                         index < ring.SourceVertices.Count;
                         index++)
                    {
                        Assert.That(ring.SourceVertices[index].y,
                            Is.EqualTo(expectedHeight).Within(1e-5f));
                    }
                }
            }
            finally
            {
                overlay?.Dispose();
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(root);
            }
        }

        private static Transform CreateBone(
            Transform parent,
            string name,
            Vector3 position)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.position = position;
            return bone;
        }

        private static Mesh BuildCylinderMesh(int sideCount)
        {
            var vertices = new Vector3[sideCount * 2];
            var weights = new BoneWeight[vertices.Length];
            var triangles = new int[sideCount * 6];
            for (var side = 0; side < sideCount; side++)
            {
                var angle = side * Mathf.PI * 2f / sideCount;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[side] = radial;
                vertices[side + sideCount] = radial + Vector3.up;
                weights[side] = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = 1f,
                };
                weights[side + sideCount] = new BoneWeight
                {
                    boneIndex0 = 1,
                    weight0 = 1f,
                };

                var next = (side + 1) % sideCount;
                var triangle = side * 6;
                triangles[triangle] = side;
                triangles[triangle + 1] = side + sideCount;
                triangles[triangle + 2] = next + sideCount;
                triangles[triangle + 3] = side;
                triangles[triangle + 4] = next + sideCount;
                triangles[triangle + 5] = next;
            }

            var mesh = new Mesh
            {
                name = "Section Ring Test Cylinder",
                vertices = vertices,
                triangles = triangles,
                boneWeights = weights,
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class TestSkeletonProvider : IReferenceModelSkeletonProvider
        {
            public TestSkeletonProvider(IReadOnlyList<ReferenceModelBone> bones)
            {
                Bones = bones;
            }

            public IReadOnlyList<ReferenceModelBone> Bones { get; }
        }
    }
}
