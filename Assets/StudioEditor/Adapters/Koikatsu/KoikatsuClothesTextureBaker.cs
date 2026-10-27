using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal sealed class KoikatsuBakedClothesTextures
    {
        public Texture2D Main { get; set; }

        public Texture2D Main02 { get; set; }

        public Texture2D Main03 { get; set; }

        public Texture2D Select(
            KoikatsuClothesTextureSlot slot,
            bool allowMaterialFallback,
            Texture sourceTexture,
            KoikatsuTextureSet sourceTextures,
            string rendererName,
            string materialName)
        {
            if (sourceTextures == null)
            {
                return null;
            }

            var selectedSource = slot != KoikatsuClothesTextureSlot.None
                ? sourceTextures.Select(slot)
                : allowMaterialFallback
                    ? sourceTextures.SelectForMaterial(
                        sourceTexture,
                        rendererName,
                        materialName)
                    : null;
            if (sourceTextures.Main03 != null &&
                ReferenceEquals(selectedSource, sourceTextures.Main03))
            {
                return Main03;
            }

            if (sourceTextures.Main02 != null &&
                ReferenceEquals(selectedSource, sourceTextures.Main02))
            {
                return Main02;
            }

            if (sourceTextures.Main != null &&
                ReferenceEquals(selectedSource, sourceTextures.Main))
            {
                return Main;
            }

            return null;
        }
    }

    internal static class KoikatsuClothesTextureBaker
    {
        private const int PatternCategory = 430;
        private const string ShaderResourcePath =
            "Shaders/KoikatsuClothesBake";

        public static KoikatsuBakedClothesTextures Bake(
            KoikatsuTextureSet sources,
            KoikatsuCardClothesPart clothes,
            int primaryColorIndex,
            KoikatsuTextureLoader textureLoader,
            ICollection<Texture2D> runtimeTextures)
        {
            if (sources == null || clothes == null || sources.Main == null ||
                sources.ColorMask == null)
            {
                return null;
            }

            if (textureLoader == null)
            {
                throw new ArgumentNullException(nameof(textureLoader));
            }

            if (runtimeTextures == null)
            {
                throw new ArgumentNullException(nameof(runtimeTextures));
            }

            var shader = Resources.Load<Shader>(ShaderResourcePath) ??
                         Shader.Find("Hidden/StudioEditor/KoikatsuClothesBake");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Koikatsu clothes texture bake shader could not be loaded.");
            }

            var colors = new[]
            {
                GetColor(clothes.Colors, primaryColorIndex),
                GetColor(clothes.Colors, 1),
                GetColor(clothes.Colors, 2),
            };
            var patterns = new Texture2D[colors.Length];
            for (var index = 0; index < colors.Length; index++)
            {
                if (colors[index]?.Pattern > 0)
                {
                    patterns[index] = textureLoader.LoadCatalogTexture(
                        PatternCategory,
                        colors[index].Pattern,
                        "MainTexAB",
                        "MainTex");
                }
            }

            // Explicit blits can run the original texture creation pass in URP.
            // Keep its pattern scale and color-mask math when that GPU program
            // is supported; the converted character material consumes only the
            // resulting texture, never the legacy forward rendering passes.
            var sourceMaterial = textureLoader.LoadVanillaMaterial(
                "chara/mm_base.unity3d", "cf_m_clothesN_create");
            bool useSourceMaterial = sourceMaterial != null &&
                sourceMaterial.shader != null && sourceMaterial.shader.isSupported &&
                sourceMaterial.HasProperty("_ColorMask") &&
                sourceMaterial.HasProperty("_PatternScale1u");
            var material = useSourceMaterial
                ? new Material(sourceMaterial)
                : new Material(shader);
            try
            {
                if (useSourceMaterial) ConfigureSourceMaterial(material, colors, patterns);
                else Configure(material, colors, patterns);
                var result = new KoikatsuBakedClothesTextures();
                result.Main = BakeMap(
                    material,
                    sources.Main,
                    sources.ColorMask,
                    $"Koikatsu Clothes {clothes.Id}");
                Add(runtimeTextures, result.Main);

                result.Main02 = BakeMap(
                    material,
                    sources.Main02,
                    sources.ColorMask02,
                    $"Koikatsu Clothes {clothes.Id} 02");
                Add(runtimeTextures, result.Main02);

                result.Main03 = BakeMap(
                    material,
                    sources.Main03,
                    sources.ColorMask03,
                    $"Koikatsu Clothes {clothes.Id} 03");
                Add(runtimeTextures, result.Main03);
                return result;
            }
            finally
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }

        private static void ConfigureSourceMaterial(
            Material material,
            IReadOnlyList<KoikatsuCardClothesColor> colors,
            IReadOnlyList<Texture2D> patterns)
        {
            for (var index = 0; index < 3; index++)
            {
                var number = index + 1;
                var color = colors[index];
                var tiling = color?.Tiling ?? Vector2.zero;
                material.SetColor(index == 0 ? "_Color" : $"_Color{number}",
                    color?.BaseColor ?? Color.white);
                material.SetColor($"_Color{number}_2", color?.PatternColor ?? Color.white);
                material.SetFloat($"_PatternScale{number}u", tiling.x);
                material.SetFloat($"_PatternScale{number}v", tiling.y);
                material.SetTexture($"_PatternMask{number}", patterns[index]);
            }
        }

        private static void Configure(
            Material material,
            IReadOnlyList<KoikatsuCardClothesColor> colors,
            IReadOnlyList<Texture2D> patterns)
        {
            for (var index = 0; index < 3; index++)
            {
                var number = index + 1;
                var color = colors[index];
                var pattern = patterns[index];
                material.SetColor(
                    $"_ChannelColor{number}",
                    color?.BaseColor ?? Color.white);
                material.SetColor(
                    $"_PatternColor{number}",
                    color?.PatternColor ?? Color.white);
                material.SetVector(
                    $"_PatternTiling{number}",
                    color?.Tiling ?? Vector2.zero);
                material.SetTexture(
                    $"_Pattern{number}",
                    pattern ?? Texture2D.whiteTexture);
                material.SetFloat(
                    $"_HasPattern{number}",
                    pattern != null ? 1f : 0f);
            }
        }

        private static Texture2D BakeMap(
            Material material,
            Texture2D source,
            Texture2D colorMask,
            string name)
        {
            if (source == null || colorMask == null)
            {
                return null;
            }

            material.SetTexture("_ColorMask", colorMask);
            var target = RenderTexture.GetTemporary(
                Math.Max(source.width, 1),
                Math.Max(source.height, 1),
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            Texture2D result = null;
            try
            {
                GL.sRGBWrite = true;
                // The original create_topN pass alpha-blends into the target.
                // Match CustomTextureCreate: pooled pixels must not fill the
                // source texture's transparent neckline and other cutouts.
                RenderTexture.active = target;
                GL.Clear(false, true, Color.clear);
                Graphics.Blit(source, target, material, 0);
                RenderTexture.active = target;
                result = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name = name,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                result.ReadPixels(
                    new Rect(0f, 0f, target.width, target.height),
                    0,
                    0);
                result.Apply(true, false);
                return result;
            }
            catch
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(result);
                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static KoikatsuCardClothesColor GetColor(
            IReadOnlyList<KoikatsuCardClothesColor> colors,
            int index)
        {
            return colors != null && index >= 0 && index < colors.Count
                ? colors[index]
                : null;
        }

        private static void Add(
            ICollection<Texture2D> textures,
            Texture2D texture)
        {
            if (texture != null)
            {
                textures.Add(texture);
            }
        }
    }
}
