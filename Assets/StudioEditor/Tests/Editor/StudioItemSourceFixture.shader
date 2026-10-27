// Metadata fixture: the game bundle supplies the rendering program at runtime.
Shader "Shader Forge/main_item_studio"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _ColorMask ("Color mask", 2D) = "black" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _DetailMask ("Detail", 2D) = "black" {}
        _LineMask ("Line", 2D) = "black" {}
        _AnotherRamp ("Ramp", 2D) = "white" {}
        _Color ("Color 1", Color) = (1,1,1,1)
        _Color2 ("Color 2", Color) = (1,1,1,1)
        _Color3 ("Color 3", Color) = (1,1,1,1)
        _ShadowColor ("Shadow", Color) = (1,1,1,1)
        _ShadowExtend ("Shadow extend", Float) = 1
        _ShadowExtendAnother ("Alternate extend", Float) = 1
    }
    SubShader { Tags { "RenderType"="Opaque" } Pass {} }
}
