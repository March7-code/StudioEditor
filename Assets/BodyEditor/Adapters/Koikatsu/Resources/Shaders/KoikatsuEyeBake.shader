Shader "Hidden/BodyEditor/KoikatsuEyeBake"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _ColorMask ("Gradient", 2D) = "white" {}
        _HighlightUp ("Upper highlight", 2D) = "white" {}
        _HighlightDown ("Lower highlight", 2D) = "white" {}
        _BaseColor ("Base color", Color) = (1,1,1,1)
        _SubColor ("Sub color", Color) = (1,1,1,1)
        _HighlightUpColor ("Upper highlight color", Color) = (1,1,1,1)
        _HighlightDownColor ("Lower highlight color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragIris
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _ColorMask;
            sampler2D _HighlightUp;
            sampler2D _HighlightDown;
            float4 _BaseColor;
            float4 _SubColor;
            float4 _HighlightUpColor;
            float4 _HighlightDownColor;
            float _GradientBlend;
            float _GradientOffsetY;
            float _GradientScale;
            float _HighlightUpOffsetY;
            float _HighlightDownOffsetY;
            float _HasGradient;
            float _HasHighlightUp;
            float _HasHighlightDown;
            float _Rotation;

            float2 RotateUv(float2 uv, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                uv -= 0.5;
                uv = float2(
                    cosine * uv.x - sine * uv.y,
                    sine * uv.x + cosine * uv.y);
                return uv + 0.5;
            }

            float4 fragIris(v2f_img input) : SV_Target
            {
                float2 uv = RotateUv(input.uv, _Rotation);
                float4 source = tex2D(_MainTex, uv);
                float2 gradientUv = uv;
                gradientUv.y =
                    (gradientUv.y - 0.5) * max(0.001, 1.0 + _GradientScale) +
                    0.5 + _GradientOffsetY;
                float gradient = tex2D(_ColorMask, gradientUv).r;
                gradient = lerp(0.0, gradient, _HasGradient);
                float3 tint = lerp(
                    _BaseColor.rgb,
                    _SubColor.rgb,
                    saturate(gradient * _GradientBlend));
                float4 result = float4(
                    saturate(source.rgb * tint * 2.0),
                    source.a);

                float4 upper = tex2D(
                    _HighlightUp,
                    uv + float2(0.0, _HighlightUpOffsetY));
                float upperAlpha = upper.a * _HighlightUpColor.a *
                                   _HasHighlightUp * source.a;
                result.rgb = lerp(
                    result.rgb,
                    upper.rgb * _HighlightUpColor.rgb,
                    upperAlpha);

                float4 lower = tex2D(
                    _HighlightDown,
                    uv + float2(0.0, _HighlightDownOffsetY));
                float lowerAlpha = lower.a * _HighlightDownColor.a *
                                   _HasHighlightDown * source.a;
                result.rgb = lerp(
                    result.rgb,
                    lower.rgb * _HighlightDownColor.rgb,
                    lowerAlpha);
                return result;
            }
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragWhite
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _BaseColor;
            float4 _SubColor;

            float4 fragWhite(v2f_img input) : SV_Target
            {
                float shade = tex2D(_MainTex, input.uv).r;
                return float4(
                    lerp(_SubColor.rgb, _BaseColor.rgb, shade),
                    1.0);
            }
            ENDHLSL
        }
    }
}
