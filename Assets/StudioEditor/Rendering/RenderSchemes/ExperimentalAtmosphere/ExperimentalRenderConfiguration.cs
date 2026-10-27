using UnityEngine;

namespace StudioEditor.Rendering.Experimental
{
    public enum SceneRenderScheme
    {
        DefaultAnime,
        ExperimentalAtmosphere,
    }

    public enum AtmosphereShadowMode
    {
        ShadowMaps,
        HardwareRayTracing,
    }

    public enum AtmosphereQuality
    {
        Low,
        Balanced,
        High,
    }

    /// <summary>The code-only switch for the scene rendering experiment.</summary>
    public static class ExperimentalRenderConfiguration
    {
        // Change this to DefaultAnime to run the original rendering scheme.
        public static SceneRenderScheme Scheme = SceneRenderScheme.ExperimentalAtmosphere;

        public static AtmosphereSettings Settings = new AtmosphereSettings();
        public static SurfaceLightingSettings Surfaces = new SurfaceLightingSettings();
        public static LocalLightingSettings LocalLighting = new LocalLightingSettings();
    }

    [System.Serializable]
    public sealed class LocalLightingSettings
    {
        public bool Enabled = true;
        public float IndirectStrength = 0.8f;
        public bool IncludeAlphaClip = true;
        public float OcclusionStrength = 0.7f;
        public float ContactShadowStrength = 0.65f;
        public float ContactRadius = 0.35f;
        public float NormalBias = 0.025f;
        public float RayDistance = 10f;
        public int RaysPerDirection = 8;
        public int ContactRaysPerDirection = 4;
        public float UpdatesPerSecond = 2f;
        public float ContactUpdatesPerSecond = 4f;
        public Vector3Int ProbeGrid = new Vector3Int(32, 16, 32);
        public float ProbeSpacing = 0.6f;
        public Vector3Int ContactGrid = new Vector3Int(80, 40, 80);
        public float ContactSpacing = 0.1f;
        public float AnchorDistance = 6f;
        public LocalLightingDebugView DebugView = LocalLightingDebugView.None;
    }

    public enum LocalLightingDebugView { None, Indirect, Occlusion, ContactShadow, Coverage }

    [System.Serializable]
    public sealed class SurfaceLightingSettings
    {
        public bool Enabled = true;
        public float DirectStrength = 1f;
        public float AmbientStrength = 0.65f;
        public float LightColorInfluence = 0.65f;
        public float ReflectionStrength = 0.7f;
        public float HairHighlight = 0.22f;
        public float SkinHighlight = 0.035f;
        public float ClothHighlight = 0.06f;
        public float AccessoryHighlight = 0.18f;
        // Maps often bring their own sun while the editor's default light is still active.
        // Keep the designated RenderSettings.sun as the key light, reduce competing directionals.
        public float SecondaryDirectionalStrength = 0.2f;
        public bool SceneReflectionProbe = true;
        public int ReflectionResolution = 128;
        public float ReflectionDistance = 80f;
        public bool ToneMapping = true;
        public float Exposure = -0.4f;
    }

    [System.Serializable]
    public sealed class AtmosphereSettings
    {
        public AtmosphereShadowMode Shadows = AtmosphereShadowMode.HardwareRayTracing;
        public AtmosphereQuality Quality = AtmosphereQuality.Balanced;
        public float Density = 0.018f;
        public float MaxDistance = 60f;
        public float BaseHeight = 0f;
        public float HeightFalloff = 0.15f;
        public Color Albedo = new Color(0.72f, 0.8f, 0.9f, 1f);
        public Color Ambient = new Color(0.25f, 0.3f, 0.38f, 1f);
        public float SunScattering = 4f;
        public float Anisotropy = 0.4f;
        public float RayDistance = 300f;
        // Skinning is sampled at this rate; transform changes are updated every frame.
        public float GeometryUpdatesPerSecond = 4f;

        public Vector3Int GetGridSize(Camera camera)
        {
            int width = Quality == AtmosphereQuality.Low ? 48 :
                Quality == AtmosphereQuality.High ? 160 : 96;
            int depth = Quality == AtmosphereQuality.Low ? 32 :
                Quality == AtmosphereQuality.High ? 96 : 64;
            float aspect = Mathf.Max(0.25f, camera.aspect);
            return new Vector3Int(width, Mathf.Clamp(Mathf.CeilToInt(width / aspect), 16, 192), depth);
        }
    }
}
