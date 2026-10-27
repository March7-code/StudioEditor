using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class BodyShadowTests
    {
        [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void SkinSceneMapStillBlocksRoomLightWithoutDiscardingColoredAmbient(int model)
        {
            using var scene = new Surface(model);
            var depth = new Texture2DArray(512,512,1,TextureFormat.RFloat,false,true);
            try
            {
                var projection=Matrix4x4.zero;projection.m03=.5f;projection.m13=.5f;projection.m23=.5f;projection.m33=1;
                scene.Material.SetTexture("_FaceSceneDepth",depth);
                scene.Material.SetFloat("_FaceSceneShadowCount",1);
                scene.Material.SetMatrixArray("_FaceSceneWorldToShadow",new[]{projection,projection,projection,projection});
                scene.Material.SetVectorArray("_FaceHairLightPositions",new[]{new Vector4(0,0,1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                scene.Material.SetVector("_FaceHairShadowWeights",Vector4.one);
                scene.Material.SetColor("_ShadowColor",new Color(.3f,.4f,.5f,1));
                Shader.SetGlobalVector("_MainLightPosition",new Vector4(0,0,1,0));
                var pixels=new float[512*512];Array.Fill(pixels,1f);
                depth.SetPixelData(pixels,0,0);depth.Apply();
                var exposed=scene.Draw()[8*16+8];
                Array.Clear(pixels,0,pixels.Length);depth.SetPixelData(pixels,0,0);depth.Apply();
                var blocked=scene.Draw()[8*16+8];
                Assert.That(exposed.r,Is.GreaterThan(.1f));
                Assert.That(blocked.maxColorComponent,Is.LessThan(.002f),"A room wall must still remove direct irradiance.");
                scene.Material.SetVectorArray("_FaceHairLightPositions",new[]{new Vector4(0,0,-1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                var differentLight=scene.Draw()[8*16+8];
                Assert.That(differentLight.r,Is.EqualTo(exposed.r).Within(.002f),"Scene maps remain scoped to their light.");
                scene.Material.SetVectorArray("_FaceHairLightPositions",new[]{new Vector4(0,0,1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                Shader.SetGlobalVector("_StudioSurfaceLighting",new Vector4(1,1,1,1));
                Shader.SetGlobalVector("unity_SHAr",new Vector4(0,0,0,.4f));
                Shader.SetGlobalVector("unity_SHAg",new Vector4(0,0,0,.2f));
                Shader.SetGlobalVector("unity_SHAb",new Vector4(0,0,0,.1f));
                var ambient=scene.Draw()[8*16+8];
                Assert.That(ambient.r,Is.GreaterThan(.01f),"Blocked direct light must not discard the environment term.");
                Assert.That(ambient.r,Is.GreaterThan(ambient.b+.01f));
            }
            finally {Object.DestroyImmediate(depth);}
        }

        [TestCase(false)] [TestCase(true)]
        public void BodyGarmentShadowSharesFacePaletteInDirectAndAmbientLight(bool ambientOnly)
        {
            using var scene = new Surface(3);
            var garment = HalfDepth(1024); var hair = HalfDepth(512);
            try
            {
                var palette = new Color(.3f,.4f,.5f,1);
                scene.Material.SetColor("_ShadowColor",palette);
                scene.Material.SetTexture("_ClothesSkinDepth",garment);
                scene.Material.SetTexture("_FaceHairDepth",hair);
                scene.Material.SetFloat("_ClothesSkinShadowStrength",1);
                scene.Material.SetFloat("_FaceHairShadowStrength",1);
                scene.Material.SetFloat("_FaceShadowReady",1);
                scene.Material.SetFloat("_FaceShadowStrength",0);
                var projection=Matrix4x4.identity;projection.m22=0;projection.m23=.5f;
                var matrices=new[]{projection,projection,projection,projection};
                var lights=new[]{new Vector4(0,0,1,0),Vector4.zero,Vector4.zero,Vector4.zero};
                scene.Material.SetMatrixArray("_ClothesSkinWorldToShadow",matrices);
                scene.Material.SetMatrixArray("_FaceHairWorldToShadow",matrices);
                scene.Material.SetVectorArray("_ClothesSkinLightPositions",lights);
                scene.Material.SetVectorArray("_FaceHairLightPositions",lights);
                scene.Material.SetVector("_ClothesSkinShadowWeights",Vector4.one);
                scene.Material.SetVector("_FaceHairShadowWeights",Vector4.one);
                Shader.SetGlobalVector("_MainLightPosition",new Vector4(0,0,1,0));
                Shader.SetGlobalVector("_StudioSurfaceLighting",new Vector4(1,ambientOnly?0:1,1,1));
                Shader.SetGlobalVector("unity_SHAr",new Vector4(0,0,0,.4f));
                Shader.SetGlobalVector("unity_SHAg",new Vector4(0,0,0,.2f));
                Shader.SetGlobalVector("unity_SHAb",new Vector4(0,0,0,.1f));
                var unblocked=scene.Draw();
                scene.Material.SetFloat("_ClothesSkinShadowCount",1);var body=scene.Draw();
                scene.Material.SetFloat("_ClothesSkinShadowCount",0);
                scene.Material.SetFloat("_SourceCharacterModel",4);
                scene.Material.SetFloat("_FaceHairShadowCount",1);var face=scene.Draw();
                int shaded=8*16+3,exposed=8*16+12;
                foreach(int i in new[]{shaded,exposed})
                {
                    Assert.That(body[i].r,Is.EqualTo(face[i].r).Within(.002f),"Body and face projected skin use the same RGB shadow response.");
                    Assert.That(body[i].g,Is.EqualTo(face[i].g).Within(.002f));
                    Assert.That(body[i].b,Is.EqualTo(face[i].b).Within(.002f));
                }
                Assert.That(body[shaded].r,Is.LessThan(unblocked[shaded].r*.5f),"Ambient illumination must not erase the garment silhouette.");
                Assert.That(body[shaded].r,Is.GreaterThan(.01f),"The shadow retains colored environment light.");
                Assert.That(body[exposed].r,Is.EqualTo(unblocked[exposed].r).Within(.001f));
                scene.Material.SetFloat("_FaceHairShadowCount",0);
                scene.Material.SetFloat("_SourceCharacterModel",3);
                scene.Material.SetFloat("_ClothesSkinShadowCount",1);
                scene.Material.SetVector("_ClothesSkinShadowWeights",new Vector4(.4f,0,0,0));
                var partial=scene.Draw();
                Assert.That(partial[shaded].r,Is.EqualTo(Mathf.Lerp(unblocked[shaded].r,body[shaded].r,.4f)).Within(.002f),"Coverage applies once to the combined direct and ambient surface.");
                scene.Material.SetVectorArray("_ClothesSkinLightPositions",new[]{new Vector4(0,0,-1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                var otherLight=scene.Draw();
                Assert.That(otherLight[shaded].r,Is.EqualTo(unblocked[shaded].r).Within(.001f),"A different light must not inherit the garment shadow.");
                scene.Material.SetVectorArray("_ClothesSkinLightPositions",lights);
                Shader.SetGlobalVector("_MainLightPosition",new Vector4(Mathf.Sqrt(.96f),0,.2f,0));
                scene.Material.SetFloat("_ClothesSkinShadowCount",0);var alreadyDark=scene.Draw();
                scene.Material.SetVectorArray("_ClothesSkinLightPositions",new[]{new Vector4(Mathf.Sqrt(.96f),0,.2f,0),Vector4.zero,Vector4.zero,Vector4.zero});
                scene.Material.SetVector("_ClothesSkinShadowWeights",Vector4.one);
                scene.Material.SetFloat("_ClothesSkinShadowCount",1);var merged=scene.Draw();
                Assert.That(merged[shaded].r,Is.EqualTo(alreadyDark[shaded].r).Within(.002f),"Garments must not darken the body's existing shadow band twice.");
            }
            finally {Object.DestroyImmediate(garment);Object.DestroyImmediate(hair);}
        }

        static Texture2DArray HalfDepth(int size)
        {
            var texture=new Texture2DArray(size,size,1,TextureFormat.R16,false,true);
            var pixels=new ushort[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=x<size/2?(ushort)16384:ushort.MaxValue;
            texture.SetPixelData(pixels,0,0);texture.Apply();return texture;
        }

        [Test]
        public void GarmentShadowKeepsOneTexelCoverageAndPartialLightStrength()
        {
            using var scene = new Surface(3);
            var depth = new Texture2DArray(1024,1024,1,TextureFormat.R16,false,true);
            try
            {
                var pixels = new ushort[1024*1024];
                for(int y=0;y<1024;y++) for(int x=0;x<1024;x++) pixels[y*1024+x]=x<512?(ushort)16384:ushort.MaxValue;
                depth.SetPixelData(pixels,0,0); depth.Apply();
                scene.Material.SetColor("_ShadowColor",new Color(.3f,.4f,.5f,1));
                scene.Material.SetTexture("_ClothesSkinDepth",depth);
                scene.Material.SetFloat("_ClothesSkinShadowCount",1);
                scene.Material.SetFloat("_ClothesSkinShadowStrength",1);
                var projection = Matrix4x4.zero;
                projection.m00=8f/1024; projection.m03=.5f-4f/1024;
                projection.m13=.5f; projection.m23=.5f; projection.m33=1;
                scene.Material.SetMatrixArray("_ClothesSkinWorldToShadow",new[]{projection,projection,projection,projection});
                scene.Material.SetVectorArray("_ClothesSkinLightPositions",new[]{new Vector4(0,0,1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                scene.Material.SetVector("_ClothesSkinShadowWeights",Vector4.one);
                Shader.SetGlobalVector("_MainLightPosition",new Vector4(0,0,1,0));
                var full = scene.Draw();
                float dark=full[8*16+1].r, lit=full[8*16+14].r;
                Assert.That(lit-dark,Is.GreaterThan(.3f));
                int edgePixels=0;
                for(int x=0;x<16;x++)
                {
                    float coverage=Mathf.InverseLerp(dark,lit,full[8*16+x].r);
                    if(coverage>.01f && coverage<.99f)edgePixels++;
                }
                Assert.That(edgePixels,Is.LessThanOrEqualTo(2),"Eight shadow texels across sixteen screen pixels need only one texel of edge coverage.");
                scene.Material.SetVector("_ClothesSkinShadowWeights",new Vector4(.4f,0,0,0));
                var partial=scene.Draw();
                Assert.That(partial[8*16+1].r,Is.EqualTo(Mathf.Lerp(lit,dark,.4f)).Within(.002f),"Hard filtering must preserve the light's partial shadow strength.");
            }
            finally { Object.DestroyImmediate(depth); }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void SkinPaletteProducesDistinctBodyBandsWithoutChangingOtherSourceSurfaces(int model)
        {
            using var scene = new Surface(model);
            Color Draw(float cosine, bool experiment, Color shadow)
            {
                scene.Material.SetColor("_ShadowColor", shadow);
                Shader.SetGlobalVector("_MainLightPosition", new Vector4(Mathf.Sqrt(1-cosine*cosine),0,cosine,0));
                Shader.SetGlobalVector("_StudioSurfaceLighting", experiment ? new Vector4(1,1,0,1) : Vector4.zero);
                return scene.Draw()[8*16+8];
            }
            var palette = new Color(.3f,.4f,.5f,1);
            var dark = Draw(.45f, true, palette); var lit = Draw(.55f, true, palette);
            var neutral = Draw(.45f, true, Color.white);
            if (model == 3)
            {
                Assert.That(lit.r-dark.r, Is.GreaterThan(.3f), "A binary body ramp must have a visible dark endpoint.");
                Assert.That(neutral.r-dark.r, Is.GreaterThan(.3f));
                Assert.That(Draw(.2f,true,palette).r, Is.EqualTo(dark.r).Within(.001f), "The shadow band must stay flat.");
                Assert.That(Draw(.8f,true,palette).r, Is.EqualTo(lit.r).Within(.001f), "The light band must stay flat.");
                var legacyPalette = Draw(.45f,false,palette); var legacyNeutral = Draw(.45f,false,Color.white);
                Assert.That(legacyPalette.r, Is.EqualTo(legacyNeutral.r).Within(.001f), "Source/default rendering keeps its palette calculation.");
                // Room illumination must still reach both bands.
                Shader.SetGlobalVector("_MainLightColor", Vector4.zero);
                Shader.SetGlobalVector("_StudioSurfaceLighting", new Vector4(1,1,1,1));
                Shader.SetGlobalVector("unity_SHAr", new Vector4(0,0,0,.4f));
                Shader.SetGlobalVector("unity_SHAg", new Vector4(0,0,0,.1f));
                scene.Material.SetColor("_ShadowColor", palette);
                var ambient = scene.Draw()[8*16+8];
                Assert.That(ambient.r, Is.GreaterThan(.02f));
                Assert.That(ambient.r, Is.GreaterThan(ambient.b+.02f), "Body bands must preserve ambient light color.");
            }
            else
            {
                Assert.That(dark.r, Is.EqualTo(neutral.r).Within(.001f), "Body palette must not affect face, eyes, hair or clothes.");
                Assert.That(dark.g, Is.EqualTo(neutral.g).Within(.001f));
                Assert.That(dark.b, Is.EqualTo(neutral.b).Within(.001f));
            }
        }

        private sealed class Surface : IDisposable
        {
            public readonly Material Material = new(Shader.Find("StudioEditor/AnimeCharacter"));
            private readonly Mesh mesh = new();
            private readonly Texture2D image = new(16,16,TextureFormat.RGBAFloat,false,true);
            private readonly Texture2D ramp = new(256,1,TextureFormat.RGBAFloat,false,true);
            private readonly RenderTexture target = RenderTexture.GetTemporary(16,16,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            private readonly RenderTexture previous = RenderTexture.active;
            private readonly bool srgb = GL.sRGBWrite;
            private readonly string[] names = {"_StudioSurfaceLighting","_StudioLocalLighting","_MainLightColor","_MainLightPosition",
                "unity_LightData","_WorldSpaceCameraPos","unity_SHAr","unity_SHAg","unity_SHAb",
                "unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC","_AdditionalLightsCount"};
            private readonly Vector4[] saved;

            public Surface(int model)
            {
                saved = Array.ConvertAll(names, Shader.GetGlobalVector);
                foreach (var name in names) Shader.SetGlobalVector(name, Vector4.zero);
                Shader.SetGlobalVector("_StudioSurfaceLighting", new Vector4(1,1,0,1));
                Shader.SetGlobalVector("_MainLightColor", Vector4.one);
                Shader.SetGlobalVector("unity_LightData", new Vector4(0,0,1,0));
                Shader.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(.5f,.5f,5,1));
                Material.SetFloat("_SourceCharacterShading",1); Material.SetFloat("_SourceCharacterModel",model);
                Material.SetFloat("_Cull",0); Material.SetFloat("_linetexon",0); Material.SetFloat("_SourceShadowExtend",0);
                Material.SetFloat("_SourceSpecularPower",0); Material.SetFloat("_SpecularPowerNail",0);
                Material.SetFloat("_SourceRimStrength",0); Material.SetFloat("_DetailNormalMapScale",0);
                Material.SetColor("_BaseColor",Color.white); Material.SetColor("_SourceShadowColor",Color.white);
                Material.SetVector("_SourceAmbientShadow",Vector4.one);
                Material.SetTexture("_DetailMask",Texture2D.blackTexture); Material.SetTexture("_LineMask",Texture2D.blackTexture);
                for (int x=0;x<256;x++) ramp.SetPixel(x,0,x<128?Color.black:Color.white);
                ramp.filterMode=FilterMode.Bilinear; ramp.wrapMode=TextureWrapMode.Clamp; ramp.Apply();
                Material.SetTexture("_SourceRamp",ramp);
                mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};
                mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; mesh.triangles=new[]{0,2,1,0,3,2};
            }

            public Color[] Draw()
            {
                RenderTexture.active=target; GL.sRGBWrite=false; GL.Clear(true,true,Color.magenta);
                GL.PushMatrix(); GL.LoadOrtho();
                try { Assert.That(Material.SetPass(0),Is.True); Graphics.DrawMeshNow(mesh,Matrix4x4.identity); }
                finally { GL.PopMatrix(); }
                image.ReadPixels(new Rect(0,0,16,16),0,0); image.Apply(); return image.GetPixels();
            }

            public void Dispose()
            {
                for(int i=0;i<names.Length;i++) Shader.SetGlobalVector(names[i],saved[i]);
                RenderTexture.active=previous; GL.sRGBWrite=srgb; RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(Material); Object.DestroyImmediate(mesh); Object.DestroyImmediate(image); Object.DestroyImmediate(ramp);
            }
        }
    }
}
