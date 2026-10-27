using System;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuSceneMaterialUtility
    {
        private const string EffectShader = "StudioEditor/KoikatsuSceneEffect";
        private const string SkyShader = "StudioEditor/KoikatsuSkyCubemap";

        public static Material TryCreate(Material source, string name, Color color)
        {
            if (source == null || source.shader == null) return null;
            var shaderName = source.shader.name;
            if (shaderName == "Shader Forge/main_item_studio")
            {
                // Convert before URP/Lit discards the legacy color mask and ramp inputs.
                var item = Create("Shaders/KoikatsuStudioItem", "StudioEditor/KoikatsuStudioItem", name);
                MaterialRenderUtility.CopySourceRenderState(source, item);
                item.SetFloat("_SourceItemModel", 1f);
                item.SetFloat("_UseColorMask", 1f);
                item.SetVector("_ChannelEnabled", Vector4.one);
                foreach (var property in new[] { "_MainTex", "_ColorMask", "_NormalMap", "_DetailMask", "_LineMask", "_AnotherRamp" })
                {
                    if (!source.HasProperty(property)) continue;
                    item.SetTexture(property, source.GetTexture(property));
                    item.SetTextureScale(property, source.GetTextureScale(property));
                    item.SetTextureOffset(property, source.GetTextureOffset(property));
                }
                foreach (var property in new[] { "_ShadowColor" })
                    if (source.HasProperty(property)) item.SetColor(property, source.GetColor(property));
                foreach (var property in new[] { "_ShadowExtend", "_ShadowExtendAnother", "_AnotherRampFull", "_DetailRLineR", "_SpecularPower", "_SpeclarHeight", "_rimpower", "_rimV" })
                    if (source.HasProperty(property)) item.SetFloat(property, source.GetFloat(property));
                for (int channel = 1; channel <= 3; channel++)
                {
                    var property = channel == 1 ? "_Color" : $"_Color{channel}";
                    if (source.HasProperty(property)) item.SetColor($"_ChannelColor{channel}", source.GetColor(property));
                }
                return item;
            }
            if (shaderName == "Skybox/Cubemap" && source.HasProperty("_Tex"))
            {
                var material = Create("Shaders/KoikatsuSkyCubemap", SkyShader, name);
                material.SetTexture("_Tex", source.GetTexture("_Tex"));
                foreach (var property in new[] { "_Exposure", "_Rotation" })
                    if (source.HasProperty(property)) material.SetFloat(property, source.GetFloat(property));
                if (source.HasProperty("_Tint")) material.SetColor("_Tint", source.GetColor("_Tint"));
                material.renderQueue = (int)RenderQueue.Background;
                return material;
            }

            bool additive = shaderName == "Shader Forge/main_item_studio_add";
            bool animatedEye = shaderName == "Shader Forge/toon_textureanimation";
            if (!additive && !animatedEye && !shaderName.StartsWith("Unlit/", StringComparison.Ordinal)) return null;
            var effect = Create("Shaders/KoikatsuSceneEffect", EffectShader, name);
            MaterialRenderUtility.SetBaseColor(effect, color);
            MaterialRenderUtility.CopySourceRenderState(source, effect);
            if (animatedEye)
            {
                effect.SetFloat("_TextureAnimation", 1f);
                foreach (var property in new[] { "_SizeSpeed", "_SizeWidth", "_AngleSpeed", "_yurayura" })
                    if (source.HasProperty(property)) effect.SetFloat(property, source.GetFloat(property));
                if (source.HasProperty("_TileAnimation")) effect.SetVector("_TileAnimation", source.GetVector("_TileAnimation"));
                MaterialRenderUtility.ConfigureCutout(effect, 0.5f);
            }
            if (additive)
            {
                MaterialRenderUtility.ConfigureTransparent(effect);
                effect.SetFloat("_SourceAdditiveColorChannels", 1f);
                effect.SetFloat("_SrcBlend", (float)BlendMode.One);
                effect.SetFloat("_DstBlend", (float)BlendMode.One);
                foreach (var property in new[] { "_Color2", "_Color3" })
                    if (source.HasProperty(property)) effect.SetColor(property, source.GetColor(property));
                if (source.HasProperty("_alpha"))
                    effect.SetFloat("_alpha", source.GetFloat("_alpha"));
            }
            // Studio effects intentionally render after the character passes.
            // Their black texels are harmless with additive blending, but opaque
            // under ordinary alpha blending when the texture has alpha = 1.
            effect.renderQueue = source.renderQueue;
            return effect;
        }

        public static bool IsEffect(Material material)
        {
            return material != null && material.shader != null && material.shader.name == EffectShader;
        }

        public static bool IsSky(Material material)
        {
            return material != null && material.shader != null && material.shader.name == SkyShader;
        }

        public static bool UsesAdditiveColorChannels(Material material)
        {
            return IsEffect(material) && material.GetFloat("_SourceAdditiveColorChannels") > 0.5f;
        }

        private static Material Create(string path, string shaderName, string name)
        {
            var shader = Resources.Load<Shader>(path) ?? Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Missing Koikatsu scene shader '{shaderName}'.");
            return new Material(shader) { name = name };
        }
    }
}
