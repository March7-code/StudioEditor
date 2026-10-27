using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class ClothesSkinShadowTests
    {
        [TestCase(LightType.Directional)] [TestCase(LightType.Point)] [TestCase(LightType.Spot)]
        public void NearbyLocalLightsKeepNativeShadowsWhenOneProjectionCannotCoverTheBody(LightType kind)
        {
            var go=new GameObject("Body light projection");
            try
            {
                var light=go.AddComponent<Light>();light.type=kind;
                var type=typeof(ExperimentalRenderSession).Assembly.GetType("StudioEditor.Rendering.Experimental.ClothesSkinShadowRenderer",true);
                var project=type.GetMethod("CanProjectBody",BindingFlags.Static|BindingFlags.NonPublic);
                var bounds=new Bounds(Vector3.zero,Vector3.one*2);
                bool Accepts()=>(bool)project.Invoke(null,new object[]{bounds,light});
                go.transform.position=Vector3.forward*.5f;
                Assert.That(Accepts(),Is.EqualTo(kind==LightType.Directional));
                go.transform.position=Vector3.forward*5;
                Assert.That(Accepts(),Is.True);
            }
            finally {Object.DestroyImmediate(go);}
        }

        [Test]
        public void GarmentMapTargetsBodySkinWithoutFaceOrEyeLayers()
        {
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            try
            {
                var type = typeof(ExperimentalRenderSession).Assembly.GetType(
                    "StudioEditor.Rendering.Experimental.ClothesSkinShadowRenderer",true);
                var accepts = type.GetMethod("IsSkin",BindingFlags.Static|BindingFlags.NonPublic);
                for (int model=1;model<=6;model++)
                {
                    material.SetFloat("_SourceCharacterModel",model);
                    Assert.That((bool)accepts.Invoke(null,new object[]{material}),Is.EqualTo(model==3),
                        "Only body skin receives the garment map; model="+model);
                }
            }
            finally {Object.DestroyImmediate(material);}
        }

        [Test]
        public void BoneEnvelopeContainsDeformedSkinWithoutTheImportedAnimationBounds()
        {
            var owner = new GameObject("Garment shadow bounds test");
            var mesh = new Mesh(); var baked = new Mesh();
            try
            {
                var a = new GameObject("Bone A").transform; a.SetParent(owner.transform); a.localPosition = Vector3.left * .3f;
                var b = new GameObject("Bone B").transform; b.SetParent(owner.transform); b.localPosition = Vector3.right * .3f;
                var renderer = owner.AddComponent<SkinnedMeshRenderer>();
                mesh.vertices = new[] { new Vector3(-.6f,-.2f,0),new Vector3(.6f,-.2f,0),new Vector3(.6f,.2f,0),new Vector3(-.6f,.2f,0) };
                mesh.triangles = new[] {0,1,2,0,2,3};
                mesh.bindposes = new[] {a.worldToLocalMatrix*owner.transform.localToWorldMatrix,b.worldToLocalMatrix*owner.transform.localToWorldMatrix};
                mesh.boneWeights = new[] {Weight(1),Weight(0),Weight(.25f),Weight(.75f)};
                renderer.bones = new[] {a,b}; renderer.rootBone = owner.transform; renderer.sharedMesh = mesh;
                renderer.localBounds = new Bounds(Vector3.zero,Vector3.one*100);
                var type = typeof(ExperimentalRenderSession).Assembly.GetType(
                    "StudioEditor.Rendering.Experimental.ClothesSkinShadowRenderer+SkinBounds",true);
                var cached = type.GetMethod("Create",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{renderer});
                Assert.That(cached,Is.Not.Null);
                foreach(float angle in new[] {0f,60f,-110f})
                {
                    a.localRotation = Quaternion.Euler(0,angle,angle*.5f); b.localRotation = Quaternion.Euler(angle,-angle,0);
                    owner.transform.SetPositionAndRotation(new Vector3(2,3,-1),Quaternion.Euler(20,35,10));
                    var bounds = (Bounds)type.GetMethod("World",BindingFlags.Instance|BindingFlags.Public).Invoke(cached,new object[]{renderer});
                    renderer.BakeMesh(baked);
                    foreach(var vertex in baked.vertices)
                        Assert.That(bounds.Contains(renderer.transform.TransformPoint(vertex)),Is.True,"The pose-following shadow projection must contain every skinned vertex.");
                    Assert.That(bounds.size.magnitude,Is.LessThan(3),"A shared 100-metre animation envelope must not dilute garment shadow resolution.");
                }
            }
            finally {Object.DestroyImmediate(owner);Object.DestroyImmediate(mesh);Object.DestroyImmediate(baked);}
            static BoneWeight Weight(float value)=>new BoneWeight {boneIndex0=0,weight0=value,boneIndex1=1,weight1=1-value};
        }
    }
}
