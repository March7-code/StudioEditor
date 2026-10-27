using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering.Experimental
{
    public sealed class LocalLightingRendererFeature : ScriptableRendererFeature
    {
        private LocalPass pass;
        public override void Create() => pass = new LocalPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var session = ExperimentalRenderSession.Current;
            if (session == null || !session.IsActive || session.RenderCamera != data.cameraData.camera ||
                data.cameraData.xr.enabled || data.cameraData.renderType != CameraRenderType.Base ||
                ExperimentalRenderConfiguration.Surfaces?.Enabled != true ||
                ExperimentalRenderConfiguration.LocalLighting?.Enabled != true) return;
            renderer.EnqueuePass(pass);
        }
        private sealed class LocalPass : ScriptableRenderPass
        {
            private sealed class Data
            {
                public LocalLightingVolume Volume;
                public Vector3 Direction;
                public Color Color;
                public bool Indirect, Contact;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer container)
            {
                var session = ExperimentalRenderSession.Current;
                var camera = container.Get<UniversalCameraData>().camera;
                var lights = container.Get<UniversalLightData>();
                Vector3 direction = Vector3.up;
                Color color = Color.black;
                int worldLightIndex = ImportedSceneRenderBridge.SelectWorldLightIndex(lights);
                if (worldLightIndex >= 0)
                {
                    var main = lights.visibleLights[worldLightIndex];
                    direction = -main.localToWorldMatrix.GetColumn(2);
                    color = main.finalColor;
                }
                var volume = session.LocalLighting;
                if (!volume.Prepare(camera, direction, color, out bool indirect, out bool contact)) return;
                var irradiance = graph.ImportTexture(volume.IrradianceHandle);
                var distance = graph.ImportTexture(volume.DistanceHandle);
                var visibility = graph.ImportTexture(volume.ContactHandle);
                using (var builder = graph.AddUnsafePass<Data>("World geometry indirect light and contact", out var data))
                {
                    data.Volume = volume; data.Direction = direction; data.Color = color;
                    data.Indirect = indirect; data.Contact = contact;
                    builder.UseTexture(irradiance, indirect ? AccessFlags.Write : AccessFlags.Read);
                    builder.UseTexture(distance, indirect ? AccessFlags.Write : AccessFlags.Read);
                    builder.UseTexture(visibility, contact ? AccessFlags.Write : AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(irradiance, Shader.PropertyToID("_StudioLocalIrradiance"));
                    builder.SetGlobalTextureAfterPass(distance, Shader.PropertyToID("_StudioLocalDistance"));
                    builder.SetGlobalTextureAfterPass(visibility, Shader.PropertyToID("_StudioLocalContact"));
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, UnsafeGraphContext ctx) =>
                        d.Volume.Dispatch(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd), d.Direction, d.Color, d.Indirect, d.Contact));
                }
            }
        }
    }
}
