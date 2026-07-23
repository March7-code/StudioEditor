using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal sealed class KoikatsuBakedEyeTexture
    {
        public KoikatsuBakedEyeTexture(
            Texture2D texture,
            Vector2 scale,
            Vector2 offset)
        {
            Texture = texture;
            Scale = scale;
            Offset = offset;
        }

        public Texture2D Texture { get; }

        public Vector2 Scale { get; }

        public Vector2 Offset { get; }
    }

    internal sealed class KoikatsuBakedEyeTextures
    {
        public KoikatsuBakedEyeTexture Left { get; set; }

        public KoikatsuBakedEyeTexture Right { get; set; }

        public Texture2D White { get; set; }

        public bool TryGetIris(string materialKey, out KoikatsuBakedEyeTexture eye)
        {
            return TryGetIris(null, materialKey, out eye);
        }

        public bool TryGetIris(
            string rendererName,
            string materialKey,
            out KoikatsuBakedEyeTexture eye)
        {
            var rendererKey = (rendererName ?? string.Empty).ToLowerInvariant();
            if (rendererKey == "cf_ohitomi_l02")
            {
                eye = Left;
                return eye?.Texture != null;
            }

            if (rendererKey == "cf_ohitomi_r02")
            {
                eye = Right;
                return eye?.Texture != null;
            }

            if (rendererKey == "cf_ohitomi_l" ||
                rendererKey == "cf_ohitomi_r" ||
                string.IsNullOrEmpty(materialKey) ||
                materialKey.Contains("sirome"))
            {
                eye = null;
                return false;
            }

            if (materialKey.Contains("hitomi_l"))
            {
                eye = Left;
                return eye?.Texture != null;
            }

            if (materialKey.Contains("hitomi_r"))
            {
                eye = Right;
                return eye?.Texture != null;
            }

            eye = null;
            return false;
        }

        public bool IsWhite(string rendererName, string materialKey)
        {
            var rendererKey = (rendererName ?? string.Empty).ToLowerInvariant();
            if (rendererKey == "cf_ohitomi_l02" ||
                rendererKey == "cf_ohitomi_r02")
            {
                return false;
            }

            return White != null &&
                   (rendererKey == "cf_ohitomi_l" ||
                    rendererKey == "cf_ohitomi_r" ||
                    !string.IsNullOrEmpty(materialKey) &&
                    materialKey.Contains("sirome"));
        }
    }

    internal static class KoikatsuEyeTextureBaker
    {
        private const int EyeWhiteCategory = 407;
        private const int EyeCategory = 408;
        private const int EyeGradientCategory = 409;
        private const int EyeHighlightUpCategory = 410;
        private const int EyeHighlightDownCategory = 411;
        private const int EyeTiltShapeIndex = 33;
        private const string ShaderResourcePath = "Shaders/KoikatsuEyeBake";

        public static KoikatsuBakedEyeTextures Bake(
            KoikatsuCardFace face,
            KoikatsuTextureLoader textureLoader,
            int coordinateIndex,
            ICollection<Texture2D> runtimeTextures)
        {
            if (face == null)
            {
                throw new ArgumentNullException(nameof(face));
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
                         Shader.Find("Hidden/StudioEditor/KoikatsuEyeBake");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Koikatsu eye texture bake shader could not be loaded.");
            }

            var originalCreateMaterial = textureLoader.LoadVanillaMaterial(
                "chara/mm_base.unity3d",
                "cf_m_eye_create");
            if (originalCreateMaterial != null &&
                (originalCreateMaterial.shader == null ||
                 !originalCreateMaterial.shader.isSupported))
            {
                originalCreateMaterial = null;
            }

            var appearance = face.Appearance;
            var highlightUp = textureLoader.LoadCatalogTexture(
                EyeHighlightUpCategory,
                appearance.HighlightUpId,
                "MainAB",
                "EyeHiUpTex",
                "ChaFileFace.hlUpId");
            var highlightDown = textureLoader.LoadCatalogTexture(
                EyeHighlightDownCategory,
                appearance.HighlightDownId,
                "MainAB",
                "EyeHiDownTex",
                "ChaFileFace.hlDownId");
            var result = new KoikatsuBakedEyeTextures();

            var whiteOverride = textureLoader
                .LoadMaterialEditorCharacterTexture(
                    "cf_m_sirome_00",
                    "MainTex");
            var whiteSource = whiteOverride ??
                              textureLoader.LoadCatalogTexture(
                                  EyeWhiteCategory,
                                  appearance.EyeWhiteId,
                                  "MainAB",
                                  "EyeWhiteTex",
                                  "ChaFileFace.whiteId");
            if (whiteSource != null)
            {
                result.White = BakeWhite(
                    shader,
                    whiteSource,
                    appearance.EyeWhiteBaseColor,
                    appearance.EyeWhiteSubColor);
                runtimeTextures.Add(result.White);
            }

            var irisOverride = textureLoader
                .LoadMaterialEditorCharacterTexture(
                    "cf_m_hitomi_00",
                    "MainTex");
            result.Left = BakeIris(
                shader,
                face,
                GetPupil(appearance.Pupils, 0),
                highlightUp,
                highlightDown,
                false,
                irisOverride,
                LoadEyeOverlay(
                    textureLoader,
                    coordinateIndex,
                    KoikatsuSkinOverlayType.EyeUnderLeft,
                    KoikatsuSkinOverlayType.EyeUnder),
                LoadEyeOverlay(
                    textureLoader,
                    coordinateIndex,
                    KoikatsuSkinOverlayType.EyeOverLeft,
                    KoikatsuSkinOverlayType.EyeOver),
                textureLoader,
                runtimeTextures,
                originalCreateMaterial);
            result.Right = BakeIris(
                shader,
                face,
                GetPupil(appearance.Pupils, 1),
                highlightUp,
                highlightDown,
                true,
                irisOverride,
                LoadEyeOverlay(
                    textureLoader,
                    coordinateIndex,
                    KoikatsuSkinOverlayType.EyeUnderRight,
                    KoikatsuSkinOverlayType.EyeUnder),
                LoadEyeOverlay(
                    textureLoader,
                    coordinateIndex,
                    KoikatsuSkinOverlayType.EyeOverRight,
                    KoikatsuSkinOverlayType.EyeOver),
                textureLoader,
                runtimeTextures,
                originalCreateMaterial);

            return result;
        }

        private static KoikatsuBakedEyeTexture BakeIris(
            Shader shader,
            KoikatsuCardFace face,
            KoikatsuCardPupil pupil,
            Texture2D highlightUp,
            Texture2D highlightDown,
            bool rightEye,
            Texture2D sourceOverride,
            Texture2D underlay,
            Texture2D overlay,
            KoikatsuTextureLoader textureLoader,
            ICollection<Texture2D> runtimeTextures,
            Material originalCreateMaterial)
        {
            if (pupil == null)
            {
                return null;
            }

            var source = sourceOverride ?? textureLoader.LoadCatalogTexture(
                EyeCategory,
                pupil.Id,
                "MainAB",
                "EyeTex",
                rightEye
                    ? "ChaFileFace.Pupil2"
                    : "ChaFileFace.Pupil1");
            if (source == null)
            {
                return null;
            }

            source = KoikatsuOverlayTextureBaker.Composite(
                source,
                underlay,
                runtimeTextures,
                $"Koikatsu Eye {(rightEye ? "R" : "L")} (KSOX underlay)",
                true);

            var gradient = textureLoader.LoadCatalogTexture(
                EyeGradientCategory,
                pupil.GradientMaskId,
                "ColorMaskAB",
                "ColorMaskTex");
            var material = originalCreateMaterial != null
                ? new Material(originalCreateMaterial)
                : new Material(shader);
            try
            {
                if (originalCreateMaterial != null)
                {
                    ConfigureOriginalMaterial(
                        material,
                        pupil,
                        gradient,
                        highlightUp,
                        highlightDown,
                        face.Appearance,
                        rightEye,
                        GetEyeRotation(face, rightEye));
                }
                else
                {
                    ConfigureFallbackMaterial(
                        material,
                        pupil,
                        gradient,
                        highlightUp,
                        highlightDown,
                        face.Appearance,
                        rightEye,
                        GetEyeRotation(face, rightEye));
                }

                var texture = Render(
                    material,
                    source,
                    0,
                    $"Koikatsu Eye {(rightEye ? "R" : "L")} {pupil.Id}");
                runtimeTextures.Add(texture);
                texture = KoikatsuOverlayTextureBaker.Composite(
                    texture,
                    overlay,
                    runtimeTextures,
                    $"Koikatsu Eye {(rightEye ? "R" : "L")} (KSOX overlay)",
                    true);
                CalculateUvTransform(
                    face.Appearance,
                    rightEye,
                    out var scale,
                    out var offset);
                return new KoikatsuBakedEyeTexture(texture, scale, offset);
            }
            finally
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }

        private static void ConfigureOriginalMaterial(
            Material material,
            KoikatsuCardPupil pupil,
            Texture2D gradient,
            Texture2D highlightUp,
            Texture2D highlightDown,
            KoikatsuCardFaceAppearance appearance,
            bool rightEye,
            float rotation)
        {
            SetTexture(material, "_ColorMask", gradient ?? Texture2D.whiteTexture);
            SetColor(material, "_Color", pupil.BaseColor);
            SetColor(material, "_Color2", pupil.SubColor);
            SetFloat(
                material,
                "_Blend",
                gradient != null ? pupil.GradientBlend : 0f);
            SetVector(
                material,
                "_grad",
                new Vector4(
                    0f,
                    Mathf.Lerp(-0.5f, 0.5f, pupil.GradientOffsetY),
                    0f,
                    Mathf.Lerp(-1f, 1f, pupil.GradientScale)));

            SetTexture(
                material,
                "_overtex1",
                highlightUp ?? Texture2D.blackTexture);
            SetTexture(
                material,
                "_overtex2",
                highlightDown ?? Texture2D.blackTexture);
            SetColor(
                material,
                "_overcolor1",
                highlightUp != null
                    ? appearance.HighlightUpColor
                    : Color.clear);
            SetColor(
                material,
                "_overcolor2",
                highlightDown != null
                    ? appearance.HighlightDownColor
                    : Color.clear);
            SetTextureOffset(
                material,
                "_overtex1",
                new Vector2(
                    0f,
                    Mathf.Lerp(0.1f, -0.1f, appearance.HighlightUpY)));
            SetTextureOffset(
                material,
                "_overtex2",
                new Vector2(
                    0f,
                    Mathf.Lerp(0.1f, -0.1f, appearance.HighlightDownY)));
            SetFloat(
                material,
                "_rotation",
                rotation);
        }

        private static void ConfigureFallbackMaterial(
            Material material,
            KoikatsuCardPupil pupil,
            Texture2D gradient,
            Texture2D highlightUp,
            Texture2D highlightDown,
            KoikatsuCardFaceAppearance appearance,
            bool rightEye,
            float rotation)
        {
            material.SetTexture("_ColorMask", gradient ?? Texture2D.whiteTexture);
            material.SetTexture(
                "_HighlightUp",
                highlightUp ?? Texture2D.whiteTexture);
            material.SetTexture(
                "_HighlightDown",
                highlightDown ?? Texture2D.whiteTexture);
            material.SetColor("_BaseColor", pupil.BaseColor);
            material.SetColor("_SubColor", pupil.SubColor);
            material.SetColor("_HighlightUpColor", appearance.HighlightUpColor);
            material.SetColor(
                "_HighlightDownColor",
                appearance.HighlightDownColor);
            material.SetFloat("_GradientBlend", pupil.GradientBlend);
            material.SetFloat(
                "_GradientOffsetY",
                Mathf.Lerp(-0.5f, 0.5f, pupil.GradientOffsetY));
            material.SetFloat(
                "_GradientScale",
                Mathf.Lerp(-1f, 1f, pupil.GradientScale));
            material.SetFloat(
                "_HighlightUpOffsetY",
                Mathf.Lerp(0.1f, -0.1f, appearance.HighlightUpY));
            material.SetFloat(
                "_HighlightDownOffsetY",
                Mathf.Lerp(0.1f, -0.1f, appearance.HighlightDownY));
            material.SetFloat("_HasGradient", gradient != null ? 1f : 0f);
            material.SetFloat("_HasHighlightUp", highlightUp != null ? 1f : 0f);
            material.SetFloat(
                "_HasHighlightDown",
                highlightDown != null ? 1f : 0f);
            material.SetFloat("_Rotation", rotation);
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

        private static void SetTextureOffset(
            Material material,
            string property,
            Vector2 offset)
        {
            if (material.HasProperty(property))
            {
                material.SetTextureOffset(property, offset);
            }
        }

        private static Texture2D BakeWhite(
            Shader shader,
            Texture2D source,
            Color baseColor,
            Color subColor)
        {
            var material = new Material(shader);
            try
            {
                material.SetColor("_BaseColor", baseColor);
                material.SetColor("_SubColor", subColor);
                return Render(material, source, 1, "Koikatsu Eye White");
            }
            finally
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }

        private static Texture2D Render(
            Material material,
            Texture source,
            int pass,
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
            var previousSrgbWrite = GL.sRGBWrite;
            Texture2D texture = null;
            try
            {
                GL.sRGBWrite = true;
                RenderTexture.active = target;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = previous;
                Graphics.Blit(source, target, material, pass);
                RenderTexture.active = target;
                texture = new Texture2D(
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
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply(false, false);
                return texture;
            }
            catch
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(texture);
                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static KoikatsuCardPupil GetPupil(
            IReadOnlyList<KoikatsuCardPupil> pupils,
            int index)
        {
            if (pupils == null || pupils.Count == 0)
            {
                return null;
            }

            return pupils[Math.Min(index, pupils.Count - 1)];
        }

        private static float GetEyeRotation(
            KoikatsuCardFace face,
            bool rightEye)
        {
            if (face.ShapeValues.Count <= EyeTiltShapeIndex)
            {
                return 0f;
            }

            var value = Mathf.Lerp(
                0.02f,
                -0.02f,
                face.ShapeValues[EyeTiltShapeIndex]);
            return rightEye ? -value : value;
        }

        private static Texture2D LoadEyeOverlay(
            KoikatsuTextureLoader textureLoader,
            int coordinateIndex,
            KoikatsuSkinOverlayType specificType,
            KoikatsuSkinOverlayType genericType)
        {
            return textureLoader.LoadSkinOverlayTexture(
                       coordinateIndex,
                       specificType) ??
                   textureLoader.LoadSkinOverlayTexture(
                       coordinateIndex,
                       genericType);
        }

        private static void CalculateUvTransform(
            KoikatsuCardFaceAppearance appearance,
            bool rightEye,
            out Vector2 scale,
            out Vector2 offset)
        {
            var customScale = new Vector2(
                Mathf.Lerp(1.8f, -0.2f, appearance.PupilWidth),
                Mathf.Lerp(1.8f, -0.2f, appearance.PupilHeight));
            scale = Vector2.one + customScale;

            var eyeOffset = new Vector2(
                Mathf.Lerp(0.2f, -0.6f, appearance.PupilX),
                Mathf.Lerp(-0.5f, 0.5f, appearance.PupilY));
            if (rightEye)
            {
                eyeOffset.x *= -1f;
            }

            if (eyeOffset.sqrMagnitude > 1f)
            {
                eyeOffset.Normalize();
            }

            var motion = new Vector2(
                eyeOffset.x * 0.1f * Mathf.Lerp(1f, 5f, customScale.x),
                -eyeOffset.y * 0.1f * Mathf.Lerp(1f, 5f, customScale.y));
            offset = motion - customScale * 0.5f;
        }
    }
}
