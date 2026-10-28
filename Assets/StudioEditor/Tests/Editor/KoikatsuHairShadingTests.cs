using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using StudioEditor.Rendering;
using StudioEditor.Rendering.RenderSchemes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuHairShadingTests
    {
        // Exercise the original game's compiled forward pass, including a
        // nonuniform gloss pattern and a second UV layout different from UV0.
        [TestCase("main_hair", 0)] [TestCase("main_hair", 1)]
        [TestCase("main_hair", 2)] [TestCase("main_hair", 3)]
        [TestCase("main_hair_front", 0)] [TestCase("main_hair_front", 2)]
        [TestCase("main_hair_low", 1)] [TestCase("main_hair_low", 3)]
        public void GpuMatchesOriginalHair(string shaderName, int variant)
        {
            string game = "D:/koikatsu/Koikatu/abdata/chara/";
            string bundle = game + (shaderName.EndsWith("front") ? "bo_hair_f_00.unity3d" : "bo_hair_b_00.unity3d");
            if (!File.Exists(bundle)) Assert.Ignore("Local source game assets are unavailable.");
            using var lease = KoikatsuAssetBundleCache.Acquire(bundle);
            string prefabName = shaderName.EndsWith("front") ? "p_cf_hair_f_04" :
                shaderName.EndsWith("low") ? "p_cf_hair_b_05_low" : "p_cf_hair_b_04";
            var sourceShader = lease.Bundle.LoadAsset<GameObject>(prefabName).GetComponentsInChildren<Renderer>(true)
                .SelectMany(r=>r.sharedMaterials).First(m=>m.shader.name == "Shader Forge/"+shaderName).shader;
            var owned = new List<Object>();
            var original = new Material(sourceShader);owned.Add(original);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            string[] globals = {"_WorldSpaceCameraPos","_WorldSpaceLightPos0","_LightColor0",
                "_MainLightPosition","_MainLightColor","unity_LightData","_ambientshadowG","_RampG_ST","_StudioSurfaceLighting"};
            var values = Array.ConvertAll(globals,Shader.GetGlobalVector);
            var oldRamp = Shader.GetGlobalTexture("_RampG");
            try
            {
                Texture2D Solid(Color color)
                {
                    var texture = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
                    texture.SetPixel(0,0,color);texture.Apply();owned.Add(texture);return texture;
                }
                var albedo = Solid(new Color(.32f,.16f,.27f,1));
                var detail = Solid(new Color(.28f,.36f,.24f,.16f));
                var normal = Solid(variant == 2 ? new Color(1,.61f,1,.35f) : new Color(1,.5f,1,.5f));
                var ramp = Solid(new Color(.25f+.2f*variant,0,0,1));
                var alternate = Solid(new Color(.9f-.2f*variant,0,0,1));
                var gloss = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
                gloss.wrapMode=TextureWrapMode.Repeat;owned.Add(gloss);
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                    gloss.SetPixel(x,y,new Color(.12f+.08f*Mathf.Sin(y*.7f+x*.1f),0,0,1));
                gloss.Apply();
                void Tex(string name,Texture texture){if(original.HasProperty(name))original.SetTexture(name,texture);}
                Tex("_MainTex",albedo);Tex("_NormalMap",normal);Tex("_DetailMask",detail);
                Tex("_HairGloss",gloss);Tex("_AnotherRamp",alternate);Tex("_AlphaMask",Texture2D.whiteTexture);
                Tex("_ColorMask",Texture2D.blackTexture);
                original.SetColor("_ShadowColor",new Color(.35f,.45f,.6f,1));
                original.SetFloat("_SpeclarHeight",.8f);
                original.SetFloat("_ShadowExtend",.7f);
                if(original.HasProperty("_rimpower"))original.SetFloat("_rimpower",.23f);
                if(original.HasProperty("_rimV"))original.SetFloat("_rimV",.4f);
                original.SetTextureScale("_HairGloss",new Vector2(.8f,1.3f));
                original.SetTextureOffset("_HairGloss",new Vector2(.1f,.2f));
                original.shaderKeywords=new[]{"DIRECTIONAL"};original.SetInt("_Cull",0);
                var converted = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                    original,CharacterRenderSurfaceRole.Hair,"hair","hair regression",Color.white,null));
                owned.Add(converted);
                MaterialRenderUtility.SetMainTexture(converted,albedo);
                converted.SetFloat("_SourceCharacterShading",1);converted.SetFloat("_Cull",0);
                converted.SetTexture("_SourceRamp",ramp);
                var ambient = new Vector4(.3f,.4f,.5f,variant == 3 ? -.25f : .25f);
                converted.SetVector("_SourceAmbientShadow",ambient);
                // Compare the same numeric source constants: old bundle Color
                // metadata and modern shader Color metadata encode differently.
                converted.SetVector("_SourceShadowColor",original.GetColor("_ShadowColor"));
                Shader.SetGlobalVector("_StudioSurfaceLighting",Vector4.zero);
                var direction=new Vector4(.2f,.1f,.9746794f,0);
                Shader.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(variant==1?4:.5f,.5f,variant==1?1:5,1));
                Shader.SetGlobalVector("_WorldSpaceLightPos0",direction);Shader.SetGlobalVector("_MainLightPosition",direction);
                Shader.SetGlobalVector("_LightColor0",new Vector4(.8f,.95f,1.1f,1));
                Shader.SetGlobalVector("_MainLightColor",new Vector4(.8f,.95f,1.1f,1));
                Shader.SetGlobalVector("unity_LightData",new Vector4(0,0,1,0));
                Shader.SetGlobalVector("_ambientshadowG",ambient);
                Shader.SetGlobalTexture("_RampG",ramp);Shader.SetGlobalVector("_RampG_ST",new Vector4(1,1,0,0));
                var mesh=new Mesh();owned.Add(mesh);
                mesh.vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                mesh.uv2=new[]{new Vector2(.1f,.2f),new Vector2(.7f,.2f),new Vector2(.7f,.6f),new Vector2(.1f,.6f)};
                mesh.normals=Enumerable.Repeat(Vector3.forward,4).ToArray();
                mesh.tangents=Enumerable.Repeat(new Vector4(1,0,0,1),4).ToArray();
                mesh.triangles=variant==3 ? new[]{0,1,2,0,2,3} : new[]{0,2,1,0,3,2};
                var readback=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);owned.Add(readback);
                Color Draw(Material material,int pass)
                {
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.magenta);
                    GL.PushMatrix();GL.LoadOrtho();
                    try{Assert.That(material.SetPass(pass),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}
                    finally{GL.PopMatrix();}
                    readback.ReadPixels(new Rect(0,0,32,32),0,0);readback.Apply();return readback.GetPixel(16,16);
                }
                var expected=Draw(original,original.FindPass("FORWARD"));var actual=Draw(converted,0);
                Debug.Log($"SOURCE_HAIR_GPU shader={shaderName} variant={variant} expected={expected:F6} actual={actual:F6}");
                Assert.That(expected,Is.Not.EqualTo(Color.magenta));
                for(int c=0;c<3;c++)Assert.That(actual[c],Is.EqualTo(expected[c]).Within(.002f),$"channel {c}");
            }
            finally
            {
                for(int i=0;i<globals.Length;i++)Shader.SetGlobalVector(globals[i],values[i]);
                Shader.SetGlobalTexture("_RampG",oldRamp);RenderTexture.active=previous;GL.sRGBWrite=srgb;
                RenderTexture.ReleaseTemporary(target);foreach(var item in owned)Object.DestroyImmediate(item);
            }
        }

        // The source front-hair shader blends with ZWrite On and its materials
        // draw at queue 2850. Losing either lets overlapping strands composite
        // in triangle order, which reads as bright patches from some angles.
        [Test]
        public void FrontHairKeepsSourceQueueAndDepthWrite()
        {
            string bundle = "D:/koikatsu/Koikatu/abdata/chara/bo_hair_f_00.unity3d";
            if (!File.Exists(bundle)) Assert.Ignore("Local source game assets are unavailable.");
            using var lease = KoikatsuAssetBundleCache.Acquire(bundle);
            var source = lease.Bundle.LoadAsset<GameObject>("p_cf_hair_f_04").GetComponentsInChildren<Renderer>(true)
                .SelectMany(r=>r.sharedMaterials).First(m=>m != null && m.shader.name == "Shader Forge/main_hair_front");
            Assert.That(source.renderQueue,Is.EqualTo(2850));
            var converted = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                source,CharacterRenderSurfaceRole.Hair,"hair","front hair render state",Color.white,null));
            try
            {
                Assert.That(converted.renderQueue,Is.EqualTo(source.renderQueue));
                Assert.That(converted.GetFloat("_Surface"),Is.EqualTo(1));
                Assert.That(converted.GetFloat("_ZWrite"),Is.EqualTo(1),"Source FORWARD pass writes depth while blending.");
            }
            finally {Object.DestroyImmediate(converted);}
        }
    }
}
