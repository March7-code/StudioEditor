using System;
using System.Collections.Generic;
using BodyEditor.ReferenceModels;
using NUnit.Framework;
using UnityEngine;

namespace BodyEditor.Tests
{
    public sealed class ReferenceMeshTopologyTests
    {
        [Test]
        public void SharedTriangleEdgeIsEmittedOnlyOnce()
        {
            var mesh = new Mesh();

            try
            {
                mesh.vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                };
                mesh.triangles = new[]
                {
                    0, 1, 2,
                    0, 2, 3,
                };

                var lineIndices =
                    ReferenceMeshTopology.BuildUniqueTriangleEdges(mesh);

                Assert.That(lineIndices.Length, Is.EqualTo(10));
                var edgeKeys = new HashSet<ulong>();
                for (var index = 0; index < lineIndices.Length; index += 2)
                {
                    var first = Math.Min(
                        lineIndices[index],
                        lineIndices[index + 1]);
                    var second = Math.Max(
                        lineIndices[index],
                        lineIndices[index + 1]);
                    edgeKeys.Add(((ulong)(uint)first << 32) | (uint)second);
                }

                Assert.That(edgeKeys.Count, Is.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
