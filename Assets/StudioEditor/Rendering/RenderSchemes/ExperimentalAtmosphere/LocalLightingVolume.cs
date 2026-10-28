using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace StudioEditor.Rendering.Experimental
{
    /// <summary>World-space directional irradiance and short-range geometry visibility caches.</summary>
    internal sealed class LocalLightingVolume : IDisposable
    {
        internal RenderTexture Irradiance, Distance, Contact;
        internal RTHandle IrradianceHandle, DistanceHandle, ContactHandle;
        internal Vector3 ProbeOrigin, ContactOrigin;
        internal Vector3Int ProbeSize, ContactSize;
        private readonly ExperimentalRenderSession session;
        private IRayTracingShader shader;
        private RaySurfaceGeometry hitGeometry;
        private GraphicsBuffer scratch;
        private float nextIndirect, nextContact;
        private bool ready, dirty = true;
        private Camera lastCamera;
        private Vector3 lastSunDirection;
        private Color lastSunColor;
        private int lastRevision = -1;
        private float probeSpacing, contactSpacing, lastRadius, lastRayDistance;
        private int lastRays, lastContactRays;
        public bool Ready => ready;
        public string Status { get; private set; } = "Waiting for camera";
        public void Invalidate() => dirty = true;

        public LocalLightingVolume(ExperimentalRenderSession session)
        {
            this.session = session;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        internal bool Prepare(Camera camera, Vector3 sunDirection, Color sunColor, out bool updateIndirect,
            out bool updateContact)
        {
            updateIndirect = updateContact = false;
            var settings = ExperimentalRenderConfiguration.LocalLighting;
            try
            {
                var world = session.RayWorld;
                if (!world.PrepareLighting(camera, out var error))
                {
                    Status = string.IsNullOrEmpty(error) ? "Hardware ray tracing unavailable" : error;
                    ready = false;
                    return false;
                }
                if (shader == null)
                {
                    foreach (var candidate in Resources.LoadAll<UnityEngine.Object>("StudioEditorAtmosphere/LocalLighting"))
                        if (candidate is RayTracingShader asset) { shader = world.Context.CreateRayTracingShader(asset); break; }
                    if (shader == null) throw new InvalidOperationException("LocalLighting ray tracing shader is missing.");
                    hitGeometry = new RaySurfaceGeometry();
                }
                hitGeometry.Prepare(world);
                var probe = ClampSize(settings.ProbeGrid, 8, 64);
                var contact = ClampSize(settings.ContactGrid, 16, 128);
                float giStep = Mathf.Clamp(settings.ProbeSpacing, 0.1f, 4);
                float aoStep = Mathf.Clamp(settings.ContactSpacing, 0.025f, 0.5f);
                if (Irradiance == null || ProbeSize != probe || ContactSize != contact)
                {
                    ReleaseTextures();
                    ProbeSize = probe; ContactSize = contact;
                    Irradiance = Create(probe, 6, GraphicsFormat.R16G16B16A16_SFloat, "World irradiance lobes", out IrradianceHandle);
                    Distance = Create(probe, 6, GraphicsFormat.R16G16_SFloat, "World irradiance distance moments", out DistanceHandle);
                    Contact = Create(contact, 7, GraphicsFormat.R16_SFloat, "World contact ray distances", out ContactHandle);
                    dirty = true;
                }
                // Follow camera yaw only. With pitch the shallow cache (4 m tall by default)
                // swings metres up or down, and nearby surfaces cross its fade border.
                Vector3 heading = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.zero;
                Vector3 anchor = camera.transform.position + heading * Mathf.Clamp(settings.AnchorDistance, 0, 50);
                Vector3 giOrigin = Snap(anchor, giStep) - ((Vector3)probe - Vector3.one) * (giStep * 0.5f);
                Vector3 aoOrigin = Snap(anchor, aoStep * 4) - ((Vector3)contact - Vector3.one) * (aoStep * 0.5f);
                bool changed = lastCamera != camera || probeSpacing != giStep || contactSpacing != aoStep ||
                    lastRadius != settings.ContactRadius || lastRayDistance != settings.RayDistance || lastRays != settings.RaysPerDirection ||
                    lastContactRays != settings.ContactRaysPerDirection ||
                    (lastSunDirection - sunDirection).sqrMagnitude > 0.0001f ||
                    Mathf.Abs(lastSunColor.r - sunColor.r) + Mathf.Abs(lastSunColor.g - sunColor.g) + Mathf.Abs(lastSunColor.b - sunColor.b) > 0.01f;
                bool moveGi = ProbeOrigin != giOrigin;
                bool moveAo = ContactOrigin != aoOrigin;
                bool topology = lastRevision != world.Revision;
                updateIndirect = dirty || changed || moveGi || Time.unscaledTime >= nextIndirect;
                updateContact = dirty || changed || moveAo || topology || Time.unscaledTime >= nextContact;
                ProbeOrigin = giOrigin; ContactOrigin = aoOrigin;
                probeSpacing = giStep; contactSpacing = aoStep;
                lastCamera = camera; lastSunDirection = sunDirection; lastSunColor = sunColor;
                lastRadius = settings.ContactRadius; lastRayDistance = settings.RayDistance; lastRays = settings.RaysPerDirection;
                lastContactRays = settings.ContactRaysPerDirection;
                lastRevision = world.Revision;
                ulong bytes = Math.Max(world.Acceleration.GetBuildScratchBufferRequiredSizeInBytes(),
                    shader.GetTraceScratchBufferRequiredSizeInBytes((uint)Math.Max(probe.x * 6, contact.x * 7),
                        (uint)Math.Max(probe.y, contact.y), (uint)Math.Max(probe.z, contact.z)));
                if (bytes > 0 && (scratch == null || (ulong)scratch.count * 4 < bytes))
                {
                    scratch?.Dispose();
                    scratch = new GraphicsBuffer(RayTracingHelper.ScratchBufferTarget, (int)((bytes + 3) / 4), 4);
                }
                Status = $"Geometry GI {probe.x}x{probe.y}x{probe.z}, contact {contact.x}x{contact.y}x{contact.z}";
                return true;
            }
            catch (Exception exception)
            {
                Status = exception.Message;
                ready = false;
                Debug.LogWarning("[Local lighting] " + exception.Message);
                return false;
            }
        }

        internal void Dispatch(CommandBuffer cmd, Vector3 direction, Color color, bool updateIndirect, bool updateContact)
        {
            var settings = ExperimentalRenderConfiguration.LocalLighting;
            var world = session.RayWorld;
            if (updateIndirect || updateContact)
            {
                world.Acceleration.Build(cmd, scratch);
                shader.SetAccelerationStructure(cmd, "_LocalScene", world.Acceleration);
                hitGeometry.Bind(cmd, shader);
                shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalIrradianceOutput"), Irradiance);
                shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalDistanceOutput"), Distance);
                shader.SetTextureParam(cmd, Shader.PropertyToID("_LocalContactOutput"), Contact);
                shader.SetVectorParam(cmd, Shader.PropertyToID("_LocalSunDirection"), direction);
                shader.SetVectorParam(cmd, Shader.PropertyToID("_LocalSunColor"), color);
                shader.SetVectorParam(cmd, Shader.PropertyToID("_LocalTraceSettings"), new Vector4(
                    Mathf.Clamp(settings.RayDistance, 0.5f, 100), Mathf.Clamp(settings.ContactRadius, 0.05f, 2),
                    Mathf.Clamp(settings.NormalBias, 0.001f, 0.1f), Mathf.Clamp(settings.RaysPerDirection, 1, 32)));
                shader.SetIntParam(cmd, Shader.PropertyToID("_LocalContactRays"), Mathf.Clamp(settings.ContactRaysPerDirection, 1, 16));
                var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                var ambient = new Color[6];
                RenderSettings.ambientProbe.Evaluate(directions, ambient);
                for (int i = 0; i < 6; i++)
                    shader.SetVectorParam(cmd, Shader.PropertyToID("_LocalSky" + i), ambient[i]);
                if (updateIndirect)
                {
                    shader.SetIntParam(cmd, Shader.PropertyToID("_LocalTraceMode"), 0);
                    SetGrid(cmd, ProbeSize, ProbeOrigin, probeSpacing);
                    shader.Dispatch(cmd, scratch, (uint)ProbeSize.x * 6, (uint)ProbeSize.y, (uint)ProbeSize.z);
                    nextIndirect = Time.unscaledTime + 1 / Mathf.Clamp(settings.UpdatesPerSecond, 0.25f, 30);
                }
                if (updateContact)
                {
                    shader.SetIntParam(cmd, Shader.PropertyToID("_LocalTraceMode"), 1);
                    SetGrid(cmd, ContactSize, ContactOrigin, contactSpacing);
                    shader.Dispatch(cmd, scratch, (uint)ContactSize.x * 7, (uint)ContactSize.y, (uint)ContactSize.z);
                    nextContact = Time.unscaledTime + 1 / Mathf.Clamp(settings.ContactUpdatesPerSecond, 0.25f, 30);
                }
                ready = true; dirty = false;
            }
            cmd.SetGlobalVector("_StudioLocalLighting", new Vector4(ready ? 1 : 0,
                Mathf.Clamp(settings.IndirectStrength, 0, 3), Mathf.Clamp01(settings.OcclusionStrength),
                Mathf.Clamp01(settings.ContactShadowStrength)));
            cmd.SetGlobalVector("_StudioProbeGrid", new Vector4(ProbeSize.x, ProbeSize.y, ProbeSize.z, probeSpacing));
            cmd.SetGlobalVector("_StudioProbeOrigin", ProbeOrigin);
            cmd.SetGlobalVector("_StudioContactGrid", new Vector4(ContactSize.x, ContactSize.y, ContactSize.z, contactSpacing));
            cmd.SetGlobalVector("_StudioContactOrigin", ContactOrigin);
            cmd.SetGlobalVector("_StudioContactSettings", new Vector4(Mathf.Clamp(settings.ContactRadius, 0.05f, 2),
                Mathf.Clamp(settings.NormalBias, 0.001f, 0.1f), 0, 0));
            cmd.SetGlobalVector("_StudioLocalSunDirection", direction);
            cmd.SetGlobalInt("_StudioLocalDebug", (int)settings.DebugView);
        }

        private void SetGrid(CommandBuffer cmd, Vector3Int size, Vector3 origin, float spacing)
        {
            shader.SetVectorParam(cmd, Shader.PropertyToID("_LocalTraceGrid"), new Vector4(size.x, size.y, size.z, spacing));
            shader.SetVectorParam(cmd, Shader.PropertyToID("_LocalTraceOrigin"), origin);
        }

        private static Vector3Int ClampSize(Vector3Int size, int min, int max) =>
            new Vector3Int(Mathf.Clamp(size.x, min, max), Mathf.Clamp(size.y, min, max), Mathf.Clamp(size.z, min, max));
        private static Vector3 Snap(Vector3 value, float step) => new Vector3(
            Mathf.Round(value.x / step) * step, Mathf.Round(value.y / step) * step, Mathf.Round(value.z / step) * step);
        private static RenderTexture Create(Vector3Int size, int lobes, GraphicsFormat format, string name, out RTHandle handle)
        {
            var texture = new RenderTexture(size.x * lobes, size.y, 0)
            {
                name = name, graphicsFormat = format, dimension = TextureDimension.Tex3D, volumeDepth = size.z,
                enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.Create()) { CoreUtils.Destroy(texture); throw new InvalidOperationException("Cannot allocate " + name); }
            handle = RTHandles.Alloc(texture);
            return texture;
        }
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == session.RenderCamera) Shader.SetGlobalVector("_StudioLocalLighting", Vector4.zero);
        }
        private void ReleaseTextures()
        {
            IrradianceHandle?.Release(); DistanceHandle?.Release(); ContactHandle?.Release();
            foreach (var texture in new[] { Irradiance, Distance, Contact })
                if (texture != null) { texture.Release(); CoreUtils.Destroy(texture); }
            Irradiance = Distance = Contact = null;
            IrradianceHandle = DistanceHandle = ContactHandle = null;
            ready = false;
        }
        public void Dispose()
        {
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Shader.SetGlobalVector("_StudioLocalLighting", Vector4.zero);
            ReleaseTextures(); hitGeometry?.Dispose(); scratch?.Dispose();
        }
    }
}
