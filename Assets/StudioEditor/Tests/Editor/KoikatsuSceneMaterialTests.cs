using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuSceneMaterialTests
    {
        [TestCase(0f)] [TestCase(.25f)] [TestCase(1f)] [TestCase(2f)]
        public void AdditiveEffectGpuMatchesOriginalThreeChannelCompositing(float opacity)
        {
            const string bundle = "D:/koikatsu/Koikatu/abdata/studio/mat/00.unity3d";
            if (!File.Exists(bundle)) Assert.Ignore("Local source game assets are unavailable.");
            using var lease = KoikatsuAssetBundleCache.Acquire(bundle);
            var shader = lease.Bundle.LoadAsset<Material>("m_koi_stu_efect00_00").shader;
            Assert.That(shader.name, Is.EqualTo("Shader Forge/main_item_studio_add"));
            var source = new Material(shader) { renderQueue = 4050 };
            var mask = new Texture2D(8, 1, TextureFormat.RGBAFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var root = new GameObject("effect");
            var materials = new List<Material>();
            var target = RenderTexture.GetTemporary(64, 8, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(64, 8, TextureFormat.RGBAFloat, false, true);
            var mesh = new Mesh();
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                mask.SetPixels(new[] { Color.clear, Color.red, Color.green, Color.blue, Color.white,
                    new Color(.65f,.4f,.2f,.1f), new Color(.3f,.8f,.55f,0), new Color(.9f,.6f,.95f,.2f) });
                mask.Apply();
                var colors = new[] {new Color(.75f,.31f,.3f,.2f), new Color(.2f,.9f,.5f,.1f), new Color(.4f,.3f,.95f,0)};
                source.SetTexture("_MainTex", mask);
                source.SetTextureScale("_MainTex", new Vector2(2, 1));
                source.SetTextureOffset("_MainTex", new Vector2(.125f, 0));
                // Distinct prefab values catch scene-card overrides lost after conversion.
                source.SetColor("_Color", Color.white);source.SetColor("_Color2", Color.black);source.SetColor("_Color3", Color.gray);
                var renderer = root.AddComponent<MeshRenderer>();renderer.sharedMaterial = source;
                KoikatsuMaterialConverter.Convert(root, materials);
                var converted = renderer.sharedMaterial;
                var item = new KoikatsuSceneItem(0, 0, 0) { Colors = colors, Alpha = opacity };
                var entry = new KoikatsuStudioListEntry(0,0,0,"effect","","","","",false,
                    new[]{true,true,true},new[]{false,false,false},false,false,null,"");
                KoikatsuStudioItemAppearance.Apply(root,item,entry,null,null);
                root.name = "effect_alpha";
                // The source uses rendNormal for colors and rendAlpha for _alpha.
                KoikatsuStudioItemAppearance.Apply(root,item,entry,null,null);
                source.SetColor("_Color", colors[0]);source.SetColor("_Color2", colors[1]);source.SetColor("_Color3", colors[2]);
                source.SetFloat("_alpha", opacity);
                Assert.That(converted.GetFloat("_alpha"), Is.EqualTo(opacity));
                Assert.That(converted.renderQueue, Is.EqualTo(source.renderQueue));
                mesh.vertices = new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)};
                mesh.uv = new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles = new[]{0,2,1,0,3,2};
                Color[] Draw(Material material, int pass)
                {
                    RenderTexture.active = target;GL.sRGBWrite = false;
                    GL.Clear(true,true,new Color(.04f,.05f,.08f,0));
                    GL.PushMatrix();GL.LoadOrtho();
                    try { Assert.That(material.SetPass(pass), Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity); }
                    finally { GL.PopMatrix(); }
                    readback.ReadPixels(new Rect(0,0,64,8),0,0);readback.Apply();return readback.GetPixels();
                }
                var expected = Draw(source, source.FindPass("FORWARD"));
                var actual = Draw(converted, 0);
                Assert.That(expected[4*64].a,Is.EqualTo(1f).Within(.002f),"Original pass drew no pixels.");
                float error = 0;
                for (int x = 0; x < 64; x++)
                for (int channel = 0; channel < 4; channel++)
                {
                    error = Mathf.Max(error,Mathf.Abs(actual[4*64+x][channel]-expected[4*64+x][channel]));
                    Assert.That(actual[4*64+x][channel],Is.EqualTo(expected[4*64+x][channel]).Within(.002f),$"pixel {x} channel {channel}");
                }
                Debug.Log($"SOURCE_ADDITIVE_GPU alpha={opacity} maximum error={error:F8}");
            }
            finally
            {
                RenderTexture.active = previous;GL.sRGBWrite = srgb;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(root);foreach(var material in materials) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);Object.DestroyImmediate(mask);Object.DestroyImmediate(readback);Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void StudioItemConversionRetainsTextureColorChannelsAndNormalInputs()
        {
            var source = new Material(AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/StudioEditor/Tests/Editor/StudioItemSourceFixture.shader"));
            var texture = new Texture2D(2, 2);
            var root = new GameObject("Sword without vertex colors");
            var materials = new List<Material>();
            try
            {
                source.SetTexture("_ColorMask", texture);
                source.SetTextureScale("_ColorMask", new Vector2(2, 3));
                source.SetTexture("_NormalMap", texture);
                source.SetColor("_Color2", Color.yellow);
                source.SetFloat("_ShadowExtendAnother", .66f);
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = source;
                KoikatsuMaterialConverter.Convert(root, materials);
                var result = renderer.sharedMaterial;
                Assert.That(result.shader.name, Is.EqualTo("StudioEditor/KoikatsuStudioItem"));
                Assert.That(result.GetTexture("_ColorMask"), Is.SameAs(texture));
                Assert.That(result.GetTextureScale("_ColorMask"), Is.EqualTo(new Vector2(2, 3)));
                Assert.That(result.GetTexture("_NormalMap"), Is.SameAs(texture));
                Assert.That(result.GetColor("_ChannelColor2"), Is.EqualTo(Color.yellow));
                Assert.That(result.GetFloat("_ShadowExtendAnother"), Is.EqualTo(.66f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var material in materials) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void StudioItemGpuUsesMaskWithoutVertexColorsAndKeepsUnmaskedTexels()
        {
            var material = new Material(Shader.Find("StudioEditor/KoikatsuStudioItem"));
            var mask = new Texture2D(4, 1, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var target = RenderTexture.GetTemporary(32, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var image = new Texture2D(32, 8, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                // Last texel overlaps all channels: legacy blue wins, rather
                // than averaging red, green and blue into an unrelated gray.
                mask.SetPixels(new[] {Color.clear, Color.red, Color.green, Color.white});mask.Apply();
                material.SetTexture("_ColorMask", mask);
                material.SetFloat("_UseColorMask", 1);
                material.SetFloat("_SourceItemModel", 1);
                material.SetFloat("_Cull", 0);
                material.SetVector("_ChannelEnabled", Vector4.one);
                material.SetVector("_SourceAmbientShadow", Vector4.one);
                material.SetColor("_ChannelColor1", Color.red);
                material.SetColor("_ChannelColor2", Color.green);
                material.SetColor("_ChannelColor3", Color.blue);
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 32, 8), 0, 0);image.Apply();
                var unmasked = image.GetPixel(4, 4);
                Assert.That(Mathf.Min(unmasked.r, unmasked.g, unmasked.b), Is.GreaterThan(.1f));
                AssertDominant(image.GetPixel(12, 4), 0);
                AssertDominant(image.GetPixel(20, 4), 1);
                AssertDominant(image.GetPixel(28, 4), 2);
            }
            finally
            {
                RenderTexture.active = previous;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);Object.DestroyImmediate(mask);Object.DestroyImmediate(material);
            }
        }

        private static void AssertDominant(Color color, int channel)
        {
            Assert.That(color[channel], Is.GreaterThan(.1f));
            Assert.That(color[(channel + 1) % 3], Is.LessThan(.02f));
            Assert.That(color[(channel + 2) % 3], Is.LessThan(.02f));
        }

        [Test]
        public void TransparentMaskRetainsUnlitTextureOpacityAndStudioQueue()
        {
            var source = new Material(Shader.Find("Unlit/Transparent")) { renderQueue = 4999 };
            var texture = new Texture2D(2, 2);
            source.mainTexture = texture;
            source.mainTextureScale = new Vector2(2, 3);
            var root = new GameObject("mask");
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = source;
            var materials = new List<Material>();
            try
            {
                KoikatsuMaterialConverter.Convert(root, materials);
                var result = renderer.sharedMaterial;
                Assert.That(result.shader.name, Is.EqualTo("StudioEditor/KoikatsuSceneEffect"));
                Assert.That(result.mainTexture, Is.SameAs(texture));
                Assert.That(result.mainTextureScale, Is.EqualTo(new Vector2(2, 3)));
                Assert.That(result.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha));
                Assert.That(result.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(result.GetFloat("_ZWrite"), Is.Zero);
                Assert.That(result.renderQueue, Is.EqualTo(4999));
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderer.receiveShadows, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var material in materials) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SkyGeometryRetainsCubemapTintAndExposure()
        {
            var source = new Material(Shader.Find("Skybox/Cubemap"));
            var cubemap = new Cubemap(2, TextureFormat.RGBA32, false);
            source.SetTexture("_Tex", cubemap);
            source.SetColor("_Tint", Color.cyan);
            source.SetFloat("_Exposure", 1.4f);
            source.SetFloat("_Rotation", 32f);
            Material result = null;
            try
            {
                result = KoikatsuSceneMaterialUtility.TryCreate(source, "sky", Color.white);
                Assert.That(result.shader.name, Is.EqualTo("StudioEditor/KoikatsuSkyCubemap"));
                Assert.That(result.GetTexture("_Tex"), Is.SameAs(cubemap));
                Assert.That(result.GetColor("_Tint"), Is.EqualTo(Color.cyan));
                Assert.That(result.GetFloat("_Exposure"), Is.EqualTo(1.4f));
                Assert.That(result.GetFloat("_Rotation"), Is.EqualTo(32f));
                Assert.That(result.renderQueue, Is.EqualTo((int)RenderQueue.Background));
            }
            finally
            {
                Object.DestroyImmediate(result);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(cubemap);
            }
        }
    }
}
