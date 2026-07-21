using System;
using BodyEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.Rendering.RenderSchemes
{
    public sealed class DefaultAnimeCharacterRenderScheme :
        ICharacterRenderScheme
    {
        public const string SchemeId = "bodyeditor.default-anime";

        private const string ShaderResourcePath =
            "Shaders/BodyEditorAnimeCharacter";

        private const string ShaderName = "BodyEditor/AnimeCharacter";

        public string Id => SchemeId;

        public Material CreateMaterial(CharacterRenderMaterialContext context)
        {
            var shader = ResolveShader();
            var material = new Material(shader)
            {
                name = string.IsNullOrWhiteSpace(context.MaterialName)
                    ? "Body Editor Anime Material"
                    : context.MaterialName,
            };

            MaterialRenderUtility.SetBaseColor(material, context.BaseColor);
            MaterialRenderUtility.CopySourceRenderState(
                context.SourceMaterial,
                material);
            ConfigureCharacterMaterial(material, context);
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntime()
        {
            CharacterRenderSchemeRegistry.Register(
                new DefaultAnimeCharacterRenderScheme(),
                true);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor()
        {
            CharacterRenderSchemeRegistry.Register(
                new DefaultAnimeCharacterRenderScheme(),
                true);
        }
#endif

        private static Shader ResolveShader()
        {
            var shader = Resources.Load<Shader>(ShaderResourcePath) ??
                         Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The default anime character shader could not be loaded.");
            }

            return shader;
        }

        private static void ConfigureCharacterMaterial(
            Material material,
            CharacterRenderMaterialContext context)
        {
            var deepShadow = new Color(0.42f, 0.45f, 0.52f, 1f);
            var shadow = new Color(0.72f, 0.75f, 0.80f, 1f);
            var ambientStrength = 0.25f;
            var specularStrength = 0.25f;
            var specularPower = 40f;
            var rimStrength = 0.12f;
            var rimPower = 4f;
            var outlineWidth = 0.08f;

            switch (context.Role)
            {
                case CharacterRenderSurfaceRole.Skin:
                    deepShadow = new Color(0.58f, 0.46f, 0.50f, 1f);
                    shadow = new Color(0.82f, 0.70f, 0.72f, 1f);
                    ambientStrength = 0.32f;
                    specularStrength = 0.12f;
                    specularPower = 32f;
                    rimStrength = 0.08f;
                    outlineWidth = 0.06f;
                    break;
                case CharacterRenderSurfaceRole.Face:
                    deepShadow = new Color(0.62f, 0.49f, 0.52f, 1f);
                    shadow = new Color(0.85f, 0.73f, 0.74f, 1f);
                    ambientStrength = 0.36f;
                    specularStrength = 0.08f;
                    specularPower = 28f;
                    rimStrength = 0.06f;
                    outlineWidth = 0.05f;
                    break;
                case CharacterRenderSurfaceRole.Hair:
                    deepShadow = new Color(0.32f, 0.36f, 0.48f, 1f);
                    shadow = new Color(0.65f, 0.68f, 0.78f, 1f);
                    ambientStrength = 0.25f;
                    specularStrength = 0.55f;
                    specularPower = 64f;
                    rimStrength = 0.22f;
                    rimPower = 3f;
                    outlineWidth = 0.12f;
                    break;
                case CharacterRenderSurfaceRole.Accessory:
                    specularStrength = 0.30f;
                    outlineWidth = 0.06f;
                    break;
            }

            SetColor(material, "_DeepShadowColor", deepShadow);
            SetColor(material, "_ShadowColor", shadow);
            SetVector(
                material,
                "_BandThresholds",
                new Vector4(0.18f, 0.38f, 0.58f, 0.78f));
            SetFloat(material, "_BandSoftness", 0.015f);
            SetFloat(material, "_AmbientStrength", ambientStrength);
            SetFloat(material, "_SpecularStrength", specularStrength);
            SetFloat(material, "_SpecularPower", specularPower);
            SetFloat(material, "_RimStrength", rimStrength);
            SetFloat(material, "_RimPower", rimPower);

            var outlineColor = context.RequestedOutlineColor ??
                               FindColor(
                                   context.SourceMaterial,
                                   "_LineColor",
                                   "_OutlineColor") ??
                               new Color(
                                   context.BaseColor.r * 0.18f,
                                   context.BaseColor.g * 0.18f,
                                   context.BaseColor.b * 0.18f,
                                   1f);
            outlineColor.a = 1f;
            if (IsFaceDetail(context.MaterialKey) ||
                GetFloat(material, "_Surface", 0f) > 0.5f)
            {
                outlineWidth = 0f;
            }

            SetColor(material, "_OutlineColor", outlineColor);
            SetFloat(material, "_OutlineWidth", outlineWidth);

            var normalMap = FindTexture(
                context.SourceMaterial,
                "_BumpMap",
                "_NormalMap",
                "_NormalTex");
            if (normalMap != null)
            {
                SetTexture(material, "_NormalMap", normalMap);
                SetTexture(material, "_BumpMap", normalMap);
                var normalStrength = Mathf.Clamp(
                    FindFloat(
                        context.SourceMaterial,
                        1f,
                        "_BumpScale",
                        "_NormalScale"),
                    0f,
                    2f);
                SetFloat(material, "_NormalStrength", normalStrength);
                SetFloat(material, "_BumpScale", normalStrength);
                material.EnableKeyword("_NORMALMAP");
            }

            var styleMask = FindTexture(
                context.SourceMaterial,
                "_LightMap",
                "_LightMapTex",
                "_LightMapTexture",
                "_LightMapMask");
            if (styleMask != null)
            {
                SetTexture(material, "_StyleMask", styleMask);
                SetFloat(material, "_StyleMaskStrength", 1f);
            }

            var metallicMap = FindTexture(
                context.SourceMaterial,
                "_MetallicGlossMap");
            if (metallicMap != null)
            {
                SetTexture(material, "_MetallicGlossMap", metallicMap);
                SetFloat(material, "_MetallicMapStrength", 1f);
            }

            var specularMap = FindTexture(
                context.SourceMaterial,
                "_SpecGlossMap");
            if (specularMap != null)
            {
                SetTexture(material, "_SpecGlossMap", specularMap);
                SetFloat(material, "_SpecularMapStrength", 1f);
            }

            var ramp = FindTexture(
                context.SourceMaterial,
                "_ShadowRamp",
                "_Shadow_Ramp",
                "_RampTex",
                "_AnotherRamp");
            if (ramp != null)
            {
                SetTexture(material, "_RampMap", ramp);
                SetFloat(material, "_RampStrength", 1f);
            }

            SetFloat(
                material,
                "_Metallic",
                Mathf.Clamp01(FindFloat(
                    context.SourceMaterial,
                    0f,
                    "_Metallic")));
            SetFloat(
                material,
                "_Smoothness",
                Mathf.Clamp01(FindFloat(
                    context.SourceMaterial,
                    context.Role == CharacterRenderSurfaceRole.Hair
                        ? 0.65f
                        : 0.35f,
                    "_Smoothness",
                    "_Glossiness",
                    "_GlossMapScale")));

            var specularColor = FindColor(
                context.SourceMaterial,
                "_SpecColor");
            if (specularColor.HasValue)
            {
                SetColor(material, "_SpecularColor", specularColor.Value);
            }

            var emissionColor = FindColor(
                context.SourceMaterial,
                "_EmissionColor");
            var emissionEnabled = context.SourceMaterial != null &&
                (context.SourceMaterial.IsKeywordEnabled("_EMISSION") ||
                 (context.SourceMaterial.globalIlluminationFlags &
                  MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0);
            if (emissionEnabled && emissionColor.HasValue)
            {
                var emissionMap = FindTexture(
                    context.SourceMaterial,
                    "_EmissionMap");
                if (emissionMap != null)
                {
                    SetTexture(material, "_EmissionMap", emissionMap);
                }

                SetColor(material, "_EmissionColor", emissionColor.Value);
            }
        }

        private static bool IsFaceDetail(string materialKey)
        {
            return materialKey.Contains("hitomi") ||
                   materialKey.Contains("sirome") ||
                   materialKey.Contains("mayuge") ||
                   materialKey.Contains("eyeline") ||
                   materialKey.Contains("noseline") ||
                   materialKey.Contains("tooth") ||
                   materialKey.Contains("canine") ||
                   materialKey.Contains("tang") ||
                   materialKey.Contains("namida");
        }

        private static Texture FindTexture(
            Material material,
            params string[] properties)
        {
            if (material == null)
            {
                return null;
            }

            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (material.HasProperty(property))
                {
                    var texture = material.GetTexture(property);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        private static Color? FindColor(
            Material material,
            params string[] properties)
        {
            if (material == null)
            {
                return null;
            }

            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (material.HasProperty(property))
                {
                    return material.GetColor(property);
                }
            }

            return null;
        }

        private static float FindFloat(
            Material material,
            float fallback,
            params string[] properties)
        {
            if (material == null)
            {
                return fallback;
            }

            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (material.HasProperty(property))
                {
                    return material.GetFloat(property);
                }
            }

            return fallback;
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

        private static float GetFloat(
            Material material,
            string property,
            float fallback)
        {
            return material.HasProperty(property)
                ? material.GetFloat(property)
                : fallback;
        }
    }
}
