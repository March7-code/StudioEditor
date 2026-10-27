Shader "Hidden/StudioEditor/FaceHairDepth"
{
    Properties
    {
        _BaseMap("Base", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Cutoff("Cutout", Float) = 0.5
        _HairAlphaClip("Clip", Float) = 0
        _HairFrontOnly("Front hair only", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            // Nearest linear/projected depth without a hardware depth buffer.
            // This also handles thin hair cards viewed from their reverse side.
            Cull Off ZWrite Off ZTest Always
            Blend One One
            BlendOp Min
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4x4 _HairShadowClipMatrix, _HairShadowTextureMatrix;
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff, _HairAlphaClip, _HairFrontOnly;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 frontMask:TEXCOORD7; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; float frontMask:TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 world = float4(TransformObjectToWorld(input.positionOS.xyz), 1);
                output.positionCS = mul(_HairShadowClipMatrix, world);
                float4 shadow = mul(_HairShadowTextureMatrix, world);
                output.depth = shadow.zw;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.frontMask = input.frontMask.x;
                return output;
            }
            float Frag(Varyings input):SV_Target
            {
                if (_HairFrontOnly > .5) clip(input.frontMask - .5);
                if (_HairAlphaClip > .5)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                return saturate(input.depth.x / input.depth.y);
            }
            ENDHLSL
        }
    }
}
