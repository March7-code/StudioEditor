using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuBodyMaskBaker
    {
        private const string ShaderResourcePath =
            "Shaders/KoikatsuBodyMaskBake";

        public static Texture2D Bake(
            Texture2D source,
            Texture2D alphaMask,
            ICollection<Texture2D> runtimeTextures)
        {
            return Bake(
                source,
                alphaMask,
                true,
                true,
                runtimeTextures);
        }

        public static Texture2D Bake(
            Texture2D source,
            Texture2D alphaMask,
            bool useRedChannel,
            bool useGreenChannel,
            ICollection<Texture2D> runtimeTextures)
        {
            return Bake(
                source,
                alphaMask,
                Vector2.one,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                useRedChannel,
                useGreenChannel,
                runtimeTextures);
        }

        public static Texture2D Bake(
            Texture2D source,
            Texture2D alphaMask,
            Vector2 mainScale,
            Vector2 mainOffset,
            Vector2 maskScale,
            Vector2 maskOffset,
            ICollection<Texture2D> runtimeTextures)
        {
            return Bake(
                source,
                alphaMask,
                mainScale,
                mainOffset,
                maskScale,
                maskOffset,
                true,
                true,
                runtimeTextures);
        }

        public static Texture2D Bake(
            Texture2D source,
            Texture2D alphaMask,
            Vector2 mainScale,
            Vector2 mainOffset,
            Vector2 maskScale,
            Vector2 maskOffset,
            bool useRedChannel,
            bool useGreenChannel,
            ICollection<Texture2D> runtimeTextures)
        {
            if (source == null || alphaMask == null)
            {
                return source;
            }

            if (runtimeTextures == null)
            {
                throw new ArgumentNullException(nameof(runtimeTextures));
            }

            var shader = Resources.Load<Shader>(ShaderResourcePath) ??
                         Shader.Find(
                             "Hidden/StudioEditor/KoikatsuBodyMaskBake");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Koikatsu body mask bake shader could not be loaded.");
            }

            var material = new Material(shader);
            var target = RenderTexture.GetTemporary(
                Math.Max(source.width, 1),
                Math.Max(source.height, 1),
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                material.SetTexture("_AlphaMask", alphaMask);
                var sampleScale = new Vector2(
                    maskScale.x / NonZero(mainScale.x),
                    maskScale.y / NonZero(mainScale.y));
                var sampleOffset = maskOffset -
                    Vector2.Scale(mainOffset, sampleScale);
                material.SetVector("_MaskScale", sampleScale);
                material.SetVector("_MaskOffset", sampleOffset);
                material.SetVector(
                    "_MaskChannels",
                    new Vector4(
                        useRedChannel ? 1f : 0f,
                        useGreenChannel ? 1f : 0f,
                        0f,
                        0f));
                Graphics.Blit(source, target, material, 0);
                RenderTexture.active = target;
                result = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    name = source.name + " (Koikatsu Body Masked)",
                    filterMode = source.filterMode,
                    wrapMode = source.wrapMode,
                };
                result.ReadPixels(
                    new Rect(0f, 0f, target.width, target.height),
                    0,
                    0);
                result.Apply(false, false);
                runtimeTextures.Add(result);
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
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }

        private static float NonZero(float value)
        {
            return Mathf.Abs(value) > 0.000001f ? value : 1f;
        }
    }
}
