Shader "StudioEditor/KoikatsuSceneEffect"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        [HideInInspector] _Color2 ("Second channel color", Color) = (1,1,1,1)
        [HideInInspector] _Color3 ("Third channel color", Color) = (1,1,1,1)
        [HideInInspector] _alpha ("Additive intensity", Float) = 1
        [HideInInspector] _SourceAdditiveColorChannels ("Source additive channels", Float) = 0
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _ZWrite ("Z write", Float) = 1
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _AlphaClip ("Alpha clip", Float) = 0
        [HideInInspector] _Cutoff ("Alpha cutoff", Float) = 0.5
        [HideInInspector] _TextureAnimation ("Texture animation", Float) = 0
        [HideInInspector] _TileAnimation ("Tile animation", Vector) = (1,1,0,0)
        [HideInInspector] _SizeSpeed ("Size speed", Float) = 0
        [HideInInspector] _SizeWidth ("Size width", Float) = 0
        [HideInInspector] _AngleSpeed ("Angle speed", Float) = 0
        [HideInInspector] _yurayura ("Swing", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                float4 _Color2, _Color3;
                float _alpha, _SourceAdditiveColorChannels;
                float _AlphaClip;
                float _Cutoff;
                float _TextureAnimation;
                float4 _TileAnimation;
                float _SizeSpeed, _SizeWidth, _AngleSpeed, _yurayura;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_TextureAnimation > 0.5)
                {
                    float2 tiles = max(_TileAnimation.xy, 1.0);
                    float scale = 1.0 + 2.0 * _SizeWidth * (1.0 + sin(_Time.y * _SizeSpeed * 10.0));
                    float angle = lerp(_Time.y, sin(_Time.w) * 0.1, _yurayura) * _AngleSpeed * 6.283185;
                    float sine, cosine;
                    sincos(angle, sine, cosine);
                    float2 centered = (uv - 0.5) * scale;
                    uv = float2(dot(centered, float2(cosine, sine)), dot(centered, float2(-sine, cosine)));
                    float frame = round(tiles.x * tiles.y * _Time.y * _TileAnimation.z);
                    float row = floor(frame / tiles.x);
                    uv = (uv + 0.5 + float2(frame - tiles.x * row, row)) / tiles;
                }
                uv = uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                if (_SourceAdditiveColorChannels > 0.5)
                {
                    // main_item_studio_add encodes three color masks, not an RGB image.
                    // Higher channels suppress lower ones; the source combines them
                    // with a component-wise maximum and ignores texture/color alpha.
                    float greenBlue4 = max(texel.g, texel.b);
                    greenBlue4 *= greenBlue4;
                    greenBlue4 *= greenBlue4;
                    float blue4 = texel.b * texel.b;
                    blue4 *= blue4;
                    float red = saturate(texel.r - greenBlue4 * greenBlue4);
                    float green = saturate(texel.g - blue4 * blue4);
                    float3 color = max(max(red * _BaseColor.rgb, green * _Color2.rgb), texel.b * _Color3.rgb);
                    return float4(color * _alpha, 1.0);
                }
                half4 color = texel * _BaseColor;
                if (_AlphaClip > 0.5) clip(color.a - _Cutoff);
                return color;
            }
            ENDHLSL
        }
    }
}
