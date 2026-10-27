Shader "Hidden/StudioEditor/KoikatsuLightmapDecode"
{
    Properties { _MainTex("Source RGBM lightmap", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 Frag(v2f_img input) : SV_Target
            {
                float4 encoded = tex2D(_MainTex, input.uv);
                // Desktop Unity 5 source bundles use RGBM, with a gamma-space
                // range of 5. URP desktop lightmaps use full HDR instead.
                return float4(encoded.rgb * (34.493242 * pow(encoded.a, 2.2)), 1);
            }
            ENDHLSL
        }
    }
}
