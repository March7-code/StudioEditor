using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using StudioEditor.Rendering;
using StudioEditor.Rendering.RenderSchemes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuEyeShadingTests
    {
        [TestCase(true, 0)] [TestCase(true, 1)] [TestCase(true, 2)]
        [TestCase(false, 0)] [TestCase(false, 1)] [TestCase(false, 2)] [TestCase(false, 3)]
        public void EyeGpuMatchesOriginalIncludingHighlightsAndExpression(bool white, int variant)
        {
            const string bundlePath = "D:/koikatsu/Koikatu/abdata/chara/bo_head_00.unity3d";
            if (!File.Exists(bundlePath)) Assert.Ignore("Local source game assets are unavailable.");
            using var lease = KoikatsuAssetBundleCache.Acquire(bundlePath);
            var owned = new List<Object>();
            string[] globals = {"_WorldSpaceCameraPos", "_WorldSpaceLightPos0", "_LightColor0",
                "_MainLightPosition", "_MainLightColor", "unity_LightData", "_ambientshadowG", "_RampG_ST", "_StudioSurfaceLighting"};
            var oldValues = Array.ConvertAll(globals, Shader.GetGlobalVector);
            var oldRamp = Shader.GetGlobalTexture("_RampG");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            try
            {
                Texture2D Solid(Color color)
                {
                    var texture = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
                    texture.SetPixel(0,0,color);texture.Apply();owned.Add(texture);return texture;
                }
                var head = lease.Bundle.LoadAsset<GameObject>("p_cf_head_00");
                var source = Array.Find(head.GetComponentsInChildren<Renderer>(true),
                    renderer => renderer.name == (white ? "cf_Ohitomi_L" : "cf_Ohitomi_L02")).sharedMaterial;
                var original = new Material(source);owned.Add(original);
                var albedo = Solid(white ? new Color(.8f,.7f,.65f,1) : new Color(.22f,.06f,.15f,1));
                var clear = Solid(Color.clear);
                var ramp = Solid(new Color(.2f+variant*.3f,0,0,1));
                var expression = variant == 3 ? Solid(new Color(.12f,.3f,.5f,.35f)) : clear;
                var upper = variant >= 2 ? Solid(new Color(.1f,.2f,.3f,.6f)) : clear;
                var lower = variant >= 2 ? Solid(new Color(.8f,.7f,.6f,.3f)) : clear;
                var upperColor = new Color(.8f,.3f,.1f,.75f);
                var lowerColor = new Color(.1f,.6f,.8f,.4f);
                original.SetTexture("_MainTex",albedo);
                original.SetTextureScale("_MainTex",Vector2.one);original.SetTextureOffset("_MainTex",Vector2.zero);
                original.SetColor("_shadowcolor",new Color(.62f,.76f,.94f,1));
                if (!white)
                {
                    original.SetTexture("_overtex1",upper);original.SetTexture("_overtex2",lower);
                    original.SetColor("_overcolor1",upperColor);original.SetColor("_overcolor2",lowerColor);
                    original.SetTexture("_expression",expression);original.SetFloat("_exppower",variant == 3 ? .7f : 0);
                    original.SetFloat("_isHighLight",1);original.SetFloat("_rotation",0);
                }
                original.shaderKeywords = new[]{"DIRECTIONAL"};
                var converted = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                    original,CharacterRenderSurfaceRole.Face,white ? "cf_m_sirome_00" : "cf_m_hitomi_00","Eye regression",Color.white,null));
                owned.Add(converted);
                Assert.That(converted.GetFloat("_SourceCharacterModel"),Is.EqualTo(white ? 5 : 6));
                MaterialRenderUtility.SetMainTexture(converted,albedo);
                converted.SetFloat("_SourceCharacterShading",1);
                converted.SetFloat("_SourceLegacyLightCount",0);
                converted.SetFloat("_Cull",0);
                converted.SetTexture("_SourceRamp",ramp);
                converted.SetVector("_SourceAmbientShadow",new Vector4(.2f,.32f,.45f,.15f));
                if (!white)
                {
                    var bake = new Material(Shader.Find("Hidden/StudioEditor/KoikatsuEyeBake"));owned.Add(bake);
                    bake.SetTexture("_HighlightUp",upper);bake.SetTexture("_HighlightDown",lower);
                    bake.SetColor("_HighlightUpColor",upperColor);bake.SetColor("_HighlightDownColor",lowerColor);
                    bake.SetFloat("_HasHighlightUp",1);bake.SetFloat("_HasHighlightDown",1);
                    var mask = new RenderTexture(8,8,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                    mask.Create();owned.Add(mask);Graphics.Blit(albedo,mask,bake,3);
                    converted.SetTexture("_IrisHighlightMask",mask);converted.SetFloat("_IrisHighlightEnabled",1);
                    converted.SetTexture("_IrisExpressionMap",expression);
                    converted.SetFloat("_IrisExpressionStrength",variant == 3 ? .7f : 0);
                }
                var direction = variant == 1 ? new Vector4(0,0,1,0) : new Vector4(1,0,0,0);
                Shader.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(.5f,.5f,5,1));
                Shader.SetGlobalVector("_WorldSpaceLightPos0",direction);Shader.SetGlobalVector("_MainLightPosition",direction);
                Shader.SetGlobalVector("_LightColor0",new Vector4(.65f,.9f,1.1f,1));
                Shader.SetGlobalVector("_MainLightColor",new Vector4(.65f,.9f,1.1f,1));
                Shader.SetGlobalVector("unity_LightData",new Vector4(0,0,1,0));
                Shader.SetGlobalVector("_ambientshadowG",new Vector4(.2f,.32f,.45f,.15f));
                Shader.SetGlobalTexture("_RampG",ramp);Shader.SetGlobalVector("_RampG_ST",new Vector4(1,1,0,0));
                Shader.SetGlobalVector("_StudioSurfaceLighting",Vector4.zero);
                var mesh = new Mesh();owned.Add(mesh);
                mesh.vertices = new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};
                mesh.uv = new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                mesh.uv2=mesh.uv;mesh.uv3=mesh.uv;
                mesh.normals = new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.tangents = new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                mesh.triangles = new[]{0,2,1,0,3,2};
                var readback = new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);owned.Add(readback);
                Color Draw(Material material,int pass)
                {
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.magenta);
                    GL.PushMatrix();GL.LoadOrtho();
                    try{Assert.That(material.SetPass(pass),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}
                    finally{GL.PopMatrix();}
                    readback.ReadPixels(new Rect(0,0,32,32),0,0);readback.Apply();return readback.GetPixel(16,16);
                }
                var expected=Draw(original,original.FindPass("FORWARD"));
                var actual=Draw(converted,0);
                Debug.Log($"SOURCE_EYE_GPU white={white} variant={variant} expected={expected:F6} actual={actual:F6}");
                Assert.That(expected,Is.Not.EqualTo(Color.magenta));
                for(int i=0;i<4;i++)Assert.That(actual[i],Is.EqualTo(expected[i]).Within(.002f),$"channel {i}");
            }
            finally
            {
                for(int i=0;i<globals.Length;i++)Shader.SetGlobalVector(globals[i],oldValues[i]);
                Shader.SetGlobalTexture("_RampG",oldRamp);
                RenderTexture.active=previous;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                foreach(var item in owned)Object.DestroyImmediate(item);
            }
        }
    }
}
