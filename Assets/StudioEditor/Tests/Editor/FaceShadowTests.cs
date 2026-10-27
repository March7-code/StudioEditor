using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using StudioEditor.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioEditor.Tests
{
    public sealed class FaceShadowTests
    {
        [Test]
        public void BindingKeepsSourceMeshAndFollowsHeadWhilePreservingOtherPropertyBlocks()
        {
            var root = new GameObject("Face binding test");
            var mesh = new Mesh();
            var sourceUV = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            try
            {
                var head = new GameObject("Head").transform;head.SetParent(root.transform);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                mesh.vertices = new[] { new Vector3(-1,0,0),new Vector3(1,0,0),new Vector3(1,2,0),new Vector3(-1,2,0) };
                mesh.normals = new[] {Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.triangles = new[] {0,1,2,0,2,3};mesh.uv=sourceUV;mesh.uv2=sourceUV;
                mesh.bindposes = new[] {Matrix4x4.identity};
                mesh.boneWeights = new[] {Weight(),Weight(),Weight(),Weight()};
                mesh.AddBlendShapeFrame("Expression",100,new Vector3[4],new Vector3[4],new Vector3[4]);
                renderer.bones=new[]{head};renderer.rootBone=head;renderer.sharedMesh=mesh;
                var original=new MaterialPropertyBlock();original.SetFloat("_UnrelatedValue",.73f);renderer.SetPropertyBlock(original);
                var binding=root.AddComponent<FaceShadowBinding>();
                Assert.That(binding.Configure(renderer,head),Is.True);
                var copy=renderer.sharedMesh;
                Assert.That(copy,Is.Not.SameAs(mesh));
                Assert.That(copy.uv,Is.EqualTo(sourceUV));Assert.That(copy.uv2,Is.EqualTo(sourceUV));
                Assert.That(mesh.uv3,Is.Empty);Assert.That(copy.uv3.Length,Is.EqualTo(4));
                var data = new List<Vector2>();copy.GetUVs(2,data);
                Assert.That(copy.GetVertexAttributeDimension(VertexAttribute.TexCoord2),Is.EqualTo(2));
                Assert.That(data[0].x,Is.EqualTo(.07f).Within(.0001f));
                Assert.That(data[2].x,Is.EqualTo(.93f).Within(.0001f));
                Assert.That(data[0].y,Is.Zero);Assert.That(data[2].y,Is.EqualTo(1));
                Assert.That(copy.blendShapeCount,Is.EqualTo(1));Assert.That(copy.boneWeights,Is.EqualTo(mesh.boneWeights));
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_FaceShadowReady"),Is.EqualTo(1));
                Assert.That(block.GetFloat("_UnrelatedValue"),Is.EqualTo(.73f));
                Assert.That(block.GetTexture("_FaceShadowMap"),Is.SameAs(FaceShadowBinding.ShadowMap));
                head.rotation=Quaternion.Euler(20,75,15);
                InvokeLifecycle(binding,"LateUpdate");renderer.GetPropertyBlock(block);
                var forward=block.GetVector("_FaceShadowForward");
                Assert.That(Vector3.Distance(new Vector3(forward.x,forward.y,forward.z),head.forward),Is.LessThan(.00001f));
                // EditMode does not schedule lifecycle events for this runtime component.
                binding.enabled=false;InvokeLifecycle(binding,"OnDisable");renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_FaceShadowReady"),Is.Zero);
                binding.enabled=true;InvokeLifecycle(binding,"OnEnable");renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_FaceShadowReady"),Is.EqualTo(1));
                InvokeLifecycle(binding,"OnDestroy");Object.DestroyImmediate(binding);
                Assert.That(renderer.sharedMesh,Is.SameAs(mesh));Assert.That(copy==null,Is.True);
            }
            finally {Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);}
            static BoneWeight Weight()=>new BoneWeight{boneIndex0=0,weight0=1};
        }

        private static void InvokeLifecycle(FaceShadowBinding binding,string method) =>
            typeof(FaceShadowBinding).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(binding,null);

        [Test]
        public void FullLightingVariantsStayWithinGpuSamplerLimit()
        {
            var shader=Shader.Find("StudioEditor/AnimeCharacter");
            var variants=new ShaderVariantCollection();
            try
            {
                foreach(var keywords in new[]
                {
                    new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHTS","_ADDITIONAL_LIGHT_SHADOWS","_SHADOWS_SOFT_HIGH","_LIGHT_LAYERS"},
                    new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHTS","_ADDITIONAL_LIGHT_SHADOWS","_SHADOWS_SOFT_HIGH","_LIGHT_LAYERS","_CLUSTER_LIGHT_LOOP"}
                })
                    variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ScriptableRenderPipeline,keywords));
                variants.WarmUp();
                foreach(var message in ShaderUtil.GetShaderMessages(shader))
                    Assert.That(message.severity.ToString(),Is.Not.EqualTo("Error"),message.message);
            }
            finally {Object.DestroyImmediate(variants);}
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
        public void HairDepthPassKeepsNearestSurfaceAndRespectsTransparentTips(bool cutout, bool frontOnly)
        {
            var shader=Resources.Load<Shader>("StudioEditorAtmosphere/FaceHairDepth");Assert.That(shader,Is.Not.Null);
            var material=new Material(shader);var mesh=new Mesh();
            var alpha=new Texture2D(2,1,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var readback=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
            var target=RenderTexture.GetTemporary(32,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
            var previousClip=Shader.GetGlobalMatrix("_HairShadowClipMatrix");
            var previousTexture=Shader.GetGlobalMatrix("_HairShadowTextureMatrix");
            try
            {
                alpha.SetPixels(new[]{Color.clear,Color.white});alpha.Apply();material.SetTexture("_BaseMap",alpha);
                material.SetFloat("_HairAlphaClip",cutout?1:0);
                material.SetFloat("_HairFrontOnly",frontOnly?1:0);
                mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};
                mesh.SetUVs(7,new[]{Vector2.zero,Vector2.one,Vector2.one,Vector2.zero});
                var clip=Matrix4x4.identity;clip.m00=2;clip.m03=-1;clip.m11=2;clip.m13=-1;clip.m22=0;clip.m23=.5f;
                Shader.SetGlobalMatrix("_HairShadowClipMatrix",clip);
                RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(false,true,Color.white);
                foreach(float distance in new[]{.25f,.7f})
                {
                    var texture=Matrix4x4.identity;texture.m22=0;texture.m23=distance;
                    Shader.SetGlobalMatrix("_HairShadowTextureMatrix",texture);
                    Assert.That(material.SetPass(0),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);
                }
                readback.ReadPixels(new Rect(0,0,32,32),0,0);readback.Apply();
                Assert.That(readback.GetPixel(24,16).r,Is.EqualTo(.25f).Within(.0001f));
                Assert.That(readback.GetPixel(8,16).r,Is.EqualTo(cutout||frontOnly?1:.25f).Within(.0001f));
            }
            finally
            {
                Shader.SetGlobalMatrix("_HairShadowClipMatrix",previousClip);Shader.SetGlobalMatrix("_HairShadowTextureMatrix",previousTexture);
                RenderTexture.active=active;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(material);Object.DestroyImmediate(mesh);Object.DestroyImmediate(alpha);Object.DestroyImmediate(readback);
            }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void GpuCurvesTrackLightInHeadSpaceAndOnlyAffectExperimentalFaceSkin(int model)
        {
            var owned = new List<Object>();
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));owned.Add(material);
            var mesh = new Mesh();owned.Add(mesh);
            var readback = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);owned.Add(readback);
            var target=RenderTexture.GetTemporary(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
            string[] globals={"_StudioSurfaceLighting","_StudioLocalLighting","_MainLightColor","_MainLightPosition",
                "unity_LightData","_WorldSpaceCameraPos","unity_SHAr","unity_SHAg","unity_SHAb","unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC"};
            var saved=Array.ConvertAll(globals,Shader.GetGlobalVector);
            try
            {
                Assert.That(FaceShadowBinding.ShadowMap,Is.Not.Null);
                material.SetFloat("_SourceCharacterShading",1);material.SetFloat("_SourceCharacterModel",model);
                material.SetFloat("_Cull",0);material.SetFloat("_linetexon",0);material.SetFloat("_SourceShadowExtend",0);
                material.SetFloat("_SourceSpecularPower",0);material.SetFloat("_SpecularPowerNail",0);
                material.SetColor("_BaseColor",Color.white);material.SetColor("_SourceShadowColor",Color.white);
                material.SetColor("_ShadowColor",Color.white);
                material.SetVector("_SourceAmbientShadow",new Vector4(.25f,.25f,.25f,0));
                material.SetFloat("_FaceShadowReady",1);material.SetTexture("_FaceShadowMap",FaceShadowBinding.ShadowMap);
                material.SetVector("_FaceShadowForward",new Vector4(0,0,1,0));material.SetVector("_FaceShadowRight",new Vector4(1,0,0,0));
                var ramp = new Texture2D(256,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};owned.Add(ramp);
                for(int x=0;x<256;x++)ramp.SetPixel(x,0,x<128?Color.black:Color.white);ramp.Apply();material.SetTexture("_SourceRamp",ramp);
                Shader.SetGlobalVector("_StudioSurfaceLighting",new Vector4(1,1,1,1));
                Shader.SetGlobalVector("_StudioLocalLighting",Vector4.zero);Shader.SetGlobalVector("_MainLightColor",Vector4.one);
                Shader.SetGlobalVector("unity_LightData",new Vector4(0,0,1,0));Shader.SetGlobalVector("_WorldSpaceCameraPos",new Vector4(.5f,.5f,5,1));
                foreach(var name in new[]{"unity_SHAr","unity_SHAg","unity_SHAb","unity_SHBr","unity_SHBg","unity_SHBb","unity_SHC"})Shader.SetGlobalVector(name,Vector4.zero);
                mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};
                mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.uv3=mesh.uv;mesh.triangles=new[]{0,2,1,0,3,2};
                Color[] Draw(float strength,float yaw,bool experimental=true)
                {
                    material.SetFloat("_FaceShadowStrength",strength);
                    var direction=Quaternion.Euler(0,yaw,0)*Vector3.forward;
                    Shader.SetGlobalVector("_MainLightPosition",new Vector4(direction.x,direction.y,direction.z,0));
                    Shader.SetGlobalVector("_StudioSurfaceLighting",experimental?new Vector4(1,1,1,1):Vector4.zero);
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,Color.magenta);
                    GL.PushMatrix();GL.LoadOrtho();
                    try{Assert.That(material.SetPass(0),Is.True);Graphics.DrawMeshNow(mesh,Matrix4x4.identity);}
                    finally{GL.PopMatrix();}
                    readback.ReadPixels(new Rect(0,0,64,64),0,0);readback.Apply();return readback.GetPixels();
                }
                var before=Draw(0,60);var after=Draw(1,60);
                // A hair-only depth map must shade its matching light without
                // changing another light, other surfaces, or the legacy path.
                var hairDepth=new Texture2DArray(512,512,1,TextureFormat.R16,false,true);owned.Add(hairDepth);
                var depthData=new ushort[512*512];
                for(int y=0;y<512;y++)for(int x=0;x<512;x++)depthData[y*512+x]=x<256?(ushort)16384:ushort.MaxValue;
                hairDepth.SetPixelData(depthData,0,0);hairDepth.Apply();
                material.SetTexture("_FaceHairDepth",hairDepth);material.SetFloat("_FaceHairShadowCount",1);
                material.SetVector("_FaceHairShadowWeights",Vector4.one);
                var projection=Matrix4x4.identity;projection.m22=0;projection.m23=.5f;
                material.SetMatrixArray("_FaceHairWorldToShadow",new[]{projection,projection,projection,projection});
                var source=Vector3.forward;
                material.SetVectorArray("_FaceHairLightPositions",new[]{new Vector4(source.x,source.y,source.z,0),Vector4.zero,Vector4.zero,Vector4.zero});
                material.SetFloat("_FaceHairShadowStrength",1);
                material.SetFloat("_FaceHairShadowCount",0);var hairUnblocked=Draw(0,0);
                material.SetFloat("_FaceHairShadowCount",1);var hairShadow=Draw(0,0);
                // Scene occlusion is shared by face skin and eye layers, but
                // remains separate from the actor's filtered local hair map.
                material.SetFloat("_FaceHairShadowCount",0);
                material.SetTexture("_FaceSceneDepth",hairDepth);
                material.SetMatrixArray("_FaceSceneWorldToShadow",new[]{projection,projection,projection,projection});
                material.SetFloat("_FaceSceneShadowCount",1);var sceneShadow=Draw(0,0);
                if(model==4 || model==5 || model==6)
                {
                    Assert.That(sceneShadow[32*64+12].r,Is.LessThan(hairUnblocked[32*64+12].r*.05f),
                        "Rooms must continue to block the key light on both skin and eyes.");
                    Assert.That(sceneShadow[32*64+52].r,Is.EqualTo(hairUnblocked[32*64+52].r).Within(.0001f));
                }
                else for(int i=0;i<sceneShadow.Length;i++)Assert.That(sceneShadow[i].r,Is.EqualTo(hairUnblocked[i].r).Within(.0001f));
                material.SetFloat("_FaceSceneShadowCount",0);material.SetFloat("_FaceHairShadowCount",1);
                if(model==4)
                {
                    int shaded=32*64+12;
                    Assert.That(hairShadow[shaded].r,Is.LessThan(hairUnblocked[shaded].r-.01f),"Hair must cast a visible shadow.");
                    Assert.That(hairShadow[shaded].r,Is.EqualTo(after[shaded].r).Within(.0001f),"Hair must use the curve shadow's skin palette, not black out the key light.");
                    Assert.That(hairShadow[shaded].r,Is.GreaterThan(hairUnblocked[shaded].r*.5f),"The source skin shadow retains a colored direct-light response.");
                    Assert.That(hairShadow[32*64+52].r,Is.EqualTo(hairUnblocked[32*64+52].r).Within(.0001f));
                    // Already imported faces can still carry the old exclusion
                    // channel. It must not punch holes in hair or SDF shadows.
                    void FormerEyeMask(float amount) => mesh.SetUVs(2,new[] {
                        new Vector3(0,0,amount),new Vector3(1,0,amount),
                        new Vector3(1,1,amount),new Vector3(0,1,amount)});
                    FormerEyeMask(1);var eyeHair=Draw(0,0);
                    for(int i=0;i<eyeHair.Length;i++)Assert.That(eyeHair[i].r,Is.EqualTo(hairShadow[i].r).Within(.0001f),
                        "Eye skin must receive the same hair contour and shadow palette as the rest of the face.");
                    var eyeCurve=Draw(1,60);
                    Assert.That(eyeCurve[shaded].r,Is.EqualTo(after[shaded].r).Within(.0001f),
                        "Eye skin must also retain the face's SDF shadow.");
                    FormerEyeMask(.5f);var eyeTransition=Draw(0,0);
                    for(int i=0;i<eyeTransition.Length;i++)Assert.That(eyeTransition[i].r,Is.EqualTo(hairShadow[i].r).Within(.0001f),
                        "Former eye weights must not soften the hair silhouette.");
                    mesh.SetUVs(2,new[] {new Vector3(0,0,0),new Vector3(1,0,1),
                        new Vector3(1,1,1),new Vector3(0,1,0)});
                    var eyeProjection=projection;eyeProjection.m00=0;eyeProjection.m03=.25f;
                    material.SetMatrixArray("_FaceHairWorldToShadow",new[]{eyeProjection,eyeProjection,eyeProjection,eyeProjection});
                    var crispEyeBoundary=Draw(0,0);
                    Assert.That(crispEyeBoundary[shaded].r,Is.EqualTo(hairShadow[shaded].r).Within(.0002f),
                        "The face must keep the full hard shadow.");
                    Assert.That(crispEyeBoundary[32*64+52].r,Is.EqualTo(hairShadow[shaded].r).Within(.0002f),
                        "Hair coverage must remain continuous across the former eye-region boundary.");
                    material.SetMatrixArray("_FaceHairWorldToShadow",new[]{projection,projection,projection,projection});
                    FormerEyeMask(0);
                    material.SetVectorArray("_FaceHairLightPositions",new[]{new Vector4(Mathf.Sin(Mathf.PI/3),0,.5f,0),Vector4.zero,Vector4.zero,Vector4.zero});
                    var combinedShadow=Draw(1,60);
                    Assert.That(combinedShadow[shaded].r,Is.EqualTo(after[shaded].r).Within(.0001f),"Hair and curve shadows must not stack into a darker shadow.");
                    material.SetVectorArray("_FaceHairLightPositions",new[]{new Vector4(0,0,1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                    var otherLight=Draw(0,-60);material.SetFloat("_FaceHairShadowCount",0);var otherUnblocked=Draw(0,-60);
                    for(int i=0;i<otherLight.Length;i++)Assert.That(otherLight[i].r,Is.EqualTo(otherUnblocked[i].r).Within(.0001f));
                    foreach(var name in new[]{"unity_SHAr","unity_SHAg","unity_SHAb"})Shader.SetGlobalVector(name,new Vector4(0,0,0,.2f));
                    Shader.SetGlobalVector("_MainLightColor",Vector4.zero);var ambientOnly=Draw(0,0);
                    material.SetFloat("_FaceHairShadowCount",1);var ambientHair=Draw(0,0);
                    material.SetFloat("_FaceHairShadowCount",0);var ambientCurve=Draw(1,180);
                    Assert.That(ambientHair[shaded].r,Is.EqualTo(ambientCurve[shaded].r).Within(.0001f),"Hair must remain visible in the same skin palette when direct light contributes nothing.");
                    Assert.That(ambientHair[shaded].r,Is.LessThan(ambientOnly[shaded].r-.002f),"Unshadowed SH must not erase the hair silhouette.");
                    material.SetFloat("_FaceHairShadowCount",1);Shader.SetGlobalVector("_MainLightColor",Vector4.one);var blockedWithAmbient=Draw(0,0);
                    Assert.That(blockedWithAmbient[shaded].r-hairShadow[shaded].r,Is.EqualTo(ambientHair[shaded].r).Within(.0001f),"The hair surface palette must apply to both direct and environment light without removing SH energy.");
                    Shader.SetGlobalVector("_MainLightColor",Vector4.zero);
                    material.SetVector("_SourceAmbientShadow",new Vector4(.5f,.5f,.5f,.74f));
                    var paleHair=Draw(0,0);
                    material.SetColor("_ShadowColor",new Color(.7f,.5f,.4f,1));
                    var tintedAmbient=Draw(0,0);
                    Assert.That(tintedAmbient[shaded].r,Is.LessThan(paleHair[shaded].r-.002f),"The face contact tint must remain effective even when the source ambient-shadow overlay is pale.");
                    Assert.That(tintedAmbient[shaded].r,Is.GreaterThan(tintedAmbient[shaded].g));
                    Assert.That(tintedAmbient[shaded].g,Is.GreaterThan(tintedAmbient[shaded].b));
                    Assert.That(tintedAmbient[32*64+52].r,Is.EqualTo(ambientOnly[32*64+52].r).Within(.0001f),"The shadow tint must not change exposed skin.");
                    material.SetFloat("_FaceHairShadowStrength",.5f);var partialAmbient=Draw(0,0);
                    Assert.That(partialAmbient[shaded].r,Is.EqualTo((tintedAmbient[shaded].r+ambientOnly[shaded].r)*.5f).Within(.0002f),"Shadow strength must blend the contact palette, not multiply shaded skin twice.");
                    // Use the actual character-default pale source overlay.
                    // A dark synthetic source palette alone missed this visual
                    // regression: the valid SDF became nearly invisible.
                    material.SetFloat("_FaceHairShadowCount",0);
                    var paleCurve=Draw(1,60);var paleWithoutCurve=Draw(0,60);var partialCurve=Draw(.5f,60);
                    var faceTint=material.GetColor("_ShadowColor");material.SetColor("_ShadowColor",Color.white);
                    var originalPaleCurve=Draw(1,60);material.SetColor("_ShadowColor",faceTint);
                    Assert.That(paleCurve[shaded].r,Is.EqualTo(tintedAmbient[shaded].r).Within(.0002f),
                        "SDF and hair must share the visible face shadow endpoint under ambient light.");
                    Assert.That(paleCurve[shaded].r,Is.LessThan(paleWithoutCurve[shaded].r-.02f),
                        "The default pale source overlay must not erase the SDF in the final composite.");
                    Assert.That(partialCurve[shaded].r,Is.EqualTo((paleCurve[shaded].r+paleWithoutCurve[shaded].r)*.5f).Within(.0002f),
                        "Replacing the curve palette must not apply its coverage or strength twice.");
                    Assert.That(paleCurve[32*64+52].r,Is.EqualTo(originalPaleCurve[32*64+52].r).Within(.0002f),
                        "Exposed skin must keep its previous palette.");
                    FormerEyeMask(1);var paleEyeCurve=Draw(1,60);FormerEyeMask(0);
                    Assert.That(paleEyeCurve[shaded].r,Is.EqualTo(paleCurve[shaded].r).Within(.0002f),
                        "Eye skin must share the face's SDF under ambient illumination.");
                    material.SetFloat("_FaceShadowReady",0);var paleUnavailable=Draw(1,60);material.SetFloat("_FaceShadowReady",1);
                    Assert.That(paleUnavailable[shaded].r,Is.EqualTo(paleWithoutCurve[shaded].r).Within(.0002f));
                    Shader.SetGlobalVector("_MainLightColor",Vector4.one);
                    var directCurve=Draw(1,60);var directWithoutCurve=Draw(0,60);var directPartialCurve=Draw(.5f,60);
                    Assert.That(directPartialCurve[shaded].r,Is.EqualTo((directCurve[shaded].r+directWithoutCurve[shaded].r)*.5f).Within(.001f));
                    var paleLegacy=Draw(0,60,false);var paleLegacyWithCurve=Draw(1,60,false);
                    Assert.That(paleLegacyWithCurve[shaded].r,Is.EqualTo(paleLegacy[shaded].r).Within(.0002f));
                    material.SetFloat("_FaceHairShadowStrength",1);material.SetColor("_ShadowColor",Color.white);
                    material.SetVector("_SourceAmbientShadow",new Vector4(.25f,.25f,.25f,0));
                    Shader.SetGlobalVector("_MainLightColor",Vector4.one);
                    foreach(var name in new[]{"unity_SHAr","unity_SHAg","unity_SHAb"})Shader.SetGlobalVector(name,Vector4.zero);
                    material.SetFloat("_FaceHairShadowCount",1);var legacyHair=Draw(0,0,false);
                    material.SetFloat("_FaceHairShadowCount",0);var legacyNoHair=Draw(0,0,false);
                    for(int i=0;i<legacyHair.Length;i++)Assert.That(legacyHair[i].r,Is.EqualTo(legacyNoHair[i].r).Within(.0001f));
                }
                else for(int i=0;i<hairShadow.Length;i++)Assert.That(hairShadow[i].r,Is.EqualTo(hairUnblocked[i].r).Within(.0001f));
                material.SetFloat("_FaceHairShadowCount",0);
                // Garments shade body skin independently of face UV readiness.
                // They keep its colored dark endpoint and leave the face and
                // other material families unchanged.
                var clothesDepth=new Texture2DArray(1024,1024,1,TextureFormat.R16,false,true);owned.Add(clothesDepth);
                var clothesData=new ushort[1024*1024];
                for(int y=0;y<1024;y++)for(int x=0;x<1024;x++)clothesData[y*1024+x]=x<512?(ushort)16384:ushort.MaxValue;
                clothesDepth.SetPixelData(clothesData,0,0);clothesDepth.Apply();
                material.SetTexture("_ClothesSkinDepth",clothesDepth);
                material.SetVector("_ClothesSkinShadowWeights",Vector4.one);
                material.SetMatrixArray("_ClothesSkinWorldToShadow",new[]{projection,projection,projection,projection});
                material.SetVectorArray("_ClothesSkinLightPositions",new[]{new Vector4(0,0,1,0),Vector4.zero,Vector4.zero,Vector4.zero});
                material.SetFloat("_FaceShadowReady",0);
                var clothesUnblocked=Draw(0,0);
                var darkSkin=Draw(0,180);
                material.SetFloat("_ClothesSkinShadowCount",1);var clothesShadow=Draw(0,0);
                if(model==3)
                {
                    int shaded=32*64+12,unshaded=32*64+52;
                    Assert.That(clothesShadow[shaded].r,Is.EqualTo(darkSkin[shaded].r).Within(.0001f),"Clothes must use the skin's colored shadow endpoint.");
                    Assert.That(clothesShadow[shaded].r,Is.LessThan(clothesUnblocked[shaded].r-.01f));
                    Assert.That(clothesShadow[shaded].r,Is.GreaterThan(clothesUnblocked[shaded].r*.5f),"Garment occlusion must not black out the character light.");
                    Assert.That(clothesShadow[unshaded].r,Is.EqualTo(clothesUnblocked[unshaded].r).Within(.0001f));
                    foreach(var name in new[]{"unity_SHAr","unity_SHAg","unity_SHAb"})Shader.SetGlobalVector(name,new Vector4(0,0,0,.2f));
                    Shader.SetGlobalVector("_MainLightColor",Vector4.zero);var clothesAmbient=Draw(0,0);
                    Shader.SetGlobalVector("_MainLightColor",Vector4.one);var clothesWithAmbient=Draw(0,0);
                    Assert.That(clothesWithAmbient[shaded].r-clothesShadow[shaded].r,Is.EqualTo(clothesAmbient[shaded].r).Within(.0001f),"Clothes must preserve ambient and bounce illumination.");
                    foreach(var name in new[]{"unity_SHAr","unity_SHAg","unity_SHAb"})Shader.SetGlobalVector(name,Vector4.zero);
                    var wrongLight=Draw(0,-60);material.SetFloat("_ClothesSkinShadowCount",0);var wrongUnblocked=Draw(0,-60);
                    Assert.That(wrongLight[shaded].r,Is.EqualTo(wrongUnblocked[shaded].r).Within(.0001f),"Garments may only shade their matching light.");
                    material.SetFloat("_ClothesSkinShadowCount",1);var legacyClothes=Draw(0,0,false);
                    material.SetFloat("_ClothesSkinShadowCount",0);var legacyWithoutClothes=Draw(0,0,false);
                    Assert.That(legacyClothes[shaded].r,Is.EqualTo(legacyWithoutClothes[shaded].r).Within(.0001f));
                }
                else for(int i=0;i<clothesShadow.Length;i++)Assert.That(clothesShadow[i].r,Is.EqualTo(clothesUnblocked[i].r).Within(.0001f),
                    "Garment depth must not shade the face, eyes or non-skin surfaces, even with stale receiver properties.");
                material.SetFloat("_ClothesSkinShadowCount",0);material.SetFloat("_FaceShadowReady",1);
                if(model!=4)
                {
                    for(int i=0;i<before.Length;i++)Assert.That(after[i].r,Is.EqualTo(before[i].r).Within(.0001f));
                    return;
                }
                Assert.That(after[32*64+12].r,Is.LessThan(after[32*64+52].r-.05f),"Right-side light must illuminate the right side of the face.");
                var opposite=Draw(1,-60);
                for(int y=3;y<61;y++)for(int x=3;x<61;x++)
                    Assert.That(after[y*64+x].r,Is.EqualTo(opposite[y*64+63-x].r).Within(.003f),"Left and right curves must mirror at "+x+","+y+".");
                // Rotate the head basis and light together. Shape must stay attached to the head.
                var rotation=Quaternion.Euler(0,40,0);var forward=rotation*Vector3.forward;var right=rotation*Vector3.right;
                material.SetVector("_FaceShadowForward",new Vector4(forward.x,forward.y,forward.z,0));
                material.SetVector("_FaceShadowRight",new Vector4(right.x,right.y,right.z,0));
                var rotated=Draw(1,100);
                for(int i=0;i<after.Length;i++)Assert.That(rotated[i].r,Is.EqualTo(after[i].r).Within(.003f));
                material.SetVector("_FaceShadowForward",new Vector4(0,0,1,0));material.SetVector("_FaceShadowRight",new Vector4(1,0,0,0));
                var legacy=Draw(0,60,false);var legacyWithSetting=Draw(1,60,false);
                for(int i=0;i<legacy.Length;i++)Assert.That(legacyWithSetting[i].r,Is.EqualTo(legacy[i].r).Within(.0001f));
                material.SetFloat("_FaceShadowReady",0);var unavailable=Draw(1,60);
                for(int i=0;i<before.Length;i++)Assert.That(unavailable[i].r,Is.EqualTo(before[i].r).Within(.0001f));
                Debug.Log("FACE_SHADOW_GPU head rotation, mirrored lights, fallback and legacy isolation passed");
            }
            finally
            {
                for(int i=0;i<globals.Length;i++)Shader.SetGlobalVector(globals[i],saved[i]);
                RenderTexture.active=active;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                foreach(var item in owned)Object.DestroyImmediate(item);
            }
        }
    }
}
