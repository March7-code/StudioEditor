using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace StudioEditor.Rendering.Experimental
{
    // The experiment is selected in code, so URP cannot infer resource use from a feature type.
    internal sealed class ExperimentalRenderResourceStripper :
        IRenderPipelineGraphicsSettingsStripper<RayTracingRenderPipelineResources>
    {
        public bool active => true;
        public bool CanRemoveSettings(RayTracingRenderPipelineResources settings) => false;
    }
}
