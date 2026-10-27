Shader "StudioEditor/KoikatsuSkyCubemap"
{
    Properties
    {
        _Tex ("Cubemap", Cube) = "" {}
        _Tint ("Tint", Color) = (0.5,0.5,0.5,1)
        _Exposure ("Exposure", Float) = 1
        _Rotation ("Rotation", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Background" "Queue" = "Background" }
        Cull Off ZWrite Off
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            TEXTURECUBE(_Tex); SAMPLER(sampler_Tex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tex_HDR;
                half4 _Tint;
                float _Exposure;
                float _Rotation;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float sine, cosine;
                sincos(radians(_Rotation), sine, cosine);
                output.direction = float3(cosine * input.positionOS.x - sine * input.positionOS.z,
                    input.positionOS.y, sine * input.positionOS.x + cosine * input.positionOS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 sampleValue = SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, input.direction);
                half3 color = DecodeHDREnvironment(sampleValue, _Tex_HDR);
                #if defined(UNITY_COLORSPACE_GAMMA)
                    const half tintScale = 2.0h;
                #else
                    const half tintScale = 4.5947938h;
                #endif
                return half4(color * _Tint.rgb * tintScale * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
