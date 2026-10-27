using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuFaceColorTests
    {
        [TestCase(false)] [TestCase(true)]
        public void BodyCreationAndClothingMaskKeepSkinColorIndependentOfSrgbWriteState(bool initialSrgbWrite)
        {
            const string root="D:/koikatsu/Koikatu/abdata";
            if(!Directory.Exists(root))Assert.Ignore("Local source game assets are unavailable.");
            var textures=new List<Texture2D>();var leases=new List<KoikatsuAssetBundleLease>();
            var previous=GL.sRGBWrite;
            try
            {
                Texture2D Solid(Color color,bool linear)
                {
                    var t=new Texture2D(4,4,TextureFormat.RGBA32,false,linear);
                    t.SetPixels(Array.ConvertAll(new int[16],_=>color));t.Apply();textures.Add(t);return t;
                }
                var source=Solid(Color.white,false);var mask=Solid(Color.red,true);var alpha=Solid(new Color(.8f,.6f,0,1),true);
                var catalog=new KoikatsuListCatalog();var loader=new KoikatsuTextureLoader(root,catalog,leases,null,textures);
                var appearance=new KoikatsuCardBodyAppearance(0,0,new Color(.9f,.65f,.4f,1),
                    new Color(.8f,.4f,.2f,1),0,Array.Empty<int>(),Array.Empty<Color>(),
                    Array.Empty<int>(),Array.Empty<Vector4>(),0,Color.clear,0,Color.white,0,Color.white,Color.white);
                var fm=new Material(Shader.Find("Hidden/StudioEditor/KoikatsuFaceBake"));
                fm.SetTexture("_ColorMask",mask);fm.SetColor("_Color",appearance.SkinMainColor);
                foreach(int i in new[]{3,4,5,6,7})fm.SetColor("_Color"+i,Color.clear);
                var rt=RenderTexture.GetTemporary(4,4,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                var active=RenderTexture.active;GL.sRGBWrite=true;Graphics.Blit(source,rt,fm,0);
                var face=Solid(Color.clear,false);RenderTexture.active=rt;face.ReadPixels(new Rect(0,0,4,4),0,0);face.Apply();
                RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(fm);
                GL.sRGBWrite=initialSrgbWrite;
                var baked=KoikatsuBodyTextureBaker.Bake(source,mask,appearance,loader,catalog,textures);
                Assert.That(GL.sRGBWrite,Is.EqualTo(initialSrgbWrite));
                GL.sRGBWrite=initialSrgbWrite;
                var masked=KoikatsuBodyMaskBaker.Bake(baked,alpha,textures);
                Assert.That(GL.sRGBWrite,Is.EqualTo(initialSrgbWrite));
                var raw=baked.GetPixel(1,1);var maskedRaw=masked.GetPixel(1,1);
                Debug.Log($"SKIN_BAKE_RGB initialSrgb={initialSrgbWrite} skin={appearance.SkinMainColor:F6} face={face.GetPixel(1,1):F6} body={raw:F6} masked={maskedRaw:F6}");
                for(int c=0;c<3;c++)
                {
                    Assert.That(raw[c],Is.EqualTo(appearance.SkinMainColor[c]).Within(.005f),"Skin color must survive creation.");
                    Assert.That(raw[c],Is.EqualTo(face.GetPixel(1,1)[c]).Within(.005f),"Face and body must encode the same skin color.");
                    Assert.That(maskedRaw[c],Is.EqualTo(raw[c]).Within(.005f),"Alpha masking must preserve RGB.");
                }
                Assert.That(maskedRaw.a,Is.EqualTo(.48f).Within(.005f),"Clothing masks still control visibility.");
            }
            finally
            {
                GL.sRGBWrite=previous;foreach(var t in textures)Object.DestroyImmediate(t);
                foreach(var lease in leases)lease.Dispose();
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void FaceCompositionGpuMatchesSourceSkinMasksAndPaintLayouts(int variant)
        {
            const string path = "D:/koikatsu/Koikatu/abdata/chara/mm_base.unity3d";
            if (!File.Exists(path)) Assert.Ignore("Local source game assets are unavailable.");
            using var lease = KoikatsuAssetBundleCache.Acquire(path);
            var owned = new List<Object>();
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var target = RenderTexture.GetTemporary(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            try
            {
                var original = new Material(lease.Bundle.LoadAsset<Material>("cf_m_face_create"));owned.Add(original);
                Texture2D Pattern(int channel)
                {
                    var texture=new Texture2D(16,16,TextureFormat.RGBAFloat,false,true){wrapMode=TextureWrapMode.Clamp};
                    for(int y=0;y<16;y++)for(int x=0;x<16;x++)
                    {
                        float u=x/15f,v=y/15f;
                        var color = channel == 0 ? new Color(.7f+.2f*u,.5f+.3f*v,.4f+.2f*u,1) :
                            channel == 1 ? (variant == 0 ? new Color(u,v,0,1) : new Color(u,1-v,v*.7f,1)) :
                            new Color(u,v,1-u,(u+v)*.35f);
                        texture.SetPixel(x,y,color);
                    }
                    texture.Apply();owned.Add(texture);return texture;
                }
                var source=Pattern(0);original.SetTexture("_MainTex",source);
                original.SetTexture("_ColorMask",Pattern(1));original.SetTexture("_paintmask",Pattern(2));
                original.SetColor("_Color",new Color(.9f,.6f,.4f,1));
                original.SetColor("_Color2",new Color(.8f,.4f,.3f,1));
                foreach(int i in new[]{3,4,5,6,7})
                {
                    original.SetTexture("_Texture"+i,Pattern(2));
                    original.SetColor("_Color"+i,new Color(.2f+i*.08f,.9f-i*.06f,.7f,variant == 0 ? 0 : .5f));
                    original.SetTextureScale("_Texture"+i,new Vector2(.7f,.8f));
                    original.SetTextureOffset("_Texture"+i,new Vector2(.12f,.04f));
                }
                original.SetVector("_paint1",new Vector4(.03f,-.05f,.13f*variant,.5f));
                original.SetVector("_paint2",new Vector4(-.04f,.02f,-.23f*variant,.6f));
                original.SetVector("_hokuro",new Vector4(.05f,-.03f,0,.3f));
                original.SetVector("_tex4uv",new Vector4(.12f,-.1f,-.3f,-.4f));
                original.SetVector("_tex5uv",new Vector4(-.07f,.15f,.2f,.1f));
                original.shaderKeywords=new[]{"DIRECTIONAL"};
                var port=new Material(Shader.Find("Hidden/StudioEditor/KoikatsuFaceBake"));owned.Add(port);
                port.CopyPropertiesFromMaterial(original);
                var mesh=new Mesh();owned.Add(mesh);
                mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                mesh.triangles=new[]{0,2,1,0,3,2};
                var readback=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);owned.Add(readback);
                Color[] Draw(Material material)
                {
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.magenta);
                    GL.PushMatrix();GL.LoadOrtho();
                    try{Assert.That(material.SetPass(0),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}
                    finally{GL.PopMatrix();}
                    readback.ReadPixels(new Rect(0,0,32,32),0,0);readback.Apply();return readback.GetPixels();
                }
                var expected=Draw(original);var actual=Draw(port);float maximum=0;
                for(int i=0;i<expected.Length;i++)for(int c=0;c<4;c++)maximum=Mathf.Max(maximum,Mathf.Abs(actual[i][c]-expected[i][c]));
                Debug.Log($"SOURCE_FACE_BAKE variant={variant} maxError={maximum:F8} center={actual[528]:F6}");
                Assert.That(expected[528],Is.Not.EqualTo(Color.magenta));
                Assert.That(maximum,Is.LessThan(.0001f));
            }
            finally
            {
                RenderTexture.active=previous;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                foreach(var item in owned)Object.DestroyImmediate(item);
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void BakedFaceTintIsAppliedOnceAndDoesNotWhitenOtherHeadParts(bool baked)
        {
            const string path="D:/koikatsu/Koikatu/abdata/chara/bo_head_00.unity3d";
            if(!File.Exists(path))Assert.Ignore("Local source game assets are unavailable.");
            using var lease=KoikatsuAssetBundleCache.Acquire(path);
            var head=Object.Instantiate(lease.Bundle.LoadAsset<GameObject>("p_cf_head_00"));
            var texture=new Texture2D(1,1);var materials=new List<Material>();
            var skin=new Color(.9f,.65f,.4f,1);
            try
            {
                KoikatsuMaterialConverter.ConvertFace(head,skin,texture,null,
                    KoikatsuCardFaceAppearance.CreateDefault(),
                    new KoikatsuFaceTextures { EyelineUp=texture, EyelineShadow=texture },materials,baked);
                var face=Array.Find(head.GetComponentsInChildren<Renderer>(true),r=>r.name=="cf_O_face");
                var expected=baked ? Color.white : skin;
                for(int c=0;c<4;c++)Assert.That(face.sharedMaterial.GetColor("_BaseColor")[c],Is.EqualTo(expected[c]).Within(.000001f));
                Assert.That(face.sharedMaterial.GetTexture("_BaseMap"),Is.SameAs(texture));
                var eyeline=Array.Find(head.GetComponentsInChildren<Renderer>(true),r=>r.name=="cf_O_eyeline");
                for(int c=0;c<4;c++)Assert.That(eyeline.sharedMaterials[1].GetColor("_BaseColor")[c],Is.EqualTo(skin[c]).Within(.000001f),"Eyeline shadow keeps its separate skin tint.");
            }
            finally
            {
                Object.DestroyImmediate(head);Object.DestroyImmediate(texture);
                foreach(var material in materials)Object.DestroyImmediate(material);
            }
        }
    }
}
