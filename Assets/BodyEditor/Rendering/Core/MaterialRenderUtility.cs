using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.Rendering
{
    public static class MaterialRenderUtility
    {
        public static void SetBaseColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }

        public static void SetMainTexture(Material material, Texture texture)
        {
            if (material == null)
            {
                return;
            }

            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
        }

        public static void SetMainTextureTransform(
            Material material,
            Vector2 scale,
            Vector2 offset)
        {
            if (material == null)
            {
                return;
            }

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

        public static void CopyMainTextureTransform(
            Material source,
            Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            SetMainTextureTransform(
                destination,
                source.mainTextureScale,
                source.mainTextureOffset);
        }

        public static void CopySourceRenderState(
            Material source,
            Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            if (source.HasProperty("_Cull") && destination.HasProperty("_Cull"))
            {
                destination.SetFloat("_Cull", source.GetFloat("_Cull"));
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
                ConfigureCutout(destination, cutoff);
                return;
            }

            if (string.Equals(
                    renderType,
                    "Transparent",
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureTransparent(destination);
            }
        }

        public static void ConfigureTransparent(
            Material material,
            int renderQueue = (int)RenderQueue.Transparent)
        {
            if (material == null)
            {
                return;
            }

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
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.OneMinusSrcAlpha);
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

        public static void ConfigureCutout(Material material, float cutoff)
        {
            if (material == null)
            {
                return;
            }

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
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 1f);
            }

            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
    }
}
