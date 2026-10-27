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
                // Matches the normalized coordinates of the accepted offline curves.
                float halfWidth = Mathf.Max(bounds.size.x, bounds.size.y) / 1.72f;
                // All face skin, including the eyelids, shares the same SDF
                // coordinates and receives the same projected hair silhouette.
                var uv = new List<Vector2>(positions.Length);
                for (int i = 0; i < positions.Length; i++)
                    uv.Add(new Vector2(.5f + positions[i].x / (2f * halfWidth),
                        (positions[i].y - bounds.min.y) / bounds.size.y));
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
