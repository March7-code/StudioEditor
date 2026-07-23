using System;
using System.Collections.Generic;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuMaterialConverter
    {
        private static readonly Color SkinColor =
            new Color(1f, 0.78f, 0.70f, 1f);
        private static readonly Color DetailColor =
            new Color(0.16f, 0.07f, 0.05f, 1f);
        private const int FaceDetailQueue = (int)RenderQueue.Transparent - 10;
        private const int FaceForegroundQueue =
            (int)RenderQueue.Transparent + 100;

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
                null,
                null);
        }

        public static void ConvertHair(
            GameObject model,
            KoikatsuCardHairPart hair,
            Texture2D hairGlossTexture,
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
                hairGlossTexture,
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
                null,
                CharacterRenderSurfaceRole.Clothes);
        }

        public static void ConvertAccessory(
            GameObject model,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair,
            KoikatsuAccessoryRendererMap rendererMap,
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
                rendererMap,
                runtimeMaterials,
                CharacterRenderSurfaceRole.Accessory);
        }

        public static void ApplyMaterialEditorMainTextures(
            GameObject model,
            KoikatsuTextureLoader textureLoader,
            int objectType = 4,
            int coordinateIndex = -1,
            int slot = -1)
        {
            if (model == null || textureLoader == null)
            {
                return;
            }

            textureLoader.ApplyMaterialEditorProperties(
                model,
                objectType,
                coordinateIndex,
                slot);
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

        public static void ConfigureFaceRenderQueues(
            GameObject model,
            KoikatsuCardFaceAppearance appearance)
        {
            if (model == null || appearance == null)
            {
                return;
            }

            var eyesQueue = appearance.ForegroundEyes == 2
                ? FaceForegroundQueue
                : FaceDetailQueue;
            var eyebrowQueue = appearance.ForegroundEyebrow == 2
                ? FaceForegroundQueue + 6
                : FaceDetailQueue + 6;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                var key = (renderer.name ?? string.Empty).ToLowerInvariant();
                switch (key)
                {
                    case "cf_ohitomi_l":
                    case "cf_ohitomi_r":
                        ConfigureTransparentQueue(renderer, eyesQueue);
                        break;
                    case "cf_ohitomi_l02":
                    case "cf_ohitomi_r02":
                        ConfigureTransparentQueue(renderer, eyesQueue + 1);
                        break;
                    case "cf_o_eyeline":
                        ConfigureTransparentQueue(renderer, eyesQueue + 2);
                        break;
                    case "cf_o_eyeline_low":
                        ConfigureTransparentQueue(renderer, eyesQueue + 4);
                        break;
                    case "cf_o_mayuge":
                        ConfigureTransparentQueue(renderer, eyebrowQueue);
                        break;
                    case "cf_o_noseline":
                        ConfigureTransparentQueue(
                            renderer,
                            FaceDetailQueue + 8);
                        break;
                }
            }
        }

        private static void ConfigureTransparentQueue(
            Renderer renderer,
            int firstQueue)
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                MaterialRenderUtility.ConfigureTransparent(
                    materials[index],
                    firstQueue + index);
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
                if (materials[index] == null)
                {
                    continue;
                }

                var texture = PreserveMaterialEditorFaceTexture(
                    materials[index],
                    textures[textureIndex]);

                var color = colors[Math.Min(index, colors.Count - 1)];
                if (texture == null)
                {
                    // A list entry named "none" is a real asset in some
                    // installations, but it means an absent face layer in
                    // the original character pipeline. Make only this
                    // submaterial transparent so a sibling MaterialEditor
                    // layer can still render.
                    // Keep RGB white while hiding the layer. If a later
                    // MaterialEditor texture replaces this slot, restoring
                    // alpha must not leave the material permanently black.
                    var clear = new Color(1f, 1f, 1f, 0f);
                    materials[index].color = clear;
                    if (materials[index].HasProperty("_BaseColor"))
                    {
                        materials[index].SetColor("_BaseColor", clear);
                    }

                    MaterialRenderUtility.ConfigureTransparent(
                        materials[index]);
                    continue;
                }

                hasTexture = true;
                materials[index].color = color;
                if (materials[index].HasProperty("_BaseColor"))
                {
                    materials[index].SetColor("_BaseColor", color);
                }

                MaterialRenderUtility.SetMainTexture(materials[index], texture);
                MaterialRenderUtility.ConfigureTransparent(materials[index]);
            }

            if (hasTexture)
            {
                renderer.enabled = true;
            }
            else
            {
                // Do not leave the prefab's original face-detail material in
                // place when its card texture cannot be resolved. That
                // material is usually a Built-in shader and renders as an
                // opaque or malformed cf_O_noseline layer in URP.
                renderer.enabled = false;
            }
        }

        private static Texture PreserveMaterialEditorFaceTexture(
            Material material,
            Texture fallback)
        {
            var current = GetMainTexture(material);
            return current != null &&
                   current.name.StartsWith(
                       "Koikatsu MaterialEditor ",
                       StringComparison.OrdinalIgnoreCase)
                ? current
                : fallback;
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
            Texture2D hairGlossTexture,
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
                                           renderer.name,
                                           materialKey,
                                           out bakedIris);
                    var hasBakedWhite = eyeTextures != null &&
                                        eyeTextures.IsWhite(
                                            renderer.name,
                                            materialKey);
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
                    var usesFinalMaterialEditorAlbedo =
                        UsesFinalMaterialEditorAlbedo(source);
                    var materialColor = hasBakedIris || hasBakedWhite ||
                                        hasBakedClothes
                        ? Color.white
                        : SelectColor(
                            renderer.name,
                            source?.name,
                            source,
                            hair,
                            clothes,
                            skinColor,
                            source != null && source.HasProperty("_Color")
                                ? source.GetColor("_Color")
                                : Color.white);
                    if (clothes != null && !hasBakedClothes)
                    {
                        materialColor = GetClothesColor(clothes, 0) ??
                                        materialColor;
                    }
                    if (usesFinalMaterialEditorAlbedo &&
                        (hair != null || clothes != null))
                    {
                        materialColor = Color.white;
                    }
                    var materialName =
                        (source != null ? source.name : renderer.name) +
                        " (Koikatsu Preview)";
                    var isHairAccessory = hair != null &&
                        renderer.name.StartsWith(
                            "cf_acs",
                            StringComparison.Ordinal);
                    var converted = characterScheme != null
                        ? characterScheme.CreateMaterial(
                            new CharacterRenderMaterialContext(
                                source,
                                characterStyle.Value,
                                materialKey,
                                materialName,
                                materialColor,
                                hair?.OutlineColor,
                                hair != null
                                    ? SelectHairChannel(
                                        hair,
                                        isHairAccessory,
                                        1)
                                    : GetClothesColor(clothes, 1),
                                hair != null
                                    ? SelectHairChannel(
                                        hair,
                                        isHairAccessory,
                                        2)
                                    : GetClothesColor(clothes, 2),
                                GetClothesColor(clothes, 3),
                                !usesFinalMaterialEditorAlbedo &&
                                (hair != null ||
                                 clothes != null && !hasBakedClothes) &&
                                HasVertexColors(renderer),
                                hairGlossTexture))
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
            KoikatsuAccessoryRendererMap rendererMap,
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

                var rendererInfo = rendererMap != null && rendererMap.TryGet(
                    renderer,
                    out var mappedRole)
                    ? mappedRole
                    : null;
                var rendererRole = rendererInfo?.Role ??
                                   KoikatsuAccessoryRendererRole.Unknown;
                if (rendererRole == KoikatsuAccessoryRendererRole.Alpha &&
                    SelectAccessoryColor(
                        rendererInfo,
                        accessory,
                        hair,
                        Color.white).a <= 0f)
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
                    var hasVertexColorChannels = HasVertexColors(renderer);
                    var hasColorMaskChannels = HasColorMask(source);
                    var sourceBaseColor = GetSourceColor(source, "_Color");
                    var previewColor = SelectMaterialEditorColorOverride(
                        source,
                        "Color",
                        SelectAccessoryColor(
                            rendererInfo,
                            accessory,
                            hair,
                            sourceBaseColor));
                    var secondaryColor = SelectMaterialEditorColorOverride(
                        source,
                        "Color2",
                        SelectAccessoryChannel(
                            rendererInfo,
                            accessory,
                            hair,
                            1,
                            source,
                            "_Color2"));
                    var tertiaryColor = SelectMaterialEditorColorOverride(
                        source,
                        "Color3",
                        SelectAccessoryChannel(
                            rendererInfo,
                            accessory,
                            hair,
                            2,
                            source,
                            "_Color3"));
                    var quaternaryColor = rendererRole ==
                                          KoikatsuAccessoryRendererRole.Alpha
                        ? SelectAccessoryColor(
                            rendererInfo,
                            accessory,
                            hair,
                            GetSourceColor(source, "_Color4"))
                        : GetSourceColor(source, "_Color4");
                    quaternaryColor = SelectMaterialEditorColorOverride(
                        source,
                        "Color4",
                        quaternaryColor);
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
                            hair?.OutlineColor,
                            secondaryColor,
                            tertiaryColor,
                            quaternaryColor,
                            hasVertexColorChannels));
                    if (converted == null)
                    {
                        throw new InvalidOperationException(
                            $"Character render scheme '{characterScheme.Id}' " +
                            "returned no material.");
                    }

                    // Accessory card colors are shader channels, not a
                    // blanket albedo multiplier. Keep the channel values for
                    // meshes or masks that carry channel weights, while the
                    // base texture remains neutral everywhere else.
                    MaterialRenderUtility.SetBaseColor(converted, Color.white);
                    if (converted.HasProperty("_Color"))
                    {
                        converted.SetColor("_Color", Opaque(previewColor));
                    }
                    if (converted.HasProperty("_UseColorMaskChannels"))
                    {
                        converted.SetFloat(
                            "_UseColorMaskChannels",
                            hasColorMaskChannels ? 1f : 0f);
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
            KoikatsuAccessoryRendererInfo info,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair,
            Color sourceColor)
        {
            var role = info?.Role ?? KoikatsuAccessoryRendererRole.Unknown;
            switch (role)
            {
                case KoikatsuAccessoryRendererRole.Hair:
                    return hair != null ? Opaque(hair.StartColor) : sourceColor;
                case KoikatsuAccessoryRendererRole.Normal:
                    return info.UseColor01
                        ? GetAccessoryColor(accessory, 0, sourceColor)
                        : sourceColor;
                case KoikatsuAccessoryRendererRole.Alpha:
                    return GetAccessoryColor(accessory, 3, sourceColor);
                default:
                    return sourceColor;
            }
        }

        private static Color? SelectAccessoryChannel(
            KoikatsuAccessoryRendererInfo info,
            KoikatsuCardAccessory accessory,
            KoikatsuCardHairPart hair,
            int index,
            Material source,
            string sourceProperty)
        {
            var role = info?.Role ?? KoikatsuAccessoryRendererRole.Unknown;
            switch (role)
            {
                case KoikatsuAccessoryRendererRole.Hair:
                    return hair != null ? Opaque(hair.StartColor) :
                        GetSourceColor(source, sourceProperty);
                case KoikatsuAccessoryRendererRole.Normal:
                    return UsesAccessoryColor(info, index)
                        ? GetAccessoryColorNullable(
                            accessory,
                            index,
                            GetSourceColor(source, sourceProperty))
                        : GetSourceColor(source, sourceProperty);
                default:
                    return GetSourceColor(source, sourceProperty);
            }
        }

        private static bool UsesAccessoryColor(
            KoikatsuAccessoryRendererInfo info,
            int index)
        {
            if (info == null)
            {
                return false;
            }

            switch (index)
            {
                case 0:
                    return info.UseColor01;
                case 1:
                    return info.UseColor02;
                case 2:
                    return info.UseColor03;
                default:
                    return false;
            }
        }

        private static Color GetAccessoryColor(
            KoikatsuCardAccessory accessory,
            int index,
            Color fallback)
        {
            return accessory != null && accessory.Colors != null &&
                   index >= 0 && index < accessory.Colors.Count
                ? Opaque(accessory.Colors[index])
                : fallback;
        }

        private static Color? GetAccessoryColorNullable(
            KoikatsuCardAccessory accessory,
            int index,
            Color? fallback)
        {
            return accessory != null && accessory.Colors != null &&
                   index >= 0 && index < accessory.Colors.Count
                ? Opaque(accessory.Colors[index])
                : fallback;
        }

        private static Color GetSourceColor(
            Material source,
            string propertyName)
        {
            return source != null && source.HasProperty(propertyName)
                ? source.GetColor(propertyName)
                : Color.white;
        }

        private static Color SelectMaterialEditorColorOverride(
            Material source,
            string propertyName,
            Color fallback)
        {
            return HasMaterialEditorOverride(source, propertyName)
                ? GetSourceColor(source, "_" + propertyName)
                : fallback;
        }

        private static Color? SelectMaterialEditorColorOverride(
            Material source,
            string propertyName,
            Color? fallback)
        {
            return HasMaterialEditorOverride(source, propertyName)
                ? GetSourceColor(source, "_" + propertyName)
                : fallback;
        }

        private static Color? GetClothesColor(
            KoikatsuCardClothesPart clothes,
            int index)
        {
            if (clothes == null || clothes.Colors == null ||
                index < 0 || index >= clothes.Colors.Count)
            {
                return null;
            }

            return Opaque(clothes.Colors[index].BaseColor);
        }

        private static bool HasVertexColors(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null && skinned.sharedMesh != null)
            {
                return skinned.sharedMesh.colors32 != null &&
                       skinned.sharedMesh.colors32.Length != 0;
            }

            var filter = renderer != null
                ? renderer.GetComponent<MeshFilter>()
                : null;
            return filter != null && filter.sharedMesh != null &&
                   filter.sharedMesh.colors32 != null &&
                   filter.sharedMesh.colors32.Length != 0;
        }

        private static bool HasColorMask(Material material)
        {
            return material != null &&
                   material.HasProperty("_ColorMask") &&
                   material.GetTexture("_ColorMask") != null;
        }

        private static bool UsesFinalMaterialEditorAlbedo(Material material)
        {
            return HasMaterialEditorOverride(material, "MainTex") &&
                   !HasMaterialEditorOverride(material, "Color") &&
                   !HasMaterialEditorOverride(material, "Color2") &&
                   !HasMaterialEditorOverride(material, "Color3") &&
                   !HasMaterialEditorOverride(material, "Color4");
        }

        private static bool HasMaterialEditorOverride(
            Material material,
            string propertyName)
        {
            return material != null &&
                   string.Equals(
                       material.GetTag(
                           "StudioEditor.MaterialEditor." + propertyName,
                           false,
                           string.Empty),
                       "1",
                       StringComparison.Ordinal);
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

            if (material.HasProperty("MainTex"))
            {
                return material.GetTexture("MainTex");
            }

            if (material.HasProperty("_BaseMap"))
            {
                return material.GetTexture("_BaseMap");
            }

            return material.HasProperty("BaseMap")
                ? material.GetTexture("BaseMap")
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
            Material source,
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
                if (source != null &&
                    source.HasProperty("Color") &&
                    string.Equals(
                        source.GetTag(
                            "StudioEditor.MaterialEditor.Color",
                            false,
                            string.Empty),
                        "1",
                        StringComparison.Ordinal))
                {
                    return Opaque(source.GetColor("Color"));
                }

                return new Color(0.88f, 0.36f, 0.42f, 1f);
            }

            if (hair != null)
            {
                if (rendererName.StartsWith(
                        "cf_acs",
                        StringComparison.Ordinal) &&
                    hair.AccessoryColors.Count != 0)
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

        private static Color SelectHairChannel(
            KoikatsuCardHairPart hair,
            bool accessory,
            int index)
        {
            if (accessory && hair.AccessoryColors != null &&
                index >= 0 && index < hair.AccessoryColors.Count)
            {
                return Opaque(hair.AccessoryColors[index]);
            }

            return index == 2 ? Opaque(hair.EndColor) : Opaque(hair.StartColor);
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
