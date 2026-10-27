using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering.Experimental
{
    /// <summary>Owns a temporary pipeline override and restores it on teardown.</summary>
    public sealed class ExperimentalRenderSession : MonoBehaviour
    {
        public const string PipelineResource = "StudioEditorAtmosphere/ExperimentalPipeline";
        public static ExperimentalRenderSession Current { get; private set; }

        public Camera CameraOverride { get; set; }
        public Camera RenderCamera => CameraOverride != null ? CameraOverride : Camera.main;
        public bool IsActive => pipeline != null;
        public string Status { get; internal set; } = "Default Anime";
        public int RayTracingInstances => rayWorld?.InstanceCount ?? 0;
        public bool HardwareShadowsActive { get; internal set; }
        public bool SceneReflectionReady => surfaceLighting?.ReflectionReady ?? false;
        public string SceneReflectionStatus => surfaceLighting?.ReflectionStatus ?? "Disabled";
        public bool LocalLightingReady => localLighting?.Ready ?? false;
        public string LocalLightingStatus => localLighting?.Status ?? "Disabled";

        private readonly Dictionary<int, RenderPipelineAsset> originalPipelines =
            new Dictionary<int, RenderPipelineAsset>();
        private UniversalRenderPipelineAsset pipeline;
        private GeometryShadowWorld rayWorld;
        private ExperimentalSurfaceLighting surfaceLighting;
        private LocalLightingVolume localLighting;
        private ExperimentalLitMaterials localMaterials;
        private FaceHairShadowRenderer faceHairShadows;
        private ClothesSkinShadowRenderer clothesSkinShadows;
        private bool failed;
        private SceneRenderScheme lastScheme;

        internal GeometryShadowWorld RayWorld => rayWorld ?? (rayWorld = new GeometryShadowWorld());
        public void RefreshRayGeometry() => rayWorld?.InvalidateGeometry();
        public void RefreshSceneReflection() => surfaceLighting?.RefreshReflection();
        internal LocalLightingVolume LocalLighting => localLighting ?? (localLighting = new LocalLightingVolume(this));
        public void RefreshLocalLighting() { localLighting?.Invalidate(); rayWorld?.InvalidateGeometry(); }

        internal void ReleaseRayWorld()
        {
            rayWorld?.Dispose();
            rayWorld = null;
            HardwareShadowsActive = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Current != null) return;
            var owner = new GameObject("Studio Editor Experimental Rendering");
            DontDestroyOnLoad(owner);
            owner.AddComponent<ExperimentalRenderSession>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (Current != null && Current != this)
            {
                enabled = false;
                return;
            }
            Current = this;
            ApplyScheme(ExperimentalRenderConfiguration.Scheme);
        }

        private void LateUpdate()
        {
            var scheme = ExperimentalRenderConfiguration.Scheme;
            if (scheme != lastScheme) failed = false;
            if (!failed) ApplyScheme(scheme);
            var settings = ExperimentalRenderConfiguration.Settings;
            var local = ExperimentalRenderConfiguration.LocalLighting;
            bool localActive = IsActive && ExperimentalRenderConfiguration.Surfaces?.Enabled == true && local?.Enabled == true;
            if (localActive) localMaterials?.Tick();
            else
            {
                localLighting?.Dispose(); localLighting = null;
                localMaterials?.Restore();
            }
            if (!localActive && (settings == null || settings.Density <= 0 ||
                settings.Shadows != AtmosphereShadowMode.HardwareRayTracing)) ReleaseRayWorld();
            surfaceLighting?.Tick();
        }

        public bool ApplyScheme(SceneRenderScheme scheme)
        {
            ExperimentalRenderConfiguration.Scheme = scheme;
            lastScheme = scheme;
            if (scheme == SceneRenderScheme.DefaultAnime)
            {
                Restore();
                return true;
            }
            if (Current != null && Current != this) return false;
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DTextures)
                return Fail("The atmosphere experiment requires compute shaders and 3D textures.");
            if (pipeline == null)
            {
                if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                    return Fail("The atmosphere experiment requires URP.");
                var template = Resources.Load<UniversalRenderPipelineAsset>(PipelineResource);
                if (template == null) return Fail("ExperimentalPipeline resource is missing.");
                pipeline = Instantiate(template);
                pipeline.name = "Experimental Atmosphere (runtime)";
                pipeline.hideFlags = HideFlags.HideAndDontSave;
                Current = this;
                Status = "Atmosphere enabled";
                if (Application.isPlaying)
                {
                    surfaceLighting = new ExperimentalSurfaceLighting(this);
                    faceHairShadows = new FaceHairShadowRenderer(this);
                    clothesSkinShadows = new ClothesSkinShadowRenderer(this);
                    localMaterials = new ExperimentalLitMaterials();
                }
            }
            int level = QualitySettings.GetQualityLevel();
            if (!originalPipelines.ContainsKey(level))
                originalPipelines.Add(level, QualitySettings.renderPipeline);
            if (QualitySettings.renderPipeline != pipeline)
                QualitySettings.renderPipeline = pipeline;
            return true;
        }

        private bool Fail(string message)
        {
            failed = true;
            Status = message;
            Debug.LogWarning("[Render experiment] " + message, this);
            return false;
        }

        public void Restore()
        {
            faceHairShadows?.Dispose(); faceHairShadows = null;
            clothesSkinShadows?.Dispose(); clothesSkinShadows = null;
            localLighting?.Dispose(); localLighting = null;
            localMaterials?.Restore(); localMaterials = null;
            surfaceLighting?.Dispose();
            surfaceLighting = null;
            ReleaseRayWorld();
            if (pipeline != null)
            {
                int selectedLevel = QualitySettings.GetQualityLevel();
                foreach (var original in originalPipelines)
                {
                    // QualitySettings has no per-level setter. Preserve the user's selected level.
                    QualitySettings.SetQualityLevel(original.Key, false);
                    if (QualitySettings.renderPipeline == pipeline)
                        QualitySettings.renderPipeline = original.Value;
                }
                QualitySettings.SetQualityLevel(selectedLevel, false);
                CoreUtils.Destroy(pipeline);
                pipeline = null;
            }
            originalPipelines.Clear();
            Status = "Default Anime";
            if (Current == this) Current = null;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
