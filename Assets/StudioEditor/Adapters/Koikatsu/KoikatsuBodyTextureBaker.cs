using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuBodyTextureBaker
    {
        public static Texture2D Bake(Texture2D source, Texture2D colorMask,
            KoikatsuCardBodyAppearance appearance, KoikatsuTextureLoader loader,
            KoikatsuListCatalog catalog, ICollection<Texture2D> runtimeTextures)
        {
            var original = loader.LoadVanillaMaterial("chara/mm_base.unity3d", "cf_m_body_create");
            if (source == null || original?.shader == null || !original.shader.isSupported) return source;
            var material = new Material(original);
            RenderTexture target = null;
            Texture2D result = null;
            var active = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            try
            {
                material.SetTexture("_MainTex", source);
                material.SetTexture("_ColorMask", colorMask);
                // Legacy bundle shaders do not carry the current shader's
                // color-property metadata. Upload linear uniforms explicitly,
                // then encode the output once, just like the face baker.
                void SetColor(string property, Color color) => material.SetVector(property,
                    QualitySettings.activeColorSpace == ColorSpace.Linear ? (Vector4)color.linear : color);
                SetColor("_Color", appearance.SkinMainColor);
                SetColor("_Color2", appearance.SkinSubColor);
                SetColor("_Color5", appearance.NailColor);
                material.SetTexture("_Texture4", loader.LoadCatalogTexture(422, appearance.SunburnId,
                    "MainAB", "SunburnTex", "ChaFileBody.sunburnId"));
                SetColor("_Color4", appearance.SunburnColor);
                for (int i = 0; i < 2; i++)
                {
                    int id = i < appearance.PaintIds.Count ? appearance.PaintIds[i] : 0;
                    string textureProperty = i == 0 ? "_Texture3" : "_Texture6";
                    material.SetTexture(textureProperty, loader.LoadCatalogTexture(421, id,
                        "MainAB", "PaintTex", "ChaFileBody.paintId"));
                    SetColor(i == 0 ? "_Color3" : "_Color6",
                        i < appearance.PaintColors.Count ? appearance.PaintColors[i] : Color.clear);
                    if (i < appearance.PaintLayouts.Count && i < appearance.PaintLayoutIds.Count &&
                        catalog.TryGet(3, appearance.PaintLayoutIds[i], out var layout))
                    {
                        var rate = appearance.PaintLayouts[i];
                        float Read(string key) => float.TryParse(layout.Get(key),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                            out var value) ? value : 0f;
                        var center = new Vector4(Read("CenterX"), Read("CenterY"), 0, Read("CenterScale"));
                        var move = new Vector4(Read("MoveX"), Read("MoveY"), 0, Read("AddScale"));
                        material.SetVector(i == 0 ? "_paint1" : "_paint2", new Vector4(
                            center.x + Mathf.Lerp(move.x, -move.x, rate.x),
                            center.y + Mathf.Lerp(move.y, -move.y, rate.y),
                            Mathf.Lerp(-1, 1, rate.z), center.w + Mathf.Lerp(-move.w, move.w, rate.w)));
                    }
                }
                target = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture.active = target;
                GL.Clear(false, true, Color.clear);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, target, material, 0);
                RenderTexture.active = target;
                result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false)
                    { name = "Koikatsu Body Create", wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                result.Apply();
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
                RenderTexture.active = active;
                GL.sRGBWrite = previousSrgbWrite;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                KoikatsuCharacterAssembler.DestroyRuntimeObject(material);
            }
        }
    }
}
