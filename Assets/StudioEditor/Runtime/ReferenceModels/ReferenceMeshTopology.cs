using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    public static class ReferenceMeshTopology
    {
        public static int[] BuildUniqueTriangleEdges(Mesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            var edgeKeys = new HashSet<ulong>();
            var lineIndices = new List<int>();
            for (var subMeshIndex = 0;
                 subMeshIndex < mesh.subMeshCount;
                 subMeshIndex++)
            {
                if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    continue;
                }

                var triangles = mesh.GetIndices(subMeshIndex, true);
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    AddEdge(triangles[index], triangles[index + 1], edgeKeys, lineIndices);
                    AddEdge(triangles[index + 1], triangles[index + 2], edgeKeys, lineIndices);
                    AddEdge(triangles[index + 2], triangles[index], edgeKeys, lineIndices);
                }
            }

            return lineIndices.ToArray();
        }

        private static void AddEdge(
            int first,
            int second,
            ISet<ulong> edgeKeys,
            ICollection<int> lineIndices)
        {
            if (first == second)
            {
                return;
            }

            var minimum = Math.Min(first, second);
            var maximum = Math.Max(first, second);
            var key = ((ulong)(uint)minimum << 32) | (uint)maximum;
            if (!edgeKeys.Add(key))
            {
                return;
            }

            lineIndices.Add(minimum);
            lineIndices.Add(maximum);
        }
    }
}
