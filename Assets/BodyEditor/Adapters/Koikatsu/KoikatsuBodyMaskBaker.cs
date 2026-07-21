using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.ReferenceModels
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
                             "Hidden/BodyEditor/KoikatsuBodyMaskBake");
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
    }
}
