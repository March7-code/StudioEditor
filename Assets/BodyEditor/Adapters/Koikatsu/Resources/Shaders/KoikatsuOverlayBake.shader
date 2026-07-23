Shader "Hidden/BodyEditor/KoikatsuOverlayBake"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Overlay ("Overlay", 2D) = "black" {}
        _CompositeOverlayAlpha ("Composite overlay alpha", Float) = 0
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
            sampler2D _Overlay;
            float _CompositeOverlayAlpha;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 source = tex2D(_MainTex, input.uv);
                fixed4 overlay = tex2D(_Overlay, input.uv);
                fixed alpha = saturate(overlay.a);
                if (_CompositeOverlayAlpha > 0.5)
                {
                    fixed outputAlpha = alpha + source.a * (1.0 - alpha);
                    fixed3 premultiplied = overlay.rgb * alpha +
                        source.rgb * source.a * (1.0 - alpha);
                    fixed3 outputColor = outputAlpha > 0.0001
                        ? premultiplied / outputAlpha
                        : 0.0;
                    return fixed4(outputColor, outputAlpha);
                }

                source.rgb = lerp(source.rgb, overlay.rgb, alpha);
                return source;
            }
            ENDHLSL
        }
    }
}
