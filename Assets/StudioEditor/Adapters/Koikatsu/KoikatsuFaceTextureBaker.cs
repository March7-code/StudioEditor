using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuFaceTextureBaker
    {
        private const int FacePaintCategory = 405;
        private const int CheekCategory = 402;
        private const int LipLineCategory = 404;
        private const int MoleCategory = 415;
        private const int EyeshadowCategory = 401;
        private const int LipCategory = 403;

        public static Texture2D Bake(
            Material createMaterial,
            Texture2D baseTexture,
            Texture2D colorMask,
            KoikatsuCard card,
            KoikatsuTextureLoader textureLoader,
            int coordinateIndex,
            ICollection<Texture2D> runtimeTextures)
        {
            if (createMaterial == null || baseTexture == null ||
                card?.Face?.Appearance == null || textureLoader == null ||
                runtimeTextures == null)
            {
                return baseTexture;
            }

            if (createMaterial.shader == null || !createMaterial.shader.isSupported)
            {
                Debug.LogWarning(
                    "Koikatsu's original face creation shader is unavailable; " +
                    "using the head texture unchanged.");
                return baseTexture;
            }

            var appearance = card.Face.Appearance;
            var makeup = appearance.BaseMakeup;
            if (card.Coordinates != null &&
                coordinateIndex >= 0 &&
                coordinateIndex < card.Coordinates.Count &&
                card.Coordinates[coordinateIndex].MakeupEnabled)
            {
                makeup = card.Coordinates[coordinateIndex].Makeup;
            }

            var material = new Material(createMaterial)
            {
                name = "Koikatsu Face Create (Runtime)",
            };
            try
            {
                SetTexture(material, "_MainTex", baseTexture);
                SetTexture(material, "_ColorMask", colorMask);
                SetColor(material, "_Color", card.Body.Appearance.SkinMainColor);
                SetColor(material, "_Color2", card.Body.Appearance.SkinSubColor);

                SetTexture(
                    material,
                    "_Texture3",
                    LoadFacePaint(
                        textureLoader,
                        makeup?.PaintIds,
                        0));
                SetColor(
                    material,
                    "_Color3",
                    GetColor(makeup?.PaintColors, 0));
                SetVector(
                    material,
                    "_paint1",
                    GetPaintLayout(makeup?.PaintLayouts, 0));

                SetTexture(
                    material,
                    "_Texture7",
                    LoadFacePaint(
                        textureLoader,
                        makeup?.PaintIds,
                        1));
                SetColor(
                    material,
                    "_Color7",
                    GetColor(makeup?.PaintColors, 1));
                SetVector(
                    material,
                    "_paint2",
                    GetPaintLayout(makeup?.PaintLayouts, 1));

                SetTexture(
                    material,
                    "_Texture4",
                    textureLoader.LoadCatalogTexture(
                        CheekCategory,
                        makeup?.CheekId ?? 0,
                        "MainAB",
                        "CheekTex",
                        "ChaFileFace.baseMakeup.cheekId"));
                SetColor(material, "_Color4", makeup?.CheekColor ?? Color.white);

                SetTexture(
                    material,
                    "_Texture5",
                    textureLoader.LoadCatalogTexture(
                        LipLineCategory,
                        appearance.LipLineId,
                        "MainAB",
                        "LiplineTex",
                        "ChaFileFace.lipLineId"));
                SetColor(material, "_Color5", appearance.LipLineColor);

                SetTexture(
                    material,
                    "_Texture6",
                    textureLoader.LoadCatalogTexture(
                        MoleCategory,
                        appearance.MoleId,
                        "MainAB",
                        "MoleTex",
                        "ChaFileFace.moleId"));
                SetColor(material, "_Color6", appearance.MoleColor);
                SetVector(material, "_hokuro", appearance.MoleLayout);

                SetTexture(
                    material,
                    "_overtex3",
                    textureLoader.LoadCatalogTexture(
                        EyeshadowCategory,
                        makeup?.EyeshadowId ?? 0,
                        "MainAB",
                        "EyeshadowTex",
                        "ChaFileFace.baseMakeup.eyeshadowId"));
                SetColor(
                    material,
                    "_overcolor3",
                    makeup?.EyeshadowColor ?? Color.white);

                SetTexture(
                    material,
                    "_overtex1",
                    textureLoader.LoadCatalogTexture(
                        LipCategory,
                        makeup?.LipId ?? 0,
                        "MainAB",
                        "LipTex",
                        "ChaFileFace.baseMakeup.lipId"));
                SetColor(
                    material,
                    "_overcolor1",
                    makeup?.LipColor ?? Color.white);

                var texture = Render(
                    material,
                    baseTexture,
                    "Koikatsu Face Create");
                runtimeTextures.Add(texture);
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Koikatsu original face texture creation failed; " +
                    $"using the head texture unchanged: {exception.Message}");
                return baseTexture;
            }
            finally
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }

        private static Texture2D LoadFacePaint(
            KoikatsuTextureLoader textureLoader,
            IReadOnlyList<int> ids,
            int index)
        {
            if (ids == null || index < 0 || index >= ids.Count)
            {
                return null;
            }

            return textureLoader.LoadCatalogTexture(
                FacePaintCategory,
                ids[index],
                "MainAB",
                "PaintTex",
                "ChaFileFace.baseMakeup.paintId");
        }

        private static Color GetColor(
            IReadOnlyList<Color> colors,
            int index)
        {
            return colors != null && index >= 0 && index < colors.Count
                ? colors[index]
                : Color.white;
        }

        private static Vector4 GetPaintLayout(
            IReadOnlyList<Vector4> layouts,
            int index)
        {
            if (layouts == null || index < 0 || index >= layouts.Count)
            {
                return Vector4.zero;
            }

            var value = layouts[index];
            return new Vector4(
                Mathf.Lerp(0.25f, -0.25f, value.x),
                Mathf.Lerp(0.3f, -0.3f, value.y),
                Mathf.Lerp(1f, -1f, value.z),
                Mathf.Lerp(-8f, 0.7f, value.w));
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

        private static Texture2D Render(
            Material material,
            Texture2D source,
            string name)
        {
            var width = Math.Max(source.width, 1);
            var height = Math.Max(source.height, 1);
            var target = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, target, material, 0);
                RenderTexture.active = target;
                result = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    name = name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                result.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0);
                result.Apply(false, false);
                return result;
            }
            catch
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
