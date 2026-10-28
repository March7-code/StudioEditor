using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.Rendering
{
    // Carries the approved, chin-converging grayscale in a separate UV channel.
    // The imported mesh, UV0, UV1, normals and blend shapes remain source-owned.
    [DefaultExecutionOrder(30000)]
    public sealed class FaceShadowBinding : MonoBehaviour
    {
        private static readonly int ReadyId = Shader.PropertyToID("_FaceShadowReady");
        private static readonly int MapId = Shader.PropertyToID("_FaceShadowMap");
        private static readonly int ForwardId = Shader.PropertyToID("_FaceShadowForward");
        private static readonly int RightId = Shader.PropertyToID("_FaceShadowRight");
        private static Texture2D sharedMap;
        private SkinnedMeshRenderer target;
        private Transform headFrame;
        private Mesh originalMesh, ownedMesh;
        private MaterialPropertyBlock block;
        private Vector3 previousForward, previousRight;
        private bool applied;

        public SkinnedMeshRenderer Target => target;
        public Transform HeadFrame => headFrame;
        public Transform HeadRoot { get; private set; }
        public Bounds HeadLocalBounds { get; private set; }

        public static Texture2D ShadowMap
        {
            get
            {
                if (sharedMap != null) return sharedMap;
                const int size = 1024;
                var bytes = Resources.Load<TextAsset>("StudioEditor/ConvergentFaceShadow");
                if (bytes == null || bytes.bytes.Length != size * size * 2) return null;
                sharedMap = new Texture2D(size, size, TextureFormat.R16, false, true)
                {
                    name = "Convergent face shadow (linear)",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                sharedMap.LoadRawTextureData(bytes.bytes);
                sharedMap.Apply(false, true);
                return sharedMap;
            }
        }

        public bool Configure(SkinnedMeshRenderer renderer, Transform frame, Transform headRoot = null)
        {
            ReleaseMesh();
            if (renderer == null || renderer.sharedMesh == null || frame == null || ShadowMap == null) return false;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                var positions = baked.vertices;
                if (positions.Length == 0) return false;
                var toHead = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var bounds = new Bounds(toHead.MultiplyPoint3x4(positions[0]), Vector3.zero);
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = toHead.MultiplyPoint3x4(positions[i]);
                    bounds.Encapsulate(positions[i]);
                }
                if (bounds.size.y < 1e-5f || bounds.size.x < 1e-5f) return false;
                // Fit the outermost shadow contour to this face: one concave cubic from
                // the brow to the actual chin point (as the approved offline curves),
                // scaled to the face edge. UV3.x is the position relative to that
                // contour, so inner contours converge to the chin and bend more
                // towards the edge, and gray reaches 0 / 1 at the cheek silhouette.
                // All face skin, including the eyelids, shares the same SDF
                // coordinates and receives the same projected hair silhouette.
                var contour = FitOuterContour(positions, bounds);
                var uv = new List<Vector2>(positions.Length);
                for (int i = 0; i < positions.Length; i++)
                {
                    float t = Mathf.Clamp01((contour.Top - positions[i].y) / (contour.Top - contour.Chin.y));
                    float radius = Mathf.Max(contour.Width(t), contour.A * .01f);
                    float x = positions[i].x - contour.Chin.x;
                    uv.Add(new Vector2(.5f + .5f * Mathf.Sign(x) * Mathf.Clamp01(Mathf.Abs(x) / radius),
                        (positions[i].y - bounds.min.y) / bounds.size.y));
                }
                target = renderer;
                headFrame = frame;
                HeadRoot = headRoot != null ? headRoot : frame;
                HeadLocalBounds = bounds;
                originalMesh = renderer.sharedMesh;
                ownedMesh = Instantiate(originalMesh);
                ownedMesh.name = originalMesh.name + " (face shadow UV)";
                ownedMesh.SetUVs(2, uv);
                renderer.sharedMesh = ownedMesh;
                applied = false;
                Synchronize();
                return true;
            }
            finally { DestroyOwned(baked); }
        }

        internal readonly struct OuterContour
        {
            public OuterContour(Vector3 chin, float top, float a, float c) { Chin = chin; Top = top; A = a; C = c; }
            public Vector3 Chin { get; }
            public float Top { get; }
            public float A { get; }
            public float C { get; }
            // Control x (A, A, C, 0) at t = 0, 1/3, 2/3, 1: a vertical tangent at
            // the brow, x' <= 0 and x'' <= 0 for A/2 <= C <= A, ending at the chin.
            public float Width(float t)
            {
                float s = 1 - t;
                return A * (s * s * s + 3 * s * s * t) + C * 3 * s * t * t;
            }
        }

        private const int ContourRows = 32;

        internal static OuterContour FitOuterContour(Vector3[] positions, Bounds bounds)
        {
            // The chin is the lowest vertex near the symmetry plane.
            var chin = new Vector3(0, float.MaxValue, 0);
            float near = bounds.extents.x * .15f;
            foreach (var position in positions)
                if (Mathf.Abs(position.x - bounds.center.x) <= near && position.y < chin.y) chin = position;
            if (chin.y == float.MaxValue) chin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            chin.x = bounds.center.x;
            float top = bounds.max.y;
            if (top - chin.y < 1e-5f) return new OuterContour(chin, bounds.max.y, bounds.extents.x, bounds.extents.x);
            // Per-row silhouette above the chin, then a weighted least-squares fit
            // of A and C. The chin rows carry little weight: all contours meet there.
            var width = new float[ContourRows];
            foreach (var position in positions)
            {
                float t = (top - position.y) / (top - chin.y);
                if (t < 0 || t >= 1) continue;
                int row = Mathf.Min((int)(t * ContourRows), ContourRows - 1);
                width[row] = Mathf.Max(width[row], Mathf.Abs(position.x - chin.x));
            }
            double aa = 0, ac = 0, cc = 0, ay = 0, cy = 0;
            void Basis(float t, out float a, out float c)
            {
                float s = 1 - t;
                a = s * s * s + 3 * s * s * t; c = 3 * s * t * t;
            }
            for (int row = 0; row < ContourRows; row++)
            {
                if (width[row] <= 0) continue;
                float t = (row + .5f) / ContourRows, w = t < .9f ? 1 : .25f;
                Basis(t, out float a, out float c);
                aa += w * a * a; ac += w * a * c; cc += w * c * c; ay += w * a * width[row]; cy += w * c * width[row];
            }
            double Error(double A, double C) => A * A * aa + 2 * A * C * ac + C * C * cc - 2 * (A * ay + C * cy);
            double bestA = 0, bestC = 0, best = double.MaxValue;
            double det = aa * cc - ac * ac;
            if (det > 1e-12)
            {
                double A = (ay * cc - cy * ac) / det, C = (cy * aa - ay * ac) / det;
                if (A > 0 && C >= .5 * A && C <= A) { bestA = A; bestC = C; best = Error(A, C); }
            }
            foreach (double ratio in new[] { .5, 1.0 })
            {
                double denominator = aa + 2 * ratio * ac + ratio * ratio * cc;
                double k = denominator > 1e-12 ? Math.Max(0, (ay + ratio * cy) / denominator) : 0;
                double error = Error(k, k * ratio);
                if (error < best) { best = error; bestA = k; bestC = k * ratio; }
            }
            if (bestA <= 0) return new OuterContour(chin, top, bounds.extents.x, bounds.extents.x);
            // Scale the curve to envelope the face edge, ignoring the 1% outliers
            // and the singular chin tip.
            var contour = new OuterContour(chin, top, (float)bestA, (float)bestC);
            var ratios = new List<float>(positions.Length);
            foreach (var position in positions)
            {
                float t = (top - position.y) / (top - chin.y);
                if (t < 0 || t >= .9f) continue;
                float radius = contour.Width(t);
                if (radius > 1e-6f) ratios.Add(Mathf.Abs(position.x - chin.x) / radius);
            }
            if (ratios.Count == 0) return contour;
            ratios.Sort();
            float scale = ratios[Mathf.Min(ratios.Count - 1, (int)(ratios.Count * .99f))];
            return new OuterContour(chin, top, contour.A * scale, contour.C * scale);
        }

        private void LateUpdate() => Synchronize();
        private void OnEnable() { applied = false; Synchronize(); }
        private void Synchronize()
        {
            if (target == null || headFrame == null || ownedMesh == null) return;
            block ??= new MaterialPropertyBlock();
            var forward = headFrame.forward.normalized;
            var right = headFrame.right.normalized;
            if (applied && forward == previousForward && right == previousRight) return;
            target.GetPropertyBlock(block);
            block.SetFloat(ReadyId, 1f);
            block.SetTexture(MapId, ShadowMap);
            block.SetVector(ForwardId, new Vector4(forward.x, forward.y, forward.z, 0));
            block.SetVector(RightId, new Vector4(right.x, right.y, right.z, 0));
            target.SetPropertyBlock(block);
            previousForward = forward;
            previousRight = right;
            applied = true;
        }
        private void OnDisable()
        {
            if (target != null)
            {
                block ??= new MaterialPropertyBlock();
                target.GetPropertyBlock(block);
                block.SetFloat(ReadyId, 0f);
                target.SetPropertyBlock(block);
            }
            applied = false;
        }
        private void OnDestroy() => ReleaseMesh();
        private void ReleaseMesh()
        {
            OnDisable();
            if (target != null && target.sharedMesh == ownedMesh) target.sharedMesh = originalMesh;
            DestroyOwned(ownedMesh);
            ownedMesh = originalMesh = null;
            target = null;
            headFrame = null;
            HeadRoot = null;
        }
        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
