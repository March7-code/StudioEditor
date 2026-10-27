Shader "Hidden/StudioEditor/KoikatsuFaceBake"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _ColorMask ("Skin mask", 2D) = "white" {}
        _Color ("Main skin", Color) = (1,1,1,1)
        _Color2 ("Secondary skin", Color) = (1,1,1,1)
        _Texture3 ("Paint 1", 2D) = "black" {}
        _Texture4 ("Cheek", 2D) = "black" {}
        _Texture5 ("Lip line", 2D) = "black" {}
        _Texture6 ("Mole", 2D) = "black" {}
        _Texture7 ("Paint 2", 2D) = "black" {}
        _paintmask ("Paint mask", 2D) = "white" {}
        _Color3 ("Paint 1 color", Color) = (1,1,1,1)
        _Color4 ("Cheek color", Color) = (1,1,1,1)
        _Color5 ("Lip line color", Color) = (1,1,1,1)
        _Color6 ("Mole color", Color) = (1,1,1,1)
        _Color7 ("Paint 2 color", Color) = (1,1,1,1)
        _paint1 ("Paint 1 layout", Vector) = (0,0,0,0.5)
        _paint2 ("Paint 2 layout", Vector) = (0,0,0,0.5)
        _hokuro ("Mole layout", Vector) = (0,0,0,0.5)
        _tex4uv ("Cheek layout", Vector) = (0,0,1,1)
        _tex5uv ("Lip line layout", Vector) = (0,0,1,1)
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
            sampler2D _MainTex, _ColorMask, _Texture3, _Texture4, _Texture5;
            sampler2D _Texture6, _Texture7, _paintmask;
            float4 _MainTex_ST, _ColorMask_ST, _Texture3_ST, _Texture4_ST;
            float4 _Texture5_ST, _Texture6_ST, _Texture7_ST, _paintmask_ST;
            float4 _Color, _Color2, _Color3, _Color4, _Color5, _Color6, _Color7;
            float4 _paint1, _paint2, _hokuro, _tex4uv, _tex5uv;

            float2 TransformUv(float2 uv, float4 st) { return uv * st.xy + st.zw; }
            float2 PaintUv(float2 uv, float4 layout, bool rotate)
            {
                float scale = 2 - 2 * layout.w;
                uv = (uv + layout.xy) * (2 * scale) + .5 - scale;
                if (rotate)
                {
                    float sine, cosine;
                    sincos(layout.z * 6.283185, sine, cosine);
                    uv -= .5;
                    uv = float2(dot(uv, float2(cosine, sine)), dot(uv, float2(-sine, cosine))) + .5;
                }
                return uv;
            }
            float3 Overlay(float3 color, float4 textureColor, float4 tint, float mask)
            {
                return lerp(color, textureColor.rgb * tint.rgb, textureColor.a * tint.a * mask);
            }
            float4 frag(v2f_img input) : SV_Target
            {
                // Source create_head DIRECTIONAL DXBC: red/green skin masks
                // multiply independently; blue preserves uncolored texture regions.
                float2 uv = input.uv;
                float3 mask = tex2D(_ColorMask, TransformUv(uv, _ColorMask_ST)).rgb;
                float3 tint = lerp(1, _Color.rgb, mask.r) * lerp(1, _Color2.rgb, mask.g);
                float3 color = max(mask.b, tint) * tex2D(_MainTex, TransformUv(uv, _MainTex_ST)).rgb;
                float2 cheekUv = (uv + _tex4uv.xy - 1) * (1 - _tex4uv.zw) + .5;
                color = Overlay(color, tex2D(_Texture4, TransformUv(cheekUv, _Texture4_ST)), _Color4, 1);
                float2 lipUv = (uv + _tex5uv.xy - 1) * (1 - _tex5uv.zw) + .5;
                color = Overlay(color, tex2D(_Texture5, TransformUv(lipUv, _Texture5_ST)), _Color5, 1);
                float paintMask = tex2D(_paintmask, TransformUv(uv, _paintmask_ST)).r;
                color = Overlay(color, tex2D(_Texture3, TransformUv(PaintUv(uv, _paint1, true), _Texture3_ST)), _Color3, paintMask);
                color = Overlay(color, tex2D(_Texture7, TransformUv(PaintUv(uv, _paint2, true), _Texture7_ST)), _Color7, paintMask);
                color = Overlay(color, tex2D(_Texture6, TransformUv(PaintUv(uv, _hokuro, false), _Texture6_ST)), _Color6, 1);
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
}
