#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "LocalLightingSampling.hlsl"

half4 StudioLocalFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    StudioLocalLight local = StudioSampleLocalLight(inputData.positionWS, inputData.normalWS);
    if (_StudioLocalDebug > 0 && _StudioLocalLighting.x > 0.5)
        return half4(StudioLocalDebugColor(local, inputData.positionWS), surfaceData.alpha);
    // Source map lightmaps already contain occluded indirect lighting and
    // emissive fixtures. Adding the ray cache again doubles that illumination.
    #if !defined(LIGHTMAP_ON)
        inputData.bakedGI = inputData.bakedGI * local.skyVisibility + local.indirect;
    #endif
    surfaceData.occlusion *= local.occlusion;
    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    if (local.sunVisibility < 0.999h)
    {
        BRDFData brdf;
        InitializeBRDFData(surfaceData, brdf);
        BRDFData coat = CreateClearCoatBRDFData(surfaceData, brdf);
        AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surfaceData);
        Light main = GetMainLight(inputData, CalculateShadowMask(inputData), ao);
        #ifdef _LIGHT_LAYERS
            if (!IsMatchingLightLayer(main.layerMask, GetMeshRenderingLayer())) main.color = 0;
        #endif
        #if defined(_SPECULARHIGHLIGHTS_OFF)
            bool specularOff = true;
        #else
            bool specularOff = false;
        #endif
        half3 direct = LightingPhysicallyBased(brdf, coat, main, inputData.normalWS,
            inputData.viewDirectionWS, surfaceData.clearCoatMask, specularOff);
        color.rgb = max(0, color.rgb - direct * (1 - local.sunVisibility));
    }
    return color;
}
// Keep the official URP vertex, normal-map, alpha and fog paths; intercept only lighting.
#define UniversalFragmentPBR StudioLocalFragmentPBR
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
#undef UniversalFragmentPBR
