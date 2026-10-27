#ifndef STUDIO_LOCAL_LIGHTING_INCLUDED
#define STUDIO_LOCAL_LIGHTING_INCLUDED
TEXTURE3D(_StudioLocalIrradiance);
TEXTURE3D(_StudioLocalDistance);
TEXTURE3D(_StudioLocalContact);
float4 _StudioLocalLighting;
float4 _StudioProbeGrid, _StudioProbeOrigin, _StudioContactGrid, _StudioContactOrigin;
float4 _StudioContactSettings, _StudioLocalSunDirection;
int _StudioLocalDebug;

struct StudioLocalLight { half3 indirect; half skyVisibility, occlusion, sunVisibility; };
float3 StudioAxis(uint i)
{
    return i == 0 ? float3(1,0,0) : i == 1 ? float3(-1,0,0) :
        i == 2 ? float3(0,1,0) : i == 3 ? float3(0,-1,0) :
        i == 4 ? float3(0,0,1) : float3(0,0,-1);
}
int3 StudioAtlas(int3 cell, uint lobe, float4 grid)
{
    cell = clamp(cell, 0, (int3)grid.xyz - 1);
    cell.x += lobe * (int)grid.x;
    return cell;
}
float StudioVolumeFade(float3 cell, float3 size)
{
    float3 edge = min(cell, size - 1 - cell);
    return saturate(min(edge.x, min(edge.y, edge.z)) / 2);
}
StudioLocalLight StudioSampleLocalLight(float3 position, float3 normal)
{
    StudioLocalLight result;
    result.indirect = 0; result.skyVisibility = result.occlusion = result.sunVisibility = 1;
    if (_StudioLocalLighting.x < 0.5) return result;
    float3 biasedPosition = position + normal * _StudioContactSettings.y;
    float3 cell = (biasedPosition - _StudioProbeOrigin.xyz) / _StudioProbeGrid.w;
    float fade = StudioVolumeFade(cell, _StudioProbeGrid.xyz);
    if (fade > 0)
    {
        int3 base = (int3)floor(cell);
        float3 fraction = frac(cell);
        float3 energy = 0;
        float total = 0, sky = 0;
        [loop] for (uint corner = 0; corner < 8; corner++)
        {
            int3 offset = int3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
            float3 weight = lerp(1 - fraction, fraction, offset);
            float triWeight = weight.x * weight.y * weight.z;
            float3 delta = biasedPosition - (_StudioProbeOrigin.xyz + (base + offset) * _StudioProbeGrid.w);
            float distance = length(delta);
            float3 toward = distance > 0.0001 ? delta / distance : normal;
            float visibility = 0;
            [loop] for (uint axis = 0; axis < 3; axis++)
            {
                float axisWeight = abs(toward[axis]);
                uint directionLobe = axis * 2 + (toward[axis] < 0 ? 1 : 0);
                float2 moments = LOAD_TEXTURE3D(_StudioLocalDistance, StudioAtlas(base + offset, directionLobe, _StudioProbeGrid)).xy;
                float variance = max(moments.y - moments.x * moments.x, 0.0001);
                float error = max(distance - moments.x, 0);
                visibility += axisWeight * (error > 0 ? pow(variance / (variance + error * error), 3) : 1);
            }
            visibility /= max(abs(toward.x) + abs(toward.y) + abs(toward.z), 0.001);
            [loop] for (uint axis = 0; axis < 3; axis++)
            {
                float lobeWeight = abs(normal[axis]);
                uint lobe = axis * 2 + (normal[axis] < 0 ? 1 : 0);
                float4 sample = LOAD_TEXTURE3D(_StudioLocalIrradiance, StudioAtlas(base + offset, lobe, _StudioProbeGrid));
                float w = triWeight * lobeWeight * visibility * (sample.a >= 0 ? 1 : 0);
                energy += sample.rgb * w; sky += max(0, sample.a) * w; total += w;
            }
        }
        if (total > 0.00001)
        {
            result.indirect = energy / total * _StudioLocalLighting.y * fade;
            result.skyVisibility = lerp(1, sky / total, fade);
        }
    }
    cell = (biasedPosition - _StudioContactOrigin.xyz) / _StudioContactGrid.w;
    fade = StudioVolumeFade(cell, _StudioContactGrid.xyz);
    if (fade > 0)
    {
        int3 base = (int3)floor(cell);
        float3 fraction = frac(cell);
        float ao = 0, aoWeight = 0, shadow = 0, shadowWeight = 0;
        [loop] for (uint corner = 0; corner < 8; corner++)
        {
            int3 offset = int3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
            float3 weight = lerp(1 - fraction, fraction, offset);
            float3 probe = _StudioContactOrigin.xyz + (base + offset) * _StudioContactGrid.w;
            // Sample from the outward side of the receiver, avoiding probes inside the receiver.
            float w = weight.x * weight.y * weight.z * saturate(dot(probe - position, normal) / _StudioContactGrid.w + 0.05);
            [loop] for (uint axis = 0; axis < 3; axis++)
            {
                uint lobe = axis * 2 + (normal[axis] < 0 ? 1 : 0);
                float aw = w * abs(normal[axis]);
                float visibility = LOAD_TEXTURE3D(_StudioLocalContact, StudioAtlas(base + offset, lobe, _StudioContactGrid)).r;
                ao += visibility * aw; aoWeight += aw;
            }
            float sunDistance = LOAD_TEXTURE3D(_StudioLocalContact, StudioAtlas(base + offset, 6, _StudioContactGrid)).r;
            sunDistance -= dot(biasedPosition - probe, _StudioLocalSunDirection.xyz);
            shadow += smoothstep(max(0, _StudioContactSettings.x - _StudioContactGrid.w * 2),
                _StudioContactSettings.x + _StudioContactGrid.w * 2, sunDistance) * w;
            shadowWeight += w;
        }
        if (aoWeight > 0.00001) result.occlusion = lerp(1, ao / aoWeight, _StudioLocalLighting.z * fade);
        if (shadowWeight > 0.00001) result.sunVisibility = lerp(1, shadow / shadowWeight, _StudioLocalLighting.w * fade);
    }
    return result;
}
half3 StudioLocalDebugColor(StudioLocalLight local, float3 position)
{
    if (_StudioLocalDebug == 1) return local.indirect;
    if (_StudioLocalDebug == 2) return local.occlusion.xxx;
    if (_StudioLocalDebug == 3) return local.sunVisibility.xxx;
    float3 giCell = (position - _StudioProbeOrigin.xyz) / _StudioProbeGrid.w;
    float3 aoCell = (position - _StudioContactOrigin.xyz) / _StudioContactGrid.w;
    return half3(StudioVolumeFade(giCell, _StudioProbeGrid.xyz),
        StudioVolumeFade(aoCell, _StudioContactGrid.xyz), 0);
}
#endif
