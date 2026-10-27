using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class CharacterEnvironmentLightingTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void SourceSurfacesReceiveEnvironmentAndLocalBounceWithOcclusion(int model)
        {
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var mesh = new Mesh();
            var readback = new Texture2D(16,16,TextureFormat.RGBAFloat,false,true);
            var target = RenderTexture.GetTemporary(16,16,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            string[] names = {"_StudioSurfaceLighting","_StudioLocalLighting","_MainLightColor","_MainLightPosition",
                "unity_LightData","_WorldSpaceCameraPos","unity_SHAr","unity_SHAg","unity_SHAb",
                "unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC",
                "_StudioProbeGrid","_StudioProbeOrigin","_StudioContactGrid","_StudioContactOrigin","_StudioContactSettings","_AdditionalLightsCount"};
            var values = Array.ConvertAll(names,Shader.GetGlobalVector);
            string[] arrays = {"_AdditionalLightsPosition","_AdditionalLightsColor","_AdditionalLightsAttenuation","_AdditionalLightsSpotDir","unity_LightIndices"};
            var oldArrays = Array.ConvertAll(arrays,Shader.GetGlobalVectorArray);
            string[] textureNames = {"_StudioLocalIrradiance","_StudioLocalDistance","_StudioLocalContact"};
            var oldTextures = Array.ConvertAll(textureNames,Shader.GetGlobalTexture);
            var volumes = new System.Collections.Generic.List<Texture3D>();
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                material.SetFloat("_SourceCharacterShading",1);
                material.SetFloat("_SourceCharacterModel",model);
                material.SetColor("_BaseColor",Color.white);
                material.SetFloat("_Cull",0);
                material.SetTexture("_SourceRamp",Texture2D.whiteTexture);
                material.SetVector("_SourceAmbientShadow",new Vector4(.5f,.5f,.5f,1));
                material.SetVector("_SourceLineColor",new Vector4(.5f,.5f,.5f,.5f));
                Shader.SetGlobalVector("_StudioSurfaceLighting",new Vector4(1,1,1,1));
                Shader.SetGlobalVector("_StudioLocalLighting",Vector4.zero);
                Shader.SetGlobalVector("_MainLightColor",Vector4.zero);
                Shader.SetGlobalVector("_MainLightPosition",new Vector4(0,0,1,0));
                Shader.SetGlobalVector("unity_LightData",new Vector4(0,0,1,0));
                Shader.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(.5f,.5f,5,1));
                foreach(var name in new[]{"unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC"})Shader.SetGlobalVector(name,Vector4.zero);
                mesh.vertices = new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)};
                mesh.uv = new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                mesh.normals = new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.tangents = new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                mesh.triangles = new[]{0,2,1,0,3,2};
                Color Draw(Color ambient)
                {
                    Shader.SetGlobalVector("unity_SHAr",new Vector4(0,0,0,ambient.r));
                    Shader.SetGlobalVector("unity_SHAg",new Vector4(0,0,0,ambient.g));
                    Shader.SetGlobalVector("unity_SHAb",new Vector4(0,0,0,ambient.b));
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.magenta);
                    GL.PushMatrix();GL.LoadOrtho();
                    try{Assert.That(material.SetPass(0),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}
                    finally{GL.PopMatrix();}
                    readback.ReadPixels(new Rect(0,0,16,16),0,0);readback.Apply();return readback.GetPixel(8,8);
                }
                var warm=Draw(new Color(.6f,.16f,.025f));
                var cool=Draw(new Color(.025f,.16f,.6f));
                var dark=Draw(Color.black);
                Assert.That(warm.r-cool.r,Is.GreaterThan(.1f),"Red ambient must affect the source surface.");
                Assert.That(cool.b-warm.b,Is.GreaterThan(.1f),"Blue ambient must affect the source surface.");
                Assert.That(dark.maxColorComponent,Is.LessThan(.001f),"The fixed source fill must not illuminate an unlit room.");
                Texture3D Volume(Color color)
                {
                    var volume = new Texture3D(56,8,8,TextureFormat.RGBAFloat,false);
                    var pixels = new Color[56*8*8];
                    Array.Fill(pixels,color);volume.SetPixels(pixels);volume.Apply();volumes.Add(volume);return volume;
                }
                Shader.SetGlobalVector("_StudioProbeGrid",new Vector4(8,8,8,1));
                Shader.SetGlobalVector("_StudioProbeOrigin",new Vector4(-3,-3,-3,0));
                Shader.SetGlobalVector("_StudioContactGrid",new Vector4(8,8,8,1));
                Shader.SetGlobalVector("_StudioContactOrigin",new Vector4(-3,-3,-3,0));
                Shader.SetGlobalVector("_StudioContactSettings",Vector4.zero);
                Shader.SetGlobalTexture("_StudioLocalIrradiance",Volume(new Color(.3f,.07f,.01f,1)));
                Shader.SetGlobalTexture("_StudioLocalDistance",Volume(new Color(100,10000,0,0)));
                Shader.SetGlobalTexture("_StudioLocalContact",Volume(Color.white));
                Shader.SetGlobalVector("_StudioLocalLighting",new Vector4(1,1,1,0));
                var bounce=Draw(Color.black);
                Assert.That(bounce.r,Is.GreaterThan(.1f),"Local bounce must reach source skin and clothes without a direct light.");
                Assert.That(bounce.r-bounce.b,Is.GreaterThan(.1f),"Local bounce must preserve its color.");
                Shader.SetGlobalTexture("_StudioLocalContact",Volume(Color.black));
                var occluded=Draw(Color.black);
                Assert.That(occluded.maxColorComponent,Is.LessThan(.001f),"Contact occlusion must also apply to local bounce.");
                Debug.Log($"CHARACTER_AMBIENT_GPU model={model} warm={warm:F5} cool={cool:F5} dark={dark:F5} bounce={bounce:F5} occluded={occluded:F5}");
                Shader.SetGlobalVector("_StudioLocalLighting",Vector4.zero);
                Shader.SetGlobalVector("_MainLightColor",Vector4.one);
                Shader.SetGlobalVector("_AdditionalLightsCount",Vector4.zero);
                Shader.SetGlobalVector("unity_LightData",new Vector4(0,1,1,0));
                material.EnableKeyword("_ADDITIONAL_LIGHTS");
                Shader.SetGlobalVectorArray("unity_LightIndices",new[]{Vector4.zero,Vector4.zero});
                Shader.SetGlobalVectorArray("_AdditionalLightsPosition",new[]{new Vector4(.5f,.5f,1,1)});
                Shader.SetGlobalVectorArray("_AdditionalLightsColor",new[]{new Vector4(.08f,.02f,.005f,1)});
                Shader.SetGlobalVectorArray("_AdditionalLightsAttenuation",new[]{new Vector4(.01f,1,0,1)});
                Shader.SetGlobalVectorArray("_AdditionalLightsSpotDir",new[]{new Vector4(0,0,1,0)});
                var keyOnly=Draw(Color.black);
                Shader.SetGlobalVector("_AdditionalLightsCount",new Vector4(1,0,0,0));
                var point=Draw(Color.black);
                Assert.That(point.r-keyOnly.r,Is.GreaterThan(.005f),"A point light weaker than the key must still contribute.");
                Shader.SetGlobalVectorArray("_AdditionalLightsAttenuation",new[]{new Vector4(.01f,1,2,-1)});
                var spot=Draw(Color.black);
                Assert.That(spot.r-keyOnly.r,Is.GreaterThan(.005f),"An aligned weak spot light must still contribute.");
                Shader.SetGlobalVectorArray("_AdditionalLightsSpotDir",new[]{new Vector4(0,0,-1,0)});
                var away=Draw(Color.black);
                Assert.That(away.r,Is.EqualTo(keyOnly.r).Within(.001f),"A spot pointing away must not illuminate the surface.");
                Shader.SetGlobalVector("_StudioSurfaceLighting",Vector4.zero);
                var legacyOff=Draw(Color.black);
                Shader.SetGlobalVectorArray("_AdditionalLightsSpotDir",new[]{new Vector4(0,0,1,0)});
                var legacyOn=Draw(Color.black);
                Assert.That(legacyOn.r,Is.EqualTo(legacyOff.r).Within(.001f),"Default Anime must retain the game's selected-light behavior.");
                Debug.Log($"CHARACTER_WEAK_LIGHT_GPU model={model} key={keyOnly:F5} point={point:F5} spot={spot:F5} away={away:F5}");
            }
            finally
            {
                for(int i=0;i<names.Length;i++)Shader.SetGlobalVector(names[i],values[i]);
                for(int i=0;i<arrays.Length;i++)if(oldArrays[i]!=null&&oldArrays[i].Length>0)Shader.SetGlobalVectorArray(arrays[i],oldArrays[i]);
                for(int i=0;i<textureNames.Length;i++)Shader.SetGlobalTexture(textureNames[i],oldTextures[i]);
                foreach(var volume in volumes)Object.DestroyImmediate(volume);
                RenderTexture.active=previous;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(material);Object.DestroyImmediate(mesh);Object.DestroyImmediate(readback);
            }
        }

    }
}
