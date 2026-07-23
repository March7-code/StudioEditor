using System;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering.RenderSchemes
{
    public sealed class DefaultAnimeCharacterRenderScheme :
        ICharacterRenderScheme
    {
        public const string SchemeId = "bodyeditor.default-anime";

        private const string ShaderResourcePath =
            "Shaders/StudioEditorAnimeCharacter";

        private const string ShaderName = "StudioEditor/AnimeCharacter";

        public string Id => SchemeId;

        public Material CreateMaterial(CharacterRenderMaterialContext context)
        {
            var shader = ResolveShader();
            var material = new Material(shader)
            {
                name = string.IsNullOrWhiteSpace(context.MaterialName)
                    ? "Studio Editor Anime Material"
                    : context.MaterialName,
            };

            MaterialRenderUtility.SetBaseColor(material, context.BaseColor);
            MaterialRenderUtility.CopySourceRenderState(
                context.SourceMaterial,
                material);
            CopyKoikatsuMaterialState(
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
            var lightColorInfluence = 0.20f;
            var specularPower = 40f;
            var rimPower = 4f;
            var outlineWidth = 0.08f;
            var bandThresholds =
                new Vector4(0.18f, 0.38f, 0.58f, 0.78f);
            var bandSoftness = 0.004f;

            switch (context.Role)
            {
                case CharacterRenderSurfaceRole.Skin:
                    deepShadow = new Color(0.58f, 0.46f, 0.50f, 1f);
                    shadow = new Color(0.82f, 0.70f, 0.72f, 1f);
                    ambientStrength = 0.32f;
                    lightColorInfluence = 0.08f;
                    specularPower = 32f;
                    outlineWidth = 0.06f;
                    bandThresholds =
                        new Vector4(0.18f, 0.45f, 0.51f, 0.78f);
                    bandSoftness = 0.014f;
                    break;
                case CharacterRenderSurfaceRole.Face:
                    deepShadow = new Color(0.62f, 0.49f, 0.52f, 1f);
                    shadow = new Color(0.85f, 0.73f, 0.74f, 1f);
                    ambientStrength = 0.36f;
                    lightColorInfluence = 0.05f;
                    specularPower = 28f;
                    outlineWidth = 0.05f;
                    bandThresholds =
                        new Vector4(0.18f, 0.45f, 0.51f, 0.78f);
                    bandSoftness = 0.014f;
                    break;
                case CharacterRenderSurfaceRole.Hair:
                    deepShadow = new Color(0.34f, 0.34f, 0.34f, 1f);
                    shadow = new Color(0.82f, 0.82f, 0.82f, 1f);
                    ambientStrength = 0.25f;
                    lightColorInfluence = 0.12f;
                    specularPower = 64f;
                    rimPower = 3f;
                    outlineWidth = 0.12f;
                    break;
                case CharacterRenderSurfaceRole.Accessory:
                    outlineWidth = 0.06f;
                    break;
            }

            SetColor(material, "_DeepShadowColor", deepShadow);
            SetColor(material, "_ShadowColor", shadow);
            var sourceShadowColor = HasMaterialEditorOverride(
                                        context.SourceMaterial,
                                        "ShadowColor")
                ? FindColor(
                    context.SourceMaterial,
                    "ShadowColor",
                    "_ShadowColor")
                : null;
            if (sourceShadowColor.HasValue)
            {
                SetColor(material, "_ShadowColor", sourceShadowColor.Value);
            }
            SetVector(
                material,
                "_BandThresholds",
                bandThresholds);
            SetFloat(material, "_BandSoftness", bandSoftness);
            SetFloat(material, "_AmbientStrength", ambientStrength);
            SetFloat(material, "_LightColorInfluence", lightColorInfluence);
            SetFloat(material, "_SpecularStrength", 0f);
            SetFloat(material, "_SpecularPower", specularPower);
            SetFloat(material, "_RimStrength", 0f);
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

            // Keep Koikatsu's material color channels available to the render
            // shader. Face materials use these same channels for makeup and
            // must retain the values copied from the original material.
            SetColor(material, "_Color", context.BaseColor);
            if (context.Role == CharacterRenderSurfaceRole.Hair ||
                context.Role == CharacterRenderSurfaceRole.Clothes ||
                context.Role == CharacterRenderSurfaceRole.Accessory)
            {
                SetColor(
                    material,
                    "_Color2",
                    context.SecondaryColor ?? context.BaseColor);
                SetColor(
                    material,
                    "_Color3",
                    context.TertiaryColor ?? context.BaseColor);
                SetColor(
                    material,
                    "_Color4",
                    context.QuaternaryColor ?? context.BaseColor);
            }
            SetFloat(
                material,
                "_UseHairGradient",
                0f);
            SetFloat(
                material,
                "_UseVertexColorChannels",
                context.UseVertexColorChannels ? 1f : 0f);
            SetFloat(
                material,
                "_UseFlatColor",
                IsFlatColorDetail(context.MaterialKey) ? 1f : 0f);
            SetFloat(
                material,
                "_FaceSphereNormalBlend",
                context.Role == CharacterRenderSurfaceRole.Face &&
                !IsFaceDetail(context.MaterialKey)
                    ? 0.85f
                    : 0f);
            SetFloat(
                material,
                "_FaceSphereLowerCylinder",
                context.Role == CharacterRenderSurfaceRole.Face &&
                !IsFaceDetail(context.MaterialKey)
                    ? 1f
                    : 0f);
            SetFloat(material, "_UseToon", 1f);
            if (context.HairGlossTexture != null)
            {
                SetTexture(
                    material,
                    "_HairGloss",
                    context.HairGlossTexture);
            }
            var hairGloss = context.HairGlossTexture ?? FindTexture(
                context.SourceMaterial,
                "_HairGloss");
            SetFloat(
                material,
                "_UseHairGloss",
                hairGloss != null ? 1f : 0f);

            var normalMap = FindTexture(
                context.SourceMaterial,
                "_BumpMap",
                "_NormalMap",
                "_NormalTex",
                "BumpMap",
                "NormalMap",
                "NormalTex");
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
                "_LightMapMask",
                "LightMap",
                "LightMapTex",
                "LightMapTexture",
                "LightMapMask");
            if (styleMask != null)
            {
                SetTexture(material, "_StyleMask", styleMask);
                SetFloat(material, "_StyleMaskStrength", 1f);
            }

            var metallicMap = FindTexture(
                context.SourceMaterial,
                "_MetallicGlossMap",
                "MetallicGlossMap");
            if (metallicMap != null)
            {
                SetTexture(material, "_MetallicGlossMap", metallicMap);
                SetFloat(material, "_MetallicMapStrength", 1f);
            }

            var specularMap = FindTexture(
                context.SourceMaterial,
                "_SpecGlossMap",
                "SpecGlossMap");
            if (specularMap != null)
            {
                SetTexture(material, "_SpecGlossMap", specularMap);
                SetFloat(material, "_SpecularMapStrength", 1f);
            }

            // Ramp textures are shader-specific LUTs. Import one only when the
            // caller explicitly guarantees the StudioEditor ramp layout.
            if (context.ToonRampTexture != null)
            {
                SetTexture(material, "_RampMap", context.ToonRampTexture);
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
            SetFloat(
                material,
                "_SpecularPower",
                Mathf.Clamp(
                    FindFloat(
                        context.SourceMaterial,
                        specularPower,
                        "SpecularPower",
                        "_SpecularPower"),
                    1f,
                    128f));

            var specularColor = FindColor(
                context.SourceMaterial,
                "SpecularColor",
                "_SpecularColor",
                "_SpecColor");
            if (!HasMaterialEditorOverride(
                    context.SourceMaterial,
                    "SpecularColor"))
            {
                specularColor = null;
            }
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

        private static bool IsFlatColorDetail(string materialKey)
        {
            return materialKey.Contains("mayuge") ||
                   materialKey.Contains("eyeline") ||
                   materialKey.Contains("noseline") ||
                   materialKey.Contains("namida");
        }

        private static bool HasMaterialEditorOverride(
            Material material,
            string propertyName)
        {
            return material != null &&
                   string.Equals(
                       material.GetTag(
                           "StudioEditor.MaterialEditor." + propertyName,
                           false,
                           string.Empty),
                       "1",
                       StringComparison.Ordinal);
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

        private static void CopyKoikatsuMaterialState(
            Material source,
            Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            var textureProperties = new[]
            {
                "_MainTex",
                "_Texture2",
                "_Texture3",
                "_Texture4",
                "_Texture5",
                "_Texture6",
                "_Texture7",
                "_ColorMask",
                "_AlphaMask",
                "_DetailMask",
                "_NormalMapDetail",
                "_LineMask",
                "_HairGloss",
                "_NormalMap",
                "_overtex1",
                "_overtex2",
                "_overtex3",
                "_paint1",
                "_paint2",
                "_hokuro",
            };
            for (var index = 0; index < textureProperties.Length; index++)
            {
                var property = textureProperties[index];
                if (!source.HasProperty(property) ||
                    !destination.HasProperty(property))
                {
                    continue;
                }

                var texture = source.GetTexture(property);
                destination.SetTexture(property, texture);
                destination.SetTextureScale(
                    property,
                    source.GetTextureScale(property));
                destination.SetTextureOffset(
                    property,
                    source.GetTextureOffset(property));
            }

            var colorProperties = new[]
            {
                "_Color",
                "_Color1_2",
                "_Color2",
                "_Color2_2",
                "_Color3",
                "_Color3_2",
                "_Color4",
                "_Color4_2",
                "_Color5",
                "_Color6",
                "_Color7",
                "_LineColor",
                "_overcolor1",
                "_overcolor2",
                "_overcolor3",
            };
            for (var index = 0; index < colorProperties.Length; index++)
            {
                var property = colorProperties[index];
                if (source.HasProperty(property) &&
                    destination.HasProperty(property))
                {
                    destination.SetColor(property, source.GetColor(property));
                }
            }

            var floatProperties = new[]
            {
                "_Blend",
                "_exppower",
                "_isHighLight",
                "_reverse",
                "_rotation",
                "_alpha_a",
                "_alpha_b",
                "_nipsize",
                "_linetexon",
                "_DetailNormalMapScale",
                "_SpecularPowerNail",
                "_liquidftop",
                "_liquidfbot",
                "_liquidbtop",
                "_liquidbbot",
                "_liquidface",
                "_PatternScale1u",
                "_PatternScale1v",
                "_PatternScale2u",
                "_PatternScale2v",
                "_PatternScale3u",
                "_PatternScale3v",
                "_PatternScale4u",
                "_PatternScale4v",
                "_TileAnimation",
                "_SizeSpeed",
                "_SizeWidth",
                "_angleSpeed",
                "_yurayura",
                "_nip_specular",
            };
            for (var index = 0; index < floatProperties.Length; index++)
            {
                var property = floatProperties[index];
                if (source.HasProperty(property) &&
                    destination.HasProperty(property))
                {
                    destination.SetFloat(property, source.GetFloat(property));
                }
            }

            var vectorProperties = new[]
            {
                "_grad",
            };
            for (var index = 0; index < vectorProperties.Length; index++)
            {
                var property = vectorProperties[index];
                if (source.HasProperty(property) &&
                    destination.HasProperty(property))
                {
                    destination.SetVector(property, source.GetVector(property));
                }
            }
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
