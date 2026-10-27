Shader "StudioEditor/KoikatsuStudioItem"
{
    Properties
    {
        _MainTex ("Main texture", 2D) = "white" {}
        _ColorMask ("Color mask", 2D) = "black" {}
        _NormalMap ("Normal map", 2D) = "bump" {}
        _DetailMask ("Detail mask", 2D) = "black" {}
        _LineMask ("Line mask", 2D) = "black" {}
        _AnotherRamp ("Alternate ramp", 2D) = "white" {}
        [HideInInspector] _SourceRamp ("Scene ramp", 2D) = "white" {}
        [HideInInspector] _SourceAmbientShadow ("Scene ambient shadow", Vector) = (.2,.2,.2,1)
        [HideInInspector] _SourceItemModel ("Source item shading", Float) = 0
        [HideInInspector] _UseColorMask ("Texture color channels", Float) = 0
        _ShadowColor ("Shadow color", Color) = (.628,.628,.628,1)
        _ShadowExtend ("Shadow extend", Float) = 1
        _ShadowExtendAnother ("Alternate shadow extend", Float) = 1
        _AnotherRampFull ("Alternate ramp full", Float) = 0
        _DetailRLineR ("Detail red replaces line red", Float) = 0
        _SpecularPower ("Specular power", Float) = 0
        _SpeclarHeight ("Specular height", Float) = .98
        _rimpower ("Rim width", Float) = .5
        _rimV ("Rim strength", Float) = .5
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
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
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
                half4 tangentWS : TEXCOORD4;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
            TEXTURE2D(_LineMask); SAMPLER(sampler_LineMask);
            TEXTURE2D(_AnotherRamp); SAMPLER(sampler_AnotherRamp);
            TEXTURE2D(_SourceRamp); SAMPLER(sampler_SourceRamp);
            TEXTURE2D(_Pattern1);
            SAMPLER(sampler_Pattern1);
            TEXTURE2D(_Pattern2);
            SAMPLER(sampler_Pattern2);
            TEXTURE2D(_Pattern3);
            SAMPLER(sampler_Pattern3);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _ColorMask_ST;
                float4 _NormalMap_ST;
                float4 _DetailMask_ST;
                float4 _LineMask_ST;
                float4 _AnotherRamp_ST;
                half4 _SourceAmbientShadow;
                half4 _ShadowColor;
                half _SourceItemModel;
                half _UseColorMask;
                half _ShadowExtend;
                half _ShadowExtendAnother;
                half _AnotherRampFull;
                half _DetailRLineR;
                half _SpecularPower;
                half _SpeclarHeight;
                half _rimpower;
                half _rimV;
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
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz),
                    input.tangentOS.w * GetOddNegativeScale());
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
                float angle = _SourceItemModel > .5h ? rotationDegrees * PI : radians(rotationDegrees);
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                float2 centered = _SourceItemModel > .5h ? uv + transform.xy - .5 : (uv - 0.5) * transform.zw;
                centered = float2(
                    centered.x * cosine - centered.y * sine,
                    centered.x * sine + centered.y * cosine);
                float2 result = _SourceItemModel > .5h ? centered * transform.zw + .5 : centered + 0.5 + transform.xy;
                float2 clamped = clamp(result, 0.0001, 0.9999);
                return lerp(result, clamped, step(0.5, clampUv));
            }

            half3 PatternColor(
                half3 baseColor,
                half4 patternColor,
                half pattern,
                half hasPattern)
            {
                if (_SourceItemModel > .5h)
                    return lerp(patternColor.rgb, baseColor,
                        saturate(max(pattern, 1 - patternColor.a) + 1 - hasPattern));
                return lerp(
                    baseColor,
                    patternColor.rgb,
                    saturate(pattern) * saturate(hasPattern));
            }

            half3 SourceOverlay(half3 baseColor, half3 blendColor)
            {
                return saturate(lerp(2 * baseColor * blendColor,
                    1 - 2 * (1 - baseColor) * (1 - blendColor), step(.5h, baseColor)));
            }

            // main_item_studio has one Forward pass, with a ramp and an ambient
            // shadow floor. A Lambert multiplier loses the colored metal in shade.
            half3 SourceDiffuse(Varyings input, half3 albedo, half3 normalWS, Light light)
            {
                half4 detail = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, TRANSFORM_TEX(input.uv, _DetailMask));
                half4 lineMask = SAMPLE_TEXTURE2D(_LineMask, sampler_LineMask, TRANSFORM_TEX(input.uv, _LineMask));
                half3 ambient = saturate(lerp(_SourceAmbientShadow.rgb, 1.0h, _SourceAmbientShadow.a));
                half value = max(max(albedo.r, albedo.g), max(albedo.b, .00001h));
                half3 chroma = lerp(1.0h, albedo / value, .5h);
                half detailRed = lerp(lineMask.r, detail.r, _DetailRLineR);
                half shadowScale = .33h + .67h * saturate(2 - detailRed - _ShadowExtendAnother);
                half3 shadow = SourceOverlay(chroma * _ShadowColor.rgb * .9h, ambient) * shadowScale;
                half ramp = SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp,
                    half2(saturate(dot(normalWS, light.direction)), .5h)).r;
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half halfAngle = saturate(dot(normalWS, SafeNormalize(viewWS + light.direction)));
                half alternate = SAMPLE_TEXTURE2D(_AnotherRamp, sampler_AnotherRamp,
                    halfAngle * _AnotherRamp_ST.xy + _AnotherRamp_ST.zw).r;
                half visibility = light.shadowAttenuation * light.distanceAttenuation;
                half extend = lerp(1, min(1 - detail.g, 1 - lineMask.b), _ShadowExtend);
                half weight = lerp(ramp, alternate, lerp(detailRed, 1, _AnotherRampFull)) * visibility * extend;
                half specular = saturate((5 * pow(max(halfAngle, .0001h), 256 * _SpecularPower) - 4) * _SpecularPower);
                half rim = saturate(2.5h * pow(1 - saturate(dot(normalWS, viewWS)), 1 + 9 * _rimpower) - .5h);
                rim *= _rimV * saturate(10 * detail.b - 9) * (1 - detail.g);
                half3 result = albedo * lerp(shadow, 1, saturate(weight)) + specular + min(rim, .5h);
                return result * max(.4h + .6h * light.color, _SourceAmbientShadow.rgb);
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
                    _PatternColor1,
                    pattern1,
                    _HasPattern1);
                half3 color2 = PatternColor(
                    _ChannelColor2.rgb,
                    _PatternColor2,
                    pattern2,
                    _HasPattern2);
                half3 color3 = PatternColor(
                    _ChannelColor3.rgb,
                    _PatternColor3,
                    pattern3,
                    _HasPattern3);

                half3 weights = saturate(input.color.rgb) *
                                saturate(_ChannelEnabled.rgb);
                if (_UseColorMask > .5h)
                    weights = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask,
                        TRANSFORM_TEX(input.uv, _ColorMask)).rgb * saturate(_ChannelEnabled.rgb);
                half total = weights.r + weights.g + weights.b;
                half3 tint = (color1 * weights.r + color2 * weights.g +
                              color3 * weights.b) / max(total, 0.0001h);
                tint = lerp(half3(1.0h, 1.0h, 1.0h), tint,
                            saturate(total));
                if (_UseColorMask > .5h)
                {
                    // Legacy masks blend sequentially; overlapping channels
                    // deliberately replace the preceding color.
                    tint = lerp(1.0h, color1, weights.r);
                    tint = lerp(tint, color2, weights.g);
                    tint = lerp(tint, color3, weights.b);
                }

                half3 normalWS = normalize(input.normalWS);
                half3 lighting = SampleSH(normalWS) + input.vertexLighting;
                Light mainLight = GetMainLight(
                    TransformWorldToShadowCoord(input.positionWS));
                #ifdef _LIGHT_LAYERS
                uint meshLayers = GetMeshRenderingLayer();
                if (!IsMatchingLightLayer(mainLight.layerMask, meshLayers)) mainLight.color = 0;
                #endif
                if (_SourceItemModel > .5h)
                {
                    // The legacy material chooses one Forward light. Respect
                    // URP's audience layers when that light is in Forward+.
                    Light sourceLight = mainLight;
                    half brightness = Luminance(mainLight.color) * mainLight.distanceAttenuation;
                    #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light candidate = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        #ifdef _LIGHT_LAYERS
                        if (!IsMatchingLightLayer(candidate.layerMask, meshLayers)) continue;
                        #endif
                        half candidateBrightness = Luminance(candidate.color) * candidate.distanceAttenuation;
                        if (candidateBrightness > brightness) { sourceLight = candidate; brightness = candidateBrightness; }
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light candidate = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        #ifdef _LIGHT_LAYERS
                        if (!IsMatchingLightLayer(candidate.layerMask, meshLayers)) continue;
                        #endif
                        half candidateBrightness = Luminance(candidate.color) * candidate.distanceAttenuation;
                        if (candidateBrightness > brightness) { sourceLight = candidate; brightness = candidateBrightness; }
                    LIGHT_LOOP_END
                    #endif
                    half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(input.uv, _NormalMap)));
                    half3 tangent = normalize(input.tangentWS.xyz);
                    half3 bitangent = cross(normalWS, tangent) * input.tangentWS.w;
                    if (dot(input.tangentWS.xyz, input.tangentWS.xyz) > .001h)
                        normalWS = normalize(normalTS.x * tangent + normalTS.y * bitangent + normalTS.z * normalWS);
                    return half4(SourceDiffuse(input, source.rgb * tint, normalWS, sourceLight) + _EmissionColor.rgb, source.a);
                }
                lighting += LightingLambert(
                    mainLight.color *
                    (mainLight.distanceAttenuation *
                     mainLight.shadowAttenuation),
                    mainLight.direction,
                    normalWS);

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint additionalLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                    #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(additionalLight.layerMask, meshLayers)) continue;
                    #endif
                    lighting += LightingLambert(additionalLight.color * additionalLight.distanceAttenuation *
                        additionalLight.shadowAttenuation, additionalLight.direction, normalWS);
                }
                #endif
                LIGHT_LOOP_BEGIN(additionalLightCount)
                {
                    Light additionalLight = GetAdditionalLight(
                        lightIndex,
                        input.positionWS);
                    #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(additionalLight.layerMask, meshLayers)) continue;
                    #endif
                    lighting += LightingLambert(
                        additionalLight.color *
                        (additionalLight.distanceAttenuation *
                         additionalLight.shadowAttenuation),
                        additionalLight.direction,
                        normalWS);
                }
                LIGHT_LOOP_END
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
