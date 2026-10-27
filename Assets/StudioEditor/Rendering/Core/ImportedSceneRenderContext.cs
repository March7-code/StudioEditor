using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering
{
    public enum ImportedLightAudience { All, Characters, World, None }

    public sealed class ImportedLightTarget : MonoBehaviour
    {
        public ImportedLightAudience Audience { get; set; }
    }

    public readonly struct ImportedSceneLight
    {
        public ImportedSceneLight(Color color, float intensity, Vector3 eulerAngles, bool shadows,
            LightType type = LightType.Directional)
        {
            Color = color;
            Intensity = float.IsFinite(intensity) ? Mathf.Max(0f, intensity) : 0f;
            EulerAngles = eulerAngles;
            Shadows = shadows;
            Type = type;
        }
        public Color Color { get; }
        public float Intensity { get; }
        public Vector3 EulerAngles { get; }
        public bool Shadows { get; }
        public LightType Type { get; }
    }

    public sealed class ImportedSceneEnvironment
    {
        public ImportedSceneEnvironment(AmbientMode mode, float intensity, Color sky,
            Color equator, Color ground, SphericalHarmonicsL2 probe)
        {
            Mode = mode; Intensity = intensity; Sky = sky;
            Equator = equator; Ground = ground; Probe = probe;
        }
        public AmbientMode Mode { get; }
        public float Intensity { get; }
        public Color Sky { get; }
        public Color Equator { get; }
        public Color Ground { get; }
        public SphericalHarmonicsL2 Probe { get; }
    }

    // Format adapters supply source intent. Render bridges own pipeline-specific state.
    [ExecuteAlways]
    public sealed class ImportedSceneRenderContext : MonoBehaviour
    {
        private static readonly List<ImportedSceneRenderContext> contexts = new();
        public static IReadOnlyList<ImportedSceneRenderContext> Contexts => contexts;
        public ImportedSceneLight CharacterLight { get; private set; }
        public ImportedSceneLight MapLight { get; private set; }
        public Light WorldMainLight { get; private set; }
        public bool OwnsSceneLighting { get; private set; }
        public ImportedSceneEnvironment Environment { get; private set; }
        public Texture ColorLookup { get; private set; }
        public float ColorLookupBlend { get; private set; }
        public Texture ColorLookupSecondary { get; private set; }
        public float ColorLookupSecondaryBlend { get; private set; }
        public Texture CharacterRamp { get; private set; }
        public Color CharacterAmbientShadow { get; private set; }
        public float CharacterLineDepth { get; private set; } = 0.5f;
        public float CharacterLineWidth { get; private set; } = 0.5f;
        public bool UseLinearLightIntensity { get; private set; } = true;
        public IReadOnlyList<Renderer> CharacterRenderers { get; private set; } = Array.Empty<Renderer>();
        public IReadOnlyList<ImportedLightTarget> SceneLights { get; private set; } = Array.Empty<ImportedLightTarget>();
        public int Revision { get; private set; }
        private bool configured;

        public void ConfigureEnvironment(ImportedSceneEnvironment environment)
        {
            Environment = environment;
            Revision++;
        }

        public void ConfigureCharacterShading(Texture ramp, Color ambientShadow,
            float lineDepth = 0.5f, float lineWidth = 0.5f, bool useLinearLightIntensity = true)
        {
            CharacterRamp = ramp;
            CharacterAmbientShadow = ambientShadow;
            CharacterLineDepth = Mathf.Clamp01(lineDepth);
            CharacterLineWidth = Mathf.Clamp01(lineWidth);
            UseLinearLightIntensity = useLinearLightIntensity;
            Revision++;
        }

        public void Configure(ImportedSceneLight characterLight, ImportedSceneLight mapLight,
            Texture colorLookup, float blend, IReadOnlyList<Renderer> characterRenderers,
            IReadOnlyList<ImportedLightTarget> sceneLights = null, Light worldMainLight = null,
            bool ownsSceneLighting = false, Texture colorLookupSecondary = null,
            float colorLookupSecondaryBlend = 0f)
        {
            CharacterLight = characterLight;
            MapLight = mapLight;
            WorldMainLight = worldMainLight;
            OwnsSceneLighting = ownsSceneLighting;
            ColorLookup = colorLookup;
            ColorLookupBlend = float.IsFinite(blend) ? Mathf.Clamp01(blend) : 0f;
            ColorLookupSecondary = colorLookupSecondary;
            ColorLookupSecondaryBlend = float.IsFinite(colorLookupSecondaryBlend)
                ? Mathf.Clamp01(colorLookupSecondaryBlend) : 0f;
            CharacterRenderers = characterRenderers ?? Array.Empty<Renderer>();
            SceneLights = sceneLights ?? Array.Empty<ImportedLightTarget>();
            configured = true;
            Revision++;
            Register();
        }
        private void Register()
        {
            if (configured && isActiveAndEnabled && !contexts.Contains(this)) contexts.Add(this);
        }
        private void OnEnable() => Register();
        private void OnDisable() => contexts.Remove(this);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => contexts.Clear();
    }
}
