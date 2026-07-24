Shader "Hidden/StudioEditor/KoikatsuBodyMaskBake"
{
    Properties
    {
        _MainTex ("Body", 2D) = "white" {}
        _AlphaMask ("Alpha mask", 2D) = "white" {}
        _MaskScale ("Mask scale", Vector) = (1, 1, 0, 0)
        _MaskOffset ("Mask offset", Vector) = (0, 0, 0, 0)
        _MaskChannels ("Mask channels", Vector) = (1, 1, 0, 0)
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
            sampler2D _AlphaMask;
            float4 _MaskScale;
            float4 _MaskOffset;
            float4 _MaskChannels;

            float4 frag(v2f_img input) : SV_Target
            {
                float4 body = tex2D(_MainTex, input.uv);
                float2 mask = saturate(
                    tex2D(
                        _AlphaMask,
                        input.uv * _MaskScale.xy + _MaskOffset.xy).rg);
                body.a *= lerp(1.0, mask.r, _MaskChannels.x) *
                    lerp(1.0, mask.g, _MaskChannels.y);
                return body;
            }
            ENDHLSL
        }
    }
}
