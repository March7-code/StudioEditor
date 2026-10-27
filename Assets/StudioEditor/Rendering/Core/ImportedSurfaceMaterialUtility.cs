using UnityEngine;

namespace StudioEditor.Rendering
{
    /// <summary>Transfers explicit surface channels to the URP fallback without inventing missing maps.</summary>
    public static class ImportedSurfaceMaterialUtility
    {
        public static void CopyToLit(Material source, Material destination)
        {
            if (source == null || destination == null || destination.shader == null ||
                destination.shader.name != "Universal Render Pipeline/Lit") return;
            CopyFloat(source, destination, "_Metallic", "_Metallic");
            if (!CopyFloat(source, destination, "_Smoothness", "_Smoothness"))
                CopyFloat(source, destination, "_Glossiness", "_Smoothness");
            if (CopyTexture(source, destination, "_BumpMap", "_BumpMap") ||
                CopyTexture(source, destination, "_NormalMap", "_BumpMap"))
            {
                destination.EnableKeyword("_NORMALMAP");
                if (!CopyFloat(source, destination, "_BumpScale", "_BumpScale"))
                    CopyFloat(source, destination, "_NormalStrength", "_BumpScale");
            }
            if (CopyTexture(source, destination, "_MetallicGlossMap", "_MetallicGlossMap"))
                destination.EnableKeyword("_METALLICSPECGLOSSMAP");
            CopyTexture(source, destination, "_OcclusionMap", "_OcclusionMap");
            CopyFloat(source, destination, "_OcclusionStrength", "_OcclusionStrength");
            if (source.HasProperty("_EmissionColor") && destination.HasProperty("_EmissionColor"))
            {
                var emission = source.GetColor("_EmissionColor");
                destination.SetColor("_EmissionColor", emission);
                CopyTexture(source, destination, "_EmissionMap", "_EmissionMap");
                bool emissive = source.IsKeywordEnabled("_EMISSION") ||
                    (source.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
                if (emissive && emission.maxColorComponent > 0) destination.EnableKeyword("_EMISSION");
            }
            // Gloss maps may encode either metalness or specular color. Preserve only explicit workflows.
            bool specular = source.shader.name == "Standard (Specular setup)" ||
                source.HasProperty("_WorkflowMode") && source.GetFloat("_WorkflowMode") < 0.5f;
            if (specular)
            {
                destination.SetFloat("_WorkflowMode", 0);
                destination.EnableKeyword("_SPECULAR_SETUP");
                if (source.HasProperty("_SpecColor")) destination.SetColor("_SpecColor", source.GetColor("_SpecColor"));
                if (CopyTexture(source, destination, "_SpecGlossMap", "_SpecGlossMap"))
                    destination.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
        }

        private static bool CopyFloat(Material source, Material target, string from, string to)
        {
            if (!source.HasProperty(from) || !target.HasProperty(to)) return false;
            target.SetFloat(to, source.GetFloat(from));
            return true;
        }

        private static bool CopyTexture(Material source, Material target, string from, string to)
        {
            if (!source.HasProperty(from) || !target.HasProperty(to)) return false;
            var texture = source.GetTexture(from);
            if (texture == null) return false;
            target.SetTexture(to, texture);
            target.SetTextureScale(to, source.GetTextureScale(from));
            target.SetTextureOffset(to, source.GetTextureOffset(from));
            return true;
        }
    }
}
