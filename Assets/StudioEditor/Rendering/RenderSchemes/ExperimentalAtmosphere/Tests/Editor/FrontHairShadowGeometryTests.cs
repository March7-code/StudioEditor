using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class FrontHairShadowGeometryTests
    {
        private static Type GeometryType => typeof(ExperimentalRenderSession).Assembly.GetType(
            "StudioEditor.Rendering.Experimental.FrontHairShadowGeometry", true);

        [Test]
        public void CombinedHairKeepsWholeFrontCardsAndRejectsSideBackAndLateralCards()
        {
            var mesh = CombinedHair();
            try
            {
                var args = new object[] {mesh, mesh.vertices, new Bounds(new Vector3(0,1,0),new Vector3(2,2,1)), 0};
                var mask = (List<Vector2>)GeometryType.GetMethod("BuildMask", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,args);
                Assert.That(args[3], Is.EqualTo(2));
                for (int i = 0; i < 4; i++) Assert.That(mask[i], Is.EqualTo(Vector2.one));
                for (int i = 4; i < 16; i++) Assert.That(mask[i], Is.EqualTo(Vector2.zero),
                    "Reject the entire side card, including its crown vertices that coincide with the front card.");
                // Classification adds no horizontal clipping edge or new geometry.
                Assert.That(mesh.vertexCount, Is.EqualTo(16));
                Assert.That(mesh.GetTriangles(0).Length + mesh.GetTriangles(1).Length, Is.EqualTo(24));
            }
            finally {Object.DestroyImmediate(mesh);}
        }

        [TestCase(false)] [TestCase(true)]
        public void MaskPreservesSkinnedAppearanceAndRestoresOnlyItsOwnMesh(bool replaced)
        {
            var owner = new GameObject("Front hair mask lifecycle");
            var faceMesh = new Mesh(); var hairMesh = CombinedHair(); var replacement = new Mesh();
            IDisposable geometry = null;
            try
            {
                owner.transform.SetPositionAndRotation(new Vector3(3,2,-1), Quaternion.Euler(10,25,-5));
                var face = new GameObject("Face");face.transform.SetParent(owner.transform,false);
                faceMesh.vertices = new[] {new Vector3(-1,0,0),new Vector3(1,0,0),new Vector3(1,2,0),new Vector3(-1,2,0)};
                faceMesh.normals = new[] {Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
                faceMesh.triangles = new[] {0,1,2,0,2,3};
                faceMesh.bindposes = new[] {owner.transform.worldToLocalMatrix * face.transform.localToWorldMatrix};
                var weight = new BoneWeight {boneIndex0=0,weight0=1};
                faceMesh.boneWeights = new[] {weight,weight,weight,weight};
                var skin = face.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=faceMesh;skin.bones=new[]{owner.transform};skin.rootBone=owner.transform;
                var binding=face.AddComponent<FaceShadowBinding>();Assert.That(binding.Configure(skin,owner.transform),Is.True);
                var hair = new GameObject("Combined hair");hair.transform.SetParent(owner.transform,false);
                hair.transform.localScale = new Vector3(1.2f,.9f,1.05f);
                var renderer=hair.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=hairMesh;
                renderer.bones=new[]{owner.transform};renderer.rootBone=owner.transform;
                hairMesh.bindposes=new[]{owner.transform.worldToLocalMatrix*hair.transform.localToWorldMatrix};
                var weights=new BoneWeight[16];for(int i=0;i<16;i++)weights[i]=weight;hairMesh.boneWeights=weights;
                hairMesh.AddBlendShapeFrame("Hair shape",100,new Vector3[16],new Vector3[16],new Vector3[16]);
                var originalUv=new List<Vector2>();for(int i=0;i<16;i++)originalUv.Add(new Vector2(.3f,.2f));hairMesh.SetUVs(7,originalUv);
                geometry=(IDisposable)Activator.CreateInstance(GeometryType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{renderer,binding},null);
                var copy=renderer.sharedMesh;
                Assert.That(copy,Is.Not.SameAs(hairMesh));
                Assert.That(copy.vertices,Is.EqualTo(hairMesh.vertices));Assert.That(copy.normals,Is.EqualTo(hairMesh.normals));
                Assert.That(copy.boneWeights,Is.EqualTo(hairMesh.boneWeights));Assert.That(copy.bindposes,Is.EqualTo(hairMesh.bindposes));
                Assert.That(copy.uv,Is.EqualTo(hairMesh.uv));Assert.That(copy.blendShapeCount,Is.EqualTo(1));
                var copiedUv=new List<Vector2>();copy.GetUVs(7,copiedUv);
                Assert.That(copiedUv[0],Is.EqualTo(Vector2.one));Assert.That(copiedUv[4],Is.EqualTo(Vector2.zero));
                if(replaced)renderer.sharedMesh=replacement;
                geometry.Dispose();geometry=null;
                Assert.That(renderer.sharedMesh,Is.SameAs(replaced?replacement:hairMesh));
                Assert.That(copy==null,Is.True);
                var restored=new List<Vector2>();hairMesh.GetUVs(7,restored);Assert.That(restored,Is.EqualTo(originalUv));
            }
            finally
            {
                geometry?.Dispose();Object.DestroyImmediate(owner);Object.DestroyImmediate(faceMesh);
                Object.DestroyImmediate(hairMesh);Object.DestroyImmediate(replacement);
            }
        }

        private static Mesh CombinedHair()
        {
            var mesh=new Mesh();
            mesh.vertices=new[]
            {
                new Vector3(-.4f,1.1f,.7f),new Vector3(.4f,1.1f,.7f),new Vector3(.4f,2,.7f),new Vector3(-.4f,2,.7f),
                new Vector3(-.4f,2,.7f),new Vector3(.4f,2,.7f),new Vector3(.8f,-.5f,.8f),new Vector3(.5f,-.5f,.8f),
                new Vector3(-.4f,1.2f,-.7f),new Vector3(.4f,1.2f,-.7f),new Vector3(.4f,2.2f,-.7f),new Vector3(-.4f,2.2f,-.7f),
                new Vector3(1.2f,1.2f,.7f),new Vector3(1.5f,1.2f,.7f),new Vector3(1.5f,2.2f,.7f),new Vector3(1.2f,2.2f,.7f)
            };
            mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2,0,2,3,4,5,6,4,6,7},0);
            mesh.SetTriangles(new[]{8,9,10,8,10,11,12,13,14,12,14,15},1);
            var uv=new Vector2[16];var normals=new Vector3[16];for(int i=0;i<16;i++){uv[i]=new Vector2(i*.01f,.25f);normals[i]=Vector3.forward;}
            mesh.uv=uv;mesh.normals=normals;return mesh;
        }
    }
}
