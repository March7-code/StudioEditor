using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering.Experimental
{
    // Tag complete, indexed hair islands, rather than cutting a horizontal
    // plane through long side locks. UV7 is used only by the local depth pass;
    // the visible mesh, blend shapes and GPU skinning retain their geometry.
    internal sealed class FrontHairShadowGeometry : IDisposable
    {
        private readonly Renderer source;
        private readonly Mesh original;
        private Mesh owned;

        internal int AcceptedTriangles { get; }
        internal bool IsCurrent => source != null && SourceMesh(source) == (owned != null ? owned : original);

        internal FrontHairShadowGeometry(Renderer renderer, FaceShadowBinding face)
        {
            source = renderer;
            original = SourceMesh(renderer);
            if (original == null || !original.isReadable || face == null || face.HeadFrame == null) return;
            var baked = new Mesh();
            try
            {
                Vector3[] vertices;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    // BakeMesh(true) compensates renderer scale before the
                    // renderer-to-head transform is applied.
                    skin.BakeMesh(baked, true);
                    vertices = baked.vertices;
                }
                else vertices = original.vertices;
                var toHead = face.HeadFrame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toHead.MultiplyPoint3x4(vertices[i]);
                var mask = BuildMask(original, vertices, face.HeadLocalBounds, out int triangles);
                AcceptedTriangles = triangles;
                if (triangles == 0) return;
                owned = UnityEngine.Object.Instantiate(original);
                owned.name = original.name + " (front hair shadow mask)";
                owned.hideFlags = HideFlags.HideAndDontSave;
                owned.SetUVs(7, mask);
                SetSourceMesh(source, owned);
            }
            finally { CoreUtils.Destroy(baked); }
        }

        internal static List<Vector2> BuildMask(Mesh mesh, Vector3[] headPositions, Bounds face, out int acceptedTriangles)
        {
            int count = mesh.vertexCount;
            var result = new List<Vector2>(count);
            for (int i = 0; i < count; i++) result.Add(Vector2.zero);
            acceptedTriangles = 0;
            if (headPositions.Length != count || count == 0 || face.size.y <= 0 || face.size.x <= 0) return result;
            var parents = new int[count];
            for (int i = 0; i < count; i++) parents[i] = i;
            int Find(int index)
            {
                while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
                return index;
            }
            var indices = new List<int[]>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                if (mesh.GetTopology(slot) != MeshTopology.Triangles) continue;
                var triangles = mesh.GetTriangles(slot);
                indices.Add(triangles);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    parents[Find(triangles[i + 1])] = Find(triangles[i]);
                    parents[Find(triangles[i + 2])] = Find(triangles[i]);
                }
            }
            // Do not weld positions across UV seams: many combined modded
            // hairstyles share crown positions between independent hair cards.
            var bounds = new Dictionary<int, Bounds>();
            for (int i = 0; i < count; i++)
            {
                int island = Find(i);
                if (!bounds.TryGetValue(island, out var b)) b = new Bounds(headPositions[i], Vector3.zero);
                else b.Encapsulate(headPositions[i]);
                bounds[island] = b;
            }
            var front = new HashSet<int>();
            float bottom = face.min.y + face.size.y * .45f;
            float halfWidth = face.extents.x * 1.05f;
            foreach (var pair in bounds)
            {
                var b = pair.Value;
                if (b.min.y >= bottom && b.min.z >= face.center.z &&
                    b.min.x >= face.center.x - halfWidth && b.max.x <= face.center.x + halfWidth)
                    front.Add(pair.Key);
            }
            for (int i = 0; i < count; i++) if (front.Contains(Find(i))) result[i] = Vector2.one;
            foreach (var triangles in indices)
                for (int i = 0; i < triangles.Length; i += 3)
                    if (front.Contains(Find(triangles[i]))) acceptedTriangles++;
            return result;
        }

        private static Mesh SourceMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
            renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;

        private static void SetSourceMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skin) skin.sharedMesh = mesh;
            else if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var filter)) filter.sharedMesh = mesh;
        }

        public void Dispose()
        {
            if (owned != null && source != null && SourceMesh(source) == owned) SetSourceMesh(source, original);
            CoreUtils.Destroy(owned);
            owned = null;
        }
    }
}
