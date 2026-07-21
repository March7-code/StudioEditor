using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuMaterialConverter
    {
        private const string CharacterShaderResourcePath =
            "Shaders/KoikatsuCharacter";

        private static readonly Color SkinColor =
            new Color(1f, 0.78f, 0.70f, 1f);
        private static readonly Color DetailColor =
            new Color(0.16f, 0.07f, 0.05f, 1f);

        private enum KoikatsuCharacterMaterialStyle
        {
            Skin,
            Face,
            Hair,
            Clothes,
            Accessory,
        }

        public static void Convert(
            GameObject model,
            ICollection<Material> runtimeMaterials)
        {
            Convert(
                model,
                runtimeMaterials,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        public static void ConvertHair(
            GameObject model,
            KoikatsuCardHairPart hair,
            ICollection<Material> runtimeMaterials)
        {
            if (hair == null)
            {
                throw new ArgumentNullException(nameof(hair));
            }

            Convert(
                model,
                runtimeMaterials,
                hair,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                KoikatsuCharacterMaterialStyle.Hair);
        }

        public static void ConvertClothes(
            GameObject model,
            KoikatsuCardClothesPart clothes,
            KoikatsuTextureSet textures,
            KoikatsuBakedClothesTextures bakedTextures,
            KoikatsuClothesRendererMap rendererMap,
            ICollection<Material> runtimeMaterials)
        {
            if (clothes == null)
            {
                throw new ArgumentNullException(nameof(clothes));
            }

            Convert(
                model,
                runtimeMaterials,
                null,
                clothes,
                textures,
                null,
                null,
                null,
                rendererMap,
                bakedTextures,
                KoikatsuCharacterMaterialStyle.Clothes);
        }

        public static void ConvertAccessory(
            GameObject model,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair,
            ICollection<Material> runtimeMaterials)
        {
            if (accessory == null)
            {
                throw new ArgumentNullException(nameof(accessory));
            }

            ConvertAccessoryRenderers(
                model,
                accessory,
                hair,
                runtimeMaterials,
                KoikatsuCharacterMaterialStyle.Accessory);
        }

        public static void ApplyMaterialEditorMainTextures(
            GameObject model,
            KoikatsuTextureLoader textureLoader)
        {
            if (model == null || textureLoader == null)
            {
                return;
            }

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var materials = renderers[rendererIndex].sharedMaterials;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    var texture = textureLoader
                        .LoadMaterialEditorCharacterTexture(
                            material.name,
                            "MainTex");
                    if (texture != null)
                    {
                        SetMainTexture(material, texture);
                    }
                }
            }
        }

        public static void ConvertSkin(
            GameObject model,
            Color skinColor,
            Texture2D skinTexture,
            string textureRendererMarker,
            bool alphaClip,
            ICollection<Material> runtimeMaterials)
        {
            Convert(
                model,
                runtimeMaterials,
                null,
                null,
                null,
                skinColor,
                new SkinTextureOverride(
                    skinTexture,
                    textureRendererMarker,
                    alphaClip),
                null,
                null,
                null,
                KoikatsuCharacterMaterialStyle.Skin);
        }

        public static void ConvertFace(
            GameObject model,
            Color skinColor,
            Texture2D skinTexture,
            KoikatsuBakedEyeTextures eyeTextures,
            KoikatsuCardFaceAppearance appearance,
            KoikatsuFaceTextures faceTextures,
            ICollection<Material> runtimeMaterials)
        {
            Convert(
                model,
                runtimeMaterials,
                null,
                null,
                null,
                skinColor,
                new SkinTextureOverride(skinTexture, "cf_o_face"),
                eyeTextures,
                null,
                null,
                KoikatsuCharacterMaterialStyle.Face);
            ApplyFaceTextures(
                model,
                skinColor,
                appearance,
                faceTextures);
        }

        private static void ApplyFaceTextures(
            GameObject model,
            Color skinColor,
            KoikatsuCardFaceAppearance appearance,
            KoikatsuFaceTextures textures)
        {
            if (appearance == null || textures == null)
            {
                return;
            }

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                switch (renderer.name)
                {
                    case "cf_O_mayuge":
                        ApplyFaceTexture(
                            renderer,
                            new[] { textures.Eyebrow },
                            new[] { appearance.EyebrowColor });
                        break;
                    case "cf_O_noseline":
                        ApplyFaceTexture(
                            renderer,
                            new[] { textures.Nose },
                            new[] { Color.white });
                        break;
                    case "cf_O_eyeline":
                        ApplyFaceTexture(
                            renderer,
                            new[]
                            {
                                textures.EyelineUp,
                                textures.EyelineShadow,
                            },
                            new[]
                            {
                                appearance.EyelineColor,
                                skinColor,
                            });
                        break;
                    case "cf_O_eyeline_low":
                        ApplyFaceTexture(
                            renderer,
                            new[] { textures.EyelineDown },
                            new[] { appearance.EyelineColor });
                        break;
                }
            }
        }

        private static void ApplyFaceTexture(
            Renderer renderer,
            IReadOnlyList<Texture2D> textures,
            IReadOnlyList<Color> colors)
        {
            var materials = renderer.sharedMaterials;
            var hasTexture = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var textureIndex = Math.Min(index, textures.Count - 1);
                var texture = textures[textureIndex];
                if (texture == null || materials[index] == null)
                {
                    continue;
                }

                hasTexture = true;
                var color = colors[Math.Min(index, colors.Count - 1)];
                materials[index].color = color;
                if (materials[index].HasProperty("_BaseColor"))
                {
                    materials[index].SetColor("_BaseColor", color);
                }

                SetMainTexture(materials[index], texture);
                ConfigureTransparent(materials[index]);
            }

            renderer.enabled = hasTexture;
        }

        private static void Convert(
            GameObject model,
            ICollection<Material> runtimeMaterials,
            KoikatsuCardHairPart hair,
            KoikatsuCardClothesPart clothes,
            KoikatsuTextureSet textures,
            Color? skinColor,
            SkinTextureOverride skinTexture,
            KoikatsuBakedEyeTextures eyeTextures,
            KoikatsuClothesRendererMap clothesRendererMap,
            KoikatsuBakedClothesTextures bakedClothesTextures,
            KoikatsuCharacterMaterialStyle? characterStyle)
        {
            var shader = ResolveShader(characterStyle);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No compatible Unity shader is available for Koikatsu materials.");
            }

            var renderers = GetRenderersForRuntimeLighting(model);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (ShouldHide(renderer.name))
                {
                    renderer.enabled = false;
                    continue;
                }

                var sourceMaterials = renderer.sharedMaterials;
                var materialCount = Math.Max(sourceMaterials.Length, 1);
                var convertedMaterials = new Material[materialCount];
                var hasVisibleEyeLayer = false;
                for (var materialIndex = 0;
                     materialIndex < materialCount;
                     materialIndex++)
                {
                    var source = materialIndex < sourceMaterials.Length
                        ? sourceMaterials[materialIndex]
                        : null;
                    var materialKey = GetMaterialKey(
                        renderer.name,
                        source?.name);
                    KoikatsuBakedEyeTexture bakedIris = null;
                    var hasBakedIris = eyeTextures != null &&
                                       eyeTextures.TryGetIris(
                                           materialKey,
                                           out bakedIris);
                    var hasBakedWhite = eyeTextures?.White != null &&
                                        materialKey.Contains("sirome");
                    hasVisibleEyeLayer |= hasBakedIris || hasBakedWhite;
                    var clothesTextureSlot = KoikatsuClothesTextureSlot.None;
                    var hasExactClothesMap = clothesRendererMap != null;
                    clothesRendererMap?.TryGet(
                        renderer,
                        out clothesTextureSlot);
                    var sourceMainTexture = GetMainTexture(source);
                    var bakedClothesTexture =
                        bakedClothesTextures?.Select(
                            clothesTextureSlot,
                            !hasExactClothesMap,
                            sourceMainTexture,
                            textures,
                            renderer.name,
                            source?.name);
                    var hasBakedClothes = bakedClothesTexture != null;
                    var materialColor = hasBakedIris || hasBakedWhite ||
                                        hasBakedClothes
                        ? Color.white
                        : SelectColor(
                            renderer.name,
                            source?.name,
                            hair,
                            clothes,
                            skinColor,
                            source != null && source.HasProperty("_Color")
                                ? source.GetColor("_Color")
                                : Color.white);
                    var converted = new Material(shader)
                    {
                        name = (source != null ? source.name : renderer.name) +
                               " (Koikatsu Preview)",
                        color = materialColor,
                    };
                    if (converted.HasProperty("_BaseColor"))
                    {
                        converted.SetColor("_BaseColor", materialColor);
                    }

                    ApplySourceRendering(source, converted);
                    if (characterStyle.HasValue)
                    {
                        ConfigureCharacterMaterial(
                            source,
                            converted,
                            characterStyle.Value,
                            materialKey,
                            materialColor,
                            hair?.OutlineColor);
                    }

                    var mainTexture = hasBakedIris
                        ? bakedIris.Texture
                        : hasBakedWhite
                            ? eyeTextures.White
                            : hasBakedClothes
                                ? bakedClothesTexture
                            : SelectTexture(
                                renderer.name,
                                source?.name,
                                source,
                                textures,
                                skinTexture,
                                clothesTextureSlot,
                                !hasExactClothesMap);
                    if (mainTexture != null)
                    {
                        SetMainTexture(converted, mainTexture);
                        CopyMainTextureTransform(source, converted);
                    }

                    if (skinTexture != null && skinTexture.AlphaClip &&
                        materialKey.Contains(skinTexture.RendererMarker))
                    {
                        ConfigureCutout(converted, 0.5f);
                    }

                    if (hasBakedWhite)
                    {
                        ConfigureTransparent(
                            converted,
                            (int)UnityEngine.Rendering.RenderQueue.Transparent);
                    }
                    else if (hasBakedIris)
                    {
                        SetMainTextureTransform(
                            converted,
                            bakedIris.Scale,
                            bakedIris.Offset);
                        ConfigureTransparent(
                            converted,
                            (int)UnityEngine.Rendering.RenderQueue.Transparent + 1);
                    }

                    convertedMaterials[materialIndex] = converted;
                    runtimeMaterials.Add(converted);
                }

                renderer.sharedMaterials = convertedMaterials;
                if (hasVisibleEyeLayer)
                {
                    // EyeLookMaterialControll is unavailable on imported assets.
                    // Its normal runtime responsibility includes enabling these layers.
                    renderer.enabled = true;
                }
            }
        }

        private static string GetMaterialKey(
            string rendererName,
            string materialName)
        {
            return ((rendererName ?? string.Empty) + " " +
                    (materialName ?? string.Empty)).ToLowerInvariant();
        }

        private static void SetMainTexture(Material material, Texture texture)
        {
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
        }

        private static void SetMainTextureTransform(
            Material material,
            Vector2 scale,
            Vector2 offset)
        {
            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", scale);
                material.SetTextureOffset("_MainTex", offset);
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureOffset("_BaseMap", offset);
            }
        }

        private static void ConfigureTransparent(
            Material material,
            int renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat(
                    "_SrcBlend",
                    (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = renderQueue;
        }

        private static void ConvertAccessoryRenderers(
            GameObject model,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair,
            ICollection<Material> runtimeMaterials,
            KoikatsuCharacterMaterialStyle characterStyle)
        {
            var shader = ResolveShader(characterStyle);

            var renderers = GetRenderersForRuntimeLighting(model);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var sourceMaterials = renderer.sharedMaterials;
                var materialCount = Math.Max(sourceMaterials.Length, 1);
                var convertedMaterials = new Material[materialCount];
                var previewColor = SelectAccessoryColor(
                    renderer.name,
                    accessory,
                    hair);

                if (renderer.name.StartsWith("oa_", StringComparison.Ordinal) &&
                    previewColor.a <= 0f)
                {
                    renderer.gameObject.SetActive(false);
                }

                for (var materialIndex = 0;
                     materialIndex < materialCount;
                     materialIndex++)
                {
                var source = materialIndex < sourceMaterials.Length
                        ? sourceMaterials[materialIndex]
                        : null;
                    var converted = new Material(shader)
                    {
                        name = (source != null ? source.name : renderer.name) +
                               " (Koikatsu Accessory Preview)",
                        color = Opaque(previewColor),
                    };
                    ApplySourceRendering(source, converted);
                    ConfigureCharacterMaterial(
                        source,
                        converted,
                        characterStyle,
                        GetMaterialKey(renderer.name, source?.name),
                        Opaque(previewColor),
                        hair?.OutlineColor);
                    var mainTexture = GetMainTexture(source);
                    if (mainTexture != null)
                    {
                        SetMainTexture(converted, mainTexture);
                        CopyMainTextureTransform(source, converted);
                    }

                    convertedMaterials[materialIndex] = converted;
                    runtimeMaterials.Add(converted);
                }

                renderer.sharedMaterials = convertedMaterials;
            }
        }

        private static Shader ResolveShader(
            KoikatsuCharacterMaterialStyle? characterStyle)
        {
            Shader shader = null;
            if (characterStyle.HasValue)
            {
                shader = Resources.Load<Shader>(CharacterShaderResourcePath) ??
                         Shader.Find("BodyEditor/KoikatsuCharacter");
            }

            shader = shader ??
                     Shader.Find("Universal Render Pipeline/Lit") ??
                     Shader.Find("Standard") ??
                     Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No compatible Unity shader is available for Koikatsu materials.");
            }

            return shader;
        }

        private static void ConfigureCharacterMaterial(
            Material source,
            Material converted,
            KoikatsuCharacterMaterialStyle style,
            string materialKey,
            Color baseColor,
            Color? requestedOutlineColor)
        {
            var deepShadow = new Color(0.42f, 0.45f, 0.52f, 1f);
            var shadow = new Color(0.72f, 0.75f, 0.80f, 1f);
            var ambientStrength = 0.25f;
            var specularStrength = 0.25f;
            var specularPower = 40f;
            var rimStrength = 0.12f;
            var rimPower = 4f;
            var outlineWidth = 0.08f;

            switch (style)
            {
                case KoikatsuCharacterMaterialStyle.Skin:
                    deepShadow = new Color(0.58f, 0.46f, 0.50f, 1f);
                    shadow = new Color(0.82f, 0.70f, 0.72f, 1f);
                    ambientStrength = 0.32f;
                    specularStrength = 0.12f;
                    specularPower = 32f;
                    rimStrength = 0.08f;
                    outlineWidth = 0.06f;
                    break;
                case KoikatsuCharacterMaterialStyle.Face:
                    deepShadow = new Color(0.62f, 0.49f, 0.52f, 1f);
                    shadow = new Color(0.85f, 0.73f, 0.74f, 1f);
                    ambientStrength = 0.36f;
                    specularStrength = 0.08f;
                    specularPower = 28f;
                    rimStrength = 0.06f;
                    outlineWidth = 0.05f;
                    break;
                case KoikatsuCharacterMaterialStyle.Hair:
                    deepShadow = new Color(0.32f, 0.36f, 0.48f, 1f);
                    shadow = new Color(0.65f, 0.68f, 0.78f, 1f);
                    ambientStrength = 0.25f;
                    specularStrength = 0.55f;
                    specularPower = 64f;
                    rimStrength = 0.22f;
                    rimPower = 3f;
                    outlineWidth = 0.12f;
                    break;
                case KoikatsuCharacterMaterialStyle.Accessory:
                    specularStrength = 0.30f;
                    outlineWidth = 0.06f;
                    break;
            }

            SetColor(converted, "_DeepShadowColor", deepShadow);
            SetColor(converted, "_ShadowColor", shadow);
            SetVector(
                converted,
                "_BandThresholds",
                new Vector4(0.18f, 0.38f, 0.58f, 0.78f));
            SetFloat(converted, "_BandSoftness", 0.015f);
            SetFloat(converted, "_AmbientStrength", ambientStrength);
            SetFloat(converted, "_SpecularStrength", specularStrength);
            SetFloat(converted, "_SpecularPower", specularPower);
            SetFloat(converted, "_RimStrength", rimStrength);
            SetFloat(converted, "_RimPower", rimPower);

            var outlineColor = requestedOutlineColor ??
                               FindColor(
                                   source,
                                   "_LineColor",
                                   "_OutlineColor") ??
                               new Color(
                                   baseColor.r * 0.18f,
                                   baseColor.g * 0.18f,
                                   baseColor.b * 0.18f,
                                   1f);
            outlineColor.a = 1f;
            if (IsFaceDetail(materialKey) ||
                GetFloat(converted, "_Surface", 0f) > 0.5f)
            {
                outlineWidth = 0f;
            }

            SetColor(converted, "_OutlineColor", outlineColor);
            SetFloat(converted, "_OutlineWidth", outlineWidth);

            var normalMap = FindTexture(
                source,
                "_BumpMap",
                "_NormalMap",
                "_NormalTex");
            if (normalMap != null)
            {
                SetTexture(converted, "_NormalMap", normalMap);
                SetTexture(converted, "_BumpMap", normalMap);
                var normalStrength = Mathf.Clamp(
                    FindFloat(source, 1f, "_BumpScale", "_NormalScale"),
                    0f,
                    2f);
                SetFloat(
                    converted,
                    "_NormalStrength",
                    normalStrength);
                SetFloat(converted, "_BumpScale", normalStrength);
                converted.EnableKeyword("_NORMALMAP");
            }

            var styleMask = FindTexture(
                source,
                "_LightMap",
                "_LightMapTex",
                "_LightMapTexture",
                "_LightMapMask");
            if (styleMask != null)
            {
                SetTexture(converted, "_StyleMask", styleMask);
                SetFloat(converted, "_StyleMaskStrength", 1f);
            }

            var metallicMap = FindTexture(source, "_MetallicGlossMap");
            if (metallicMap != null)
            {
                SetTexture(converted, "_MetallicGlossMap", metallicMap);
                SetFloat(converted, "_MetallicMapStrength", 1f);
            }

            var specularMap = FindTexture(source, "_SpecGlossMap");
            if (specularMap != null)
            {
                SetTexture(converted, "_SpecGlossMap", specularMap);
                SetFloat(converted, "_SpecularMapStrength", 1f);
            }

            var ramp = FindTexture(
                source,
                "_ShadowRamp",
                "_Shadow_Ramp",
                "_RampTex",
                "_AnotherRamp");
            if (ramp != null)
            {
                SetTexture(converted, "_RampMap", ramp);
                SetFloat(converted, "_RampStrength", 1f);
            }

            SetFloat(
                converted,
                "_Metallic",
                Mathf.Clamp01(FindFloat(source, 0f, "_Metallic")));
            SetFloat(
                converted,
                "_Smoothness",
                Mathf.Clamp01(
                    FindFloat(
                        source,
                        style == KoikatsuCharacterMaterialStyle.Hair
                            ? 0.65f
                            : 0.35f,
                        "_Smoothness",
                        "_Glossiness",
                        "_GlossMapScale")));

            var specularColor = FindColor(source, "_SpecColor");
            if (specularColor.HasValue)
            {
                SetColor(converted, "_SpecularColor", specularColor.Value);
            }

            var emissionColor = FindColor(source, "_EmissionColor");
            var emissionEnabled = source != null &&
                (source.IsKeywordEnabled("_EMISSION") ||
                 (source.globalIlluminationFlags &
                  MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0);
            if (emissionEnabled && emissionColor.HasValue)
            {
                var emissionMap = FindTexture(source, "_EmissionMap");
                if (emissionMap != null)
                {
                    SetTexture(converted, "_EmissionMap", emissionMap);
                }

                SetColor(converted, "_EmissionColor", emissionColor.Value);
            }
        }

        private static bool IsFaceDetail(string materialKey)
        {
            return materialKey.Contains("hitomi") ||
                   materialKey.Contains("sirome") ||
                   materialKey.Contains("mayuge") ||
                   materialKey.Contains("eyeline") ||
                   materialKey.Contains("noseline") ||
                   materialKey.Contains("tooth") ||
                   materialKey.Contains("canine") ||
                   materialKey.Contains("tang") ||
                   materialKey.Contains("namida");
        }

        private static Texture FindTexture(
            Material material,
            params string[] properties)
        {
            if (material == null)
            {
                return null;
            }

            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (!material.HasProperty(property))
                {
                    continue;
                }

                var texture = material.GetTexture(property);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static Color? FindColor(
            Material material,
            params string[] properties)
        {
            if (material == null)
            {
                return null;
            }

            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (material.HasProperty(property))
                {
                    return material.GetColor(property);
                }
            }

            return null;
        }

        private static float FindFloat(
            Material material,
            float fallback,
            params string[] properties)
        {
            if (material == null)
            {
                return fallback;
            }

            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (material.HasProperty(property))
                {
                    return material.GetFloat(property);
                }
            }

            return fallback;
        }

        private static void SetTexture(
            Material material,
            string property,
            Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetColor(
            Material material,
            string property,
            Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        private static void SetVector(
            Material material,
            string property,
            Vector4 value)
        {
            if (material.HasProperty(property))
            {
                material.SetVector(property, value);
            }
        }

        private static void SetFloat(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
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

        private static Renderer[] GetRenderersForRuntimeLighting(
            GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                renderer.lightmapIndex = -1;
                renderer.realtimeLightmapIndex = -1;
                renderer.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
                renderer.realtimeLightmapScaleOffset =
                    new Vector4(1f, 1f, 0f, 0f);
                renderer.lightProbeUsage = LightProbeUsage.Off;
            }

            var terrains = model.GetComponentsInChildren<Terrain>(true);
            for (var index = 0; index < terrains.Length; index++)
            {
                var terrain = terrains[index];
                terrain.lightmapIndex = -1;
                terrain.realtimeLightmapIndex = -1;
                terrain.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
                terrain.realtimeLightmapScaleOffset =
                    new Vector4(1f, 1f, 0f, 0f);
            }

            return renderers;
        }

        private static void ApplySourceRendering(
            Material source,
            Material converted)
        {
            if (source == null || converted == null)
            {
                return;
            }

            if (source.HasProperty("_Cull") && converted.HasProperty("_Cull"))
            {
                converted.SetFloat("_Cull", source.GetFloat("_Cull"));
            }

            var renderType = source.GetTag("RenderType", false, string.Empty);
            var cutout = source.HasProperty("_CutoutClip") &&
                         source.GetFloat("_CutoutClip") > 0.5f ||
                         string.Equals(
                             renderType,
                             "TransparentCutout",
                             StringComparison.OrdinalIgnoreCase);
            if (cutout)
            {
                var cutoff = source.HasProperty("_Cutoff")
                    ? source.GetFloat("_Cutoff")
                    : 0.5f;
                ConfigureCutout(converted, cutoff);
                return;
            }

            if (string.Equals(
                    renderType,
                    "Transparent",
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureTransparent(converted);
            }
        }

        private static void CopyMainTextureTransform(
            Material source,
            Material converted)
        {
            if (source == null || converted == null)
            {
                return;
            }

            SetMainTextureTransform(
                converted,
                source.mainTextureScale,
                source.mainTextureOffset);
        }

        private static void ConfigureCutout(Material material, float cutoff)
        {
            material.SetOverrideTag("RenderType", "TransparentCutout");
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 1f);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
            }

            if (material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", Mathf.Clamp01(cutoff));
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat(
                    "_SrcBlend",
                    (float)UnityEngine.Rendering.BlendMode.One);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)UnityEngine.Rendering.BlendMode.Zero);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 1f);
            }

            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        private static Color SelectAccessoryColor(
            string rendererName,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair)
        {
            if (rendererName.StartsWith("cf_hair", StringComparison.Ordinal) &&
                hair != null)
            {
                return hair.StartColor;
            }

            var colorIndex = rendererName.StartsWith("oa_", StringComparison.Ordinal)
                ? 3
                : 0;
            return colorIndex < accessory.Colors.Count
                ? accessory.Colors[colorIndex]
                : Color.white;
        }

        private static bool ShouldHide(string rendererName)
        {
            return rendererName.StartsWith("cf_O_namida_", StringComparison.Ordinal) ||
                   rendererName.StartsWith("cf_O_gag_eye_", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "o_shadowcaster", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "o_dankon", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "o_gomu", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "o_mnpa", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "o_mnpb", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "cf_O_canine", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "cf_O_tooth", StringComparison.Ordinal) ||
                   string.Equals(rendererName, "o_tang", StringComparison.Ordinal);
        }

        private static Texture GetMainTexture(Material material)
        {
            if (material == null)
            {
                return null;
            }

            if (material.HasProperty("_MainTex"))
            {
                return material.GetTexture("_MainTex");
            }

            return material.HasProperty("_BaseMap")
                ? material.GetTexture("_BaseMap")
                : null;
        }

        private static Texture SelectTexture(
            string rendererName,
            string materialName,
            Material source,
            KoikatsuTextureSet textures,
            SkinTextureOverride skinTexture,
            KoikatsuClothesTextureSlot clothesTextureSlot,
            bool allowMaterialFallback)
        {
            var key = GetMaterialKey(rendererName, materialName);
            if (skinTexture != null && skinTexture.Texture != null &&
                key.Contains(skinTexture.RendererMarker))
            {
                return skinTexture.Texture;
            }

            if (textures != null)
            {
                var sourceTexture = GetMainTexture(source);
                var selected = clothesTextureSlot !=
                               KoikatsuClothesTextureSlot.None
                    ? textures.Select(clothesTextureSlot)
                    : allowMaterialFallback
                        ? textures.SelectForMaterial(
                            sourceTexture,
                            rendererName,
                            materialName)
                        : null;
                if (selected != null)
                {
                    return selected;
                }
            }

            return GetMainTexture(source);
        }

        private static Color SelectColor(
            string rendererName,
            string materialName,
            KoikatsuCardHairPart hair,
            KoikatsuCardClothesPart clothes,
            Color? skinColor,
            Color fallbackColor)
        {
            var key = GetMaterialKey(rendererName, materialName);
            if (key.Contains("hitomi_l") || key.Contains("hitomi_r"))
            {
                return Color.white;
            }

            if (key.Contains("sirome") || key.Contains("tooth") ||
                key.Contains("canine"))
            {
                return Color.white;
            }

            if (key.Contains("mayuge") || key.Contains("eyeline") ||
                key.Contains("noseline"))
            {
                return DetailColor;
            }

            if (key.Contains("tang"))
            {
                return new Color(0.88f, 0.36f, 0.42f, 1f);
            }

            if (hair != null)
            {
                if (key.Contains("acs") && hair.AccessoryColors.Count != 0)
                {
                    return Opaque(hair.AccessoryColors[0]);
                }

                return Opaque(hair.BaseColor);
            }

            if (clothes != null && clothes.Colors.Count != 0)
            {
                var colorIndex = SelectClothesColorIndex(
                    key,
                    clothes.Colors.Count);
                return Opaque(clothes.Colors[colorIndex].BaseColor);
            }

            if (skinColor.HasValue)
            {
                return Opaque(skinColor.Value);
            }

            return fallbackColor;
        }

        private static int SelectClothesColorIndex(string key, int count)
        {
            if (count > 3 && key.Contains("04"))
            {
                return 3;
            }

            if (count > 2 && key.Contains("03"))
            {
                return 2;
            }

            if (count > 1 && key.Contains("02"))
            {
                return 1;
            }

            return 0;
        }

        private static Color Opaque(Color color)
        {
            color.a = 1f;
            return color;
        }

        private sealed class SkinTextureOverride
        {
            public SkinTextureOverride(
                Texture2D texture,
                string rendererMarker,
                bool alphaClip = false)
            {
                Texture = texture;
                RendererMarker = (rendererMarker ?? string.Empty)
                    .ToLowerInvariant();
                AlphaClip = alphaClip;
            }

            public Texture2D Texture { get; }

            public string RendererMarker { get; }

            public bool AlphaClip { get; }
        }
    }
}
