using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering.Experimental
{
    internal struct AtmosphereFrame
    {
        public Vector4 Grid, Range, Albedo, Ambient, SunDirection, SunColor;
        public Matrix4x4 InverseProjection, CameraToWorld, WorldToCamera;
        public float Orthographic, FarClipValue, Anisotropy, RayDistance, UseMainShadow;

        public void SetCompute(ComputeCommandBuffer cmd, ComputeShader shader)
        {
            cmd.SetComputeVectorParam(shader, Shader.PropertyToID("_FogGrid"), Grid);
            cmd.SetComputeVectorParam(shader, Shader.PropertyToID("_FogRange"), Range);
            cmd.SetComputeVectorParam(shader, Shader.PropertyToID("_FogAlbedo"), Albedo);
            cmd.SetComputeVectorParam(shader, Shader.PropertyToID("_FogAmbient"), Ambient);
            cmd.SetComputeVectorParam(shader, Shader.PropertyToID("_FogSunDirection"), SunDirection);
            cmd.SetComputeVectorParam(shader, Shader.PropertyToID("_FogSunColor"), SunColor);
            cmd.SetComputeMatrixParam(shader, Shader.PropertyToID("_FogInverseProjection"), InverseProjection);
            cmd.SetComputeMatrixParam(shader, Shader.PropertyToID("_FogCameraToWorld"), CameraToWorld);
            cmd.SetComputeFloatParam(shader, Shader.PropertyToID("_FogOrthographic"), Orthographic);
            cmd.SetComputeFloatParam(shader, Shader.PropertyToID("_FogFarClipValue"), FarClipValue);
            cmd.SetComputeFloatParam(shader, Shader.PropertyToID("_FogAnisotropy"), Anisotropy);
            cmd.SetComputeFloatParam(shader, Shader.PropertyToID("_FogUseMainShadow"), UseMainShadow);
        }

        public void SetMaterial(Material material)
        {
            material.SetVector("_FogGrid", Grid);
            material.SetVector("_FogRange", Range);
            material.SetMatrix("_FogInverseProjection", InverseProjection);
            material.SetMatrix("_FogCameraToWorld", CameraToWorld);
            material.SetMatrix("_FogWorldToCamera", WorldToCamera);
            material.SetFloat("_FogOrthographic", Orthographic);
            material.SetFloat("_FogFarClipValue", FarClipValue);
        }
    }

    public sealed class AtmosphereRendererFeature : ScriptableRendererFeature
    {
        private AtmospherePass pass;
        public override void Create()
        {
            pass?.Dispose();
            pass = new AtmospherePass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var session = ExperimentalRenderSession.Current;
            if (session == null || !session.IsActive || session.RenderCamera != data.cameraData.camera ||
                data.cameraData.renderType != CameraRenderType.Base || data.cameraData.xr.enabled ||
                ExperimentalRenderConfiguration.Settings == null ||
                ExperimentalRenderConfiguration.Settings.Density <= 0f) return;
            if (pass.IsReady) renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => pass?.Dispose();

        private sealed class AtmospherePass : ScriptableRenderPass
        {
            private readonly ComputeShader compute;
            private readonly Material composite;
            private readonly int injectKernel, integrateKernel;
            private bool warned;
            public bool IsReady => compute != null && composite != null;

            public AtmospherePass()
            {
                var source = Resources.Load<ComputeShader>("StudioEditorAtmosphere/Atmosphere");
                if (source != null)
                {
                    compute = Object.Instantiate(source);
                    compute.hideFlags = HideFlags.HideAndDontSave;
                    injectKernel = compute.FindKernel("Inject");
                    integrateKernel = compute.FindKernel("Integrate");
                }
                var shader = Resources.Load<Shader>("StudioEditorAtmosphere/AtmosphereComposite");
                if (shader != null) composite = CoreUtils.CreateEngineMaterial(shader);
                ConfigureInput(ScriptableRenderPassInput.Depth);
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer container)
            {
                var resources = container.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var cameraData = container.Get<UniversalCameraData>();
                var lights = container.Get<UniversalLightData>();
                var session = ExperimentalRenderSession.Current;
                if (session == null) return;
                var settings = ExperimentalRenderConfiguration.Settings;
                var size = settings.GetGridSize(cameraData.camera);
                float near = Mathf.Max(0.001f, cameraData.camera.nearClipPlane);
                var frame = new AtmosphereFrame
                {
                    Grid = new Vector4(size.x, size.y, size.z, near),
                    Range = new Vector4(Mathf.Clamp(settings.MaxDistance, near + 0.01f,
                        cameraData.camera.farClipPlane), settings.BaseHeight,
                        Mathf.Clamp(settings.HeightFalloff, 0, 10), Mathf.Clamp(settings.Density, 0, 2)),
                    Albedo = settings.Albedo.linear,
                    Ambient = settings.Ambient.linear,
                    Anisotropy = Mathf.Clamp(settings.Anisotropy, -0.8f, 0.8f),
                    RayDistance = Mathf.Clamp(settings.RayDistance, 1, 2000),
                    InverseProjection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true).inverse,
                    CameraToWorld = cameraData.GetViewMatrix().inverse,
                    WorldToCamera = cameraData.GetViewMatrix(),
                    Orthographic = cameraData.camera.orthographic ? 1 : 0,
                    FarClipValue = SystemInfo.usesReversedZBuffer ? 0 : 1,
                    SunDirection = Vector3.up,
                };
                int worldLightIndex = ImportedSceneRenderBridge.SelectWorldLightIndex(lights);
                frame.UseMainShadow = worldLightIndex >= 0 && worldLightIndex == lights.mainLightIndex ? 1f : 0f;
                if (worldLightIndex >= 0)
                {
                    var main = lights.visibleLights[worldLightIndex];
                    frame.SunDirection = -main.localToWorldMatrix.GetColumn(2);
                    frame.SunColor = main.finalColor * Mathf.Clamp(settings.SunScattering, 0, 50);
                }

                var lighting = CreateVolume(graph, size, "Atmosphere medium lighting");
                var integral = CreateVolume(graph, size, "Atmosphere accumulated scattering");
                TextureHandle visibility = TextureHandle.nullHandle;
                GeometryShadowWorld world = null;
                bool useRays = false;
                if (settings.Shadows == AtmosphereShadowMode.HardwareRayTracing && worldLightIndex >= 0)
                {
                    world = session.RayWorld;
                    useRays = world.Prepare(cameraData.camera, size, settings, out var error);
                    if (!useRays && !warned && !string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning("[Render experiment] " + error);
                        warned = true;
                    }
                    if (useRays)
                    {
                        visibility = graph.ImportTexture(world.Visibility);
                        using (var builder = graph.AddUnsafePass<RayData>("Atmosphere geometry shadow rays", out var data))
                        {
                            data.World = world; data.Frame = frame;
                            builder.UseTexture(visibility, AccessFlags.Write);
                            builder.SetRenderFunc(static (RayData d, UnsafeGraphContext ctx) =>
                                d.World.Dispatch(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd), d.Frame));
                        }
                    }
                }
                session.HardwareShadowsActive = useRays;
                session.Status = useRays ? "Atmosphere / hardware geometry shadows" : "Atmosphere / shadow maps";
                if (!useRays)
                    visibility = graph.CreateTexture(new TextureDesc(1, 1)
                    {
                        name = "Atmosphere fallback visibility", slices = 1, dimension = TextureDimension.Tex3D,
                        colorFormat = GraphicsFormat.R32_SFloat, clearBuffer = true, clearColor = Color.white,
                    });

                using (var builder = graph.AddComputePass<InjectData>("Atmosphere inject 3D medium", out var data))
                {
                    data.Frame = frame; data.Compute = compute; data.Kernel = injectKernel;
                    data.Output = lighting; data.Visibility = visibility; data.UseRays = useRays;
                    builder.UseTexture(lighting, AccessFlags.Write);
                    builder.UseTexture(visibility, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderFunc(static (InjectData d, ComputeGraphContext ctx) =>
                    {
                        d.Frame.SetCompute(ctx.cmd, d.Compute);
                        ctx.cmd.SetComputeFloatParam(d.Compute, Shader.PropertyToID("_FogUseRayTracing"), d.UseRays ? 1 : 0);
                        ctx.cmd.SetComputeTextureParam(d.Compute, d.Kernel, Shader.PropertyToID("_FogLightingOutput"), d.Output);
                        ctx.cmd.SetComputeTextureParam(d.Compute, d.Kernel, Shader.PropertyToID("_FogVisibility"), d.Visibility);
                        ctx.cmd.DispatchCompute(d.Compute, d.Kernel,
                            Mathf.CeilToInt(d.Frame.Grid.x / 4), Mathf.CeilToInt(d.Frame.Grid.y / 4),
                            Mathf.CeilToInt(d.Frame.Grid.z / 4));
                    });
                }
                using (var builder = graph.AddComputePass<IntegrateData>("Atmosphere integrate scattering", out var data))
                {
                    data.Frame = frame; data.Compute = compute; data.Kernel = integrateKernel;
                    data.Input = lighting; data.Output = integral;
                    builder.UseTexture(lighting, AccessFlags.Read);
                    builder.UseTexture(integral, AccessFlags.Write);
                    builder.SetRenderFunc(static (IntegrateData d, ComputeGraphContext ctx) =>
                    {
                        d.Frame.SetCompute(ctx.cmd, d.Compute);
                        ctx.cmd.SetComputeTextureParam(d.Compute, d.Kernel, Shader.PropertyToID("_FogLighting"), d.Input);
                        ctx.cmd.SetComputeTextureParam(d.Compute, d.Kernel, Shader.PropertyToID("_FogIntegralOutput"), d.Output);
                        ctx.cmd.DispatchCompute(d.Compute, d.Kernel,
                            Mathf.CeilToInt(d.Frame.Grid.x / 8), Mathf.CeilToInt(d.Frame.Grid.y / 8), 1);
                    });
                }
                var color = resources.activeColorTexture;
                var desc = graph.GetTextureDesc(color);
                desc.name = "Atmosphere composite"; desc.clearBuffer = false;
                var destination = graph.CreateTexture(desc);
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Atmosphere composite", out var data))
                {
                    data.Frame = frame; data.Material = composite; data.Color = color;
                    data.Lighting = lighting; data.Integral = integral;
                    builder.UseTexture(color, AccessFlags.Read);
                    builder.UseTexture(lighting, AccessFlags.Read);
                    builder.UseTexture(integral, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                    {
                        d.Frame.SetMaterial(d.Material);
                        d.Material.SetTexture("_FogLighting", d.Lighting);
                        d.Material.SetTexture("_FogIntegral", d.Integral);
                        Blitter.BlitTexture(ctx.cmd, d.Color, new Vector4(1, 1, 0, 0), d.Material, 0);
                    });
                }
                resources.cameraColor = destination;
            }

            private static TextureHandle CreateVolume(RenderGraph graph, Vector3Int grid, string name)
            {
                return graph.CreateTexture(new TextureDesc(grid.x, grid.y)
                {
                    name = name, slices = grid.z, dimension = TextureDimension.Tex3D,
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true, filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, clearBuffer = false,
                });
            }

            public void Dispose() { CoreUtils.Destroy(composite); CoreUtils.Destroy(compute); }
            private sealed class RayData { public GeometryShadowWorld World; public AtmosphereFrame Frame; }
            private sealed class InjectData
            {
                public AtmosphereFrame Frame; public ComputeShader Compute; public int Kernel;
                public TextureHandle Output, Visibility; public bool UseRays;
            }
            private sealed class IntegrateData
            {
                public AtmosphereFrame Frame; public ComputeShader Compute; public int Kernel;
                public TextureHandle Input, Output;
            }
            private sealed class CompositeData
            {
                public AtmosphereFrame Frame; public Material Material;
                public TextureHandle Color, Lighting, Integral;
            }
        }
    }
}
