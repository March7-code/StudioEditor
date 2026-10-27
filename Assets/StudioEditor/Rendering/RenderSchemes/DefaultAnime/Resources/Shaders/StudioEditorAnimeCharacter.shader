Shader "StudioEditor/AnimeCharacter"
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
        _FaceShadowStrength("Face curve shadow", Range(0,1)) = 1
        _FaceShadowSoftness("Face shadow edge width", Range(0.001,0.1)) = 0.003
        _FaceHairShadowStrength("Hair shadow on face", Range(0,1)) = 1
        [HideInInspector] _FaceHairShadowCount("Hair shadow lights", Float) = 0
        [HideInInspector] _FaceHairDepth("Hair shadow depth", 2DArray) = "" {}
        [HideInInspector] _FaceSceneShadowCount("Face scene shadow lights", Float) = 0
        [HideInInspector] _FaceSceneDepth("Face scene depth", 2DArray) = "" {}
        _ClothesSkinShadowStrength("Clothes shadow on skin", Range(0,1)) = 1
        [HideInInspector] _ClothesSkinShadowCount("Clothes shadow lights", Float) = 0
        [HideInInspector] _ClothesSkinDepth("Clothes shadow depth", 2DArray) = "" {}
        [HideInInspector] _FaceShadowReady("Face shadow coordinates ready", Float) = 0
        [HideInInspector] _FaceShadowMap("Face shadow grayscale", 2D) = "gray" {}
        [HideInInspector] _FaceShadowForward("Face forward", Vector) = (0,0,1,0)
        [HideInInspector] _FaceShadowRight("Face right", Vector) = (1,0,0,0)
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
        [HideInInspector] _ColorMaskBlendMode("Color mask blend mode", Float) = 0
        [HideInInspector] _ImportedCharacterKey("Imported character lighting", Float) = 0
        [HideInInspector] _UseHairGloss("Use hair gloss", Float) = 0
        [HideInInspector] _UseFlatColor("Use flat color", Float) = 0
        [HideInInspector] _UseToon("Use toon lighting", Float) = 1
        [HideInInspector] _StudioSurfaceRole("Imported surface role", Float) = 0
        [HideInInspector] _SourceHairFront("Source front hair", Float) = 0
        [HideInInspector] _SourceHairLow("Source low-detail hair", Float) = 0
        [HideInInspector] _SourceCharacterModel("Source diffuse model", Float) = 0
        [HideInInspector] _SourceCharacterShading("Source scene shading", Float) = 0
        [HideInInspector] _SourceShadowColor("Source shadow color", Color) = (.628,.628,.628,1)
        [HideInInspector] _SourceShadowExtend("Source shadow extend", Float) = .5
        [HideInInspector] _SourceShadowExtendAnother("Source alternate shadow extend", Float) = 0
        [HideInInspector] _SourceDetailRLineR("Source detail red line red", Float) = 0
        [HideInInspector] _SourceAmbientShadow("Source ambient shadow", Vector) = (.5,.5,.5,1)
        [HideInInspector] _SourceSpecularPower("Source specular strength", Float) = 0
        [HideInInspector] _SourceSpecularColor("Source specular color", Color) = (1,1,1,0)
        [HideInInspector] _SourceSpecularHeight("Source specular height", Float) = .98
        [HideInInspector] _SourceRimPower("Source rim power", Float) = .5
        [HideInInspector] _SourceRimStrength("Source rim strength", Float) = 0
        [HideInInspector] _SourceDetailBLineG("Source detail blue line green", Float) = 0
        [HideInInspector] _SourceNotUseTextureSpecular("Source untextured specular", Float) = 0
        [HideInInspector] _SourceLineColor("Source line color", Vector) = (.5,.5,.5,.5)
        [HideInInspector] _SourceLegacyLightCount("Source legacy light count", Float) = 0
        [HideInInspector] _SourceLineWidth("Source line width", Float) = .5
        [HideInInspector] _SourceRamp("Source scene ramp", 2D) = "white" {}
        [HideInInspector] _AnotherRamp("Source alternate ramp", 2D) = "white" {}
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
        [HideInInspector] _DetailMask("Detail mask", 2D) = "black" {}
        [HideInInspector] _NormalMapDetail("Detail normal", 2D) = "bump" {}
        [HideInInspector] _LineMask("Line mask", 2D) = "black" {}
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
        [HideInInspector] _IrisExpressionMap("Iris expression", 2D) = "black" {}
        [HideInInspector] _IrisExpressionStrength("Iris expression strength", Float) = 0
        [HideInInspector] _IrisHighlightMask("Baked iris highlight visibility", 2D) = "black" {}
        [HideInInspector] _IrisHighlightEnabled("Use baked iris highlight visibility", Float) = 0
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
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/StudioEditor/Rendering/RenderSchemes/ExperimentalAtmosphere/Resources/StudioEditorAtmosphere/LocalLightingSampling.hlsl"

            // Supplied only while the scene experiment renders its target camera.
            float4 _StudioSurfaceLighting; // enabled, direct, ambient, light color influence
            float4 _StudioSurfaceHighlights; // skin, hair, clothes, accessories
            float _StudioSurfaceReflection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 hairUV : TEXCOORD1;
                float2 faceShadowUV : TEXCOORD2; // Shared face/eyelid SDF UV
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
                float2 colorMaskUV : TEXCOORD7;
                float2 rawUV : TEXCOORD8;
                float2 hairUV : TEXCOORD9;
                float2 faceShadowUV : TEXCOORD10;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_ARRAY(_FaceSceneDepth);
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
            TEXTURE2D(_IrisExpressionMap);
            SAMPLER(sampler_IrisExpressionMap);
            TEXTURE2D(_IrisHighlightMask);
            TEXTURE2D(_SourceRamp);
            SAMPLER(sampler_SourceRamp);
            TEXTURE2D(_FaceShadowMap);
            TEXTURE2D_ARRAY(_FaceHairDepth);
            TEXTURE2D_ARRAY(_ClothesSkinDepth);
            TEXTURE2D(_AnotherRamp);
            TEXTURE2D(_DetailMask);
            SAMPLER(sampler_DetailMask);
            TEXTURE2D(_NormalMapDetail);
            TEXTURE2D(_LineMask);
            SAMPLER(sampler_LineMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _ColorMask_ST;
                float4 _IrisExpressionMap_ST;
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
                half _ColorMaskBlendMode;
                half _ImportedCharacterKey;
                half _IrisExpressionStrength;
                half _IrisHighlightEnabled;
                half _UseHairGloss;
                half _UseFlatColor;
                half _UseToon;
                half _StudioSurfaceRole;
                half _SourceHairFront;
                half _SourceHairLow;
                float4 _HairGloss_ST;
                half _SourceCharacterModel;
                half _FaceShadowStrength;
                half _FaceShadowSoftness;
                half _FaceShadowReady;
                half _FaceHairShadowStrength;
                half _FaceHairShadowCount;
                half _FaceSceneShadowCount;
                float4x4 _FaceSceneWorldToShadow[4];
                float4 _FaceHairShadowWeights;
                float4x4 _FaceHairWorldToShadow[4];
                float4 _FaceHairLightPositions[4];
                half _ClothesSkinShadowStrength;
                half _ClothesSkinShadowCount;
                float4 _ClothesSkinShadowWeights;
                float4x4 _ClothesSkinWorldToShadow[4];
                float4 _ClothesSkinLightPositions[4];
                float4 _FaceShadowForward;
                float4 _FaceShadowRight;
                half _SourceCharacterShading;
                half4 _SourceShadowColor;
                half4 _SourceAmbientShadow;
                half _SourceShadowExtend;
                half _SourceShadowExtendAnother;
                half _SourceDetailRLineR;
                float4 _DetailMask_ST;
                float4 _LineMask_ST;
                float4 _AnotherRamp_ST;
                float4 _NormalMap_ST;
                float4 _NormalMapDetail_ST;
                half _DetailNormalMapScale;
                half _SpecularPowerNail;
                half _linetexon;
                half _SourceSpecularPower;
                half4 _SourceSpecularColor;
                half _SourceSpecularHeight;
                half _SourceRimPower;
                half _SourceRimStrength;
                half _SourceDetailBLineG;
                half _SourceNotUseTextureSpecular;
                half4 _SourceLineColor;
                half _SourceLineWidth;
                float _SourceLegacyLightCount;
                float4 _SourceLinearLightColors[32];
                float4 _SourceLegacyLightColors[32];
                float4 _SourceLightPositions[32];
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
                output.colorMaskUV = TRANSFORM_TEX(input.uv, _ColorMask);
                output.rawUV = input.uv;
                output.hairUV = input.hairUV;
                output.faceShadowUV = input.faceShadowUV;
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
                    saturate(_StudioSurfaceLighting.x > 0.5 ?
                        _StudioSurfaceLighting.w : _LightColorInfluence));
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
                half strength = _SpecularStrength;
                if (_StudioSurfaceLighting.x > 0.5)
                {
                    // Roles come from the import contract; face details keep their existing treatment.
                    strength = _StudioSurfaceRole == 1 ? _StudioSurfaceHighlights.x :
                        _StudioSurfaceRole == 2 ? 0.0h :
                        _StudioSurfaceRole == 3 ? _StudioSurfaceHighlights.y :
                        _StudioSurfaceRole == 4 ? _StudioSurfaceHighlights.z : _StudioSurfaceHighlights.w;
                    strength *= saturate(dot(normalWS, light.direction));
                }
                return highlight * strength * mask * attenuation *
                       EvaluateLightColor(light.color) * specularColor;
            }

            // Diffuse terms transcribed from the source main_opaque / main_hair
            // forward programs. The original shaders have no ForwardAdd pass.
            half3 SourceOverlay(half3 baseColor, half3 blendColor)
            {
                return saturate(lerp(2 * baseColor * blendColor,
                    1 - 2 * (1 - baseColor) * (1 - blendColor), step(0.5h, baseColor)));
            }

            #include "KoikatsuBodyClothesLighting.hlsl"
            #include "KoikatsuHairLighting.hlsl"

            // Source toon_eyew_lod0 / toon_eye_lod0 DIRECTIONAL programs.
            // Highlight colors are already baked; their mask also lifts the
            // iris lighting weight in the original, independently of N.L.
            half3 EvaluateSourceEye(half3 albedo, half3 normalWS, Light light,
                half3 environment, half detailVisibility)
            {
                half3 ambient = saturate(lerp(_SourceAmbientShadow.rgb, 1.0h, _SourceAmbientShadow.a));
                half3 shadow = SourceOverlay(_SourceShadowColor.rgb, ambient);
                half noL = dot(normalWS, light.direction);
                half weight = _SourceCharacterModel == 5
                    ? SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp, half2(saturate(noL), .5h)).r
                    : saturate(max(noL, 0) + detailVisibility);
                half3 illumination = max(.4h + .6h * light.color, _SourceAmbientShadow.rgb);
                if (_StudioSurfaceLighting.x > .5h)
                    illumination = .6h * light.color * _StudioSurfaceLighting.y *
                        light.distanceAttenuation * light.shadowAttenuation + environment;
                half3 color = albedo * lerp(shadow, 1.0h, weight) * illumination;
                return _SourceCharacterModel == 6 ? saturate(color) : color;
            }

            half3 EvaluateSourceCharacter(Varyings input, half3 albedo, half3 normalWS,
                Light light, bool frontFace, half3 environment, half irisVisibility)
            {
                if (_SourceCharacterModel == 5 || _SourceCharacterModel == 6)
                    return EvaluateSourceEye(albedo, normalWS, light, environment, irisVisibility);
                if (_SourceCharacterModel == 2)
                    return EvaluateSourceHair(input, albedo, normalWS, light, frontFace, environment);
                return EvaluateSourceBodyClothes(input, albedo, normalWS, light, frontFace, environment);
            }

            void GatherSourceLight(Varyings input, half3 albedo, half3 normalWS, bool frontFace,
                half irisVisibility, StudioLocalLight local, Light candidate, bool mainSun,
                inout Light selected, inout half brightness, inout half3 direct)
            {
                candidate = SourceFaceSceneLight(input, candidate);
                candidate.color = SourceLegacyLightColor(candidate, input.positionWS);
                half value = Luminance(candidate.color) * candidate.distanceAttenuation;
                if (_StudioSurfaceLighting.x > .5h && value > 0)
                {
                    // The cache's sun visibility belongs only to world lights.
                    #ifdef _LIGHT_LAYERS
                    if (mainSun && (candidate.layerMask & 1u) != 0u) candidate.shadowAttenuation *= local.sunVisibility;
                    #endif
                    candidate.shadowAttenuation *= local.occlusion;
                    direct += EvaluateSourceCharacter(input, albedo, normalWS, candidate, frontFace, 0, irisVisibility);
                }
                if (value > brightness) { selected = candidate; brightness = value; }
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
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
                        input.colorMaskUV).rgb);
                    half maskWeight = mask.r + mask.g + mask.b;
                    half3 channelTint = (
                        _Color.rgb * mask.r +
                        _Color2.rgb * mask.g +
                        _Color3.rgb * mask.b) / max(maskWeight, 0.0001h);
                    channelTint = lerp(
                        half3(1.0h, 1.0h, 1.0h),
                        channelTint,
                        saturate(maskWeight));
                    if (_ColorMaskBlendMode > 0.5h)
                    {
                        channelTint = lerp(half3(1, 1, 1), _Color.rgb, mask.r);
                        channelTint = lerp(channelTint, _Color2.rgb, mask.g);
                        channelTint = lerp(channelTint, _Color3.rgb, mask.b);
                    }
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
                half irisDetailVisibility = 0;
                if (_IrisExpressionStrength > 0.0h && _SourceCharacterModel != 5)
                {
                    half3 expressionView = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    float2 expressionUV = input.rawUV - 0.06 * float2(
                        dot(input.tangentWS, expressionView), dot(input.bitangentWS, expressionView));
                    expressionUV = expressionUV * _IrisExpressionMap_ST.xy + _IrisExpressionMap_ST.zw;
                    half4 expression = SAMPLE_TEXTURE2D(_IrisExpressionMap, sampler_IrisExpressionMap, expressionUV);
                    half weight = saturate(expression.a * _IrisExpressionStrength);
                    irisDetailVisibility += weight;
                    baseSample.rgb = lerp(baseSample.rgb, expression.rgb, weight);
                    baseSample.a = max(baseSample.a, weight);
                }
                if (_SourceCharacterModel == 6 && _IrisHighlightEnabled > .5h)
                {
                    half4 highlight = SAMPLE_TEXTURE2D(_IrisHighlightMask, sampler_BaseMap, input.uv);
                    baseSample.rgb = lerp(baseSample.rgb, highlight.rgb, highlight.a);
                    baseSample.a = max(baseSample.a, highlight.a);
                    irisDetailVisibility += highlight.a;
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
                bool sourceBodyClothes = _SourceCharacterShading > .5h &&
                    (_SourceCharacterModel == 1 || _SourceCharacterModel == 3 || _SourceCharacterModel == 4);
                bool sourceHair = _SourceCharacterModel == 2;
                bool sourceEye = _SourceCharacterModel == 5 || _SourceCharacterModel == 6;
                if (sourceEye) normalWS = geometricNormal;
                bool frontFace = IS_FRONT_VFACE(facing, true, false);
                if (sourceHair) normalWS = SourceHairNormal(input, frontFace);
                if (sourceBodyClothes)
                {
                    half3x3 sourceTangentToWorld = half3x3(normalize(input.tangentWS),
                        normalize(input.bitangentWS), geometricNormal * (frontFace ? 1 : -1));
                    normalWS = _SourceCharacterModel == 3 || _SourceCharacterModel == 4
                        ? SourceBodyNormal(input, geometricNormal, sourceTangentToWorld)
                        : normalize(TransformTangentToWorld(UnpackNormal(SAMPLE_TEXTURE2D(
                            _NormalMap, sampler_NormalMap, input.rawUV * _NormalMap_ST.xy + _NormalMap_ST.zw)), sourceTangentToWorld));
                }
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
                mainLight.shadowAttenuation = SourceBodyMainShadow(input.positionWS, mainLight.shadowAttenuation);
                #ifdef _LIGHT_LAYERS
                uint meshRenderingLayers = GetMeshRenderingLayer();
                if (!IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers)) mainLight.color = 0;
                #endif
                if (_SourceCharacterShading > 0.5h && _SourceCharacterModel > 0.5h)
                {
                    StudioLocalLight sourceLocal = StudioSampleLocalLight(input.positionWS, normalWS);
                    half3 sourceEnvironment = 0;
                    if (_StudioSurfaceLighting.x > .5h)
                    {
                        if (_StudioLocalDebug > 0 && _StudioLocalLighting.x > .5h)
                            return half4(StudioLocalDebugColor(sourceLocal, input.positionWS), baseSample.a);
                        sourceEnvironment = (EvaluateLightColor(SampleSH(normalWS)) * _StudioSurfaceLighting.z *
                            sourceLocal.skyVisibility + sourceLocal.indirect) * sourceLocal.occlusion;
                    }
                    Light sourceLight = mainLight;
                    half sourceBrightness = -1;
                    half3 sourceDirect = 0;
                    GatherSourceLight(input, baseSample.rgb, normalWS, frontFace, irisDetailVisibility,
                        sourceLocal, mainLight, true, sourceLight, sourceBrightness, sourceDirect);
                    #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    uint additionalLightCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0u;
                        lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light candidate = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        candidate.shadowAttenuation = SourceBodyAdditionalShadow(lightIndex, input.positionWS, candidate);
                        #ifdef _LIGHT_LAYERS
                        if (!IsMatchingLightLayer(candidate.layerMask, meshRenderingLayers)) continue;
                        #endif
                        GatherSourceLight(input, baseSample.rgb, normalWS, frontFace, irisDetailVisibility,
                            sourceLocal, candidate, false, sourceLight, sourceBrightness, sourceDirect);
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(additionalLightCount)
                        Light candidate = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        candidate.shadowAttenuation = SourceBodyAdditionalShadow(lightIndex, input.positionWS, candidate);
                        #ifdef _LIGHT_LAYERS
                        if (!IsMatchingLightLayer(candidate.layerMask, meshRenderingLayers)) continue;
                        #endif
                        GatherSourceLight(input, baseSample.rgb, normalWS, frontFace, irisDetailVisibility,
                            sourceLocal, candidate, false, sourceLight, sourceBrightness, sourceDirect);
                    LIGHT_LOOP_END
                    #endif
                    half3 sourceColor = EvaluateSourceCharacter(input, baseSample.rgb, normalWS,
                        sourceLight, frontFace, sourceEnvironment, irisDetailVisibility);
                    if (_StudioSurfaceLighting.x > .5h)
                    {
                        // Sum every eligible direct light; add ambient/GI once,
                        // using the selected key's toon shape as before.
                        sourceColor += sourceDirect - EvaluateSourceCharacter(input, baseSample.rgb, normalWS,
                            sourceLight, frontFace, 0, irisDetailVisibility);
                    }
                    return half4(MixFog(sourceColor, input.fogFactor), baseSample.a);
                }
                StudioLocalLight localLight = StudioSampleLocalLight(input.positionWS, normalWS);
                if (_StudioLocalDebug > 0 && _StudioLocalLighting.x > 0.5)
                    return half4(StudioLocalDebugColor(localLight, input.positionWS), baseSample.a);
                mainLight.shadowAttenuation *= lerp(localLight.sunVisibility, 1.0h, _ImportedCharacterKey);
                half halfLambert = dot(normalWS, mainLight.direction) *
                                   0.5h + 0.5h;
                half mainValue = saturate(
                    halfLambert * mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation);

                half3 lighting = EvaluateLightColor(SampleSH(normalWS)) *
                                 _AmbientStrength;
                lighting += EvaluateRamp(mainValue, rampRow) *
                            EvaluateLightColor(mainLight.color);
                if (_StudioSurfaceLighting.x > 0.5)
                {
                    // Keep the toon bands for orientation, but shadow visibility controls energy
                    // independently. A blocked key light must not illuminate the deep band.
                    lighting = EvaluateLightColor(SampleSH(normalWS)) * _StudioSurfaceLighting.z *
                        localLight.skyVisibility * localLight.occlusion + localLight.indirect * localLight.occlusion +
                        EvaluateRamp(saturate(halfLambert), rampRow) *
                        EvaluateLightColor(mainLight.color) * _StudioSurfaceLighting.y *
                        mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                }

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
                // An imported character key may be an additional directional light when the
                // map owns URP's main-light slot. Preserve its authored energy in either slot.
                half additionalStrength = lerp(_AdditionalLightStrength, 1.0h, _ImportedCharacterKey);
                additionalStrength *= _StudioSurfaceLighting.x > 0.5 ? _StudioSurfaceLighting.y : 1.0h;

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
                    if (_StudioSurfaceLighting.x > 0.5)
                        additionalLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(additionalLight.layerMask, meshRenderingLayers)) additionalLight.color = 0;
                    #endif
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
                                additionalStrength;
                    specular += EvaluateSpecular(
                        additionalLight,
                        normalWS,
                        viewWS,
                        baseSample.rgb,
                        metallic,
                        smoothness,
                        specularMask) * additionalStrength;
                }
                #endif

                LIGHT_LOOP_BEGIN(additionalLightCount)
                    Light additionalLight = GetAdditionalLight(
                        lightIndex,
                        input.positionWS);
                    if (_StudioSurfaceLighting.x > 0.5)
                        additionalLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    #ifdef _LIGHT_LAYERS
                    if (!IsMatchingLightLayer(additionalLight.layerMask, meshRenderingLayers)) additionalLight.color = 0;
                    #endif
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
                                additionalStrength;
                    specular += EvaluateSpecular(
                        additionalLight,
                        normalWS,
                        viewWS,
                        baseSample.rgb,
                        metallic,
                        smoothness,
                        specularMask) * additionalStrength;
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
                if (_StudioSurfaceLighting.x > 0.5)
                {
                    half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), baseSample.rgb, metallic);
                    half grazing = pow(1.0h - saturate(dot(normalWS, viewWS)), 5.0h);
                    half3 fresnel = f0 + (1.0h - f0) * grazing;
                    reflection = environment * fresnel * _StudioSurfaceReflection *
                        lerp(0.15h, 1.0h, smoothness);
                    if (_StudioSurfaceRole == 2 || _UseFlatColor > 0.5h) reflection = 0;
                }
                reflection *= localLight.occlusion;

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
            Tags { "LightMode" = "StudioEditorOutline" }
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
                float4 _ColorMask_ST;
                float4 _IrisExpressionMap_ST;
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
                half _ColorMaskBlendMode;
                half _ImportedCharacterKey;
                half _IrisExpressionStrength;
                half _IrisHighlightEnabled;
                half _UseHairGloss;
                half _UseFlatColor;
                half _UseToon;
                half _StudioSurfaceRole;
                half _SourceHairFront;
                half _SourceHairLow;
                float4 _HairGloss_ST;
                half _SourceCharacterModel;
                half _FaceShadowStrength;
                half _FaceShadowSoftness;
                half _FaceShadowReady;
                half _FaceHairShadowStrength;
                half _FaceHairShadowCount;
                half _FaceSceneShadowCount;
                float4x4 _FaceSceneWorldToShadow[4];
                float4 _FaceHairShadowWeights;
                float4x4 _FaceHairWorldToShadow[4];
                float4 _FaceHairLightPositions[4];
                half _ClothesSkinShadowStrength;
                half _ClothesSkinShadowCount;
                float4 _ClothesSkinShadowWeights;
                float4x4 _ClothesSkinWorldToShadow[4];
                float4 _ClothesSkinLightPositions[4];
                float4 _FaceShadowForward;
                float4 _FaceShadowRight;
                half _SourceCharacterShading;
                half4 _SourceShadowColor;
                half4 _SourceAmbientShadow;
                half _SourceShadowExtend;
                half _SourceShadowExtendAnother;
                half _SourceDetailRLineR;
                float4 _DetailMask_ST;
                float4 _LineMask_ST;
                float4 _AnotherRamp_ST;
                float4 _NormalMap_ST;
                float4 _NormalMapDetail_ST;
                half _DetailNormalMapScale;
                half _SpecularPowerNail;
                half _linetexon;
                half _SourceSpecularPower;
                half4 _SourceSpecularColor;
                half _SourceSpecularHeight;
                half _SourceRimPower;
                half _SourceRimStrength;
                half _SourceDetailBLineG;
                half _SourceNotUseTextureSpecular;
                half4 _SourceLineColor;
                half _SourceLineWidth;
                float _SourceLegacyLightCount;
                float4 _SourceLinearLightColors[32];
                float4 _SourceLegacyLightColors[32];
                float4 _SourceLightPositions[32];
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
