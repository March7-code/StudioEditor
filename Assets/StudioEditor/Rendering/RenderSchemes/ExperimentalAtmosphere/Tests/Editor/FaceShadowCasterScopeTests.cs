using System;
using System.Reflection;
using NUnit.Framework;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class FaceShadowCasterScopeTests
    {
        [Test]
        public void AccessoryHairOutsideHeadPrefabBelongsToItsOwnCharactersFaceShadows()
        {
            var character = new GameObject("Character");character.AddComponent<CharacterShadowBinding>();
            var other = new GameObject("Other character");other.AddComponent<CharacterShadowBinding>();
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var mesh = new Mesh();
            try
            {
                material.SetFloat("_StudioSurfaceRole", (float)CharacterRenderSurfaceRole.Accessory);
                // A custom shader's source model remains zero; ownership and
                // the imported role still identify this as a valid occluder.
                var skeleton = new GameObject("Head bone");skeleton.transform.SetParent(character.transform);
                var hair = new GameObject("Accessory hair");hair.transform.SetParent(skeleton.transform);
                var renderer = hair.AddComponent<MeshRenderer>();renderer.sharedMaterial = material;
                var unrelated = other.AddComponent<MeshRenderer>();unrelated.sharedMaterial = material;
                var leg = new GameObject("Leg accessory");leg.transform.SetParent(character.transform);
                leg.AddComponent<MeshRenderer>().sharedMaterial = material;
                foreach (var slot in new[] {"ct_hairB", "ct_hairS", "ct_hairO_01"})
                {
                    var side = new GameObject(slot);side.transform.SetParent(skeleton.transform);
                    side.AddComponent<MeshRenderer>().sharedMaterial=material;
                }
                var head = new GameObject("Head prefab");head.transform.SetParent(skeleton.transform);
                mesh.vertices = new[] {Vector3.zero,Vector3.right,Vector3.up};mesh.triangles = new[] {0,1,2};
                mesh.normals = new[] {Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.bindposes = new[] {Matrix4x4.identity};var weight = new BoneWeight {boneIndex0=0,weight0=1};
                mesh.boneWeights = new[] {weight,weight,weight};
                var skin = head.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh = mesh;
                skin.bones = new[] {skeleton.transform};skin.rootBone=skeleton.transform;
                var binding = head.AddComponent<FaceShadowBinding>();
                Assert.That(binding.Configure(skin,skeleton.transform,skeleton.transform),Is.True);
                var type = typeof(ExperimentalRenderSession).Assembly.GetType("StudioEditor.Rendering.Experimental.FaceHairShadowRenderer", true);
                var found = (Renderer[])type.GetMethod("FindHeadOccluders", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] {binding});
                Assert.That(found, Is.EquivalentTo(new[] {renderer}));
            }
            finally {Object.DestroyImmediate(character);Object.DestroyImmediate(other);Object.DestroyImmediate(material);Object.DestroyImmediate(mesh);}
        }

        [Test]
        public void HairCasterRespectsGameObjectAndCustomShadowLayers()
        {
            var owner = new GameObject("Caster layers");var lamp = new GameObject("Shadow light");
            try
            {
                var renderer=owner.AddComponent<MeshRenderer>();renderer.renderingLayerMask=2;
                var light=lamp.AddComponent<Light>();var data=light.GetUniversalAdditionalLightData();
                data.renderingLayers=1;data.customShadowLayers=true;data.shadowRenderingLayers=2;
                var type=typeof(ExperimentalRenderSession).Assembly.GetType("StudioEditor.Rendering.Experimental.FaceHairShadowRenderer",true);
                bool Casts()=>(bool)type.GetMethod("CastsForLight",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{renderer,light});
                Assert.That(Casts(),Is.True);
                light.cullingMask=0;Assert.That(Casts(),Is.False);
                light.cullingMask=-1;data.customShadowLayers=false;Assert.That(Casts(),Is.False);
                data.renderingLayers=2;Assert.That(Casts(),Is.True);
            }
            finally {Object.DestroyImmediate(owner);Object.DestroyImmediate(lamp);}
        }

        [TestCase(false, 0f)] [TestCase(false, 60f)] [TestCase(false, -60f)] [TestCase(true, 35f)]
        public void ProjectedHairDepthMatchesTheReceiverRay(bool point, float yaw)
        {
            var owner = new GameObject("Hair projection test");
            var mesh = new Mesh(); var material = new Material(Resources.Load<Shader>("StudioEditorAtmosphere/FaceHairDepth"));
            var image = new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);
            var target = RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var oldClip = Shader.GetGlobalMatrix("_HairShadowClipMatrix");
            var oldTexture = Shader.GetGlobalMatrix("_HairShadowTextureMatrix");
            try
            {
                var light = owner.AddComponent<Light>(); light.type = point ? LightType.Point : LightType.Directional;
                var direction = (Quaternion.Euler(0,yaw,0) * new Vector3(.2f,.35f,1)).normalized;
                var bounds = new Bounds(new Vector3(.31f,.47f,.19f),new Vector3(.2f,.24f,.14f));
                light.transform.SetPositionAndRotation(bounds.center + direction * .7f,Quaternion.LookRotation(-direction));
                var type = typeof(ExperimentalRenderSession).Assembly.GetType("StudioEditor.Rendering.Experimental.FaceHairShadowRenderer", true);
                var arguments = new object[] {bounds,light,Vector3.up,null,null};
                type.GetMethod("CreateProjection",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,arguments);
                var clip = (Matrix4x4)arguments[3]; var texture = (Matrix4x4)arguments[4];
                // An off-centre strand must shade the receiver directly behind
                // it, not its mirror across the projection's vertical axis.
                var receiver = bounds.center + new Vector3(.026f,.043f,0);
                var ray = point ? (light.transform.position-receiver).normalized : direction;
                var right = Vector3.Cross(Vector3.up,ray).normalized;
                var up = Vector3.Cross(ray,right).normalized;
                var caster = receiver + ray * .04f;
                mesh.vertices = new[] {caster-right*.012f-up*.012f,caster+right*.012f-up*.012f,
                    caster+right*.012f+up*.012f,caster-right*.012f+up*.012f};
                mesh.uv = new[] {Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; mesh.triangles = new[] {0,1,2,0,2,3};
                Shader.SetGlobalMatrix("_HairShadowClipMatrix",clip); Shader.SetGlobalMatrix("_HairShadowTextureMatrix",texture);
                RenderTexture.active=target; GL.sRGBWrite=false; GL.Clear(false,true,Color.white);
                Assert.That(material.SetPass(0),Is.True); Graphics.DrawMeshNow(mesh,Matrix4x4.identity);
                image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                Vector3 Project(Vector3 world)
                {
                    var p=texture*new Vector4(world.x,world.y,world.z,1);return new Vector3(p.x,p.y,p.z)/p.w;
                }
                var p=Project(receiver);var c=Project(caster);var open=Project(receiver+right*.05f);
                float stored=image.GetPixel((int)(p.x*256),(int)(p.y*256)).r;
                Assert.That(stored,Is.EqualTo(c.z).Within(.0005f),"Caster and receiver must address the same rasterized shadow pixel.");
                Assert.That(stored,Is.LessThan(p.z-.0001f),"Depth must increase away from the light.");
                Assert.That(image.GetPixel((int)(open.x*256),(int)(open.y*256)).r,Is.EqualTo(1).Within(.0001f));
            }
            finally
            {
                Shader.SetGlobalMatrix("_HairShadowClipMatrix",oldClip);Shader.SetGlobalMatrix("_HairShadowTextureMatrix",oldTexture);
                RenderTexture.active=active;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(owner);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);Object.DestroyImmediate(image);
            }
        }

        [TestCase(true, 1f, true)]
        [TestCase(false, 1f, false)]
        [TestCase(true, 0f, false)]
        public void OnlyActiveSourceCurvesReplaceGeometricFaceCasting(bool source, float curve, bool excluded)
        {
            var root = new GameObject("Face caster lifecycle");
            var mesh = new Mesh();var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var type = typeof(ExperimentalRenderSession).Assembly.GetType("StudioEditor.Rendering.Experimental.FaceShadowCasterScope", true);
            var scope = Activator.CreateInstance(type, true);
            try
            {
                mesh.vertices = new[] {Vector3.zero, Vector3.right, Vector3.up};
                mesh.triangles = new[] {0, 1, 2};mesh.normals = new[] {Vector3.forward, Vector3.forward, Vector3.forward};
                mesh.bindposes = new[] {Matrix4x4.identity};
                var weight = new BoneWeight {boneIndex0 = 0, weight0 = 1};
                mesh.boneWeights = new[] {weight, weight, weight};
                var renderer = root.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh = mesh;
                renderer.bones = new[] {root.transform};renderer.rootBone = root.transform;renderer.sharedMaterial = material;
                material.SetFloat("_SourceCharacterModel", 4);material.SetFloat("_SourceCharacterShading", 1);
                var binding = root.AddComponent<FaceShadowBinding>();Assert.That(binding.Configure(renderer, root.transform), Is.True);
                var block = new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                block.SetFloat("_SourceCharacterShading", source ? 1 : 0);block.SetFloat("_FaceShadowStrength", curve);
                renderer.SetPropertyBlock(block);
                foreach (var original in new[] {ShadowCastingMode.On, ShadowCastingMode.TwoSided, ShadowCastingMode.Off})
                {
                    renderer.shadowCastingMode = original;Invoke("Exclude", binding);Invoke("Exclude", binding);
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(excluded ? ShadowCastingMode.Off : original));
                    Invoke("Restore");Assert.That(renderer.shadowCastingMode, Is.EqualTo(original));
                }
                binding.enabled = false;renderer.shadowCastingMode = ShadowCastingMode.On;Invoke("Exclude", binding);
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
            }
            finally {Invoke("Restore");Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
            void Invoke(string method, params object[] args) => type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scope, args);
        }
    }
}
