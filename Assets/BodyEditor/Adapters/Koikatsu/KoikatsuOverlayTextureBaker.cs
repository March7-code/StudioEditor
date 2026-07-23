using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuOverlayTextureBaker
    {
        private const string ShaderResourcePath =
            "Shaders/KoikatsuOverlayBake";

        public static Texture2D Composite(
            Texture2D source,
            Texture2D overlay,
            ICollection<Texture2D> runtimeTextures,
            string name,
            bool compositeOverlayAlpha = false)
        {
            if (source == null || overlay == null)
            {
                return source;
            }

            if (runtimeTextures == null)
            {
                throw new ArgumentNullException(nameof(runtimeTextures));
            }

            var shader = Resources.Load<Shader>(ShaderResourcePath) ??
                         Shader.Find("Hidden/BodyEditor/KoikatsuOverlayBake");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The Koikatsu overlay bake shader could not be loaded.");
            }

            var material = new Material(shader);
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
                material.SetTexture("_Overlay", overlay);
                material.SetFloat(
                    "_CompositeOverlayAlpha",
                    compositeOverlayAlpha ? 1f : 0f);
                GL.sRGBWrite = true;
                Graphics.Blit(source, target, material, 0);
                RenderTexture.active = target;
                result = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGBA32,
                    false,
                    false)
                {
                    name = string.IsNullOrWhiteSpace(name)
                        ? source.name + " (KSOX overlay)"
                        : name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
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
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }
    }
}
