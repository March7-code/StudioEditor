Shader "BodyEditor/KoikatsuStudioItem"
{
    Properties
    {
        _MainTex ("Main texture", 2D) = "white" {}
        _ChannelColor1 ("Color 1", Color) = (1,1,1,1)
        _ChannelColor2 ("Color 2", Color) = (1,1,1,1)
        _ChannelColor3 ("Color 3", Color) = (1,1,1,1)
        _Pattern1 ("Pattern 1", 2D) = "white" {}
        _Pattern2 ("Pattern 2", 2D) = "white" {}
        _Pattern3 ("Pattern 3", 2D) = "white" {}
        _PatternColor1 ("Pattern color 1", Color) = (1,1,1,1)
        _PatternColor2 ("Pattern color 2", Color) = (1,1,1,1)
        _PatternColor3 ("Pattern color 3", Color) = (1,1,1,1)
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        [HideInInspector] _AlphaClip ("Alpha clip", Float) = 0
        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Z write", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _Surface ("Surface", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 vertexLighting : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Pattern1);
            SAMPLER(sampler_Pattern1);
            TEXTURE2D(_Pattern2);
            SAMPLER(sampler_Pattern2);
            TEXTURE2D(_Pattern3);
            SAMPLER(sampler_Pattern3);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _ChannelColor1;
                half4 _ChannelColor2;
                half4 _ChannelColor3;
                half4 _PatternColor1;
                half4 _PatternColor2;
                half4 _PatternColor3;
                half4 _EmissionColor;
                float4 _ChannelEnabled;
                float4 _PatternUV1;
                float4 _PatternUV2;
                float4 _PatternUV3;
                float _PatternRotation1;
                float _PatternRotation2;
                float _PatternRotation3;
                float _PatternClamp1;
                float _PatternClamp2;
                float _PatternClamp3;
                float _HasPattern1;
                float _HasPattern2;
                float _HasPattern3;
                float _AlphaClip;
                float _Cutoff;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.vertexLighting = VertexLighting(
                    positionInputs.positionWS,
                    output.normalWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float2 PatternUv(
                float2 uv,
                float4 transform,
                float rotationDegrees,
                float clampUv)
            {
                float angle = radians(rotationDegrees);
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                float2 centered = (uv - 0.5) * transform.zw;
                centered = float2(
                    centered.x * cosine - centered.y * sine,
                    centered.x * sine + centered.y * cosine);
                float2 result = centered + 0.5 + transform.xy;
                float2 clamped = clamp(result, 0.0001, 0.9999);
                return lerp(result, clamped, step(0.5, clampUv));
            }

            half3 PatternColor(
                half3 baseColor,
                half3 patternColor,
                half pattern,
                half hasPattern)
            {
                return lerp(
                    baseColor,
                    patternColor,
                    saturate(pattern) * saturate(hasPattern));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    TRANSFORM_TEX(input.uv, _MainTex));
                if (_AlphaClip > 0.5)
                {
                    clip(source.a - _Cutoff);
                }

                half pattern1 = SAMPLE_TEXTURE2D(
                    _Pattern1,
                    sampler_Pattern1,
                    PatternUv(
                        input.uv,
                        _PatternUV1,
                        _PatternRotation1,
                        _PatternClamp1)).r;
                half pattern2 = SAMPLE_TEXTURE2D(
                    _Pattern2,
                    sampler_Pattern2,
                    PatternUv(
                        input.uv,
                        _PatternUV2,
                        _PatternRotation2,
                        _PatternClamp2)).r;
                half pattern3 = SAMPLE_TEXTURE2D(
                    _Pattern3,
                    sampler_Pattern3,
                    PatternUv(
                        input.uv,
                        _PatternUV3,
                        _PatternRotation3,
                        _PatternClamp3)).r;

                half3 color1 = PatternColor(
                    _ChannelColor1.rgb,
                    _PatternColor1.rgb,
                    pattern1,
                    _HasPattern1);
                half3 color2 = PatternColor(
                    _ChannelColor2.rgb,
                    _PatternColor2.rgb,
                    pattern2,
                    _HasPattern2);
                half3 color3 = PatternColor(
                    _ChannelColor3.rgb,
                    _PatternColor3.rgb,
                    pattern3,
                    _HasPattern3);

                half3 weights = saturate(input.color.rgb) *
                                saturate(_ChannelEnabled.rgb);
                half total = weights.r + weights.g + weights.b;
                half3 tint = (color1 * weights.r + color2 * weights.g +
                              color3 * weights.b) / max(total, 0.0001h);
                tint = lerp(half3(1.0h, 1.0h, 1.0h), tint,
                            saturate(total));

                half3 normalWS = normalize(input.normalWS);
                half3 lighting = SampleSH(normalWS) + input.vertexLighting;
                Light mainLight = GetMainLight(
                    TransformWorldToShadowCoord(input.positionWS));
                lighting += LightingLambert(
                    mainLight.color *
                    (mainLight.distanceAttenuation *
                     mainLight.shadowAttenuation),
                    mainLight.direction,
                    normalWS);

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u;
                     lightIndex < additionalLightCount;
                     lightIndex++)
                {
                    Light additionalLight = GetAdditionalLight(
                        lightIndex,
                        input.positionWS);
                    lighting += LightingLambert(
                        additionalLight.color *
                        (additionalLight.distanceAttenuation *
                         additionalLight.shadowAttenuation),
                        additionalLight.direction,
                        normalWS);
                }
                #endif

                return half4(
                    source.rgb * tint * lighting + _EmissionColor.rgb,
                    source.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
