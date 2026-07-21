using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.ReferenceModels
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
            if (string.IsNullOrEmpty(materialKey) ||
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
                         Shader.Find("Hidden/BodyEditor/KoikatsuEyeBake");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Koikatsu eye texture bake shader could not be loaded.");
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
                if (whiteOverride != null)
                {
                    result.White = whiteOverride;
                }
                else
                {
                    result.White = BakeWhite(
                        shader,
                        whiteSource,
                        appearance.EyeWhiteBaseColor,
                        appearance.EyeWhiteSubColor);
                    runtimeTextures.Add(result.White);
                }
            }

            var irisOverride = textureLoader
                .LoadMaterialEditorCharacterTexture(
                    "cf_m_hitomi_00",
                    "MainTex");
            if (irisOverride != null)
            {
                result.Left = CreateIrisOverride(
                    irisOverride,
                    appearance,
                    false);
                result.Right = CreateIrisOverride(
                    irisOverride,
                    appearance,
                    true);
            }
            else
            {
                result.Left = BakeIris(
                    shader,
                    face,
                    GetPupil(appearance.Pupils, 0),
                    highlightUp,
                    highlightDown,
                    false,
                    textureLoader,
                    runtimeTextures);
                result.Right = BakeIris(
                    shader,
                    face,
                    GetPupil(appearance.Pupils, 1),
                    highlightUp,
                    highlightDown,
                    true,
                    textureLoader,
                    runtimeTextures);
            }

            return result;
        }

        private static KoikatsuBakedEyeTexture CreateIrisOverride(
            Texture2D texture,
            KoikatsuCardFaceAppearance appearance,
            bool rightEye)
        {
            CalculateUvTransform(
                appearance,
                rightEye,
                out var scale,
                out var offset);
            return new KoikatsuBakedEyeTexture(texture, scale, offset);
        }

        private static KoikatsuBakedEyeTexture BakeIris(
            Shader shader,
            KoikatsuCardFace face,
            KoikatsuCardPupil pupil,
            Texture2D highlightUp,
            Texture2D highlightDown,
            bool rightEye,
            KoikatsuTextureLoader textureLoader,
            ICollection<Texture2D> runtimeTextures)
        {
            if (pupil == null)
            {
                return null;
            }

            var source = textureLoader.LoadCatalogTexture(
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

            var gradient = textureLoader.LoadCatalogTexture(
                EyeGradientCategory,
                pupil.GradientMaskId,
                "ColorMaskAB",
                "ColorMaskTex");
            var material = new Material(shader);
            try
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
                material.SetColor("_HighlightUpColor", face.Appearance.HighlightUpColor);
                material.SetColor(
                    "_HighlightDownColor",
                    face.Appearance.HighlightDownColor);
                material.SetFloat("_GradientBlend", pupil.GradientBlend);
                material.SetFloat(
                    "_GradientOffsetY",
                    Mathf.Lerp(-0.5f, 0.5f, pupil.GradientOffsetY));
                material.SetFloat(
                    "_GradientScale",
                    Mathf.Lerp(-1f, 1f, pupil.GradientScale));
                material.SetFloat(
                    "_HighlightUpOffsetY",
                    Mathf.Lerp(0.1f, -0.1f, face.Appearance.HighlightUpY));
                material.SetFloat(
                    "_HighlightDownOffsetY",
                    Mathf.Lerp(0.1f, -0.1f, face.Appearance.HighlightDownY));
                material.SetFloat("_HasGradient", gradient != null ? 1f : 0f);
                material.SetFloat("_HasHighlightUp", highlightUp != null ? 1f : 0f);
                material.SetFloat(
                    "_HasHighlightDown",
                    highlightDown != null ? 1f : 0f);
                material.SetFloat("_Rotation", GetEyeRotation(face, rightEye));

                var texture = Render(
                    material,
                    source,
                    0,
                    $"Koikatsu Eye {(rightEye ? "R" : "L")} {pupil.Id}");
                runtimeTextures.Add(texture);
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
            Texture2D texture = null;
            try
            {
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

            var motion = new Vector2(
                Mathf.Clamp(
                    eyeOffset.x * 0.1f * Mathf.Lerp(1f, 5f, customScale.x),
                    -0.1f,
                    0.1f),
                Mathf.Clamp(
                    eyeOffset.y * 0.1f * Mathf.Lerp(1f, 5f, customScale.y),
                    -0.08f,
                    0.08f));
            offset = motion - customScale * 0.5f;
        }
    }
}
