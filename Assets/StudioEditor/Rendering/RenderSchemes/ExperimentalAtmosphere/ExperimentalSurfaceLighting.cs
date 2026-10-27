using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering.Experimental
{
    /// <summary>Camera-scoped surface lighting; source lights and materials are restored after rendering.</summary>
    internal sealed class ExperimentalSurfaceLighting : IDisposable
    {
        private readonly ExperimentalRenderSession session;
        private readonly List<(Light light, float intensity, float applied)> scaledLights =
            new List<(Light, float, float)>();
        private ReflectionProbe reflectionProbe;
        private Camera reflectionCamera;
        private RenderTexture reflectionTexture, captureTexture;
        private RenderTexture captureFace;
        private int nextFace = -1;
        private float nextReflection;
        private Vector3 reflectionAnchor;
        private Camera lastReflectionCamera;
        private int lastGeometryCount = -1;
        private float lastSunIntensity;
        private Quaternion lastSunRotation;
        private bool dirtyReflection = true;
        private bool rendering;
        private bool enabledLastFrame;
        private Volume displayVolume;
        private VolumeProfile displayProfile;
        private ColorAdjustments exposure;
        private DisplayCameraScope displayScope;
        private Camera scopedCamera;

        public bool ReflectionReady => reflectionProbe != null && reflectionTexture != null;
        public string ReflectionStatus => reflectionProbe == null ? "Disabled" :
            $"Capturing face={nextFace}, texture={reflectionTexture}";

        public ExperimentalSurfaceLighting(ExperimentalRenderSession session)
        {
            this.session = session;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        public void RefreshReflection() => dirtyReflection = true;

        public void Tick()
        {
            var settings = ExperimentalRenderConfiguration.Surfaces;
            bool active = session.IsActive && settings != null && settings.Enabled;
            if (!active)
            {
                RestoreLights();
                if (enabledLastFrame) ClearGlobals();
                DestroyProbe();
                RestoreCamera();
                DestroyDisplayVolume();
                enabledLastFrame = false;
                return;
            }
            enabledLastFrame = true;
            if (settings.ToneMapping)
            {
                PrepareDisplayVolume(settings.Exposure);
                PrepareDisplayCamera(session.RenderCamera);
            }
            else
            {
                RestoreCamera();
                DestroyDisplayVolume();
            }
            var camera = session.RenderCamera;
            if (!settings.SceneReflectionProbe || camera == null)
            {
                DestroyProbe();
                return;
            }
            if (reflectionProbe == null)
            {
                var owner = new GameObject("Experimental scene reflection") { hideFlags = HideFlags.HideInHierarchy };
                UnityEngine.Object.DontDestroyOnLoad(owner);
                reflectionProbe = owner.AddComponent<ReflectionProbe>();
                reflectionProbe.mode = ReflectionProbeMode.Custom;
                reflectionProbe.boxProjection = true;
                reflectionProbe.importance = 100;
                reflectionProbe.hdr = true;
                reflectionProbe.blendDistance = 2;
                reflectionProbe.nearClipPlane = 0.15f;
                var cameraOwner = new GameObject("Reflection capture camera") { hideFlags = HideFlags.HideInHierarchy };
                cameraOwner.transform.SetParent(owner.transform, false);
                reflectionCamera = cameraOwner.AddComponent<Camera>();
                reflectionCamera.enabled = false;
                reflectionCamera.cameraType = CameraType.Reflection;
                reflectionCamera.fieldOfView = 90;
                reflectionCamera.aspect = 1;
                reflectionCamera.nearClipPlane = 0.15f;
                reflectionCamera.allowHDR = true;
                reflectionCamera.allowMSAA = false;
                reflectionCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                dirtyReflection = true;
            }
            if (nextFace >= 0)
            {
                CaptureFaces();
                return;
            }
            // Refresh only when meaningful scene state changes, never six extra views every frame.
            if (Time.unscaledTime < nextReflection) return;
            var sun = RenderSettings.sun;
            var rotation = sun != null ? sun.transform.rotation : Quaternion.identity;
            float intensity = sun != null ? sun.intensity : 0;
            bool moved = Vector3.Distance(camera.transform.position, reflectionAnchor) > 4f;
            bool changed = camera != lastReflectionCamera || lastGeometryCount != session.RayTracingInstances ||
                reflectionProbe.resolution != Mathf.Clamp(Mathf.ClosestPowerOfTwo(settings.ReflectionResolution), 32, 256) ||
                reflectionProbe.cullingMask != camera.cullingMask ||
                !Mathf.Approximately(reflectionProbe.farClipPlane, Mathf.Clamp(settings.ReflectionDistance, 10, 500)) ||
                Quaternion.Angle(lastSunRotation, rotation) > 2f || Mathf.Abs(lastSunIntensity - intensity) > 0.05f;
            if (!dirtyReflection && !moved && !changed) return;
            reflectionAnchor = camera.transform.position;
            reflectionProbe.size = Vector3.one * Mathf.Clamp(settings.ReflectionDistance, 10, 500) * 2;
            reflectionProbe.farClipPlane = Mathf.Clamp(settings.ReflectionDistance, 10, 500);
            reflectionProbe.resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(settings.ReflectionResolution), 32, 256);
            reflectionProbe.cullingMask = camera.cullingMask;
            reflectionCamera.transform.position = reflectionAnchor;
            reflectionCamera.farClipPlane = reflectionProbe.farClipPlane;
            reflectionCamera.cullingMask = camera.cullingMask;
            if (captureTexture == null || captureTexture.width != reflectionProbe.resolution)
            {
                ReleaseTexture(ref captureTexture);
                captureTexture = new RenderTexture(reflectionProbe.resolution, reflectionProbe.resolution, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Experimental reflection capture", dimension = TextureDimension.Cube,
                    useMipMap = true, autoGenerateMips = false, hideFlags = HideFlags.HideAndDontSave,
                };
                captureTexture.Create();
            }
            if (captureFace == null || captureFace.width != reflectionProbe.resolution)
            {
                ReleaseTexture(ref captureFace);
                // The default constructor leaves AllowVerticalFlip off, matching URP's cube path.
                var faceDescriptor = new RenderTextureDescriptor
                {
                    width = reflectionProbe.resolution, height = reflectionProbe.resolution,
                    colorFormat = RenderTextureFormat.ARGBHalf, depthBufferBits = 24,
                    msaaSamples = 1, volumeDepth = 1, dimension = TextureDimension.Tex2D,
                };
                captureFace = new RenderTexture(faceDescriptor)
                { name = "Experimental reflection face", hideFlags = HideFlags.HideAndDontSave };
                captureFace.Create();
            }
            nextFace = 0;
            CaptureFaces();
            dirtyReflection = false;
            lastGeometryCount = session.RayTracingInstances;
            lastReflectionCamera = camera;
            lastSunRotation = rotation;
            lastSunIntensity = intensity;
            nextReflection = Time.unscaledTime + 2f;
        }

        private static readonly Vector3[] FaceDirections =
            { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] FaceUp =
            { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        private void CaptureFaces()
        {
            // A 2D target supplies the depth buffer required by RenderGraph; URP's cubemap
            // request currently loses that descriptor field. Publish only a complete cube.
            bool wasEnabled = reflectionProbe.enabled;
            reflectionProbe.enabled = false;
            try
            {
                for (int count = 0; count < 2 && nextFace < 6; count++, nextFace++)
                {
                    reflectionCamera.transform.rotation = Quaternion.LookRotation(FaceDirections[nextFace], FaceUp[nextFace]);
                    RenderPipeline.SubmitRenderRequest(reflectionCamera, new UniversalRenderPipeline.SingleCameraRequest
                    { destination = captureFace });
                    Graphics.CopyTexture(captureFace, 0, 0, captureTexture, nextFace, 0);
                }
                if (nextFace == 6)
                {
                    captureTexture.GenerateMips();
                    var previous = reflectionTexture;
                    reflectionTexture = captureTexture;
                    captureTexture = previous;
                    reflectionProbe.customBakedTexture = reflectionTexture;
                    nextFace = -1;
                }
            }
            finally { reflectionProbe.enabled = wasEnabled; }
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture != null) { texture.Release(); CoreUtils.Destroy(texture); }
            texture = null;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!session.IsActive) return;
            var settings = ExperimentalRenderConfiguration.Surfaces;
            if (settings == null || !settings.Enabled) return;
            // Cubemap faces need the same light treatment, but never the camera's atmosphere composite.
            bool probeCamera = camera == reflectionCamera;
            if (camera != session.RenderCamera && !probeCamera) return;
            RestoreLights();
            scopedCamera = camera;
            Shader.SetGlobalVector("_StudioSurfaceLighting", new Vector4(1,
                Mathf.Clamp(settings.DirectStrength, 0, 3), Mathf.Clamp(settings.AmbientStrength, 0, 2),
                Mathf.Clamp01(settings.LightColorInfluence)));
            Shader.SetGlobalVector("_StudioSurfaceHighlights", new Vector4(
                Mathf.Clamp01(settings.SkinHighlight), Mathf.Clamp01(settings.HairHighlight),
                Mathf.Clamp01(settings.ClothHighlight), Mathf.Clamp01(settings.AccessoryHighlight)));
            Shader.SetGlobalFloat("_StudioSurfaceReflection", Mathf.Clamp(settings.ReflectionStrength, 0, 2));
            var sun = RenderSettings.sun;
            if (sun != null && sun.isActiveAndEnabled && sun.type == LightType.Directional)
                foreach (var light in UnityEngine.Object.FindObjectsByType<Light>())
                {
                    if (light == sun || ImportedSceneRenderBridge.IsSourceKeyLight(light) || light.GetComponent<ImportedLightTarget>() != null || !light.isActiveAndEnabled || light.type != LightType.Directional) continue;
                    float original = light.intensity;
                    float applied = original * Mathf.Clamp01(settings.SecondaryDirectionalStrength);
                    scaledLights.Add((light, original, applied));
                    light.intensity = applied;
                }
            rendering = true;
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!rendering || camera != scopedCamera) return;
            RestoreLights();
            ClearGlobals();
            rendering = false;
            scopedCamera = null;
        }

        private void RestoreLights()
        {
            foreach (var entry in scaledLights)
                if (entry.light != null && Mathf.Approximately(entry.light.intensity, entry.applied))
                    entry.light.intensity = entry.intensity;
            scaledLights.Clear();
        }

        private static void ClearGlobals() => Shader.SetGlobalVector("_StudioSurfaceLighting", Vector4.zero);

        private void RestoreCamera()
        {
            displayScope?.Dispose();
            displayScope = null;
        }

        private void PrepareDisplayCamera(Camera camera)
        {
            if (camera == null) { RestoreCamera(); return; }
            if (displayScope == null || displayScope.Camera != camera)
            {
                RestoreCamera();
                displayScope = new DisplayCameraScope(camera, 1 << 31);
            }
        }

        private void PrepareDisplayVolume(float value)
        {
            if (displayVolume == null)
            {
                var owner = new GameObject("Experimental display transform")
                { hideFlags = HideFlags.HideInHierarchy, layer = 31 };
                UnityEngine.Object.DontDestroyOnLoad(owner);
                displayVolume = owner.AddComponent<Volume>();
                displayVolume.isGlobal = true;
                displayVolume.priority = 10000;
                displayProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                displayProfile.hideFlags = HideFlags.HideAndDontSave;
                var tonemapping = displayProfile.Add<Tonemapping>(true);
                tonemapping.mode.Override(TonemappingMode.Neutral);
                exposure = displayProfile.Add<ColorAdjustments>(false);
                exposure.postExposure.Override(Mathf.Clamp(value, -4, 4));
                displayVolume.sharedProfile = displayProfile;
            }
            exposure.postExposure.value = Mathf.Clamp(value, -4, 4);
        }

        private void DestroyDisplayVolume()
        {
            if (displayVolume != null) CoreUtils.Destroy(displayVolume.gameObject);
            if (displayProfile != null)
            {
                foreach (var component in displayProfile.components) CoreUtils.Destroy(component);
                CoreUtils.Destroy(displayProfile);
            }
            displayVolume = null;
            displayProfile = null;
            exposure = null;
        }

        private void DestroyProbe()
        {
            if (reflectionProbe != null) CoreUtils.Destroy(reflectionProbe.gameObject);
            reflectionProbe = null;
            reflectionCamera = null;
            lastReflectionCamera = null;
            ReleaseTexture(ref reflectionTexture);
            ReleaseTexture(ref captureTexture);
            ReleaseTexture(ref captureFace);
            nextFace = -1;
            dirtyReflection = true;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestoreLights();
            RestoreCamera();
            ClearGlobals();
            DestroyProbe();
            DestroyDisplayVolume();
        }
    }
}
