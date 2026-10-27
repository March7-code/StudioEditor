// Reconstructed from main_hair / main_hair_front / main_hair_low FORWARD DXBC.
half3 SourceHairNormal(Varyings input, bool frontFace)
{
    half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap,
        input.rawUV * _NormalMap_ST.xy + _NormalMap_ST.zw));
    half3x3 frame = half3x3(normalize(input.tangentWS), normalize(input.bitangentWS),
        normalize(input.normalWS) * (frontFace ? 1 : -1));
    return normalize(TransformTangentToWorld(normalTS, frame));
}

half3 EvaluateSourceHair(Varyings input, half3 albedo, half3 normalWS, Light light,
    bool frontFace, half3 environment)
{
    half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half noL = dot(normalWS, light.direction);
    half noV = dot(normalWS, view);
    half ramp = SAMPLE_TEXTURE2D(_SourceRamp, sampler_SourceRamp, half2(noL, .5h)).r;
    half alternate = _SourceHairLow > .5h ? ramp : SAMPLE_TEXTURE2D(_AnotherRamp,
        sampler_SourceRamp, noV * _AnotherRamp_ST.xy + _AnotherRamp_ST.zw).r;
    half4 detail = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask,
        input.rawUV * _DetailMask_ST.xy + _DetailMask_ST.zw);
    // Hair gloss is a strand pattern in the mesh's second UV set. The two
    // vertically offset samples and view parallax create the moving highlight.
    float glossOffset = _SourceHairFront > .5h ? .02 * noL + .99 : .005 * noL;
    float2 glossUv = input.hairUV + float2(glossOffset,
        (_SourceSpecularHeight - 1) * dot(normalize(input.bitangentWS), view));
    half gloss = SAMPLE_TEXTURE2D(_HairGloss, sampler_HairGloss,
        glossUv * _HairGloss_ST.xy + _HairGloss_ST.zw).r;
    half shifted = SAMPLE_TEXTURE2D(_HairGloss, sampler_HairGloss,
        (glossUv + float2(0, .008)) * _HairGloss_ST.xy + _HairGloss_ST.zw).r;
    half difference = .5h * (shifted - gloss);
    half3 ambient = saturate(_SourceAmbientShadow.a > 0
        ? lerp(_SourceAmbientShadow.rgb, 1, _SourceAmbientShadow.a)
        : _SourceAmbientShadow.rgb * (1 + _SourceAmbientShadow.a));
    half3 shadow = SourceOverlay(_SourceShadowColor.rgb, ambient);
    half3 lit = albedo * lerp(1, shadow, difference);
    half rampWeight = saturate(min(ramp, alternate));
    half weight = rampWeight * (1 - detail.g) * light.distanceAttenuation * light.shadowAttenuation;
    half3 color = lerp(lit * shadow, lit, weight) + gloss * (.5h * ramp + .5h);
    if (_SourceHairLow < .5h)
    {
        half hue = SourceRgbToHsv(_SourceAmbientShadow.rgb).x;
        half rim = saturate(pow(1 - saturate(noV), 1 + 9 * _SourceRimPower) * 5 - 1.5h);
        half3 rimColor = min(SourceHsvToRgb(half3(hue, .5h, .66h)) * rim, .5h);
        color += rimColor * (1 - detail.g) * _SourceRimStrength * (1 - detail.b);
    }
    color = saturate(color);
    half extend = max(max(rampWeight, 1 - detail.g), 1 - _SourceShadowExtend);
    color *= lerp(shadow, 1, extend) * (frontFace ? 1 : .66h);
    half3 illumination = max(.4h + .6h * light.color, _SourceAmbientShadow.rgb);
    if (_StudioSurfaceLighting.x > .5h)
        illumination = .6h * light.color * _StudioSurfaceLighting.y *
            light.distanceAttenuation * light.shadowAttenuation + environment;
    return color * illumination;
}
