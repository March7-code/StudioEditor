using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering
{
    // Shared by every URP scheme. Source intent comes from format adapters.
    [DefaultExecutionOrder(2000)]
    public sealed class ImportedSceneRenderBridge : MonoBehaviour
    {
        private sealed class Binding
        {
            public ImportedSceneRenderContext Context;
            public Light Light;
            public uint Layer;
            public int Revision;
            public readonly Dictionary<Renderer, uint> OriginalLayers = new();
            public readonly Dictionary<Light, uint> OriginalLightLayers = new();
            public readonly Dictionary<Renderer, MaterialPropertyBlock> OriginalItemBlocks = new();
            public Light[] WorldLights = System.Array.Empty<Light>();
            public readonly Vector4[] LinearLightColors = new Vector4[32];
            public readonly Vector4[] LegacyLightColors = new Vector4[32];
            public readonly Vector4[] SourceLightPositions = new Vector4[32];
            public int SourceLightCount = -1;
        }
        private readonly Dictionary<ImportedSceneRenderContext, Binding> bindings = new();
        private readonly Dictionary<Light, bool> hostLights = new();
        private sealed class LightColorScope
        {
            public Camera Camera;
            public readonly List<(Light light, Color original, Color applied)> Colors = new();
        }
        private readonly Stack<LightColorScope> lightColorScopes = new();
        private readonly HashSet<Light> scopedLightColors = new();
        private ImportedSceneEnvironment originalEnvironment;
        private ImportedSceneEnvironment appliedEnvironment;
        private Light originalSun;
        private Light appliedSun;
        private UniversalAdditionalLightData scopedSunData;
        private bool originalCustomShadowLayers;
        private uint originalShadowLayers;
        private Volume volume;
        private VolumeProfile profile;
        private ColorLookup lookup;
        private DisplayCameraScope displayScope;
        private Material lookupBlendMaterial;
        private RenderTexture blendedLookup;
        private Texture previousPrimaryLookup, previousSecondaryLookup;
        private float previousLookupBlend;
        private const int VolumeLayer = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var owner = new GameObject("Imported scene rendering");
            DontDestroyOnLoad(owner);
            owner.AddComponent<ImportedSceneRenderBridge>();
        }

        public static bool IsSourceKeyLight(Light light) =>
            light != null && light.GetComponent<ImportedSceneKeyLight>() != null;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void BeginCamera(ScriptableRenderContext renderContext, Camera camera)
        {
            var scope = new LightColorScope { Camera = camera };
            lightColorScopes.Push(scope);
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return;
            foreach (var binding in bindings.Values)
            {
                if (binding.Context == null || binding.Context.UseLinearLightIntensity) continue;
                foreach (var light in binding.WorldLights)
                {
                    if (light == null || !light.isActiveAndEnabled || light.intensity <= 0 ||
                        scopedLightColors.Contains(light)) continue;
                    var original = light.color;
                    // URP always builds linear light colors. Re-encode the source
                    // (color * intensity).linear energy before culling, so Lit,
                    // reflections and the ray cache receive the same radiance.
                    var applied = ((original * light.intensity).linear / light.intensity).gamma;
                    applied.a = original.a;
                    scope.Colors.Add((light, original, applied));
                    scopedLightColors.Add(light);
                    light.color = applied;
                }
            }
        }

        private void EndCamera(ScriptableRenderContext renderContext, Camera camera)
        {
            if (lightColorScopes.Count == 0 || lightColorScopes.Peek().Camera != camera) return;
            RestoreLightColors(lightColorScopes.Pop());
        }

        private void RestoreLightColors(LightColorScope scope)
        {
            foreach (var entry in scope.Colors)
            {
                if (entry.light != null && entry.light.color == entry.applied) entry.light.color = entry.original;
                scopedLightColors.Remove(entry.light);
            }
        }

        public static int SelectWorldLightIndex(UniversalLightData lights)
        {
            int result = -1;
            float brightness = -1f;
            for (int index = 0; index < lights.visibleLights.Length; index++)
            {
                var visible = lights.visibleLights[index];
                if (visible.lightType != LightType.Directional || IsSourceKeyLight(visible.light)) continue;
                if (visible.light != null && (visible.light.GetUniversalAdditionalLightData().renderingLayers.value & 1u) == 0) continue;
                if (index == lights.mainLightIndex) return index;
                float value = visible.finalColor.maxColorComponent;
                if (value <= brightness) continue;
                brightness = value;
                result = index;
            }
            return result;
        }

        private void LateUpdate()
        {
            var contexts = ImportedSceneRenderContext.Contexts;
            foreach (var context in bindings.Keys.Where(context => context == null || !contexts.Contains(context)).ToArray())
                Release(context);
            foreach (var context in contexts)
            {
                if (!bindings.TryGetValue(context, out var binding))
                {
                    uint layer = 2;
                    while (layer <= 128 && bindings.Values.Any(value => value.Layer == layer)) layer <<= 1;
                    if (layer > 128) continue;
                    if (bindings.Count == 0) originalSun = RenderSettings.sun;
                    var owner = new GameObject("Imported character key light");
                    owner.transform.SetParent(context.transform, false);
                    owner.AddComponent<ImportedSceneKeyLight>();
                    var light = owner.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                    var data = light.GetUniversalAdditionalLightData();
                    data.renderingLayers = layer;
                    data.customShadowLayers = false;
                    binding = new Binding { Context = context, Light = light, Layer = layer };
                    bindings.Add(context, binding);
                }
                if (binding.Revision != context.Revision) Apply(binding);
                ApplyLegacyLightColors(binding);
            }
            var active = contexts.LastOrDefault(context => bindings.ContainsKey(context));
            SynchronizeHostLights(contexts);
            SynchronizeEnvironment(active?.Environment);
            if (active == null) { RestoreSun(); RestoreCamera(); DestroyVolume(); return; }
            var worldSun = active.WorldMainLight;
            if (!IsWorldDirectional(worldSun))
                worldSun = !active.OwnsSceneLighting && IsWorldDirectional(originalSun) ? originalSun : null;
            if (worldSun == null)
                worldSun = Object.FindObjectsByType<Light>()
                    .Where(light => IsWorldDirectional(light) &&
                        (!active.OwnsSceneLighting || light.transform.IsChildOf(active.transform)))
                    .OrderByDescending(light => light.intensity * light.color.maxColorComponent).FirstOrDefault();
            var selected = worldSun != null ? worldSun : bindings[active].Light;
            if (selected != appliedSun)
            {
                RestoreSunShadowLayers();
                appliedSun = selected;
                if (worldSun != null)
                {
                    scopedSunData = selected.GetUniversalAdditionalLightData();
                    originalCustomShadowLayers = scopedSunData.customShadowLayers;
                    originalShadowLayers = scopedSunData.shadowRenderingLayers.value;
                }
            }
            // Character isolation affects illumination, while characters still cast onto the map.
            if (scopedSunData != null)
            {
                uint casters = 1;
                foreach (var binding in bindings.Values) casters |= binding.Layer;
                scopedSunData.customShadowLayers = true;
                scopedSunData.shadowRenderingLayers = originalShadowLayers | casters;
            }
            RenderSettings.sun = selected;
            PrepareLookup(active);
        }

        private static bool IsWorldDirectional(Light light) => light != null && light.isActiveAndEnabled &&
            light.type == LightType.Directional && !IsSourceKeyLight(light) &&
            (light.GetUniversalAdditionalLightData().renderingLayers.value & 1u) != 0;

        private static void ApplyLegacyLightColors(Binding binding)
        {
            int count = 0;
            bool changed = false;
            if (!binding.Context.UseLinearLightIntensity && QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                // The source game's GraphicsSettings has lightsUseLinearIntensity=false:
                // convert (color * intensity), rather than (color.linear * intensity).
                // Character-only lights use the shader lookup. World lights are
                // converted before culling, including lights shared with characters.
                var lights = binding.Context.SceneLights.Where(target => target != null &&
                        (target.Audience == ImportedLightAudience.All || target.Audience == ImportedLightAudience.Characters))
                    .Select(target => target.GetComponent<Light>()).Append(binding.Light)
                    .Where(light => light != null && light.isActiveAndEnabled && light.intensity > 0)
                    .OrderByDescending(light => light.intensity * light.color.maxColorComponent).Take(32);
                foreach (var light in lights)
                {
                    Vector4 linear = light.color.linear * light.intensity;
                    Vector4 legacy = (light.color * light.intensity).linear;
                    if (System.Array.IndexOf(binding.WorldLights, light) >= 0) linear = legacy;
                    Vector3 position = light.type == LightType.Directional ? -light.transform.forward : light.transform.position;
                    var encoded = new Vector4(position.x, position.y, position.z, light.type == LightType.Directional ? 0 : 1);
                    changed |= binding.LinearLightColors[count] != linear || binding.LegacyLightColors[count] != legacy ||
                        binding.SourceLightPositions[count] != encoded;
                    binding.LinearLightColors[count] = linear;binding.LegacyLightColors[count] = legacy;
                    binding.SourceLightPositions[count] = encoded;
                    count++;
                }
            }
            if (!changed && count == binding.SourceLightCount) return;
            binding.SourceLightCount = count;
            foreach (var renderer in binding.OriginalLayers.Keys)
            {
                if (renderer == null) continue;
                var block = new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                block.SetFloat("_SourceLegacyLightCount", count);
                block.SetVectorArray("_SourceLinearLightColors", binding.LinearLightColors);
                block.SetVectorArray("_SourceLegacyLightColors", binding.LegacyLightColors);
                block.SetVectorArray("_SourceLightPositions", binding.SourceLightPositions);
                renderer.SetPropertyBlock(block);
            }
        }

        private void SynchronizeHostLights(IReadOnlyList<ImportedSceneRenderContext> contexts)
        {
            if (!contexts.Any(context => context.OwnsSceneLighting))
            {
                RestoreHostLights();
                return;
            }

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (contexts.Any(context => light.transform.IsChildOf(context.transform)))
                {
                    if (hostLights.Remove(light, out var enabled)) light.enabled = enabled;
                    continue;
                }
                if (!hostLights.ContainsKey(light)) hostLights.Add(light, light.enabled);
                light.enabled = false;
            }
        }

        private void RestoreHostLights()
        {
            foreach (var entry in hostLights)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            hostLights.Clear();
        }

        private void SynchronizeEnvironment(ImportedSceneEnvironment environment)
        {
            if (environment == appliedEnvironment) return;
            if (environment != null)
            {
                originalEnvironment ??= new ImportedSceneEnvironment(RenderSettings.ambientMode,
                    RenderSettings.ambientIntensity, RenderSettings.ambientSkyColor,
                    RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor,
                    RenderSettings.ambientProbe);
                ApplyEnvironment(environment);
            }
            else if (originalEnvironment != null)
            {
                ApplyEnvironment(originalEnvironment);
                originalEnvironment = null;
            }
            appliedEnvironment = environment;
        }

        private static void ApplyEnvironment(ImportedSceneEnvironment environment)
        {
            RenderSettings.ambientMode = environment.Mode;
            RenderSettings.ambientIntensity = environment.Intensity;
            RenderSettings.ambientSkyColor = environment.Sky;
            RenderSettings.ambientEquatorColor = environment.Equator;
            RenderSettings.ambientGroundColor = environment.Ground;
            RenderSettings.ambientProbe = environment.Probe;
        }

        private static void Apply(Binding binding)
        {
            var context = binding.Context;
            RestoreLayers(binding);
            binding.WorldLights = context.GetComponentsInChildren<Light>(true).Where(light =>
                !IsSourceKeyLight(light) && (light.GetComponent<ImportedLightTarget>() is not { } target ||
                    target.Audience == ImportedLightAudience.World || target.Audience == ImportedLightAudience.All)).ToArray();
            // Studio item shaders use the same global ramp/ambient settings as
            // characters, while retaining their own illumination layer.
            foreach (var renderer in context.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.sharedMaterials.Any(material => material != null &&
                    material.HasProperty("_SourceItemModel") && material.GetFloat("_SourceItemModel") > 0.5f)) continue;
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                binding.OriginalItemBlocks.Add(renderer, original);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetTexture("_SourceRamp", context.CharacterRamp != null ? context.CharacterRamp : Texture2D.whiteTexture);
                block.SetVector("_SourceAmbientShadow", context.CharacterAmbientShadow);
                renderer.SetPropertyBlock(block);
            }
            foreach (var renderer in context.CharacterRenderers)
            {
                if (renderer == null || binding.OriginalLayers.ContainsKey(renderer)) continue;
                binding.OriginalLayers.Add(renderer, renderer.renderingLayerMask);
                renderer.renderingLayerMask = binding.Layer;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetFloat("_ImportedCharacterKey", 1f);
                block.SetFloat("_SourceCharacterShading", context.CharacterRamp != null ? 1f : 0f);
                block.SetVector("_SourceLineColor", new Vector4(.5f, .5f, .5f, 1f - context.CharacterLineDepth));
                block.SetFloat("_SourceLineWidth", context.CharacterLineWidth);
                block.SetTexture("_SourceRamp", context.CharacterRamp != null ? context.CharacterRamp : Texture2D.whiteTexture);
                // The legacy shader receives this global as raw components, including alpha.
                block.SetVector("_SourceAmbientShadow", context.CharacterAmbientShadow);
                renderer.SetPropertyBlock(block);
            }
            foreach (var target in context.SceneLights)
            {
                if (target == null || !target.TryGetComponent<Light>(out var light) || binding.OriginalLightLayers.ContainsKey(light)) continue;
                var data = light.GetUniversalAdditionalLightData();
                binding.OriginalLightLayers.Add(light, data.renderingLayers.value);
                data.renderingLayers = target.Audience switch
                {
                    ImportedLightAudience.Characters => binding.Layer,
                    ImportedLightAudience.World => 1u,
                    ImportedLightAudience.None => 0u,
                    _ => 1u | binding.Layer
                };
            }
            var source = context.CharacterLight;
            binding.Light.color = source.Color;
            binding.Light.intensity = source.Intensity;
            binding.Light.transform.rotation = Quaternion.Euler(source.EulerAngles);
            binding.Light.shadows = source.Shadows ? LightShadows.Soft : LightShadows.None;
            binding.SourceLightCount = -1;
            binding.Revision = context.Revision;
        }

        private void PrepareLookup(ImportedSceneRenderContext context)
        {
            if (!ImportedRenderConfiguration.ApplySourceColorGrading || context.ColorLookup == null || context.ColorLookupBlend <= 0f)
            { RestoreCamera(); DestroyVolume(); return; }
            var camera = Camera.main;
            if (camera == null) return;
            if (displayScope == null || displayScope.Camera != camera)
            {
                RestoreCamera();
                displayScope = new DisplayCameraScope(camera, 1 << VolumeLayer);
            }
            if (volume == null)
            {
                var owner = new GameObject("Imported scene color lookup") { layer = VolumeLayer };
                owner.transform.SetParent(transform, false);
                volume = owner.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10001;
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                lookup = profile.Add<ColorLookup>(true);
                // Amplify's source look uses an LDR LUT without URP's Neutral curve.
                // Explicit overrides also prevent a fallback/default profile from adding it.
                profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
                profile.Add<ColorAdjustments>(false).postExposure.Override(0f);
                volume.sharedProfile = profile;
            }
            lookup.texture.Override(PrepareLookupTexture(context));
            lookup.contribution.Override(context.ColorLookupBlend);
        }

        private Texture PrepareLookupTexture(ImportedSceneRenderContext context)
        {
            var primary = context.ColorLookup;
            var secondary = context.ColorLookupSecondary;
            if (secondary == null)
            {
                DestroyLookupBlend();
                return primary;
            }
            if (blendedLookup != null && previousPrimaryLookup == primary && previousSecondaryLookup == secondary &&
                previousLookupBlend == context.ColorLookupSecondaryBlend) return blendedLookup;
            if (lookupBlendMaterial == null)
            {
                var shader = Resources.Load<Shader>("Shaders/ImportedColorLookupBlend");
                lookupBlendMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
            if (blendedLookup == null || blendedLookup.width != primary.width || blendedLookup.height != primary.height)
            {
                if (blendedLookup != null) CoreUtils.Destroy(blendedLookup);
                // AmplifyColorBase also mixes LUT data into an LDR, linear ARGB32
                // cache before sampling. Do not use the URP contribution to mix
                // with an ungraded image: the secondary LUT is already authored.
                blendedLookup = new RenderTexture(primary.width, primary.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "Imported blended color lookup", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                blendedLookup.Create();
            }
            lookupBlendMaterial.SetTexture("_SecondaryLut", secondary);
            lookupBlendMaterial.SetFloat("_Blend", context.ColorLookupSecondaryBlend);
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try { GL.sRGBWrite = false;Graphics.Blit(primary, blendedLookup, lookupBlendMaterial); }
            finally { RenderTexture.active = active;GL.sRGBWrite = srgb; }
            previousPrimaryLookup = primary;previousSecondaryLookup = secondary;
            previousLookupBlend = context.ColorLookupSecondaryBlend;
            return blendedLookup;
        }

        private void DestroyLookupBlend()
        {
            if (blendedLookup != null) CoreUtils.Destroy(blendedLookup);
            if (lookupBlendMaterial != null) CoreUtils.Destroy(lookupBlendMaterial);
            blendedLookup = null;lookupBlendMaterial = null;
            previousPrimaryLookup = previousSecondaryLookup = null;
        }

        private void Release(ImportedSceneRenderContext context)
        {
            var binding = bindings[context];
            if (RenderSettings.sun == binding.Light) RenderSettings.sun = originalSun;
            RestoreLayers(binding);
            if (binding.Light != null) CoreUtils.Destroy(binding.Light.gameObject);
            bindings.Remove(context);
            // Restore before a replacement context captures the original scene state.
            if (bindings.Count == 0) RestoreSun();
        }

        private static void RestoreLayers(Binding binding)
        {
            foreach (var entry in binding.OriginalLayers)
                if (entry.Key != null)
                {
                    if (entry.Key.renderingLayerMask == binding.Layer) entry.Key.renderingLayerMask = entry.Value;
                    var block = new MaterialPropertyBlock();
                    entry.Key.GetPropertyBlock(block);
                    block.SetFloat("_ImportedCharacterKey", 0f);
                    // A standalone character already has source defaults on
                    // its materials. Releasing a scene override must not
                    // switch that character back to generic face shading.
                    var source = entry.Key.sharedMaterials.FirstOrDefault(material => material != null &&
                        material.HasProperty("_SourceCharacterShading") && material.GetFloat("_SourceCharacterShading") > .5f);
                    block.SetFloat("_SourceCharacterShading", source != null ? 1f : 0f);
                    if (source != null)
                    {
                        block.SetTexture("_SourceRamp", source.GetTexture("_SourceRamp"));
                        block.SetVector("_SourceAmbientShadow", source.GetVector("_SourceAmbientShadow"));
                        block.SetVector("_SourceLineColor", source.GetVector("_SourceLineColor"));
                        block.SetFloat("_SourceLineWidth", source.GetFloat("_SourceLineWidth"));
                    }
                    block.SetFloat("_SourceLegacyLightCount", 0f);
                    entry.Key.SetPropertyBlock(block);
                }
            binding.OriginalLayers.Clear();
            foreach (var entry in binding.OriginalItemBlocks)
                if (entry.Key != null) entry.Key.SetPropertyBlock(entry.Value);
            binding.OriginalItemBlocks.Clear();
            foreach (var entry in binding.OriginalLightLayers)
                if (entry.Key != null) entry.Key.GetUniversalAdditionalLightData().renderingLayers = entry.Value;
            binding.OriginalLightLayers.Clear();
        }
        private void RestoreSun()
        {
            if (RenderSettings.sun == appliedSun) RenderSettings.sun = originalSun;
            RestoreSunShadowLayers();
            appliedSun = null;
        }
        private void RestoreSunShadowLayers()
        {
            if (scopedSunData != null)
            {
                scopedSunData.customShadowLayers = originalCustomShadowLayers;
                scopedSunData.shadowRenderingLayers = originalShadowLayers;
            }
            scopedSunData = null;
        }
        private void RestoreCamera()
        {
            displayScope?.Dispose();
            displayScope = null;
        }
        private void DestroyVolume()
        {
            if (volume != null) CoreUtils.Destroy(volume.gameObject);
            if (profile != null)
            {
                foreach (var component in profile.components) CoreUtils.Destroy(component);
                CoreUtils.Destroy(profile);
            }
            volume = null; profile = null; lookup = null;
            DestroyLookupBlend();
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            while (lightColorScopes.Count > 0) RestoreLightColors(lightColorScopes.Pop());
            foreach (var context in bindings.Keys.ToArray()) Release(context);
            RestoreHostLights();
            SynchronizeEnvironment(null);
            RestoreSun(); RestoreCamera(); DestroyVolume();
        }
    }

    public sealed class ImportedSceneKeyLight : MonoBehaviour { }
}
