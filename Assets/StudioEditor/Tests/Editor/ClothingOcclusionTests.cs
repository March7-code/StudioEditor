using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using UnityEditor;
using UnityEngine;

namespace StudioEditor.Tests
{
    public sealed class ClothingOcclusionTests
    {
        [Test]
        public void CompatibilityBakeUsesSourcePatternDirectionScaleAndMaskOrder()
        {
            var material = new Material(Shader.Find("Hidden/StudioEditor/KoikatsuClothesBake"));
            var pattern = new Texture2D(4, 1, TextureFormat.RGBA32, false, true)
                {filterMode = FilterMode.Point,wrapMode = TextureWrapMode.Repeat};
            var mask = SolidTexture(Color.red);
            var target = RenderTexture.GetTemporary(80,2,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var image = new Texture2D(80,2,TextureFormat.RGBA32,false,true);
            var previous = RenderTexture.active;
            try
            {
                pattern.SetPixels(new[] {Color.black,Color.white,Color.white,Color.white});pattern.Apply();
                material.SetTexture("_Pattern1",pattern);material.SetFloat("_HasPattern1",1);
                material.SetTexture("_ColorMask",mask);
                material.SetColor("_ChannelColor1",Color.red);material.SetColor("_PatternColor1",Color.blue);
                foreach (var repeats in new[] {1,20})
                {
                    material.SetVector("_PatternTiling1",Vector4.one * (repeats==1?1:0));
                    Graphics.Blit(Texture2D.whiteTexture,target,material,0);
                    RenderTexture.active=target;image.ReadPixels(new Rect(0,0,80,2),0,0);image.Apply();
                    int blueRuns=0;bool wasBlue=false;
                    for(int x=0;x<80;x++)
                    {
                        var color=image.GetPixel(x,0);bool blue=color.b>.8f;
                        if(blue&&!wasBlue)blueRuns++;wasBlue=blue;
                    }
                    Assert.That(blueRuns,Is.EqualTo(repeats));
                    Assert.That(image.GetPixel(0,0).b,Is.GreaterThan(.8f));
                }
                mask.SetPixels(new[] {Color.white,Color.white,Color.white,Color.white});mask.Apply();
                material.SetColor("_ChannelColor3",Color.green);
                Graphics.Blit(Texture2D.whiteTexture,target,material,0);
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,80,2),0,0);image.Apply();
                Assert.That(image.GetPixel(0,0).g,Is.GreaterThan(.8f));
                Assert.That(image.GetPixel(0,0).r,Is.LessThan(.02f));
                Assert.That(image.GetPixel(0,0).b,Is.LessThan(.02f));
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);Object.DestroyImmediate(mask);Object.DestroyImmediate(pattern);Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ClothingBakePreservesTransparentHolesAfterReusingAnOpaqueTarget()
        {
            var source = SolidTexture(Color.white);
            source.SetPixel(0, 0, Color.clear);
            source.SetPixel(0, 1, Color.clear);
            source.Apply();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/StudioEditor/Tests/Editor/AlphaBlendClothesBake.shader");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var previousTarget = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            Texture2D baked = null;
            try
            {
                // The original create_topN pass alpha-blends. A pooled target
                // can still contain the opaque clothing rendered before it.
                var dirty = RenderTexture.GetTemporary(
                    2, 2, 0, RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);
                RenderTexture.active = dirty;
                GL.Clear(false, true, Color.white);
                RenderTexture.active = previousTarget;
                RenderTexture.ReleaseTemporary(dirty);
                GL.sRGBWrite = false;
                var bakeMap = typeof(KoikatsuClothesTextureBaker).GetMethod(
                    "BakeMap", BindingFlags.Static | BindingFlags.NonPublic);
                baked = (Texture2D)bakeMap.Invoke(null, new object[]
                    { material, source, Texture2D.whiteTexture, "Transparent clothes" });
                Assert.That(baked.GetPixel(0, 0).a, Is.LessThan(0.01f));
                Assert.That(baked.GetPixel(1, 0).a, Is.GreaterThan(0.99f));
                Assert.That(baked.GetPixel(0, 0).r, Is.LessThan(0.01f));
                Assert.That(RenderTexture.active, Is.SameAs(previousTarget));
                Assert.That(GL.sRGBWrite, Is.False);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClothingMaskUsesRenderedBaseMapInsteadOfLegacyAlias(bool hasStaleAlias)
        {
            var root = new GameObject("Clothing mask test");
            var source = SolidTexture(Color.white);
            var mask = SolidTexture(Color.white);
            var legacy = SolidTexture(Color.blue);
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var baked = new List<Texture2D>();
            try
            {
                mask.SetPixel(0, 0, new Color(0, 1, 1, 1));
                mask.SetPixel(0, 1, new Color(0, 1, 1, 1));
                mask.Apply();
                material.SetTexture("_MainTex", hasStaleAlias ? legacy : null);
                material.SetTexture("_BaseMap", source);
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                KoikatsuMaterialConverter.ApplyClothesAlphaMask(
                    root, mask, Vector2.one, Vector2.zero, true, true, baked);
                Assert.That(baked.Count, Is.EqualTo(1));
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(baked[0]));
                Assert.That(baked[0].GetPixel(0, 0).a, Is.LessThan(0.01f));
                Assert.That(baked[0].GetPixel(1, 0).a, Is.GreaterThan(0.99f));
                Assert.That(baked[0].GetPixel(1, 0).r, Is.GreaterThan(0.99f));
                Assert.That(renderer.enabled, Is.True);
                Assert.That(material.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            }
            finally
            {
                foreach (var texture in baked) Object.DestroyImmediate(texture);
                Object.DestroyImmediate(material);Object.DestroyImmediate(source);
                Object.DestroyImmediate(mask);Object.DestroyImmediate(root);
                Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void FullyCoveredInnerClothesDisableOnlyTheirRenderer()
        {
            var outer = new GameObject("Outer clothes");
            var inner = new GameObject("Inner clothes");
            var source = SolidTexture(Color.white);
            var mask = SolidTexture(new Color(0, 0, 0, 1));
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var baked = new List<Texture2D>();
            try
            {
                material.SetTexture("_BaseMap", source);
                var outerRenderer = outer.AddComponent<MeshRenderer>();
                var innerRenderer = inner.AddComponent<MeshRenderer>();
                innerRenderer.sharedMaterial = material;
                KoikatsuMaterialConverter.ApplyClothesAlphaMask(
                    inner, mask, Vector2.one, Vector2.zero, true, true, baked);
                Assert.That(innerRenderer.enabled, Is.False);
                Assert.That(outerRenderer.enabled, Is.True);
                Assert.That(inner.activeSelf, Is.True);
            }
            finally
            {
                foreach (var texture in baked) Object.DestroyImmediate(texture);
                Object.DestroyImmediate(material);Object.DestroyImmediate(source);
                Object.DestroyImmediate(mask);Object.DestroyImmediate(inner);
                Object.DestroyImmediate(outer);
            }
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(new[] { color, color, color, color });texture.Apply();
            return texture;
        }
    }
}
