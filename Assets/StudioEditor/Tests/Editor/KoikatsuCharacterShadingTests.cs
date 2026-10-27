using NUnit.Framework;
using StudioEditor.ReferenceModels;
using StudioEditor.Rendering;
using System.Reflection;
using UnityEngine;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuCharacterShadingTests
    {
        [Test]
        public void SceneOverrideAndReleaseKeepStandaloneShadingAndFaceBindings()
        {
            var root = new GameObject("Standalone shading lifecycle");
            var material = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
            var defaultRamp = new Texture2D(2, 1);
            var sceneRamp = new Texture2D(2, 1);
            var originalSun = RenderSettings.sun;
            var bridge = root.AddComponent<ImportedSceneRenderBridge>();
            try
            {
                material.SetFloat("_SourceCharacterModel", 4);
                KoikatsuCharacterShading.ApplyDefaults(new[] {material}, defaultRamp);
                var renderer = root.AddComponent<MeshRenderer>();renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();block.SetFloat("_FaceShadowReady", 1);
                block.SetFloat("_FaceHairShadowCount", 1);renderer.SetPropertyBlock(block);
                var context = root.AddComponent<ImportedSceneRenderContext>();
                context.Configure(default, default, null, 0, new[] {renderer});
                context.ConfigureCharacterShading(sceneRamp, Color.red);
                Invoke("LateUpdate");renderer.GetPropertyBlock(block);
                Assert.That(block.GetTexture("_SourceRamp"), Is.SameAs(sceneRamp));
                Invoke("OnDisable");renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_SourceCharacterShading"), Is.EqualTo(1));
                Assert.That(block.GetTexture("_SourceRamp"), Is.SameAs(defaultRamp));
                Assert.That(block.GetVector("_SourceAmbientShadow"), Is.EqualTo(material.GetVector("_SourceAmbientShadow")));
                Assert.That(block.GetFloat("_FaceShadowReady"), Is.EqualTo(1));
                Assert.That(block.GetFloat("_FaceHairShadowCount"), Is.EqualTo(1));
            }
            finally
            {
                Invoke("OnDisable");Object.DestroyImmediate(root);Object.DestroyImmediate(material);
                Object.DestroyImmediate(defaultRamp);Object.DestroyImmediate(sceneRamp);RenderSettings.sun = originalSun;
            }
            void Invoke(string name) => typeof(ImportedSceneRenderBridge)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
        }

        [Test]
        public void StandaloneDefaultsEnableEverySupportedSourceSurfaceButKeepGenericMaterials()
        {
            var materials = new Material[7];
            var ramp = new Texture2D(2, 1);
            try
            {
                for (int model = 0; model < materials.Length; model++)
                {
                    materials[model] = new Material(Shader.Find("StudioEditor/AnimeCharacter"));
                    materials[model].SetFloat("_SourceCharacterModel", model);
                }
                KoikatsuCharacterShading.ApplyDefaults(materials, ramp);
                Assert.That(materials[0].GetFloat("_SourceCharacterShading"), Is.Zero);
                for (int model = 1; model < materials.Length; model++)
                {
                    Assert.That(materials[model].GetFloat("_SourceCharacterShading"), Is.EqualTo(1), "model " + model);
                    Assert.That(materials[model].GetTexture("_SourceRamp"), Is.SameAs(ramp));
                    Assert.That(materials[model].GetVector("_SourceAmbientShadow").w, Is.EqualTo(.74f));
                }
            }
            finally
            {
                foreach (var material in materials) Object.DestroyImmediate(material);
                Object.DestroyImmediate(ramp);
            }
        }
    }
}
