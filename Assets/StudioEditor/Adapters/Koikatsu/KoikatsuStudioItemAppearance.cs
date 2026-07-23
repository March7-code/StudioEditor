using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuStudioItemAppearance
    {
        private const string ShaderResourcePath =
            "Shaders/KoikatsuStudioItem";

        public static void Apply(
            GameObject instance,
            KoikatsuSceneItem item,
            KoikatsuStudioListEntry entry,
            KoikatsuStudioItemRendererMap rendererMap,
            KoikatsuStudioPatternTextures patterns)
        {
            if (instance == null || item == null || entry == null)
            {
                return;
            }

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                var role = ResolveRole(renderer, entry, rendererMap);
                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    ApplyMaterial(
                        material,
                        role,
                        item,
                        entry,
                        materialIndex,
                        patterns);
                }
            }

            if (entry.UseColors.Count != 0 && entry.UseColors[0])
            {
                var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (var index = 0; index < particles.Length; index++)
                {
                    var main = particles[index].main;
                    main.startColor = item.Colors[0];
                }
            }
        }

        private static KoikatsuStudioRendererRole ResolveRole(
            Renderer renderer,
            KoikatsuStudioListEntry entry,
            KoikatsuStudioItemRendererMap map)
        {
            if (map != null && map.TryGetRole(renderer, out var role))
            {
                return role;
            }

            var name = renderer.name ?? string.Empty;
            if (entry.IsGlass ||
                name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return KoikatsuStudioRendererRole.Glass;
            }

            if (name.StartsWith("oa_", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return KoikatsuStudioRendererRole.Alpha;
            }

            return KoikatsuStudioRendererRole.Normal;
        }

        private static void ApplyMaterial(
            Material material,
            KoikatsuStudioRendererRole role,
            KoikatsuSceneItem item,
            KoikatsuStudioListEntry entry,
            int materialIndex,
            KoikatsuStudioPatternTextures patterns)
        {
            switch (role)
            {
                case KoikatsuStudioRendererRole.Alpha:
                    SetAlpha(material, item.Alpha);
                    break;
                case KoikatsuStudioRendererRole.Glass:
                case KoikatsuStudioRendererRole.AccessoryAlpha:
                    SetColor(material, item.Colors[7]);
                    SetTransparent(material);
                    break;
                case KoikatsuStudioRendererRole.Panel:
                    SetColor(material, item.Colors[0]);
                    break;
                case KoikatsuStudioRendererRole.AccessoryNormal:
                    var channel = SelectColorChannel(
                        material.name,
                        materialIndex,
                        entry.UseColors);
                    if (channel >= 0 && channel < item.Colors.Length)
                    {
                        SetColor(material, item.Colors[channel]);
                    }
                    break;
                case KoikatsuStudioRendererRole.Normal:
                    if (!ConfigureMultiChannel(
                            material,
                            item,
                            entry,
                            patterns))
                    {
                        channel = SelectColorChannel(
                            material.name,
                            materialIndex,
                            entry.UseColors);
                        if (channel >= 0 && channel < item.Colors.Length)
                        {
                            SetColor(material, item.Colors[channel]);
                        }
                    }
                    break;
            }

            if (entry.IsEmission && item.EmissionPower > 0f)
            {
                var emission = item.EmissionColor * item.EmissionPower;
                emission.a = 1f;
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", emission);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
        }

        private static bool ConfigureMultiChannel(
            Material material,
            KoikatsuSceneItem item,
            KoikatsuStudioListEntry entry,
            KoikatsuStudioPatternTextures patterns)
        {
            var enabled = Vector4.zero;
            for (var channel = 0; channel < 3; channel++)
            {
                enabled[channel] = IsEnabled(entry.UseColors, channel) ||
                                   IsEnabled(entry.UsePatterns, channel)
                    ? 1f
                    : 0f;
            }

            if (enabled.x + enabled.y + enabled.z <= 0f)
            {
                return false;
            }

            var shader = Resources.Load<Shader>(ShaderResourcePath) ??
                         Shader.Find(
                             "StudioEditor/KoikatsuStudioItem");
            if (shader == null)
            {
                Debug.LogWarning(
                    "The Koikatsu Studio item shader could not be loaded.");
                return false;
            }

            var mainTexture = material.mainTexture;
            var mainScale = material.mainTextureScale;
            var mainOffset = material.mainTextureOffset;
            var alphaClip = GetFloat(material, "_AlphaClip", 0f);
            var cutoff = GetFloat(material, "_Cutoff", 0.5f);
            var sourceBlend = GetFloat(
                material,
                "_SrcBlend",
                (float)BlendMode.One);
            var destinationBlend = GetFloat(
                material,
                "_DstBlend",
                (float)BlendMode.Zero);
            var zWrite = GetFloat(material, "_ZWrite", 1f);
            var cull = GetFloat(material, "_Cull", (float)CullMode.Back);
            var surface = GetFloat(material, "_Surface", 0f);
            var renderType = material.GetTag(
                "RenderType",
                false,
                "Opaque");
            var renderQueue = material.renderQueue;
            var alphaTest = material.IsKeywordEnabled("_ALPHATEST_ON");
            var transparent = material.IsKeywordEnabled(
                "_SURFACE_TYPE_TRANSPARENT");
            material.shader = shader;
            material.SetTexture(
                "_MainTex",
                mainTexture != null ? mainTexture : Texture2D.whiteTexture);
            material.SetTextureScale("_MainTex", mainScale);
            material.SetTextureOffset("_MainTex", mainOffset);
            material.SetVector("_ChannelEnabled", enabled);
            material.SetFloat("_AlphaClip", alphaClip);
            material.SetFloat("_Cutoff", cutoff);
            material.SetFloat("_SrcBlend", sourceBlend);
            material.SetFloat("_DstBlend", destinationBlend);
            material.SetFloat("_ZWrite", zWrite);
            material.SetFloat("_Cull", cull);
            material.SetFloat("_Surface", surface);
            material.SetOverrideTag("RenderType", renderType);
            material.renderQueue = renderQueue;
            if (alphaTest)
            {
                material.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
            }

            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            for (var channel = 0; channel < 3; channel++)
            {
                var number = channel + 1;
                var baseColor = channel < item.Colors.Length
                    ? item.Colors[channel]
                    : Color.white;
                var patternColorIndex = channel + 3;
                var patternColor = patternColorIndex < item.Colors.Length
                    ? item.Colors[patternColorIndex]
                    : Color.white;
                var pattern = item.Patterns != null &&
                              channel < item.Patterns.Length
                    ? item.Patterns[channel]
                    : null;
                var texture = patterns?[channel];

                material.SetColor($"_ChannelColor{number}", baseColor);
                material.SetColor(
                    $"_PatternColor{number}",
                    patternColor);
                material.SetTexture(
                    $"_Pattern{number}",
                    texture != null ? texture : Texture2D.whiteTexture);
                material.SetVector(
                    $"_PatternUV{number}",
                    pattern?.UV ?? new Vector4(0f, 0f, 1f, 1f));
                material.SetFloat(
                    $"_PatternRotation{number}",
                    pattern?.Rotation ?? 0f);
                material.SetFloat(
                    $"_PatternClamp{number}",
                    pattern != null && pattern.Clamp ? 1f : 0f);
                material.SetFloat(
                    $"_HasPattern{number}",
                    texture != null && IsEnabled(entry.UsePatterns, channel)
                        ? 1f
                        : 0f);
            }

            return true;
        }

        private static float GetFloat(
            Material material,
            string property,
            float fallback)
        {
            return material.HasProperty(property)
                ? material.GetFloat(property)
                : fallback;
        }

        private static bool IsEnabled(
            System.Collections.Generic.IReadOnlyList<bool> values,
            int index)
        {
            return values != null && index >= 0 && index < values.Count &&
                   values[index];
        }

        private static int SelectColorChannel(
            string materialName,
            int materialIndex,
            System.Collections.Generic.IReadOnlyList<bool> enabled)
        {
            materialName = (materialName ?? string.Empty).ToLowerInvariant();
            if (enabled.Count > 2 && enabled[2] &&
                (materialName.Contains("color3") ||
                 materialName.Contains("_03")))
            {
                return 2;
            }

            if (enabled.Count > 1 && enabled[1] &&
                (materialName.Contains("color2") ||
                 materialName.Contains("_02")))
            {
                return 1;
            }

            if (materialIndex < enabled.Count && enabled[materialIndex])
            {
                return materialIndex;
            }

            for (var index = 0; index < enabled.Count && index < 3; index++)
            {
                if (enabled[index])
                {
                    return index;
                }
            }

            return -1;
        }

        private static void SetAlpha(Material material, float alpha)
        {
            var color = material.color;
            color.a = Mathf.Clamp01(alpha);
            SetColor(material, color);
            if (color.a < 0.999f)
            {
                SetTransparent(material);
            }
        }

        private static void SetColor(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }

        private static void SetTransparent(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
