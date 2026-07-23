Shader "BodyEditor/AnimeCharacter"
{
    // Vertex color channels are used by Koikatsu clothing materials.
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
        _BandSoftness("Band Gaussian sigma", Range(0.001,0.15)) = 0.004
        _AmbientStrength("Ambient strength", Range(0,2)) = 0.25
        _LightColorInfluence("Light color influence", Range(0,1)) = 0.2

        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.35
        [NoScaleOffset] _MetallicGlossMap("Metallic map", 2D) = "white" {}
        _MetallicMapStrength("Metallic map strength", Range(0,1)) = 0
        [NoScaleOffset] _SpecGlossMap("Specular map", 2D) = "white" {}
        _SpecularMapStrength("Specular map strength", Range(0,1)) = 0
        _SpecularColor("Specular color", Color) = (1,1,1,1)
        _SpecularStrength("Specular strength", Range(0,2)) = 0
        _SpecularPower("Specular power", Range(1,128)) = 40
        _EnvironmentStrength("Environment reflection", Range(0,1)) = 0.25

        _RimColor("Rim color", Color) = (0.72,0.82,1,1)
        _RimStrength("Rim strength", Range(0,2)) = 0
        _RimPower("Rim power", Range(0.5,12)) = 4
        _AdditionalLightStrength("Additional lights", Range(0,2)) = 0.6

        [HDR] _EmissionColor("Emission color", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap("Emission map", 2D) = "white" {}

        _OutlineColor("Outline color", Color) = (0.05,0.05,0.06,1)
        _OutlineWidth("Outline width (cm)", Range(0,0.3)) = 0.08

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
        [HideInInspector] _Color2("Secondary color", Color) = (1,1,1,1)
        [HideInInspector] _Color3("Tertiary color", Color) = (1,1,1,1)
        [HideInInspector] _Color4("Quaternary color", Color) = (1,1,1,1)
        [HideInInspector] _LineColor("Line color", Color) = (0.05,0.05,0.06,1)
        [HideInInspector] _UseHairGradient("Use hair gradient", Float) = 0
        [HideInInspector] _UseVertexColorChannels("Use vertex color channels", Float) = 0
        [HideInInspector] _UseColorMaskChannels("Use color mask channels", Float) = 0
        [HideInInspector] _UseHairGloss("Use hair gloss", Float) = 0
        [HideInInspector] _UseFlatColor("Use flat color", Float) = 0
        _FaceSphereNormalBlend("Face sphere normal blend", Range(0,1)) = 0
        _FaceSphereLowerCylinder("Face lower cylinder", Range(0,1)) = 1
        [HideInInspector] _FaceSphereNormalEnabled("Face sphere normal enabled", Float) = 0
        [HideInInspector] _FaceSphereCenterWS("Face sphere center WS", Vector) = (0,0,0,1)
        [HideInInspector] _FaceSphereUpWS("Face sphere up WS", Vector) = (0,1,0,0)
        [HideInInspector] _UseToon("Use toon lighting", Float) = 1
        [HideInInspector] _BumpMap("Normal map", 2D) = "bump" {}
        [HideInInspector] _BumpScale("Normal strength", Float) = 1

        // Original Koikatsu material state retained by the adapter.
        [HideInInspector] _Texture2("Texture 2", 2D) = "white" {}
        [HideInInspector] _Texture3("Texture 3", 2D) = "white" {}
        [HideInInspector] _Texture4("Texture 4", 2D) = "white" {}
        [HideInInspector] _Texture5("Texture 5", 2D) = "white" {}
        [HideInInspector] _Texture6("Texture 6", 2D) = "white" {}
        [HideInInspector] _Texture7("Texture 7", 2D) = "white" {}
        [HideInInspector] _ColorMask("Color mask", 2D) = "white" {}
        [HideInInspector] _AlphaMask("Alpha mask", 2D) = "white" {}
        [HideInInspector] _DetailMask("Detail mask", 2D) = "white" {}
        [HideInInspector] _NormalMapDetail("Detail normal", 2D) = "bump" {}
        [HideInInspector] _LineMask("Line mask", 2D) = "white" {}
        [HideInInspector] _HairGloss("Hair gloss", 2D) = "white" {}
        [HideInInspector] _overtex1("Overlay 1", 2D) = "black" {}
        [HideInInspector] _overtex2("Overlay 2", 2D) = "black" {}
        [HideInInspector] _overtex3("Overlay 3", 2D) = "black" {}
        [HideInInspector] _paint1("Paint 1", 2D) = "black" {}
        [HideInInspector] _paint2("Paint 2", 2D) = "black" {}
        [HideInInspector] _hokuro("Mole", 2D) = "black" {}
        [HideInInspector] _Color1_2("Color 1 alt", Color) = (1,1,1,1)
        [HideInInspector] _Color2_2("Color 2 alt", Color) = (1,1,1,1)
        [HideInInspector] _Color3_2("Color 3 alt", Color) = (1,1,1,1)
        [HideInInspector] _Color4_2("Color 4 alt", Color) = (1,1,1,1)
        [HideInInspector] _Color5("Color 5", Color) = (1,1,1,1)
        [HideInInspector] _Color6("Color 6", Color) = (1,1,1,1)
        [HideInInspector] _Color7("Color 7", Color) = (1,1,1,1)
        [HideInInspector] _overcolor1("Overlay color 1", Color) = (1,1,1,1)
        [HideInInspector] _overcolor2("Overlay color 2", Color) = (1,1,1,1)
        [HideInInspector] _overcolor3("Overlay color 3", Color) = (1,1,1,1)
        [HideInInspector] _Blend("Gradient blend", Float) = 0
        [HideInInspector] _grad("Gradient transform", Vector) = (0,0,0,0)
        [HideInInspector] _exppower("Expression power", Float) = 0
        [HideInInspector] _isHighLight("Highlight", Float) = 0
        [HideInInspector] _reverse("Reverse", Float) = 0
        [HideInInspector] _rotation("Rotation", Float) = 0
        [HideInInspector] _alpha_a("Alpha A", Float) = 0
        [HideInInspector] _alpha_b("Alpha B", Float) = 0
        [HideInInspector] _nipsize("Nipple size", Float) = 0
        [HideInInspector] _linetexon("Line texture", Float) = 0
        [HideInInspector] _DetailNormalMapScale("Detail normal scale", Float) = 1
        [HideInInspector] _SpecularPowerNail("Nail specular", Float) = 0
        [HideInInspector] _liquidftop("Liquid front top", Float) = 0
        [HideInInspector] _liquidfbot("Liquid front bottom", Float) = 0
        [HideInInspector] _liquidbtop("Liquid back top", Float) = 0
        [HideInInspector] _liquidbbot("Liquid back bottom", Float) = 0
        [HideInInspector] _liquidface("Liquid face", Float) = 0
        [HideInInspector] _PatternScale1u("Pattern 1 U", Float) = 0
        [HideInInspector] _PatternScale1v("Pattern 1 V", Float) = 0
        [HideInInspector] _PatternScale2u("Pattern 2 U", Float) = 0
        [HideInInspector] _PatternScale2v("Pattern 2 V", Float) = 0
        [HideInInspector] _PatternScale3u("Pattern 3 U", Float) = 0
        [HideInInspector] _PatternScale3v("Pattern 3 V", Float) = 0
        [HideInInspector] _PatternScale4u("Pattern 4 U", Float) = 0
        [HideInInspector] _PatternScale4v("Pattern 4 V", Float) = 0
        [HideInInspector] _TileAnimation("Tile animation", Float) = 0
        [HideInInspector] _SizeSpeed("Size speed", Float) = 0
        [HideInInspector] _SizeWidth("Size width", Float) = 0
        [HideInInspector] _angleSpeed("Angle speed", Float) = 0
        [HideInInspector] _yurayura("Sway", Float) = 0
        [HideInInspector] _nip_specular("Nipple specular", Float) = 0
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
                half4 vertexColor : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 vertexColor : COLOR;
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
            TEXTURE2D(_HairGloss);
            SAMPLER(sampler_HairGloss);
            TEXTURE2D(_ColorMask);
            SAMPLER(sampler_ColorMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                half4 _Color2;
                half4 _Color3;
                half4 _Color4;
                half4 _DeepShadowColor;
                half4 _ShadowColor;
                half4 _BandThresholds;
                half4 _SpecularColor;
                half4 _RimColor;
                half4 _EmissionColor;
                half4 _OutlineColor;
                float4 _FaceSphereCenterWS;
                float4 _FaceSphereUpWS;
                half _NormalStrength;
                half _StyleMaskStrength;
                half _RampStrength;
                half _BandSoftness;
                half _AmbientStrength;
                half _LightColorInfluence;
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
                half _UseHairGradient;
                half _UseVertexColorChannels;
                half _UseColorMaskChannels;
                half _UseHairGloss;
                half _UseFlatColor;
                half _FaceSphereNormalBlend;
                half _FaceSphereLowerCylinder;
                half _FaceSphereNormalEnabled;
                half _UseToon;
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
                output.vertexColor = input.vertexColor;
                return output;
            }

            // A Gaussian blur of a step edge is its cumulative normal
            // distribution. This keeps the transition in light space,
            // without introducing a screen-space blur around face details.
            half GaussianCdf(
                half value,
                half center,
                half sigma)
            {
                half x = (value - center) / max(sigma, 0.0001h);
                half ax = abs(x);
                half t = 1.0h / (1.0h + 0.2316419h * ax);
                half polynomial = t * (0.3193815h + t * (
                    -0.3565638h + t * (1.781478h + t * (
                    -1.821256h + t * 1.330274h))));
                half tail = 0.3989423h * exp(-0.5h * ax * ax) *
                            polynomial;
                half positiveCdf = 1.0h - tail;
                return x < 0.0h ? 1.0h - positiveCdf : positiveCdf;
            }

            half3 DefaultRamp(half lightValue)
            {
                half softness = max(_BandSoftness, 0.001h);
                half deepToShadow = GaussianCdf(
                    lightValue,
                    _BandThresholds.y,
                    softness);
                half shadowToLight = GaussianCdf(
                    lightValue,
                    _BandThresholds.z,
                    softness);
                half3 color = lerp(
                    _DeepShadowColor.rgb,
                    _ShadowColor.rgb,
                    deepToShadow);
                return lerp(
                    color,
                    half3(1.0h, 1.0h, 1.0h),
                    shadowToLight);
            }

            half3 EvaluateRamp(
                half lightValue,
                half rampRow)
            {
                half3 fallback = DefaultRamp(lightValue);
                half3 ramp = SAMPLE_TEXTURE2D(
                    _RampMap,
                    sampler_RampMap,
                    half2(saturate(lightValue), saturate(rampRow))).rgb;
                return lerp(fallback, ramp, saturate(_RampStrength));
            }

            half3 EvaluateLightColor(half3 lightColor)
            {
                half luminance = Luminance(lightColor);
                return lerp(
                    luminance.xxx,
                    lightColor,
                    saturate(_LightColorInfluence));
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
                       EvaluateLightColor(light.color) * specularColor;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 sourceSample = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    input.uv);
                half4 baseSample = sourceSample * _BaseColor;
                half4 vertexWeights = saturate(input.vertexColor);
                half totalWeight = vertexWeights.r + vertexWeights.g +
                                   vertexWeights.b + vertexWeights.a;
                if (_UseColorMaskChannels > 0.5h)
                {
                    half3 mask = saturate(SAMPLE_TEXTURE2D(
                        _ColorMask,
                        sampler_ColorMask,
                        input.uv).rgb);
                    half maskWeight = mask.r + mask.g + mask.b;
                    half3 channelTint = (
                        _Color.rgb * mask.r +
                        _Color2.rgb * mask.g +
                        _Color3.rgb * mask.b) / max(maskWeight, 0.0001h);
                    channelTint = lerp(
                        half3(1.0h, 1.0h, 1.0h),
                        channelTint,
                        saturate(maskWeight));
                    baseSample.rgb = sourceSample.rgb * channelTint;
                }
                else if (_UseVertexColorChannels > 0.5h &&
                    totalWeight > 0.0001h)
                {
                    half3 vertexTint = (
                        _Color.rgb * vertexWeights.r +
                        _Color2.rgb * vertexWeights.g +
                        _Color3.rgb * vertexWeights.b +
                        _Color4.rgb * vertexWeights.a) / totalWeight;
                    baseSample.rgb = sourceSample.rgb * vertexTint;
                }
                #if defined(_ALPHATEST_ON)
                clip(baseSample.a - _Cutoff);
                #endif

                // Keep a material-level opt-out for import diagnostics and
                // custom render-scheme overrides.
                if (_UseToon <= 0.5h)
                {
                    return half4(baseSample.rgb, baseSample.a);
                }

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
                half faceSphereWeight = saturate(
                    _FaceSphereNormalBlend * _FaceSphereNormalEnabled);
                half3 faceSphereUpWS = SafeNormalize(_FaceSphereUpWS.xyz);
                half3 faceSphereDeltaWS =
                    input.positionWS - _FaceSphereCenterWS.xyz;
                half faceSphereHeight = dot(
                    faceSphereDeltaWS,
                    faceSphereUpWS);
                faceSphereDeltaWS -= faceSphereUpWS *
                    min(faceSphereHeight, 0.0h) *
                    saturate(_FaceSphereLowerCylinder);
                half3 faceSphereNormalWS = SafeNormalize(faceSphereDeltaWS);
                normalWS = normalize(lerp(
                    normalWS,
                    faceSphereNormalWS,
                    faceSphereWeight));
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
                half hairGloss = SAMPLE_TEXTURE2D(
                    _HairGloss,
                    sampler_HairGloss,
                    input.uv).r;
                metallicMask *= lerp(
                    1.0h,
                    metallicGloss.r,
                    saturate(_MetallicMapStrength));
                specularMask *= lerp(
                    1.0h,
                    Luminance(specularGloss.rgb),
                    saturate(_SpecularMapStrength));
                specularMask *= lerp(
                    1.0h,
                    hairGloss,
                    saturate(_UseHairGloss));
                half metallic = saturate(_Metallic * metallicMask);
                half glossAlpha = lerp(
                    1.0h,
                    metallicGloss.a,
                    saturate(_MetallicMapStrength));
                glossAlpha *= lerp(
                    1.0h,
                    specularGloss.a,
                    saturate(_SpecularMapStrength));
                glossAlpha *= lerp(
                    1.0h,
                    hairGloss,
                    saturate(_UseHairGloss));
                half smoothness = saturate(_Smoothness * glossAlpha);

                Light mainLight = GetMainLight(
                    TransformWorldToShadowCoord(input.positionWS));
                half halfLambert = dot(normalWS, mainLight.direction) *
                                   0.5h + 0.5h;
                half mainValue = saturate(
                    halfLambert * mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation);

                half3 lighting = EvaluateLightColor(SampleSH(normalWS)) *
                                 _AmbientStrength;
                lighting += EvaluateRamp(mainValue, rampRow) *
                            EvaluateLightColor(mainLight.color);

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lighting += EvaluateLightColor(input.vertexLighting) *
                            _AdditionalLightStrength;
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
                                EvaluateLightColor(additionalLight.color) *
                                attenuation *
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
                                EvaluateLightColor(additionalLight.color) *
                                attenuation *
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
                if (_UseFlatColor > 0.5h)
                {
                    color = baseSample.rgb;
                }
                color = MixFog(color, input.fogFactor);
                return half4(color, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "BodyEditorOutline" }
            Blend One Zero
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
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
                half4 _Color;
                half4 _Color2;
                half4 _Color3;
                half4 _Color4;
                half4 _DeepShadowColor;
                half4 _ShadowColor;
                half4 _BandThresholds;
                half4 _SpecularColor;
                half4 _RimColor;
                half4 _EmissionColor;
                half4 _OutlineColor;
                float4 _FaceSphereCenterWS;
                float4 _FaceSphereUpWS;
                half _NormalStrength;
                half _StyleMaskStrength;
                half _RampStrength;
                half _BandSoftness;
                half _AmbientStrength;
                half _LightColorInfluence;
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
                half _UseHairGradient;
                half _UseVertexColorChannels;
                half _UseColorMaskChannels;
                half _UseHairGloss;
                half _UseFlatColor;
                half _FaceSphereNormalBlend;
                half _FaceSphereLowerCylinder;
                half _FaceSphereNormalEnabled;
                half _UseToon;
            CBUFFER_END

            OutlineVaryings OutlineVert(OutlineAttributes input)
            {
                OutlineVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS += normalize(normalWS) * (_OutlineWidth * 0.01);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 OutlineFrag(OutlineVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_UseToon - 0.5h);
                clip(_OutlineWidth - 0.0001h);

                #if defined(_ALPHATEST_ON)
                half alpha = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    input.uv).a * _BaseColor.a;
                clip(alpha - _Cutoff);
                #endif

                half3 color = MixFog(_OutlineColor.rgb, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack Off
}
