// Reconstructed from the local main_skin / main_opaque DIRECTIONAL DXBC.
// The source has one forward lighting pass and no additive light pass.
// Source model 1 = clothes, 3 = body, 4 = face skin. Hair keeps its own path.
half3 SourceLegacyLightColor(Light light, float3 positionWS)
{
    [loop] for (uint index = 0; index < (uint)_SourceLegacyLightCount; index++)
    {
        float3 expected = _SourceLinearLightColors[index].rgb;
        float3 delta = abs(light.color - expected);
        // URP's GPU light color may use half precision. Match both color and
        // direction so coincident color values from different lights stay scoped.
        if (max(delta.x, max(delta.y, delta.z)) > .002 * max(1, max(expected.r, max(expected.g, expected.b)))) continue;
        float4 encoded = _SourceLightPositions[index];
        float3 direction = encoded.w > .5 ? normalize(encoded.xyz - positionWS) : encoded.xyz;
        if (dot(light.direction, direction) > .9999) return _SourceLegacyLightColors[index].rgb;
    }
    return light.color;
}

half3 SourceRgbToHsv(half3 c)
{
    half4 k = half4(0, -1.0h / 3, 2.0h / 3, -1);
    half4 p = lerp(half4(c.bg, k.wz), half4(c.gb, k.xy), step(c.b, c.g));
    half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
    half d = q.x - min(q.w, q.y);
    return half3(abs(q.z + (q.w - q.y) / (6 * d + 1e-6h)), d / (q.x + 1e-6h), q.x);
}

half3 SourceHsvToRgb(half3 c)
{
    half3 p = abs(frac(c.xxx + half3(0, 2.0h / 3, 1.0h / 3)) * 6 - 3);
    return c.z * lerp(1.0h, saturate(p - 1), c.y);
}

half3 SourceLineBlend(half3 c, half alpha)
{
    // The source's line alpha controls a multiply/screen blend, independently
    // of the ambient-shadow overlay and light intensity.
    return saturate(alpha > .5h ? 1 - (1 - c) * (2 - 2 * alpha) : c * (2 * alpha));
}

half3 SourceUnpackNormal(half4 packedNormal, half legacyAg)
{
    // The game's DXT5 normals use AG directly; their unused red channel is
    // often .5 rather than 1. Multiplying A by R tilts even a flat normal.
    // Keep URP decoding for BC5, platform defaults and other texture formats.
    return legacyAg > .5h ? UnpackNormalAG(packedNormal) : UnpackNormal(packedNormal);
}

half3 SourceBodyNormal(Varyings input, half3 geometricNormal, half3x3 tangentToWorld)
{
    half3 baseNormal = SourceUnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,
        input.rawUV * _NormalMap_ST.xy + _NormalMap_ST.zw), _SourceNormalEncoding.x);
    half3 detailNormal = SourceUnpackNormal(SAMPLE_TEXTURE2D(_NormalMapDetail, sampler_NormalMap,
        input.rawUV * _NormalMapDetail_ST.xy + _NormalMapDetail_ST.zw), _SourceNormalEncoding.y);
    // Reoriented normal mapping, then the source detail-strength interpolation.
    half3 t = baseNormal + half3(0, 0, 1);
    half3 u = detailNormal * half3(-1, -1, 1);
    half3 combined = t * dot(t, u) / max(t.z, 1e-4h) - u;
    half3 normalTS = lerp(baseNormal, combined, _DetailNormalMapScale);
    return normalize(TransformTangentToWorld(normalTS, tangentToWorld));
}

half SourceFaceCurveRamp(Varyings input, Light light, half originalRamp, out half curveShadow)
{
    curveShadow = 0;
    if (_StudioSurfaceLighting.x < .5h || _SourceCharacterModel != 4 ||
        _FaceShadowReady < .5h || _FaceShadowStrength <= 0) return originalRamp;
    float2 localLight = float2(dot(light.direction, _FaceShadowRight.xyz),
        dot(light.direction, _FaceShadowForward.xyz));
    float horizontal = length(localLight);
    // This map describes azimuth. Near-vertical lights keep the source's
    // elevation response instead of inventing a horizontal shadow direction.
    float threshold = acos(clamp(localLight.y / max(horizontal, 1e-5), -1, 1)) / PI;
    // Azimuth alone treats a high side light as a horizon light and splits the
    // face down the nose. A raised light illuminates more of the face: scale the
    // side-light threshold by the light's horizontal share, keeping 0 (front)
    // and 1 (behind) fixed. Lights below the head keep the azimuth boundary.
    float3 headUp = cross(_FaceShadowForward.xyz, _FaceShadowRight.xyz);
    if (dot(light.direction, headUp) > 0)
        threshold -= (1 - horizontal) * min(threshold, 1 - threshold);
    // Both textures use linear/clamp sampling. Share the source ramp sampler
    // so the additional-light + shadow variants stay within the D3D limit.
    float2 faceUV = input.faceShadowUV.xy;
    if (localLight.x < 0) faceUV.x = 1 - faceUV.x;
    float gray = SAMPLE_TEXTURE2D(_FaceShadowMap, sampler_SourceRamp, faceUV).r;
    // Sample the mirrored coordinates in full precision before applying the
    // narrow toon boundary, keeping both light directions symmetric.
    float width = max(_FaceShadowSoftness, .001);
    half visible = smoothstep(threshold - width * .5h, threshold + width * .5h, gray);
    visible = lerp(1.0h, visible, smoothstep(0, width, threshold));
    visible *= 1 - smoothstep(1 - width, 1, threshold);
    // Keep the source scene ramp's lit/shadow values and its shadow palette.
    half dark = SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp, half2(0, .5h)).r;
    half lit = SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp, half2(1, .5h)).r;
    half curveRamp = lerp(dark, lit, visible);
    half strength = saturate(_FaceShadowStrength) * smoothstep(.05, .2, horizontal);
    curveShadow = (1 - visible) * strength;
    return lerp(originalRamp, curveRamp, strength);
}

// World size of one shadow texel at a receiver, for orthographic or perspective maps.
float SourceShadowTexelWorld(float4x4 worldToShadow, float3 positionWS, float invAtlasSize)
{
    float4 projected = mul(worldToShadow, float4(positionWS, 1));
    float w = max(projected.w, 1e-5);
    float3 gradient = (worldToShadow[0].xyz - projected.x / w * worldToShadow[3].xyz) / w;
    return invAtlasSize / max(length(gradient), 1e-5);
}

// The re-sharpened comparisons below have no wide PCF to hide self-shadowing
// acne on curved skin and cloth. Offset the receiver by its own map's texel
// footprint, more at grazing angles, so the bias stays proportional across cascades.
float3 SourceShadowReceiver(float3 positionWS, half3 normalWS, half3 lightDirection, float texelWorld)
{
    half grazing = 1 - saturate(dot(normalWS, lightDirection));
    return positionWS + normalWS * (texelWorld * (1 + grazing)) + lightDirection * (texelWorld * .5);
}

half SourceCharacterDepthAt(int2 pixel, int index, bool clothes)
{
    if (clothes) return LOAD_TEXTURE2D_ARRAY(_ClothesSkinDepth, pixel, index).r;
    return LOAD_TEXTURE2D_ARRAY(_FaceHairDepth, pixel, index).r;
}

half SourceCharacterDepthCompare(float2 uv, float depth, int index, bool clothes)
{
    int resolution = clothes ? 1024 : 512;
    float2 pixel = uv * resolution - .5;
    int2 a = (int2)floor(pixel);
    float2 blend = frac(pixel);
    int2 p0 = clamp(a, 0, resolution - 1), p1 = clamp(a + 1, 0, resolution - 1);
    half v00 = step(depth, SourceCharacterDepthAt(p0, index, clothes));
    half v10 = step(depth, SourceCharacterDepthAt(int2(p1.x, p0.y), index, clothes));
    half v01 = step(depth, SourceCharacterDepthAt(int2(p0.x, p1.y), index, clothes));
    half v11 = step(depth, SourceCharacterDepthAt(p1, index, clothes));
    return lerp(lerp(v00, v10, blend.x), lerp(v01, v11, blend.x), blend.y);
}

half SourceFaceHairVisibility(Varyings input, Light light)
{
    if (_StudioSurfaceLighting.x < .5h || _SourceCharacterModel != 4 ||
        _FaceShadowReady < .5h || _FaceHairShadowStrength <= 0) return 1;
    [loop] for (int index = 0; index < (int)_FaceHairShadowCount; index++)
    {
        float4 encoded = _FaceHairLightPositions[index];
        float3 direction = encoded.w > .5 ? normalize(encoded.xyz - input.positionWS) : encoded.xyz;
        if (dot(direction, light.direction) < .9999) continue;
        // A small world-space offset avoids contact noise without erasing the
        // centimetre-scale hair/face separation as a world shadow bias can.
        float4 projected = mul(_FaceHairWorldToShadow[index],
            float4(input.positionWS + light.direction * .0005, 1));
        if (projected.w <= 0) return 1;
        float3 shadow = projected.xyz / projected.w;
        if (any(shadow < 0) || any(shadow > 1)) return 1;
        float depth = shadow.z - .000015;
        // One bilinear depth comparison covers the silhouette's edge texel.
        // A wider PCF kernel washes out the narrow, pointed bang shadows.
        // Integer loads still avoid allocating another shader sampler.
        half visible = SourceCharacterDepthCompare(shadow.xy, depth, index, false);
        // Eyelids and the surrounding skin use this same depth comparison.
        // A separate anatomical mask punches bright ovals into the shadow.
        return lerp(1.0h, visible, saturate(_FaceHairShadowStrength * _FaceHairShadowWeights[index]));
    }
    return 1;
}

half SourceClothesSkinVisibility(Varyings input, Light light)
{
    // Faces use SDF self shading and the filtered hair map. Ignore garment
    // depth even if an existing receiver still carries its old property block.
    if (_StudioSurfaceLighting.x < .5h ||
        _SourceCharacterModel != 3 || _ClothesSkinShadowStrength <= 0) return 1;
    [loop] for (int index = 0; index < (int)_ClothesSkinShadowCount; index++)
    {
        float4 encoded = _ClothesSkinLightPositions[index];
        float3 direction = encoded.w > .5 ? normalize(encoded.xyz - input.positionWS) : encoded.xyz;
        if (dot(direction, light.direction) < .9999) continue;
        // The own body is among the casters, so the receiver needs a texel-scale bias.
        float3 receiver = SourceShadowReceiver(input.positionWS, normalize(input.normalWS), light.direction,
            SourceShadowTexelWorld(_ClothesSkinWorldToShadow[index], input.positionWS, 1.0 / 1024));
        float4 projected = mul(_ClothesSkinWorldToShadow[index], float4(receiver, 1));
        if (projected.w <= 0) return 1;
        float3 shadow = projected.xyz / projected.w;
        if (any(shadow < 0) || any(shadow > 1)) return 1;
        float depth = shadow.z - .000015;
        // Keep a single texel's coverage at the garment silhouette. The outer
        // four-tap filter broadened this contact shadow across exposed skin.
        half visible = SourceCharacterDepthCompare(shadow.xy, depth, index, true);
        return lerp(1.0h, visible, saturate(_ClothesSkinShadowStrength * _ClothesSkinShadowWeights[index]));
    }
    return 1;
}

Light SourceFaceSceneLight(Varyings input, Light light)
{
    if (_StudioSurfaceLighting.x < .5h || _SourceCharacterShading < .5h ||
        (_SourceCharacterModel != 3 && _SourceCharacterModel != 4 && _SourceCharacterModel != 5 && _SourceCharacterModel != 6)) return light;
    // Body and face producers omit their own character from this scene map.
    // Do not couple receiving to curve strength: diagnostics can inspect the
    // common scene visibility independently of the SDF's surface shading.
    [loop] for (int index = 0; index < (int)_FaceSceneShadowCount; index++)
    {
        float4 encoded = _FaceHairLightPositions[index];
        float3 direction = encoded.w > .5 ? normalize(encoded.xyz - input.positionWS) : encoded.xyz;
        if (dot(direction, light.direction) < .9999) continue;
        float4 projected = mul(_FaceSceneWorldToShadow[index], float4(input.positionWS + light.direction * .0015, 1));
        if (projected.w <= 0) return light;
        float3 shadow = projected.xyz / projected.w;
        if (any(shadow < 0) || any(shadow > 1)) return light;
        // The scene map omits this actor. Keep true world occlusion while
        // self shading comes from the curve / own-character palette maps.
        float2 pixel = shadow.xy * 512 - .5;
        int2 a = (int2)floor(pixel);
        float2 blend = frac(pixel);
        int2 p0 = clamp(a, 0, 511), p1 = clamp(a + 1, 0, 511);
        float depth = shadow.z - (encoded.w > .5 ? .0000001 : .00002);
        half v00 = step(depth, LOAD_TEXTURE2D_ARRAY(_FaceSceneDepth,p0,index).r);
        half v10 = step(depth, LOAD_TEXTURE2D_ARRAY(_FaceSceneDepth,int2(p1.x,p0.y),index).r);
        half v01 = step(depth, LOAD_TEXTURE2D_ARRAY(_FaceSceneDepth,int2(p0.x,p1.y),index).r);
        half v11 = step(depth, LOAD_TEXTURE2D_ARRAY(_FaceSceneDepth,p1,index).r);
        half visible = lerp(lerp(v00,v10,blend.x),lerp(v01,v11,blend.x),blend.y);
        light.shadowAttenuation = lerp(1.0h,visible,saturate(_FaceHairShadowWeights[index]));
        break;
    }
    return light;
}

// Body skin (3) and clothes (1) take the toon ramp's binary boundary from the
// shadow map, so both need its hard edge. Face uses the SDF, hair its own map.
bool SourceBodyHardShadows()
{
    return _StudioSurfaceLighting.x > .5h && _SourceCharacterShading > .5h &&
        (_SourceCharacterModel == 3 || _SourceCharacterModel == 1);
}

// A single comparison snaps the edge to the shadow texel grid, which reads as
// torn, stair-stepped blocks on curved cloth. A 5x5 tent locates the edge with
// sub-texel precision; thresholding it at its midpoint keeps the edge where the
// caster is and restores a toon-hard boundary. URP's wide 7x7 soft filter is not
// used: thresholding that shifts corners and erases narrow shadows.
half SourceHardShadowCoverage(TEXTURE2D_SHADOW_PARAM(shadowMap, shadowSampler), float3 coord, float4 mapSize)
{
    float weights[9];
    float2 uvs[9];
    SampleShadow_ComputeSamples_Tent_Filter_5x5(float, mapSize, coord, weights, uvs);
    half coverage = 0;
    [unroll] for (int i = 0; i < 9; i++)
        coverage += weights[i] * SAMPLE_TEXTURE2D_SHADOW(shadowMap, shadowSampler, float3(uvs[i], coord.z));
    // About half a texel of antialiasing; derivatives are unsafe in the light loop.
    return smoothstep(.35h, .65h, coverage);
}

half SourceBodyMainShadow(float3 positionWS, half3 normalWS, half3 lightDirection, half fallback)
{
    // Body skin and clothes only. Do not change the light itself, which would
    // also change the face, hair and room.
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && !defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    if (SourceBodyHardShadows())
    {
        #ifdef _MAIN_LIGHT_SHADOWS_CASCADE
        half cascade = ComputeCascadeIndex(positionWS);
        #else
        half cascade = 0;
        #endif
        float texel = SourceShadowTexelWorld(_MainLightWorldToShadow[cascade], positionWS, _MainLightShadowmapSize.x);
        float4 shadow = TransformWorldToShadowCoord(SourceShadowReceiver(positionWS, normalWS, lightDirection, texel));
        if (BEYOND_SHADOW_FAR(shadow)) return 1;
        half coverage = SourceHardShadowCoverage(TEXTURE2D_SHADOW_ARGS(_MainLightShadowmapTexture,
            sampler_LinearClampCompare), shadow.xyz, _MainLightShadowmapSize);
        return LerpWhiteTo(coverage, GetMainLightShadowParams().x);
    }
    #endif
    return fallback;
}

half SourceBodyAdditionalShadow(uint index, float3 positionWS, half3 normalWS, Light light)
{
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
    if (SourceBodyHardShadows())
    {
        #if USE_CLUSTER_LIGHT_LOOP
        int lightIndex = index;
        #else
        int lightIndex = GetPerObjectLightIndex(index);
        #endif
        half4 parameters = GetAdditionalLightShadowParams(lightIndex);
        int slice = parameters.w;
        if (slice < 0) return 1;
        if (parameters.z > .5h) slice += CubeMapFaceID(-light.direction);
        #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4x4 worldToShadow = _AdditionalLightsWorldToShadow_SSBO[slice];
        #else
        float4x4 worldToShadow = _AdditionalLightsWorldToShadow[slice];
        #endif
        float texel = SourceShadowTexelWorld(worldToShadow, positionWS, _AdditionalShadowmapSize.x);
        float4 shadow = mul(worldToShadow, float4(SourceShadowReceiver(positionWS, normalWS, light.direction, texel), 1));
        shadow.xyz /= shadow.w;
        if (BEYOND_SHADOW_FAR(shadow)) return 1;
        half coverage = SourceHardShadowCoverage(TEXTURE2D_SHADOW_ARGS(_AdditionalLightsShadowmapTexture,
            sampler_LinearClampCompare), shadow.xyz, _AdditionalShadowmapSize);
        // The source character path supplies a white baked shadow mask.
        return lerp(LerpWhiteTo(coverage, parameters.x), 1, GetAdditionalLightShadowFade(positionWS));
    }
    #endif
    return light.shadowAttenuation;
}

half3 SourceProjectedSkinSurface(half3 surface, half3 shadowSurface, half visibility)
{
    // Coverage selects the skin's colored shadow endpoint. Merge with an
    // existing dark band instead of multiplying a second shadow into it.
    return lerp(surface, min(surface, shadowSurface), 1 - visibility);
}

half3 EvaluateSourceBodyClothes(Varyings input, half3 albedo, half3 normalWS,
    Light light, bool frontFace, half3 environment)
{
    bool body = _SourceCharacterModel > 2.5h;
    half4 detail = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask,
        input.rawUV * _DetailMask_ST.xy + _DetailMask_ST.zw);
    half4 lineMask = SAMPLE_TEXTURE2D(_LineMask, sampler_LineMask,
        input.rawUV * _LineMask_ST.xy + _LineMask_ST.zw);
    half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half noH = dot(normalWS, SafeNormalize(viewWS + light.direction));
    half noV = saturate(dot(normalWS, viewWS));
    half ramp = SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp,
        half2(saturate(dot(normalWS, light.direction)), .5h)).r;
    half curveShadow;
    ramp = SourceFaceCurveRamp(input, light, ramp, curveShadow);
    half hairVisibility = SourceFaceHairVisibility(input, light);
    half localShadowVisibility = min(hairVisibility, SourceClothesSkinVisibility(input, light));
    half3 ambient = saturate(lerp(_SourceAmbientShadow.rgb, 1.0h, _SourceAmbientShadow.a));
    half3 hsv = SourceRgbToHsv(albedo * (body ? 1.0h : _SourceShadowColor.rgb));
    half3 shadowChroma = SourceHsvToRgb(half3(hsv.x, hsv.y * (body ? .66h : .5h), 1));
    half3 shadow = SourceOverlay(shadowChroma * .9h, ambient);
    // The source ambient overlay can lift the dark endpoint almost to white,
    // hiding even a binary ramp on pale skin or clothes. Both use the same
    // palette limit in the experiment. Clothes retain their source material's
    // shadow tint instead of borrowing the skin palette or the generic fallback.
    if (_StudioSurfaceLighting.x > .5h &&
        (_SourceCharacterModel == 3 || _SourceCharacterModel == 1))
    {
        half3 palette = body ? _ShadowColor.rgb : saturate(_SourceShadowColor.rgb);
        shadow = min(shadow, palette);
    }
    half lineBlue = body ? lineMask.b * _DetailNormalMapScale : lineMask.b;
    half extend = 1 - 1.2h * _SourceShadowExtend * (1 - min(1 - lineBlue, 1 - detail.g));
    if (!body)
    {
        half detailRed = lerp(lineMask.r, detail.r, _SourceDetailRLineR);
        half alternate = SAMPLE_TEXTURE2D(_AnotherRamp, sampler_SourceRamp,
            abs(noH) * _AnotherRamp_ST.xy + _AnotherRamp_ST.zw).r;
        ramp = lerp(ramp, alternate, detailRed);
        shadow = saturate(shadow * max(1 - detailRed, 1 - _SourceShadowExtendAnother));
    }
    // Do not clamp extend: the source deliberately allows values below zero.
    half visibility = body ? saturate(2 * light.shadowAttenuation - 1) : light.shadowAttenuation;
    half weight = ramp * extend * saturate(visibility * light.distanceAttenuation);
    float2 shiftedUV = input.rawUV + .8 * (_SourceSpecularHeight - 1) *
        float2(dot(input.tangentWS, viewWS), dot(input.bitangentWS, viewWS));
    half shiftedRed = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask,
        shiftedUV * _DetailMask_ST.xy + _DetailMask_ST.zw).r;
    half power = _SourceSpecularPower;
    half analyticSpecular = saturate(pow(saturate(noH), max(256 * power, 1e-4h)) * power * _SourceSpecularColor.a);
    half specLuma = dot(_SourceSpecularColor.rgb, half3(.3h, .59h, .11h));
    half3 lit;
    half3 nonTextureLit;
    if (body)
    {
        half x = saturate(shiftedRed * (5.0h / 3));
        half cube = x * x * x;
        half strength = max(detail.a * power, (1 - detail.a) * _SpecularPowerNail);
        half highlight = min(min(min((x - cube) * strength, specLuma), weight), detail.a);
        half3 specularTint = SourceHsvToRgb(half3(hsv.x - .08h, .4h, .97h));
        lit = albedo * (1 + highlight * (specularTint - 1)) + cube * strength * _SourceSpecularColor.rgb;
        nonTextureLit = albedo + analyticSpecular * _SourceSpecularColor.rgb;
    }
    else
    {
        half square = saturate(shiftedRed * shiftedRed);
        half highlight = min(min(min(saturate(shiftedRed - square), power), weight), specLuma);
        half texturedSpecular = min(max(detail.a, _SpecularPowerNail), min(square, max(1 - detail.a, power)));
        texturedSpecular += analyticSpecular * (1 - detail.r);
        lit = albedo * (1 + highlight * (2 * shadow - 2)) + texturedSpecular * _SourceSpecularColor.rgb;
        nonTextureLit = albedo + analyticSpecular * (1 - detail.r) * _SourceSpecularColor.rgb;
    }
    #if STUDIO_CHARACTER_DEBUG == 7
    lit = albedo; nonTextureLit = albedo;
    #endif
    lit = saturate(lerp(lit, nonTextureLit, _SourceNotUseTextureSpecular));
    half3 color = lerp(lit * shadow, lit, weight);
    if (!body)
    {
        half rim = clamp(pow(1 - noV, 1 + 9 * _SourceRimPower) *
            ((1 - detail.b) * (25.0h / 9) - (16.0h / 9)), 0, .6h);
        #if STUDIO_CHARACTER_DEBUG == 7
        rim = 0;
        #endif
        color = saturate(color + rim * _SourceSpecularColor.rgb * _SourceRimStrength * (1 - detail.g));
    }
    half3 direct = .4h + .6h * light.color;
    half3 illumination = max(direct, _SourceAmbientShadow.rgb);
    if (_StudioSurfaceLighting.x > .5h)
    {
        // The source's fixed fill is an artistic floor, not scene irradiance.
        // The experiment replaces it with SH / local bounce, so blocked lights
        // cannot keep skin illuminated independently of the room.
        direct = .6h * light.color * _StudioSurfaceLighting.y *
            light.distanceAttenuation * light.shadowAttenuation;
        illumination = direct + environment;
        // Hair and clothes extend the skin's toon shadow, not a second
        // physical blackout of its key light. Use the same dark ramp endpoint
        // and skin palette as the curve shadow. Taking the minimum prevents
        // local occlusion from darkening already shaded skin a second time.
        half3 directSurface = color;
        if (localShadowVisibility < 1)
        {
            half darkRamp = SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp, half2(0, .5h)).r;
            half localWeight = min(weight, darkRamp * extend * saturate(visibility * light.distanceAttenuation));
            half3 localSurface = lerp(lit * shadow, lit, localWeight);
            directSurface = SourceProjectedSkinSurface(color, localSurface, localShadowVisibility);
        }
        // Body skin shares the face's projected-palette treatment for SH and
        // local bounce. Their irradiance stays intact; only the receiving skin
        // color changes. Otherwise ambient light erases garment silhouettes.
        half3 environmentSurface = _SourceCharacterModel == 3 ? directSurface : color;
        half faceVisibility = min(1 - curveShadow, hairVisibility);
        if (_SourceCharacterModel == 4 && faceVisibility < 1)
        {
            // The source overlay can be almost white. Both the approved face
            // curve and local hair projection need the same visible endpoint,
            // including ambient illumination. Replace the curve's existing
            // endpoint by its difference, preserving its coverage and strength.
            // Then take the darker surface rather than stacking the shadows.
            half3 contact = lit * min(shadow, _ShadowColor.rgb);
            half3 curveSurface = max(0, color - (lit * shadow - contact) * curveShadow);
            half3 hairSurface = SourceProjectedSkinSurface(color, contact, hairVisibility);
            half3 faceSurface = min(curveSurface, hairSurface);
            directSurface = min(directSurface, faceSurface);
            environmentSurface = faceSurface;
        }
        color = directSurface * direct + environmentSurface * environment;
    }
    else color *= illumination;
    // Both source programs apply the detail/line shadow once more after
    // direct lighting. This also applies when body line textures are disabled.
    color *= lerp(shadow, 1.0h, extend);
    if (!body && !frontFace) color *= .66h;
    half3 lineColor;
    half lineWeight;
    if (body)
    {
        half lineFactor = .5h * (1 - detail.g) + .5h;
        lineColor = SourceLineBlend(albedo * _SourceLineColor.rgb * shadow * lineFactor, _SourceLineColor.a) *
            (_StudioSurfaceLighting.x > .5h ? illumination : direct);
        lineWeight = min(.5h * (1 - lineMask.r * _DetailNormalMapScale) + .5h,
            pow(1 - .8h * _SourceLineWidth, lineMask.g));
        lineWeight = lerp(1.0h, lineWeight, _linetexon);
    }
    else
    {
        lineColor = SourceLineBlend(albedo * shadow * .5h, _SourceLineColor.a) *
            (_StudioSurfaceLighting.x > .5h ? illumination : direct);
        half detailLine = lerp(lineMask.g, detail.b, _SourceDetailBLineG);
        lineWeight = pow(.9h - .89h * _SourceLineWidth, detailLine);
    }
    return lerp(lineColor, color, lineWeight);
}
