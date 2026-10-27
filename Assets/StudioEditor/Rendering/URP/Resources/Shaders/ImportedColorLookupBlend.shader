Shader "Hidden/StudioEditor/ImportedColorLookupBlend"
{
    Properties
    {
        _MainTex ("Primary LUT", 2D) = "white" {}
        _SecondaryLut ("Secondary LUT", 2D) = "white" {}
        _Blend ("Secondary contribution", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _SecondaryLut;
            float _Blend;
            float4 Frag(v2f_img input) : SV_Target
            {
                return lerp(tex2D(_MainTex, input.uv), tex2D(_SecondaryLut, input.uv), _Blend);
            }
            ENDHLSL
        }
    }
}
