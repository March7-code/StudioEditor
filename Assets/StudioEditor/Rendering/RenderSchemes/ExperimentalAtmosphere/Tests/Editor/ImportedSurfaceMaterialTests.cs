using NUnit.Framework;
using StudioEditor.Rendering.RenderSchemes;
using UnityEngine;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class ImportedSurfaceMaterialTests
    {
        [Test]
        public void StandardSurfaceChannelsAndTextureTransformsSurviveConversion()
        {
            var source = new Material(Shader.Find("Standard"));
            var target = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = new Texture2D(2, 2);
            try
            {
                source.SetTexture("_BumpMap", texture);
                source.SetTextureScale("_BumpMap", new Vector2(2, 3));
                source.SetTextureOffset("_BumpMap", new Vector2(.1f, .2f));
                source.SetFloat("_BumpScale", .45f);
                source.SetFloat("_Glossiness", .27f);
                source.SetFloat("_Metallic", .6f);
                source.SetTexture("_MetallicGlossMap", texture);
                source.SetTexture("_OcclusionMap", texture);
                source.SetFloat("_OcclusionStrength", .3f);
                ImportedSurfaceMaterialUtility.CopyToLit(source, target);
                Assert.That(target.GetTexture("_BumpMap"), Is.SameAs(texture));
                Assert.That(target.GetTextureScale("_BumpMap"), Is.EqualTo(new Vector2(2, 3)));
                Assert.That(target.GetTextureOffset("_BumpMap"), Is.EqualTo(new Vector2(.1f, .2f)));
                Assert.That(target.GetFloat("_BumpScale"), Is.EqualTo(.45f));
                Assert.That(target.GetFloat("_Smoothness"), Is.EqualTo(.27f));
                Assert.That(target.GetFloat("_Metallic"), Is.EqualTo(.6f));
                Assert.That(target.GetFloat("_OcclusionStrength"), Is.EqualTo(.3f));
                Assert.That(target.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(target.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void SpecularWorkflowDoesNotTreatGlossAsMetalness()
        {
            var source = new Material(Shader.Find("Standard (Specular setup)"));
            var target = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = new Texture2D(2, 2);
            try
            {
                source.SetTexture("_SpecGlossMap", texture);
                source.SetColor("_SpecColor", new Color(.15f, .25f, .35f));
                ImportedSurfaceMaterialUtility.CopyToLit(source, target);
                Assert.That(target.GetFloat("_WorkflowMode"), Is.EqualTo(0));
                Assert.That(target.IsKeywordEnabled("_SPECULAR_SETUP"), Is.True);
                Assert.That(target.GetTexture("_SpecGlossMap"), Is.SameAs(texture));
                var color = target.GetColor("_SpecColor");
                var expected = source.GetColor("_SpecColor");
                Assert.That(color.r, Is.EqualTo(expected.r).Within(0.00001f));
                Assert.That(color.g, Is.EqualTo(expected.g).Within(0.00001f));
                Assert.That(color.b, Is.EqualTo(expected.b).Within(0.00001f));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void ImportedCharacterRoleIsExplicitAndDefaultHighlightsRemainUnchanged()
        {
            var scheme = new DefaultAnimeCharacterRenderScheme();
            var material = scheme.CreateMaterial(new CharacterRenderMaterialContext(null,
                CharacterRenderSurfaceRole.Hair, "hair", "Imported hair", Color.white, null));
            try
            {
                Assert.That(material.GetFloat("_StudioSurfaceRole"), Is.EqualTo((float)CharacterRenderSurfaceRole.Hair));
                Assert.That(material.GetFloat("_SpecularStrength"), Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(material); }
        }
    }
}
