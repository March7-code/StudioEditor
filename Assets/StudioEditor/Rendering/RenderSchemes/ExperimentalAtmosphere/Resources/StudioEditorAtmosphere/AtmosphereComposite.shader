Shader "Hidden/StudioEditor/ExperimentalAtmosphere"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FogVolumeCommon.hlsl"
            TEXTURE3D(_FogLighting);
            TEXTURE3D(_FogIntegral);
            SAMPLER(sampler_FogIntegral);
            float4x4 _FogWorldToCamera;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                bool isSky = depth < 0.000001;
                #else
                bool isSky = depth > 0.999999;
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float eyeDepth = isSky ? _FogRange.x :
                    clamp(-mul(_FogWorldToCamera, float4(positionWS, 1.0)).z, _FogGrid.w, _FogRange.x);
                float normalizedDepth = saturate((eyeDepth - _FogGrid.w) / (_FogRange.x - _FogGrid.w));
                uint cell = min((uint)(sqrt(normalizedDepth) * _FogGrid.z), (uint)_FogGrid.z - 1);
                float4 previous = cell == 0 ? float4(0, 0, 0, 1) :
                    SAMPLE_TEXTURE3D_LOD(_FogIntegral, sampler_FogIntegral,
                        float3(uv, (cell - 0.5) / _FogGrid.z), 0);
                float4 medium = SAMPLE_TEXTURE3D_LOD(_FogLighting, sampler_FogIntegral,
                    float3(uv, (cell + 0.5) / _FogGrid.z), 0);
                float distance = length(FogWorldPosition(uv, eyeDepth) - FogWorldPosition(uv, FogEyeDepth(cell)));
                float attenuation = exp(-medium.a * distance);
                float weight = medium.a > 0.00001 ? (1 - attenuation) / medium.a : distance;
                float3 scattering = previous.rgb + previous.a * medium.rgb * weight;
                return float4(source.rgb * (previous.a * attenuation) + scattering, source.a);
            }
            ENDHLSL
        }
    }
}
