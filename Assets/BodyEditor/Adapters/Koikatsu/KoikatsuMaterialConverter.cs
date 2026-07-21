using System;
using System.Collections.Generic;
using BodyEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuMaterialConverter
    {
        private static readonly Color SkinColor =
            new Color(1f, 0.78f, 0.70f, 1f);
        private static readonly Color DetailColor =
            new Color(0.16f, 0.07f, 0.05f, 1f);

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
                CharacterRenderSurfaceRole.Hair);
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
                CharacterRenderSurfaceRole.Clothes);
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
                CharacterRenderSurfaceRole.Accessory);
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
                        MaterialRenderUtility.SetMainTexture(material, texture);
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
                CharacterRenderSurfaceRole.Skin);
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
                CharacterRenderSurfaceRole.Face);
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

                MaterialRenderUtility.SetMainTexture(materials[index], texture);
                MaterialRenderUtility.ConfigureTransparent(materials[index]);
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
            CharacterRenderSurfaceRole? characterStyle)
        {
            var characterScheme = characterStyle.HasValue
                ? CharacterRenderSchemeRegistry.GetDefault()
                : null;
            var fallbackShader = characterScheme == null
                ? ResolveFallbackShader()
                : null;
            if (characterScheme == null && fallbackShader == null)
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
                    var materialName =
                        (source != null ? source.name : renderer.name) +
                        " (Koikatsu Preview)";
                    var converted = characterScheme != null
                        ? characterScheme.CreateMaterial(
                            new CharacterRenderMaterialContext(
                                source,
                                characterStyle.Value,
                                materialKey,
                                materialName,
                                materialColor,
                                hair?.OutlineColor))
                        : CreateFallbackMaterial(
                            source,
                            fallbackShader,
                            materialName,
                            materialColor);
                    if (converted == null)
                    {
                        throw new InvalidOperationException(
                            $"Character render scheme '{characterScheme?.Id}' " +
                            "returned no material.");
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
                        MaterialRenderUtility.SetMainTexture(
                            converted,
                            mainTexture);
                        MaterialRenderUtility.CopyMainTextureTransform(
                            source,
                            converted);
                    }

                    if (skinTexture != null && skinTexture.AlphaClip &&
                        materialKey.Contains(skinTexture.RendererMarker))
                    {
                        MaterialRenderUtility.ConfigureCutout(converted, 0.5f);
                    }

                    if (hasBakedWhite)
                    {
                        MaterialRenderUtility.ConfigureTransparent(
                            converted,
                            (int)UnityEngine.Rendering.RenderQueue.Transparent);
                    }
                    else if (hasBakedIris)
                    {
                        MaterialRenderUtility.SetMainTextureTransform(
                            converted,
                            bakedIris.Scale,
                            bakedIris.Offset);
                        MaterialRenderUtility.ConfigureTransparent(
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

        private static void ConvertAccessoryRenderers(
            GameObject model,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair,
            ICollection<Material> runtimeMaterials,
            CharacterRenderSurfaceRole characterStyle)
        {
            var characterScheme = CharacterRenderSchemeRegistry.GetDefault();

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
                    var materialName =
                        (source != null ? source.name : renderer.name) +
                        " (Koikatsu Accessory Preview)";
                    var converted = characterScheme.CreateMaterial(
                        new CharacterRenderMaterialContext(
                            source,
                            characterStyle,
                            GetMaterialKey(renderer.name, source?.name),
                            materialName,
                            Opaque(previewColor),
                            hair?.OutlineColor));
                    if (converted == null)
                    {
                        throw new InvalidOperationException(
                            $"Character render scheme '{characterScheme.Id}' " +
                            "returned no material.");
                    }

                    var mainTexture = GetMainTexture(source);
                    if (mainTexture != null)
                    {
                        MaterialRenderUtility.SetMainTexture(
                            converted,
                            mainTexture);
                        MaterialRenderUtility.CopyMainTextureTransform(
                            source,
                            converted);
                    }

                    convertedMaterials[materialIndex] = converted;
                    runtimeMaterials.Add(converted);
                }

                renderer.sharedMaterials = convertedMaterials;
            }
        }

        private static Shader ResolveFallbackShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Standard") ??
                   Shader.Find("Unlit/Texture");
        }

        private static Material CreateFallbackMaterial(
            Material source,
            Shader shader,
            string materialName,
            Color baseColor)
        {
            var material = new Material(shader)
            {
                name = materialName,
            };
            MaterialRenderUtility.SetBaseColor(material, baseColor);
            MaterialRenderUtility.CopySourceRenderState(source, material);
            return material;
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
