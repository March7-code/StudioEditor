using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering.Experimental
{
    // Own-character depth selects the body's colored shadow band. External
    // scene depth separately blocks irradiance, as it does for face skin.
    internal sealed class ClothesSkinShadowRenderer : IDisposable
    {
        private const int Resolution = 1024, MaxLights = FaceHairShadowRenderer.MaxLights;
        private static readonly int CountId = Shader.PropertyToID("_ClothesSkinShadowCount");
        private readonly ExperimentalRenderSession session;
        private readonly Shader depthShader;
        private readonly FaceEnvironmentShadowRenderer environmentShadows;
        private readonly Dictionary<CharacterShadowBinding, Character> characters = new();
        private readonly Dictionary<Material, Material> depthMaterials = new();
        private readonly MaterialPropertyBlock block = new();
        private readonly CommandBuffer commands = new() { name = "Clothes shadows on skin" };
        private float nextScan;

        private sealed class Character
        {
            public CharacterShadowBinding Binding;
            public Renderer[] Skin = Array.Empty<Renderer>(), Casters = Array.Empty<Renderer>();
            public RenderTexture Depth;
            public readonly Matrix4x4[] Matrices = new Matrix4x4[MaxLights];
            public readonly Vector4[] Lights = new Vector4[MaxLights];
            public Vector4 Weights;
            public readonly Dictionary<Renderer, SkinBounds> Bounds = new();
        }

        // Imported renderer bounds include a large shared animation envelope.
        // Bone-space vertex envelopes are conservative under linear skinning
        // but follow the current pose without baking/allocating a mesh per frame.
        private sealed class SkinBounds
        {
            public Mesh Mesh;
            public Transform[] Bones;
            public Bounds[] Envelopes;
            public bool[] Used;
            public static SkinBounds Create(Renderer renderer)
            {
                if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null || !skin.sharedMesh.isReadable) return null;
                var mesh = skin.sharedMesh;
                var poses = mesh.bindposes; var weights = mesh.boneWeights; var vertices = mesh.vertices;
                var bones = skin.bones;
                if (weights.Length != vertices.Length || poses.Length != bones.Length || bones.Length == 0) return null;
                var result = new SkinBounds { Mesh = mesh, Bones = bones, Envelopes = new Bounds[bones.Length], Used = new bool[bones.Length] };
                void Add(int bone, float weight, Vector3 vertex)
                {
                    if (weight <= 0 || bone < 0 || bone >= bones.Length || bones[bone] == null) return;
                    var local = poses[bone].MultiplyPoint3x4(vertex);
                    if (!result.Used[bone]) { result.Envelopes[bone] = new Bounds(local, Vector3.zero); result.Used[bone] = true; }
                    else result.Envelopes[bone].Encapsulate(local);
                }
                for (int index = 0; index < vertices.Length; index++)
                {
                    var w = weights[index]; var v = vertices[index];
                    Add(w.boneIndex0, w.weight0, v); Add(w.boneIndex1, w.weight1, v);
                    Add(w.boneIndex2, w.weight2, v); Add(w.boneIndex3, w.weight3, v);
                }
                return result;
            }
            public Bounds World(Renderer renderer)
            {
                bool first = true; var result = renderer.bounds;
                for (int bone = 0; bone < Bones.Length; bone++)
                {
                    if (!Used[bone] || Bones[bone] == null) continue;
                    var bounds = Envelopes[bone];
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = Bones[bone].TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                        if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                    }
                }
                // Reserve room for facial expressions, which are not bone motion.
                result.Expand(.02f);
                return result;
            }
        }

        public ClothesSkinShadowRenderer(ExperimentalRenderSession session)
        {
            this.session = session;
            // Share the existing alpha-aware minimum-depth pass with hair.
            depthShader = Resources.Load<Shader>("StudioEditorAtmosphere/FaceHairDepth");
            environmentShadows = new FaceEnvironmentShadowRenderer(depthShader);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        private static bool IsSkin(Material material) => material != null &&
            material.HasProperty("_SourceCharacterModel") &&
            material.GetFloat("_SourceCharacterModel") == 3;

        private static bool IsLocalCaster(Material material) => material != null &&
            material.HasProperty("_SourceCharacterModel") &&
            // Blended translucent surfaces need transmittance, not opaque depth.
            (material.GetFloat("_Surface") < .5f || material.IsKeywordEnabled("_ALPHATEST_ON"));

        private static bool Active(Renderer renderer) => renderer != null && renderer.enabled &&
            renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff;

        internal static bool CanProjectBody(Bounds bounds, Light light) => light.type == LightType.Directional ||
            Vector3.Distance(light.transform.position, bounds.center) > Mathf.Max(.06f, bounds.extents.magnitude + .015f);

        private void Scan()
        {
            var current = UnityEngine.Object.FindObjectsByType<CharacterShadowBinding>();
            foreach (var binding in characters.Keys.Where(binding => binding == null || !current.Contains(binding)).ToArray())
            { Release(characters[binding]); characters.Remove(binding); }
            foreach (var binding in current)
            {
                if (!characters.TryGetValue(binding, out var character))
                    characters.Add(binding, character = new Character { Binding = binding });
                SetCount(character, 0);
                var renderers = binding.GetComponentsInChildren<Renderer>(true);
                character.Skin = renderers.Where(renderer => renderer.sharedMaterials.Any(IsSkin)).ToArray();
                character.Casters = renderers.Where(renderer => renderer.sharedMaterials.Any(IsLocalCaster)).ToArray();
                foreach (var previous in character.Bounds.Keys.Where(renderer => renderer == null || !character.Skin.Contains(renderer)).ToArray())
                    character.Bounds.Remove(previous);
                foreach (var renderer in character.Skin)
                    if (!character.Bounds.TryGetValue(renderer, out var bounds) ||
                        (renderer is SkinnedMeshRenderer skinned && bounds?.Mesh != skinned.sharedMesh))
                        character.Bounds[renderer] = SkinBounds.Create(renderer);
            }
            foreach (var source in depthMaterials.Keys.Where(source => source == null).ToArray())
            { CoreUtils.Destroy(depthMaterials[source]); depthMaterials.Remove(source); }
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != session.RenderCamera) return;
            if (!session.IsActive || ExperimentalRenderConfiguration.Surfaces?.Enabled != true || depthShader == null)
            { foreach (var character in characters.Values) SetCount(character, 0); return; }
            if (Time.unscaledTime >= nextScan) { Scan(); nextScan = Time.unscaledTime + .5f; }
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var lights = UnityEngine.Object.FindObjectsByType<Light>();
            commands.Clear();
            foreach (var character in characters.Values)
            {
                SetCount(character, 0);
                if (character.Binding == null || !character.Binding.isActiveAndEnabled) continue;
                var skin = character.Skin.Where(renderer => Active(renderer) && renderer.receiveShadows &&
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0).ToArray();
                var casters = character.Casters.Where(renderer => Active(renderer) &&
                    renderer.shadowCastingMode != ShadowCastingMode.Off).ToArray();
                if (skin.Length == 0) continue;
                Bounds SkinWorld(Renderer renderer) => character.Bounds.TryGetValue(renderer, out var value) && value != null ? value.World(renderer) : renderer.bounds;
                var bounds = SkinWorld(skin[0]);
                foreach (var renderer in skin.Skip(1)) bounds.Encapsulate(SkinWorld(renderer));
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                // A single perspective map cannot cover a point light inside
                // the receiving sphere. Keep URP's native cube / spot shadow
                // in that case instead of overriding it with incomplete depth.
                var selected = lights.Where(light => CanProjectBody(bounds, light) && skin.Any(renderer =>
                        (light.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
                        FaceHairShadowRenderer.AcceptsLight(light, renderer.renderingLayerMask, bounds)))
                    .OrderByDescending(light => light.GetComponent<ImportedSceneKeyLight>() != null)
                    .ThenByDescending(light => light.intensity * light.color.maxColorComponent /
                        (light.type == LightType.Directional ? 1 : Mathf.Max(.05f, (light.transform.position - bounds.center).sqrMagnitude)))
                    .Take(MaxLights).ToArray();
                if (selected.Length == 0) continue;
                if (character.Depth == null)
                {
                    character.Depth = new RenderTexture(new RenderTextureDescriptor(Resolution, Resolution)
                    {
                        graphicsFormat = GraphicsFormat.R16_UNorm, depthBufferBits = 0,
                        dimension = TextureDimension.Tex2DArray, volumeDepth = MaxLights, msaaSamples = 1,
                    })
                    {
                        name = "Character self depth on body skin", filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                    };
                    if (!character.Depth.Create())
                    { CoreUtils.Destroy(character.Depth); character.Depth = null; continue; }
                }
                for (int index = 0; index < selected.Length; index++)
                {
                    var light = selected[index];
                    FaceHairShadowRenderer.CreateProjection(bounds, light, character.Binding.transform.up,
                        out var clip, out var texture);
                    character.Matrices[index] = texture;
                    Vector3 position = light.type == LightType.Directional ? -light.transform.forward : light.transform.position;
                    character.Lights[index] = new Vector4(position.x, position.y, position.z, light.type == LightType.Directional ? 0 : 1);
                    character.Weights[index] = light.shadowStrength;
                    commands.SetRenderTarget(character.Depth, 0, CubemapFace.Unknown, index);
                    commands.ClearRenderTarget(false, true, Color.white);
                    commands.SetGlobalMatrix("_HairShadowClipMatrix", clip);
                    commands.SetGlobalMatrix("_HairShadowTextureMatrix", texture);
                    foreach (var renderer in casters)
                    {
                        if (!FaceHairShadowRenderer.CastsForLight(renderer, light)) continue;
                        var materials = renderer.sharedMaterials;
                        int submeshes = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh?.subMeshCount ?? 0 :
                            renderer.GetComponent<MeshFilter>()?.sharedMesh?.subMeshCount ?? 0;
                        for (int slot = 0; slot < materials.Length && slot < submeshes; slot++)
                            if (IsLocalCaster(materials[slot])) commands.DrawRenderer(renderer, DepthMaterial(materials[slot]), slot, 0);
                    }
                }
                foreach (var renderer in skin)
                {
                    renderer.GetPropertyBlock(block);
                    // A character normally shares one light audience. Zero the
                    // weight of any light excluded from an individual receiver.
                    var weights = character.Weights;
                    for (int index = 0; index < selected.Length; index++)
                        if ((selected[index].cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                            !FaceHairShadowRenderer.AcceptsLight(selected[index], renderer.renderingLayerMask, bounds)) weights[index] = 0;
                    block.SetFloat(CountId, selected.Length);
                    block.SetTexture("_ClothesSkinDepth", character.Depth);
                    block.SetMatrixArray("_ClothesSkinWorldToShadow", character.Matrices);
                    block.SetVectorArray("_ClothesSkinLightPositions", character.Lights);
                    block.SetVector("_ClothesSkinShadowWeights", weights);
                    renderer.SetPropertyBlock(block);
                }
                environmentShadows.RenderBody(commands, character.Binding, skin, bounds, selected);
            }
            context.ExecuteCommandBuffer(commands); commands.Clear();
        }

        private Material DepthMaterial(Material source)
        {
            if (!depthMaterials.TryGetValue(source, out var material))
                depthMaterials.Add(source, material = CoreUtils.CreateEngineMaterial(depthShader));
            material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            var scale = source.GetTextureScale("_BaseMap"); var offset = source.GetTextureOffset("_BaseMap");
            material.SetVector("_BaseMap_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            material.SetFloat("_HairAlphaClip", source.IsKeywordEnabled("_ALPHATEST_ON") ? 1 : 0);
            return material;
        }

        private void SetCount(Character character, int count)
        {
            foreach (var renderer in character.Skin)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block); block.SetFloat(CountId, count); renderer.SetPropertyBlock(block);
            }
            if (count == 0) environmentShadows.Clear(character.Binding);
        }
        private void Release(Character character)
        { SetCount(character, 0); environmentShadows.Release(character.Binding); if (character.Depth != null) { character.Depth.Release(); CoreUtils.Destroy(character.Depth); } }
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            foreach (var character in characters.Values) Release(character);
            foreach (var material in depthMaterials.Values) CoreUtils.Destroy(material);
            environmentShadows.Dispose();
            characters.Clear(); depthMaterials.Clear(); commands.Release();
        }
    }
}
