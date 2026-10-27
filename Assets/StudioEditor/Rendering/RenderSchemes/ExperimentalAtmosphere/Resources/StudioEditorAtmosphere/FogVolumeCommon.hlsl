#ifndef STUDIO_EDITOR_FOG_VOLUME_INCLUDED
#define STUDIO_EDITOR_FOG_VOLUME_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

float4 _FogGrid; // xyz: grid dimensions, w: near eye distance
float4 _FogRange; // x: far eye distance, y: base height, z: height falloff, w: density
float4x4 _FogInverseProjection;
float4x4 _FogCameraToWorld;
float _FogOrthographic;
float _FogFarClipValue;

float FogEyeDepth(float slice)
{
    float t = slice / _FogGrid.z;
    return lerp(_FogGrid.w, _FogRange.x, t * t);
}

float3 FogWorldPosition(float2 uv, float eyeDepth)
{
    // Match the depth composite's UV convention, including the GPU projection
    // Y flip on platforms whose render textures start at the top.
    float4 v = mul(_FogInverseProjection, ComputeClipSpacePosition(uv, _FogFarClipValue));
    float3 p = v.xyz / v.w;
    p = _FogOrthographic > 0.5 ? float3(p.xy, -eyeDepth) : p * (eyeDepth / -p.z);
    return mul(_FogCameraToWorld, float4(p, 1.0)).xyz;
}

float FogDensity(float3 positionWS)
{
    return _FogRange.w * exp(-max(0.0, positionWS.y - _FogRange.y) * _FogRange.z);
}

#endif
