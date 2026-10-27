using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class FaceEnvironmentShadowTests
    {
        private static Type RendererType => typeof(ExperimentalRenderSession).Assembly.GetType(
            "StudioEditor.Rendering.Experimental.FaceEnvironmentShadowRenderer",true);

        [Test]
        public void SceneMapKeepsRoomsAndOtherActorsButOmitsItsWholeReceivingCharacter()
        {
            var root=new GameObject("Character");root.AddComponent<CharacterShadowBinding>();
            var other=new GameObject("Other actor");other.AddComponent<CharacterShadowBinding>();
            var room=new GameObject("Room");
            var mesh=new Mesh();
            var material=new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            try
            {
                var head=new GameObject("Head");head.transform.SetParent(root.transform);
                var skin=head.AddComponent<SkinnedMeshRenderer>();
                mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};
                mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward};mesh.bindposes=new[]{Matrix4x4.identity};
                var weight=new BoneWeight{boneIndex0=0,weight0=1};mesh.boneWeights=new[]{weight,weight,weight};
                skin.sharedMesh=mesh;skin.bones=new[]{head.transform};skin.rootBone=head.transform;
                var face=head.AddComponent<FaceShadowBinding>();Assert.That(face.Configure(skin,head.transform,head.transform),Is.True);
                Renderer Child(string name,Transform parent) {var go=new GameObject(name);go.transform.SetParent(parent);return go.AddComponent<MeshRenderer>();}
                var ownHair=Child("Hair",head.transform);
                var eyes=Child("Eyes",head.transform);
                var clothes=Child("Clothes",root.transform);
                var body=Child("Body and neck",root.transform);
                var tongue=Child("Tongue outside head prefab",root.transform);
                var accessory=Child("Outside head slot",root.transform);
                var otherHair=other.AddComponent<MeshRenderer>();otherHair.sharedMaterial=material;
                var otherClothes=Child("Other actor clothes",other.transform);
                material.SetFloat("_StudioSurfaceRole",(float)CharacterRenderSurfaceRole.Hair);accessory.sharedMaterial=material;
                var wall=room.AddComponent<MeshRenderer>();
                foreach (var renderer in new Renderer[]{skin,ownHair,eyes,clothes,body,tongue,accessory})
                    Assert.That(CastsOnBody(renderer),Is.False,"Body scene depth must omit every part of its own actor.");
                Assert.That(CastsOnBody(otherHair),Is.True);Assert.That(CastsOnBody(otherClothes),Is.True);Assert.That(CastsOnBody(wall),Is.True);
                Assert.That(Casts(skin),Is.False);Assert.That(Casts(ownHair),Is.False);Assert.That(Casts(eyes),Is.False);
                Assert.That(Casts(accessory),Is.False);Assert.That(Casts(clothes),Is.False);
                Assert.That(Casts(body),Is.False);Assert.That(Casts(tongue),Is.False);
                Assert.That(Casts(otherHair),Is.True);Assert.That(Casts(otherClothes),Is.True);Assert.That(Casts(wall),Is.True);
                wall.shadowCastingMode=ShadowCastingMode.Off;Assert.That(Casts(wall),Is.False);
                wall.shadowCastingMode=ShadowCastingMode.ShadowsOnly;Assert.That(Casts(wall),Is.True);
                wall.forceRenderingOff=true;Assert.That(Casts(wall),Is.False);
                Assert.That(CastsOnBody(wall),Is.False);
                bool Casts(Renderer renderer)=>(bool)RendererType.GetMethod("CastsSceneShadow",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{renderer,face});
                bool CastsOnBody(Renderer renderer)=>(bool)RendererType.GetMethod("CastsBodySceneShadow",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{renderer,root.GetComponent<CharacterShadowBinding>()});
            }
            finally {Object.DestroyImmediate(root);Object.DestroyImmediate(other);Object.DestroyImmediate(room);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
        }

        [TestCase(LightType.Directional)] [TestCase(LightType.Point)] [TestCase(LightType.Spot)]
        public void ProjectionIncludesOccludersBetweenTheLightAndFace(LightType kind)
        {
            var root=new GameObject("Light");
            try
            {
                var light=root.AddComponent<Light>();light.type=kind;root.transform.position=new Vector3(0,0,5);
                root.transform.forward=Vector3.back;
                var args=new object[]{new Bounds(Vector3.zero,Vector3.one*.2f),light,Vector3.up,null,null,null};
                RendererType.GetMethod("CreateProjection",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
                var matrix=(Matrix4x4)args[4];
                Vector3 Project(Vector3 p) {var q=matrix*new Vector4(p.x,p.y,p.z,1);return new Vector3(q.x,q.y,q.z)/q.w;}
                var face=Project(Vector3.zero);var blocker=Project(new Vector3(0,0,3));
                Assert.That(face.x,Is.EqualTo(.5f).Within(.0001f));Assert.That(face.y,Is.EqualTo(.5f).Within(.0001f));
                Assert.That(blocker.z,Is.InRange(0f,face.z),"A distant room wall must not be clipped out like a hair-only map.");
            }
            finally {Object.DestroyImmediate(root);}
        }
    }
}
