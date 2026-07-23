Shader "Hidden/StudioEditor/KoikatsuBodyMaskBake"
{
    Properties
    {
        _MainTex ("Body", 2D) = "white" {}
        _AlphaMask ("Alpha mask", 2D) = "white" {}
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

            float4 frag(v2f_img input) : SV_Target
            {
                float4 body = tex2D(_MainTex, input.uv);
                float2 mask = saturate(
                    tex2D(_AlphaMask, input.uv).rg);
                body.a *= mask.r * mask.g;
                return body;
            }
            ENDHLSL
        }
    }
}
