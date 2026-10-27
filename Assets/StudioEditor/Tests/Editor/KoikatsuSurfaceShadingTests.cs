using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using StudioEditor.Rendering;
using StudioEditor.Rendering.RenderSchemes;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuSurfaceShadingTests
    {
        [Test]
        public void LegacyLightColorsUpdateWithoutChangingSceneLightsOrGlobalSettings()
        {
            var root = new GameObject("Legacy light colors");
            var originalSun = RenderSettings.sun;
            bool linearIntensity = GraphicsSettings.lightsUseLinearIntensity;
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            try
            {
                var renderer = root.AddComponent<MeshRenderer>();renderer.sharedMaterial = material;
                var lamp = new GameObject("Card light");lamp.transform.SetParent(root.transform);
                var light = lamp.AddComponent<Light>();light.type = LightType.Directional;
                light.color = new Color(.77381f,1,.95153f);light.intensity = 1.316667f;
                var target = lamp.AddComponent<ImportedLightTarget>();target.Audience = ImportedLightAudience.Characters;
                var context = root.AddComponent<ImportedSceneRenderContext>();
                context.Configure(default,default,null,0,new Renderer[]{renderer},new[]{target});
                context.ConfigureCharacterShading(null,Color.gray,useLinearLightIntensity:false);
                var bridge = root.AddComponent<ImportedSceneRenderBridge>();
                void Tick() => bridge.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,null);
                Tick();
                var block = new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_SourceLegacyLightCount"),Is.EqualTo(1));
                Assert.That(block.GetVectorArray("_SourceLegacyLightColors")[0].x,Is.EqualTo(1.04194f).Within(.0001f));
                Assert.That(light.intensity,Is.EqualTo(1.316667f));
                light.intensity=1.6f;Tick();renderer.GetPropertyBlock(block);
                Assert.That(block.GetVectorArray("_SourceLegacyLightColors")[0],Is.EqualTo((Vector4)(light.color*1.6f).linear));
                Assert.That(GraphicsSettings.lightsUseLinearIntensity,Is.EqualTo(linearIntensity));
                context.ConfigureCharacterShading(null,Color.gray);Tick();renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_SourceLegacyLightCount"),Is.Zero);
            }
            finally {Object.DestroyImmediate(root);Object.DestroyImmediate(material);RenderSettings.sun=originalSun;}
        }

        [Test]
        public void SceneLineControlsReachCharacterPropertyBlocksAndUpdate()
        {
            var root = new GameObject("Source surface settings");
            var originalSun = RenderSettings.sun;
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var ramp = new Texture2D(1, 1);
            try
            {
                var renderer = root.AddComponent<MeshRenderer>();renderer.sharedMaterial = material;
                var context = root.AddComponent<ImportedSceneRenderContext>();
                context.Configure(default, default, null, 0, new Renderer[]{renderer});
                context.ConfigureCharacterShading(ramp, Color.gray, .22f, .68f);
                var bridge = root.AddComponent<ImportedSceneRenderBridge>();
                void Tick() => bridge.GetType().GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
                Tick();
                var block = new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Assert.That(block.GetVector("_SourceLineColor"), Is.EqualTo(new Vector4(.5f,.5f,.5f,.78f)));
                Assert.That(block.GetFloat("_SourceLineWidth"), Is.EqualTo(.68f));
                context.ConfigureCharacterShading(ramp, Color.gray, .8f, .3f);Tick();
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetVector("_SourceLineColor").w, Is.EqualTo(.2f).Within(.00001f));
                Assert.That(block.GetFloat("_SourceLineWidth"), Is.EqualTo(.3f));
            }
            finally
            {
                Object.DestroyImmediate(root);Object.DestroyImmediate(material);Object.DestroyImmediate(ramp);
                RenderSettings.sun = originalSun;
            }
        }

        // Compare the converted GPU pass directly against the game's compiled
        // forward pass. Constant textures isolate lighting from asset UV layout.
        [TestCase(false, 0)] [TestCase(false, 1)] [TestCase(false, 2)] [TestCase(false, 3)]
        [TestCase(true, 0)] [TestCase(true, 1)] [TestCase(true, 2)] [TestCase(true, 3)]
        public void GpuMatchesOriginalBodyOrClothes(bool body, int variant)
        {
            string game = @"D:/koikatsu/Koikatu/abdata/chara/";
            string bundle = game + (body ? "mm_base.unity3d" : "cpo_sailor_c_00.unity3d");
            if (!File.Exists(bundle)) Assert.Ignore("Local source game assets are unavailable.");
            var owned = new List<Object>();
            using var lease = KoikatsuAssetBundleCache.Acquire(bundle);
            var source = body ? lease.Bundle.LoadAsset<Material>("cf_m_body") :
                lease.Bundle.LoadAsset<GameObject>("p_o_top_sailor01_03").GetComponentInChildren<Renderer>(true).sharedMaterial;
            var original = new Material(source);
            owned.Add(original);
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            bool srgb = GL.sRGBWrite;
            string[] globalNames = {"_WorldSpaceCameraPos", "_WorldSpaceLightPos0", "_LightColor0", "_MainLightPosition", "_MainLightColor", "unity_LightData", "_ambientshadowG", "_LineColorG", "_RampG_ST"};
            var oldGlobals = Array.ConvertAll(globalNames, Shader.GetGlobalVector);
            float oldWidth = Shader.GetGlobalFloat("_linewidthG");
            var oldRamp = Shader.GetGlobalTexture("_RampG");
            try
            {
                Texture2D Solid(Color color)
                {
                    var texture = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
                    texture.SetPixel(0,0,color);texture.Apply();owned.Add(texture);return texture;
                }
                var albedo = Solid(body ? new Color(.72f,.52f,.43f,1) : new Color(.64f,.18f,.35f,1));
                var detail = Solid(new Color(.28f,.36f,.24f,.16f));
                var line = Solid(new Color(.2f,.65f,.3f,1));
                var flat = Solid(new Color(1,.5f,1,.5f));
                var detailNormal = Solid(new Color(1,.6f,1,.4f));
                var ramp = Solid(new Color(.25f + .25f*variant,0,0,1));
                var clear = Solid(Color.clear);
                void Tex(string name, Texture texture) { if(original.HasProperty(name)) original.SetTexture(name, texture); }
                Tex("_MainTex",albedo);Tex("_DetailMask",detail);Tex("_LineMask",line);Tex("_NormalMap",flat);
                Tex("_NormalMapDetail",detailNormal);Tex("_AlphaMask",Texture2D.whiteTexture);
                Tex("_AnotherRamp",ramp);Tex("_overtex1",clear);Tex("_overtex2",clear);Tex("_overtex3",clear);
                Tex("_NormalMask",clear);
                original.SetFloat("_SpecularPower",variant == 0 ? 0 : .35f);
                original.SetFloat("_SpecularPowerNail", .2f);
                original.SetFloat("_ShadowExtend", .6f);
                original.SetFloat("_DetailNormalMapScale", .65f);
                original.SetFloat("_linetexon",1);
                original.SetFloat("_rimV", .15f);
                original.SetFloat("_alpha_a",1);original.SetFloat("_alpha_b",1);
                original.SetColor("_SpecularColor",new Color(.8f,.75f,.7f,.2f));
                original.SetTextureScale("_MainTex",Vector2.one);original.SetTextureOffset("_MainTex",Vector2.zero);
                original.shaderKeywords = new[]{"DIRECTIONAL"};
                original.SetInt("_Cull",0);
                var converted = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                    original,body ? CharacterRenderSurfaceRole.Skin : CharacterRenderSurfaceRole.Clothes,
                    "source fixture", "converted fixture", Color.white,null));
                owned.Add(converted);
                MaterialRenderUtility.SetMainTexture(converted,albedo);
                if (body)
                {
                    Assert.That(converted.GetFloat("_SourceCharacterModel"), Is.EqualTo(3));
                    var face = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                        original, CharacterRenderSurfaceRole.Face, "face", "face regression", Color.white, null));
                    owned.Add(face);
                    Assert.That(face.GetFloat("_SourceCharacterModel"), Is.EqualTo(4), "Face skin uses the source skin shader, separately from eye and line details.");
                    Assert.That(face.HasProperty("_FaceSphereNormalBlend"), Is.False, "The sphere approximation has been removed from face shading.");
                }
                converted.SetFloat("_SourceCharacterShading",1);
                converted.SetTexture("_SourceRamp",ramp);
                converted.SetVector("_SourceAmbientShadow",new Vector4(.3f,.4f,.5f,.25f));
                converted.SetVector("_SourceLineColor",new Vector4(.5f,.5f,.5f,.7f));
                converted.SetFloat("_SourceLineWidth",.4f);
                converted.SetFloat("_Cull",0);
                var direction = new Vector4(.2f,.1f,.9746794f,0);
                Shader.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(.5f,.5f,5,1));
                Shader.SetGlobalVector("_WorldSpaceLightPos0",direction);
                Shader.SetGlobalVector("_MainLightPosition",direction);
                Shader.SetGlobalVector("_LightColor0",new Vector4(.8f,.95f,1.1f,1));
                Shader.SetGlobalVector("_MainLightColor",new Vector4(.8f,.95f,1.1f,1));
                if (variant == 3)
                {
                    var color = new Color(.77381f, 1, .95153f);
                    var linear = color.linear * 1.316667f;
                    var legacy = (color * 1.316667f).linear;
                    Shader.SetGlobalVector("_LightColor0", legacy);
                    Shader.SetGlobalVector("_MainLightColor", linear);
                    converted.SetFloat("_SourceLegacyLightCount",1);
                    converted.SetVectorArray("_SourceLinearLightColors",new Vector4[]{linear});
                    converted.SetVectorArray("_SourceLegacyLightColors",new Vector4[]{legacy});
                    converted.SetVectorArray("_SourceLightPositions",new[]{direction});
                }
                Shader.SetGlobalVector("unity_LightData",new Vector4(0,0,1,0));
                Shader.SetGlobalVector("_ambientshadowG",new Vector4(.3f,.4f,.5f,.25f));
                Shader.SetGlobalVector("_LineColorG",new Vector4(.5f,.5f,.5f,.7f));
                Shader.SetGlobalFloat("_linewidthG",.4f);
                Shader.SetGlobalTexture("_RampG",ramp);Shader.SetGlobalVector("_RampG_ST",new Vector4(1,1,0,0));
                var mesh = new Mesh();owned.Add(mesh);
                mesh.vertices = new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)};
                mesh.uv = new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                mesh.normals = new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.tangents = new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                mesh.triangles = new[]{0,2,1,0,3,2};
                var readback = new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);owned.Add(readback);
                Color Draw(Material material,int pass)
                {
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.magenta);
                    GL.PushMatrix();GL.LoadOrtho();
                    try { Assert.That(material.SetPass(pass),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity); }
                    finally { GL.PopMatrix(); }
                    readback.ReadPixels(new Rect(0,0,32,32),0,0);readback.Apply();return readback.GetPixel(16,16);
                }
                var expected = Draw(original,original.FindPass("FORWARD"));
                var actual = Draw(converted,0);
                Debug.Log($"SOURCE_SURFACE_GPU body={body} variant={variant} expected={expected:F6} actual={actual:F6}");
                Assert.That(expected,Is.Not.EqualTo(Color.magenta),"Original pass drew no pixels.");
                for(int i=0;i<3;i++) Assert.That(actual[i],Is.EqualTo(expected[i]).Within(.002f),$"channel {i}");
            }
            finally
            {
                for(int i=0;i<globalNames.Length;i++) Shader.SetGlobalVector(globalNames[i],oldGlobals[i]);
                Shader.SetGlobalFloat("_linewidthG",oldWidth);Shader.SetGlobalTexture("_RampG",oldRamp);
                RenderTexture.active=previous;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                foreach(var item in owned) Object.DestroyImmediate(item);
            }
        }
    }
}
