Shader "BodyEditor/AnimeCharacter"
{
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor] _BaseColor("Base color", Color) = (1,1,1,1)
        [Normal] _NormalMap("Normal map", 2D) = "bump" {}
        _NormalStrength("Normal strength", Range(0,2)) = 0

        [NoScaleOffset] _StyleMask("Style mask", 2D) = "white" {}
        _StyleMaskStrength("Style mask strength", Range(0,1)) = 0
        [NoScaleOffset] _RampMap("Shadow ramp", 2D) = "white" {}
        _RampStrength("Shadow ramp strength", Range(0,1)) = 0

        _DeepShadowColor("Deep shadow", Color) = (0.42,0.45,0.52,1)
        _ShadowColor("Shadow", Color) = (0.72,0.75,0.8,1)
        _BandThresholds("Band thresholds", Vector) = (0.18,0.38,0.58,0.78)
        _BandSoftness("Band softness", Range(0.001,0.15)) = 0.015
        _AmbientStrength("Ambient strength", Range(0,2)) = 0.25

        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.35
        [NoScaleOffset] _MetallicGlossMap("Metallic map", 2D) = "white" {}
        _MetallicMapStrength("Metallic map strength", Range(0,1)) = 0
        [NoScaleOffset] _SpecGlossMap("Specular map", 2D) = "white" {}
        _SpecularMapStrength("Specular map strength", Range(0,1)) = 0
        _SpecularColor("Specular color", Color) = (1,1,1,1)
        _SpecularStrength("Specular strength", Range(0,2)) = 0.25
        _SpecularPower("Specular power", Range(1,128)) = 40
        _EnvironmentStrength("Environment reflection", Range(0,1)) = 0.25

        _RimColor("Rim color", Color) = (0.72,0.82,1,1)
        _RimStrength("Rim strength", Range(0,2)) = 0.12
        _RimPower("Rim power", Range(0.5,12)) = 4
        _AdditionalLightStrength("Additional lights", Range(0,2)) = 0.6

        [HDR] _EmissionColor("Emission color", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap("Emission map", 2D) = "white" {}

        _OutlineColor("Outline color", Color) = (0.05,0.05,0.06,1)
        _OutlineWidth("Outline width", Range(0,0.3)) = 0.08

        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _AlphaClip("Alpha clip", Float) = 0
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _ZWrite("Z write", Float) = 1

        // These aliases keep the URP depth and shadow passes compatible.
        [HideInInspector] _MainTex("Base map", 2D) = "white" {}
        [HideInInspector] _Color("Base color", Color) = (1,1,1,1)
        [HideInInspector] _BumpMap("Normal map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal strength", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CharacterStyle"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 tangentWS : TEXCOORD3;
                half3 bitangentWS : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                half fogFactor : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_StyleMask);
            SAMPLER(sampler_StyleMask);
            TEXTURE2D(_RampMap);
            SAMPLER(sampler_RampMap);
            TEXTURE2D(_MetallicGlossMap);
            SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_SpecGlossMap);
            SAMPLER(sampler_SpecGlossMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DeepShadowColor;
                half4 _ShadowColor;
                half4 _BandThresholds;
                half4 _SpecularColor;
                half4 _RimColor;
                half4 _EmissionColor;
                half4 _OutlineColor;
                half _NormalStrength;
                half _StyleMaskStrength;
                half _RampStrength;
                half _BandSoftness;
                half _AmbientStrength;
                half _Metallic;
                half _Smoothness;
                half _MetallicMapStrength;
                half _SpecularMapStrength;
                half _SpecularStrength;
                half _SpecularPower;
                half _EnvironmentStrength;
                half _RimStrength;
                half _RimPower;
                half _AdditionalLightStrength;
                half _Cutoff;
                half _OutlineWidth;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;
                output.vertexLighting = VertexLighting(
                    positionInputs.positionWS,
                    normalInputs.normalWS);
                output.fogFactor = ComputeFogFactor(
                    positionInputs.positionCS.z);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half FiveBand(half value)
            {
                half softness = max(_BandSoftness, 0.001h);
                half band = 0.0h;
                band += smoothstep(
                    _BandThresholds.x - softness,
                    _BandThresholds.x + softness,
                    value) * 0.25h;
                band += smoothstep(
                    _BandThresholds.y - softness,
                    _BandThresholds.y + softness,
                    value) * 0.25h;
                band += smoothstep(
                    _BandThresholds.z - softness,
                    _BandThresholds.z + softness,
                    value) * 0.25h;
                band += smoothstep(
                    _BandThresholds.w - softness,
                    _BandThresholds.w + softness,
                    value) * 0.25h;
                return band;
            }

            half3 DefaultRamp(half band)
            {
                half low = saturate(band * 2.0h);
                half high = saturate((band - 0.5h) * 2.0h);
                half3 color = lerp(
                    _DeepShadowColor.rgb,
                    _ShadowColor.rgb,
                    low);
                return lerp(color, half3(1.0h, 1.0h, 1.0h), high);
            }

            half3 EvaluateRamp(
                half lightValue,
                half rampRow)
            {
                half band = FiveBand(lightValue);
                half3 fallback = DefaultRamp(band);
                half3 ramp = SAMPLE_TEXTURE2D(
                    _RampMap,
                    sampler_RampMap,
                    half2(saturate(lightValue), saturate(rampRow))).rgb;
                return lerp(fallback, ramp, saturate(_RampStrength));
            }

            half3 EvaluateSpecular(
                Light light,
                half3 normalWS,
                half3 viewWS,
                half3 baseColor,
                half metallic,
                half smoothness,
                half mask)
            {
                half3 halfDirection = SafeNormalize(light.direction + viewWS);
                half normalHalf = saturate(dot(normalWS, halfDirection));
                half power = max(
                    _SpecularPower * lerp(0.45h, 1.35h, smoothness),
                    1.0h);
                half highlight = pow(normalHalf, power);
                highlight = smoothstep(0.18h, 0.26h, highlight);
                half attenuation = light.distanceAttenuation *
                                   light.shadowAttenuation;
                half3 specularColor = lerp(
                    _SpecularColor.rgb,
                    baseColor,
                    metallic);
                return highlight * _SpecularStrength * mask * attenuation *
                       light.color * specularColor;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 baseSample = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    input.uv) * _BaseColor;

                #if defined(_ALPHATEST_ON)
                clip(baseSample.a - _Cutoff);
                #endif

                half3 geometricNormal = normalize(input.normalWS);
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(
                        _NormalMap,
                        sampler_NormalMap,
                        input.uv),
                    max(_NormalStrength, 0.0001h));
                half3x3 tangentToWorld = half3x3(
                    normalize(input.tangentWS),
                    normalize(input.bitangentWS),
                    geometricNormal);
                half3 mappedNormal = normalize(
                    TransformTangentToWorld(normalTS, tangentToWorld));
                half3 normalWS = normalize(lerp(
                    geometricNormal,
                    mappedNormal,
                    saturate(_NormalStrength)));
                half3 viewWS = GetWorldSpaceNormalizeViewDir(
                    input.positionWS);

                half4 styleMask = SAMPLE_TEXTURE2D(
                    _StyleMask,
                    sampler_StyleMask,
                    input.uv);
                half styleMaskWeight = saturate(_StyleMaskStrength);
                half rampRow = lerp(0.5h, styleMask.g, styleMaskWeight);
                half metallicMask = lerp(1.0h, styleMask.b, styleMaskWeight);
                half specularMask = lerp(1.0h, styleMask.a, styleMaskWeight);
                half4 metallicGloss = SAMPLE_TEXTURE2D(
                    _MetallicGlossMap,
                    sampler_MetallicGlossMap,
                    input.uv);
                half4 specularGloss = SAMPLE_TEXTURE2D(
                    _SpecGlossMap,
                    sampler_SpecGlossMap,
                    input.uv);
                metallicMask *= lerp(
                    1.0h,
                    metallicGloss.r,
                    saturate(_MetallicMapStrength));
                specularMask *= lerp(
                    1.0h,
                    Luminance(specularGloss.rgb),
                    saturate(_SpecularMapStrength));
                half metallic = saturate(_Metallic * metallicMask);
                half glossAlpha = lerp(
                    1.0h,
                    metallicGloss.a,
                    saturate(_MetallicMapStrength));
                glossAlpha *= lerp(
                    1.0h,
                    specularGloss.a,
                    saturate(_SpecularMapStrength));
                half smoothness = saturate(_Smoothness * glossAlpha);

                Light mainLight = GetMainLight(
                    TransformWorldToShadowCoord(input.positionWS));
                half halfLambert = dot(normalWS, mainLight.direction) *
                                   0.5h + 0.5h;
                half mainValue = saturate(
                    halfLambert * mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation);

                half3 lighting = SampleSH(normalWS) * _AmbientStrength;
                lighting += EvaluateRamp(mainValue, rampRow) *
                            mainLight.color;

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lighting += input.vertexLighting * _AdditionalLightStrength;
                #endif

                half3 specular = EvaluateSpecular(
                    mainLight,
                    normalWS,
                    viewWS,
                    baseSample.rgb,
                    metallic,
                    smoothness,
                    specularMask);

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                uint additionalLightCount = GetAdditionalLightsCount();

                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0u;
                     lightIndex < min(
                         URP_FP_DIRECTIONAL_LIGHTS_COUNT,
                         MAX_VISIBLE_LIGHTS);
                     lightIndex++)
                {
                    Light additionalLight = GetAdditionalLight(
                        lightIndex,
                        input.positionWS);
                    half halfLambertAdditional =
                        dot(normalWS, additionalLight.direction) *
                        0.5h + 0.5h;
                    half attenuation = additionalLight.distanceAttenuation *
                                       additionalLight.shadowAttenuation;
                    lighting += EvaluateRamp(
                                    saturate(halfLambertAdditional),
                                    rampRow) *
                                additionalLight.color * attenuation *
                                _AdditionalLightStrength;
                    specular += EvaluateSpecular(
                        additionalLight,
                        normalWS,
                        viewWS,
                        baseSample.rgb,
                        metallic,
                        smoothness,
                        specularMask) * _AdditionalLightStrength;
                }
                #endif

                LIGHT_LOOP_BEGIN(additionalLightCount)
                    Light additionalLight = GetAdditionalLight(
                        lightIndex,
                        input.positionWS);
                    half halfLambertAdditional =
                        dot(normalWS, additionalLight.direction) *
                        0.5h + 0.5h;
                    half attenuation = additionalLight.distanceAttenuation *
                                       additionalLight.shadowAttenuation;
                    lighting += EvaluateRamp(
                                    saturate(halfLambertAdditional),
                                    rampRow) *
                                additionalLight.color * attenuation *
                                _AdditionalLightStrength;
                    specular += EvaluateSpecular(
                        additionalLight,
                        normalWS,
                        viewWS,
                        baseSample.rgb,
                        metallic,
                        smoothness,
                        specularMask) * _AdditionalLightStrength;
                LIGHT_LOOP_END
                #endif

                half3 reflectionDirection = reflect(-viewWS, normalWS);
                half3 environment = GlossyEnvironmentReflection(
                    reflectionDirection,
                    input.positionWS,
                    1.0h - smoothness,
                    1.0h,
                    GetNormalizedScreenSpaceUV(input.positionCS));
                half3 reflection = environment * baseSample.rgb * metallic *
                                   _EnvironmentStrength;

                half rim = pow(
                    saturate(1.0h - dot(normalWS, viewWS)),
                    max(_RimPower, 0.5h));
                rim *= _RimStrength * saturate(halfLambert + 0.25h);

                half3 emission = SAMPLE_TEXTURE2D(
                    _EmissionMap,
                    sampler_EmissionMap,
                    input.uv).rgb * _EmissionColor.rgb;
                half3 color = baseSample.rgb * lighting + specular +
                              reflection + _RimColor.rgb * rim + emission;
                half outline = 1.0h - smoothstep(
                    0.0h,
                    max(_OutlineWidth, 0.0001h),
                    abs(dot(normalWS, viewWS)));
                outline *= step(0.0001h, _OutlineWidth);
                color = lerp(color, _OutlineColor.rgb, outline);
                color = MixFog(color, input.fogFactor);
                return half4(color, baseSample.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack Off
}
