Shader "Hidden/StudioEditor/KoikatsuClothesBake"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _ColorMask ("Color mask", 2D) = "black" {}
        _Pattern1 ("Pattern 1", 2D) = "white" {}
        _Pattern2 ("Pattern 2", 2D) = "white" {}
        _Pattern3 ("Pattern 3", 2D) = "white" {}
        _ChannelColor1 ("Color 1", Color) = (1,1,1,1)
        _ChannelColor2 ("Color 2", Color) = (1,1,1,1)
        _ChannelColor3 ("Color 3", Color) = (1,1,1,1)
        _PatternColor1 ("Pattern color 1", Color) = (1,1,1,1)
        _PatternColor2 ("Pattern color 2", Color) = (1,1,1,1)
        _PatternColor3 ("Pattern color 3", Color) = (1,1,1,1)
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _ColorMask;
            sampler2D _Pattern1;
            sampler2D _Pattern2;
            sampler2D _Pattern3;
            float4 _ChannelColor1;
            float4 _ChannelColor2;
            float4 _ChannelColor3;
            float4 _PatternColor1;
            float4 _PatternColor2;
            float4 _PatternColor3;
            float2 _PatternTiling1;
            float2 _PatternTiling2;
            float2 _PatternTiling3;
            float _HasPattern1;
            float _HasPattern2;
            float _HasPattern3;

            float2 PatternUv(float2 uv, float2 tiling)
            {
                return uv * lerp(float2(20.0, 20.0), float2(1.0, 1.0),
                                 saturate(tiling));
            }

            float3 ChannelColor(
                float2 uv,
                sampler2D patternTexture,
                float3 baseColor,
                float4 patternColor,
                float2 tiling,
                float hasPattern)
            {
                float pattern = tex2D(
                    patternTexture,
                    PatternUv(uv, tiling)).r;
                float3 patterned = lerp(patternColor.rgb, baseColor,
                    max(pattern, 1 - patternColor.a));
                return lerp(baseColor, patterned, hasPattern);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv);
                float3 mask = saturate(tex2D(_ColorMask, input.uv).rgb);
                float3 color1 = ChannelColor(
                    input.uv,
                    _Pattern1,
                    _ChannelColor1.rgb,
                    _PatternColor1,
                    _PatternTiling1,
                    _HasPattern1);
                float3 color2 = ChannelColor(
                    input.uv,
                    _Pattern2,
                    _ChannelColor2.rgb,
                    _PatternColor2,
                    _PatternTiling2,
                    _HasPattern2);
                float3 color3 = ChannelColor(
                    input.uv,
                    _Pattern3,
                    _ChannelColor3.rgb,
                    _PatternColor3,
                    _PatternTiling3,
                    _HasPattern3);

                // Match create_topN: black pattern texels receive the pattern
                // color, and later color-mask channels replace earlier ones.
                float3 tint = lerp(float3(1.0, 1.0, 1.0), color1, mask.r);
                tint = lerp(tint, color2, mask.g);
                tint = lerp(tint, color3, mask.b);
                return float4(source.rgb * tint, source.a);
            }
            ENDHLSL
        }
    }
}
