using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering.Experimental
{
    // Face lighting needs scene occlusion without receiving a second, unfiltered
    // copy of its own character through URP's shared shadow atlas. Self shading
    // uses the SDF and FaceHairShadowRenderer; this map retains other actors
    // and the environment, without sleeves, neck or oral meshes crossing the face.
    internal sealed class FaceEnvironmentShadowRenderer : IDisposable
    {
        private const int Resolution = 512;
        private readonly Shader shader;
        private readonly Dictionary<Component, Map> maps = new();
        private readonly Dictionary<Material, Material> materials = new();
        private readonly MaterialPropertyBlock block = new();
        private Renderer[] candidates = Array.Empty<Renderer>();
        private float nextScan;

        private sealed class Map
        {
            internal RenderTexture Depth;
            internal readonly Matrix4x4[] Matrices = new Matrix4x4[FaceHairShadowRenderer.MaxLights];
            internal readonly Vector4[] Lights = new Vector4[FaceHairShadowRenderer.MaxLights];
            internal Renderer[] Receivers;
        }

        internal FaceEnvironmentShadowRenderer(Shader shader) => this.shader = shader;

        internal void Render(CommandBuffer commands, FaceShadowBinding face, Bounds bounds, Light[] lights)
        {
            if (shader == null) { Clear(face); return; }
            face.Target.GetPropertyBlock(block);
            float Value(string key) => block.HasFloat(key) ? block.GetFloat(key) : face.Target.sharedMaterial.GetFloat(key);
            if (Value("_SourceCharacterShading") < .5f || Value("_FaceShadowReady") < .5f || Value("_FaceShadowStrength") <= 0)
            { Clear(face); return; }
            Renderer[] receivers;
            if (maps.TryGetValue(face, out var existing)) receivers = existing.Receivers;
            else
            {
                var targets = new List<Renderer> {face.Target};
                if (face.HeadRoot != null)
                    foreach (var renderer in face.HeadRoot.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == face.Target) continue;
                        foreach (var material in renderer.sharedMaterials)
                            if (material != null && material.HasProperty("_SourceCharacterModel") &&
                                (material.GetFloat("_SourceCharacterModel") == 5 || material.GetFloat("_SourceCharacterModel") == 6))
                            { targets.Add(renderer); break; }
                    }
                receivers = targets.ToArray();
            }
            RenderMap(commands, face, receivers, bounds, lights, face.HeadFrame.up,
                renderer => CastsSceneShadow(renderer, face));
        }

        // Reuse the same external-only visibility for body skin. Its own mesh
        // and garments select a colored toon band in the local skin map.
        internal void RenderBody(CommandBuffer commands, CharacterShadowBinding owner, Renderer[] receivers,
            Bounds bounds, Light[] lights) => RenderMap(commands, owner, receivers, bounds, lights,
                owner.transform.up, renderer => CastsBodySceneShadow(renderer, owner));

        private void RenderMap(CommandBuffer commands, Component binding, Renderer[] receivers, Bounds bounds,
            Light[] lights, Vector3 up, Func<Renderer, bool> casts)
        {
            if (shader == null) { Clear(binding); return; }
            if (!maps.TryGetValue(binding, out var map))
            {
                map = new Map { Depth = new RenderTexture(new RenderTextureDescriptor(Resolution, Resolution)
                {
                    graphicsFormat = GraphicsFormat.R32_SFloat, depthBufferBits = 0,
                    dimension = TextureDimension.Tex2DArray, volumeDepth = FaceHairShadowRenderer.MaxLights, msaaSamples = 1,
                }) { name = "Skin scene occlusion without own character", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave } };
                if (!map.Depth.Create()) { CoreUtils.Destroy(map.Depth); Clear(binding); return; }
                maps.Add(binding, map);
            }
            map.Receivers = receivers;
            if (Time.unscaledTime >= nextScan)
            {
                candidates = UnityEngine.Object.FindObjectsByType<Renderer>();
                nextScan = Time.unscaledTime + .5f;
            }
            var weights = Vector4.zero;
            for (int index = 0; index < lights.Length; index++)
            {
                var light = lights[index];
                CreateProjection(bounds, light, up, out var clip, out var texture, out var culling);
                map.Matrices[index] = texture;
                var position = light.type == LightType.Directional ? -light.transform.forward : light.transform.position;
                map.Lights[index] = new Vector4(position.x,position.y,position.z,light.type == LightType.Directional ? 0 : 1);
                weights[index] = light.shadowStrength;
                var planes = GeometryUtility.CalculateFrustumPlanes(culling);
                commands.SetRenderTarget(map.Depth, 0, CubemapFace.Unknown, index);
                commands.ClearRenderTarget(false, true, Color.white);
                commands.SetGlobalMatrix("_HairShadowClipMatrix", clip);
                commands.SetGlobalMatrix("_HairShadowTextureMatrix", texture);
                foreach (var renderer in candidates)
                {
                    if (!casts(renderer) || !FaceHairShadowRenderer.CastsForLight(renderer, light) ||
                        !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                    int count = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh?.subMeshCount ?? 0 :
                        renderer.GetComponent<MeshFilter>()?.sharedMesh?.subMeshCount ?? 0;
                    var sources = renderer.sharedMaterials;
                    for (int slot = 0; slot < count && slot < sources.Length; slot++)
                        if (sources[slot] != null) commands.DrawRenderer(renderer, DepthMaterial(sources[slot]), slot, 0);
                }
            }
            foreach (var receiver in map.Receivers)
            {
                if (receiver == null) continue;
                receiver.GetPropertyBlock(block);
                var receiverWeights = weights;
                for (int index = 0; index < lights.Length; index++)
                    if ((lights[index].cullingMask & (1 << receiver.gameObject.layer)) == 0 ||
                        !FaceHairShadowRenderer.AcceptsLight(lights[index], receiver.renderingLayerMask, bounds)) receiverWeights[index] = 0;
                block.SetFloat("_FaceSceneShadowCount", lights.Length);
                block.SetTexture("_FaceSceneDepth", map.Depth);
                block.SetMatrixArray("_FaceSceneWorldToShadow", map.Matrices);
                block.SetVectorArray("_FaceHairLightPositions", map.Lights);
                block.SetVector("_FaceHairShadowWeights", receiverWeights);
                receiver.SetPropertyBlock(block);
            }
        }

        internal static bool CastsSceneShadow(Renderer renderer, FaceShadowBinding face)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.forceRenderingOff || renderer.shadowCastingMode == ShadowCastingMode.Off) return false;
            if (renderer == face.Target || face.HeadRoot != null && renderer.transform.IsChildOf(face.HeadRoot)) return false;
            var owner = face.GetComponentInParent<CharacterShadowBinding>();
            return owner == null || renderer.GetComponentInParent<CharacterShadowBinding>() != owner;
        }

        internal static bool CastsBodySceneShadow(Renderer renderer, CharacterShadowBinding owner) =>
            renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
            !renderer.forceRenderingOff && renderer.shadowCastingMode != ShadowCastingMode.Off &&
            renderer.GetComponentInParent<CharacterShadowBinding>() != owner;

        internal static void CreateProjection(Bounds bounds, Light light, Vector3 headUp,
            out Matrix4x4 clip, out Matrix4x4 texture, out Matrix4x4 culling)
        {
            float radius = Mathf.Max(.06f, bounds.extents.magnitude + .015f);
            bool directional = light.type == LightType.Directional;
            Vector3 direction = directional ? -light.transform.forward : (light.transform.position - bounds.center).normalized;
            float reach = Mathf.Max(1, QualitySettings.shadowDistance);
            Vector3 eye = directional ? bounds.center + direction * (reach + radius) : light.transform.position;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, headUp)) < .95f ? headUp :
                Mathf.Abs(direction.y) < .95f ? Vector3.up : Vector3.right;
            var view = Matrix4x4.Scale(new Vector3(1,1,-1)) *
                Matrix4x4.TRS(eye, Quaternion.LookRotation(-direction,up), Vector3.one).inverse;
            float distance = Vector3.Distance(eye,bounds.center);
            var projection = directional ? Matrix4x4.Ortho(-radius,radius,-radius,radius,.001f,distance+radius+.02f) :
                Matrix4x4.Perspective(Mathf.Asin(Mathf.Min(.99f,radius/distance))*2*Mathf.Rad2Deg,1,.001f,distance+radius+.02f);
            culling = projection * view;
            clip = GL.GetGPUProjectionMatrix(projection,true) * view;
            var bias = Matrix4x4.identity;
            bias.m00=.5f;bias.m03=.5f;bias.m11=SystemInfo.graphicsUVStartsAtTop?-.5f:.5f;bias.m13=.5f;
            bool openGL=SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLES3;
            bias.m22=SystemInfo.usesReversedZBuffer?-1:openGL?.5f:1;
            bias.m23=SystemInfo.usesReversedZBuffer?1:openGL?.5f:0;
            texture=bias*clip;
        }

        private Material DepthMaterial(Material source)
        {
            if (!materials.TryGetValue(source,out var material)) materials.Add(source,material=CoreUtils.CreateEngineMaterial(shader));
            string texture = source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
            string color = source.HasProperty("_BaseColor") ? "_BaseColor" : source.HasProperty("_Color") ? "_Color" : null;
            material.SetTexture("_BaseMap",texture != null ? source.GetTexture(texture) : Texture2D.whiteTexture);
            var scale=texture != null ? source.GetTextureScale(texture) : Vector2.one;
            var offset=texture != null ? source.GetTextureOffset(texture) : Vector2.zero;
            material.SetVector("_BaseMap_ST",new Vector4(scale.x,scale.y,offset.x,offset.y));
            material.SetColor("_BaseColor",color != null ? source.GetColor(color) : Color.white);
            bool transparent=source.HasProperty("_Surface") && source.GetFloat("_Surface")>.5f;
            material.SetFloat("_Cutoff",source.HasProperty("_Cutoff") ? Mathf.Max(.01f,source.GetFloat("_Cutoff")) : .5f);
            material.SetFloat("_HairAlphaClip",transparent || source.IsKeywordEnabled("_ALPHATEST_ON") ? 1 : 0);
            material.SetFloat("_HairFrontOnly",0);
            return material;
        }

        internal void Clear(Component face)
        {
            if (ReferenceEquals(face,null)) return;
            if (maps.TryGetValue(face,out var map))
            {
                foreach (var receiver in map.Receivers)
                    if (receiver != null) { receiver.GetPropertyBlock(block);block.SetFloat("_FaceSceneShadowCount",0);receiver.SetPropertyBlock(block); }
            }
            else if (face is FaceShadowBinding binding && binding != null && binding.Target != null)
            { binding.Target.GetPropertyBlock(block);block.SetFloat("_FaceSceneShadowCount",0);binding.Target.SetPropertyBlock(block); }
        }
        internal void Release(Component face)
        {
            Clear(face);
            if (!maps.TryGetValue(face,out var map)) return;
            map.Depth.Release();CoreUtils.Destroy(map.Depth);maps.Remove(face);
        }
        public void Dispose()
        {
            foreach (var pair in maps) { Clear(pair.Key);pair.Value.Depth.Release();CoreUtils.Destroy(pair.Value.Depth); }
            foreach (var material in materials.Values) CoreUtils.Destroy(material);
            maps.Clear();materials.Clear();
        }
    }
}
