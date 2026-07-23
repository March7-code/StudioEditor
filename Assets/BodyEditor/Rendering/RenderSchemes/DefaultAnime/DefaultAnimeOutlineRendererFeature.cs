using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BodyEditor.Rendering.RenderSchemes
{
    public sealed class DefaultAnimeOutlineRendererFeature :
        ScriptableRendererFeature
    {
        [SerializeField]
        private LayerMask outlineLayers = -1;

        private OutlinePass outlinePass;

        public override void Create()
        {
            outlinePass = new OutlinePass(outlineLayers)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            renderer.EnqueuePass(outlinePass);
        }

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId OutlineShaderTag =
                new ShaderTagId("BodyEditorOutline");

            private readonly FilteringSettings filteringSettings;

            public OutlinePass(LayerMask layerMask)
            {
                filteringSettings = new FilteringSettings(
                    RenderQueueRange.opaque,
                    layerMask);
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                var drawingSettings = RenderingUtils.CreateDrawingSettings(
                    OutlineShaderTag,
                    renderingData,
                    cameraData,
                    lightData,
                    cameraData.defaultOpaqueSortFlags);
                var rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(
                        renderingData.cullResults,
                        drawingSettings,
                        filteringSettings));

                using (var builder =
                       renderGraph.AddRasterRenderPass<PassData>(
                           "Default Anime Inverse Hull Outline",
                           out var passData))
                {
                    passData.RendererList = rendererList;
                    builder.UseRendererList(rendererList);
                    builder.SetRenderAttachment(
                        resourceData.activeColorTexture,
                        0,
                        AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(
                        resourceData.activeDepthTexture,
                        AccessFlags.Read);
                    builder.SetRenderFunc(static (
                        PassData data,
                        RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.RendererList);
                    });
                }
            }

            private sealed class PassData
            {
                public RendererListHandle RendererList;
            }
        }
    }
}
