using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Tests
{
    public sealed class ImportedLightingSyncTests
    {
        [TestCase("p_stneo_Directionallight")]
        [TestCase("p_stneo_Pointlight")]
        [TestCase("p_stneo_Spotlight")]
        public void StudioLightUsesPrefabMaskRatherThanAllLabel(string assetName)
        {
            var path = "D:/koikatsu/Koikatu/abdata/studio/00.unity3d";
            if (!File.Exists(path)) Assert.Ignore("Local source lights are not installed.");
            var definition = KoikatsuStudioLightMetadata.Read(new KoikatsuBundleSource(path), assetName);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.CullingMask, Is.EqualTo(2064));
            Assert.That(definition.GetAudience(0), Is.EqualTo(ImportedLightAudience.World));
            Assert.That(definition.GetAudience(2), Is.EqualTo(ImportedLightAudience.All));
            var owner = new GameObject("Source light defaults");
            try
            {
                var light = owner.AddComponent<Light>();
                definition.Apply(light);
                Assert.That(light.renderMode, Is.EqualTo(LightRenderMode.Auto));
                Assert.That(light.shadowStrength, Is.EqualTo(.8f).Within(.00001f));
                Assert.That(light.shadowBias, Is.EqualTo(.0015f).Within(.00001f));
                Assert.That(light.shadowNormalBias, Is.EqualTo(.8f).Within(.00001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [TestCase(1024, 0, ImportedLightAudience.Characters)]
        [TestCase(3072, 0, ImportedLightAudience.All)]
        [TestCase(1024, 2, ImportedLightAudience.None)]
        [TestCase(3072, 1, ImportedLightAudience.Characters)]
        public void StudioLightTargetAppliesSourceLayerToggles(int mask, int target, ImportedLightAudience expected)
        {
            var definition = new KoikatsuStudioLightMetadata.Definition { CullingMask = mask };
            Assert.That(definition.GetAudience(target), Is.EqualTo(expected));
        }

        [Test]
        public void FullSceneSuppressesHostLightsAndRestoresTheirOriginalStates()
        {
            using (var fixture = new Fixture())
            {
                var disabled = fixture.AddLight("Disabled host", LightType.Point, false);
                var point = fixture.AddLight("Host point", LightType.Point);
                var context = fixture.AddContext("Card");
                var source = fixture.AddLight("Map sun", LightType.Directional, parent: context.transform);
                Configure(context, source);
                fixture.Tick();
                Assert.That(fixture.Host.enabled, Is.False);
                Assert.That(point.enabled, Is.False);
                Assert.That(disabled.enabled, Is.False);
                Assert.That(source.enabled, Is.True);
                Assert.That(RenderSettings.sun, Is.SameAs(source));
                var later = fixture.AddLight("Late host spot", LightType.Spot);
                fixture.Tick();
                Assert.That(later.enabled, Is.False);
                context.enabled = false; fixture.Tick();
                Assert.That(fixture.Host.enabled, Is.True);
                Assert.That(point.enabled, Is.True);
                Assert.That(later.enabled, Is.True);
                Assert.That(disabled.enabled, Is.False);
                Assert.That(RenderSettings.sun, Is.SameAs(fixture.Host));
            }
        }

        [Test]
        public void SceneReplacementKeepsHostSuppressedAndBridgeTeardownRestoresIt()
        {
            using (var fixture = new Fixture())
            {
                var first = fixture.AddContext("First card");
                Configure(first, null); fixture.Tick();
                var second = fixture.AddContext("Second card");
                var source = fixture.AddLight("Second map sun", LightType.Directional, parent: second.transform);
                Configure(second, source); first.enabled = false; fixture.Tick();
                Assert.That(fixture.Host.enabled, Is.False);
                Assert.That(RenderSettings.sun, Is.SameAs(source));
                // An invisible/unlit map must not silently fall back to the editor's sun.
                source.enabled = false; fixture.Tick();
                Assert.That(ImportedSceneRenderBridge.IsSourceKeyLight(RenderSettings.sun), Is.True);
                fixture.DisableBridge();
                Assert.That(fixture.Host.enabled, Is.True);
                Assert.That(RenderSettings.sun, Is.SameAs(fixture.Host));
            }
        }

        [Test]
        public void MapAmbientReplacesHostSkyAndRestoresOnRelease()
        {
            using (var fixture = new Fixture())
            {
                var before = new EnvironmentSnapshot();
                try
                {
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientIntensity = .8f;
                    RenderSettings.ambientSkyColor = Color.magenta;
                    var probe = new SphericalHarmonicsL2(); probe[1, 3] = .25f;
                    var context = fixture.AddContext("Ambient card");
                    Configure(context, null);
                    context.ConfigureEnvironment(new ImportedSceneEnvironment(AmbientMode.Flat, .6f,
                        Color.cyan, Color.gray, Color.black, probe));
                    fixture.Tick();
                    Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                    Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(Color.cyan));
                    Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(.6f));
                    context.enabled = false; fixture.Tick();
                    Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                    Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(.8f));
                    Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(Color.magenta));
                }
                finally { before.Restore(); }
            }
        }

        [Test]
        public void CharacterOnlyContextLeavesEditorLightsAvailable()
        {
            using (var fixture = new Fixture())
            {
                var context = fixture.AddContext("Character");
                context.Configure(default, default, null, 0, Array.Empty<Renderer>());
                fixture.Tick();
                Assert.That(fixture.Host.enabled, Is.True);
                Assert.That(RenderSettings.sun, Is.SameAs(fixture.Host));
            }
        }

        [TestCase(LightType.Directional)]
        [TestCase(LightType.Point)]
        [TestCase(LightType.Spot)]
        public void LegacyWorldRadianceIsScopedToRenderingAndDoesNotChangeTheCard(LightType type)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("Requires linear rendering.");
            using var fixture = new Fixture();
            var context = fixture.AddContext("Legacy card");
            var light = fixture.AddLight("Map light", type, parent: context.transform);
            var color = new Color(.6838235f,.4774281f,.2162089f,0);
            light.color = color; light.intensity = 3.02f;
            Configure(context, type == LightType.Directional ? light : null);
            context.ConfigureCharacterShading(null, Color.black, useLinearLightIntensity:false);
            fixture.Tick();
            var camera = new GameObject("Display camera").AddComponent<Camera>();
            camera.transform.SetParent(fixture.Root.transform);
            fixture.CameraEvent("BeginCamera", camera);
            var radiance = light.color.linear * light.intensity;
            Assert.That(radiance.r, Is.EqualTo(4.931f).Within(.003f));
            Assert.That(radiance.g, Is.EqualTo(2.237f).Within(.003f));
            Assert.That(radiance.b, Is.EqualTo(.384f).Within(.003f));
            Assert.That(fixture.Host.color, Is.EqualTo(Color.white));
            // Nested reflection captures must neither convert twice nor restore early.
            fixture.CameraEvent("BeginCamera", camera);
            Assert.That(light.color.linear * light.intensity, Is.EqualTo(radiance));
            fixture.CameraEvent("EndCamera", camera);
            Assert.That(light.color.linear * light.intensity, Is.EqualTo(radiance));
            fixture.CameraEvent("EndCamera", camera);
            Assert.That(light.color, Is.EqualTo(color));
            Assert.That(light.intensity, Is.EqualTo(3.02f));
            fixture.CameraEvent("BeginCamera", camera);
            fixture.DisableBridge();
            Assert.That(light.color, Is.EqualTo(color), "Teardown during rendering leaked compensated colors.");
        }

        [Test]
        public void SharedLegacyLightUsesConvertedRadianceWhileCharacterOnlyLightKeepsShaderLookup()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) Assert.Ignore("Requires linear rendering.");
            using var fixture = new Fixture();
            var context = fixture.AddContext("Legacy card");
            var shared = fixture.AddLight("Shared", LightType.Point, parent:context.transform);
            var character = fixture.AddLight("Character only", LightType.Point, parent:context.transform);
            var sharedTarget = shared.gameObject.AddComponent<ImportedLightTarget>();sharedTarget.Audience=ImportedLightAudience.All;
            var characterTarget = character.gameObject.AddComponent<ImportedLightTarget>();characterTarget.Audience=ImportedLightAudience.Characters;
            shared.color=character.color=new Color(.6f,.4f,.2f,1);shared.intensity=character.intensity=2;
            var renderer = context.gameObject.AddComponent<MeshRenderer>();
            context.Configure(new ImportedSceneLight(Color.white,1,Vector3.zero,false), default,null,0,new[]{renderer},
                new[]{sharedTarget,characterTarget});
            context.ConfigureCharacterShading(null,Color.black,useLinearLightIntensity:false);fixture.Tick();
            var block = new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            var colors = block.GetVectorArray("_SourceLinearLightColors");
            Assert.That(colors[0], Is.EqualTo((Vector4)(shared.color*2).linear));
            Assert.That(colors[1], Is.EqualTo((Vector4)(character.color.linear*2)));
            var original=character.color;
            fixture.CameraEvent("BeginCamera",null);
            Assert.That(shared.color, Is.Not.EqualTo(original));
            Assert.That(character.color, Is.EqualTo(original));
            fixture.CameraEvent("EndCamera",null);
            Assert.That(shared.color, Is.EqualTo(original));
            context.ConfigureCharacterShading(null,Color.black,useLinearLightIntensity:true);fixture.Tick();
            fixture.CameraEvent("BeginCamera",null);
            Assert.That(shared.color, Is.EqualTo(original), "Modern source lights must retain their own convention.");
            fixture.CameraEvent("EndCamera",null);
        }

        [Test]
        public void SourceMapSelectsSavedDayNightPresetAndBindsTheActualSun()
        {
            var path = "D:/koikatsu/Koikatu/abdata/studio/map/akikyoushitu.unity3d";
            if (!File.Exists(path)) Assert.Ignore("Local source map is not installed.");
            var definition = KoikatsuStudioMapLighting.Read(new KoikatsuBundleSource(path), "akikyoushitu");
            Assert.That(definition.Presets.Length, Is.EqualTo(3));
            Assert.That(definition.CreateEnvironment().Mode, Is.EqualTo(AmbientMode.Flat));
            var root = new GameObject("Map");
            try
            {
                // Build the source hierarchy from the original paths, including duplicate-name indices.
                foreach (var preset in definition.Presets)
                    foreach (var value in preset.VisiblePaths) BuildPath(root.transform, value);
                var sun = BuildPath(root.transform, definition.TargetPath).gameObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                Assert.That(definition.Apply(root, 2), Is.SameAs(sun));
                Assert.That(sun.intensity, Is.EqualTo(1f));
                Assert.That(sun.color.r, Is.EqualTo(.38013625f).Within(.00001f));
                foreach (var preset in definition.Presets)
                    foreach (var value in preset.VisiblePaths)
                        Assert.That(BuildPath(root.transform, value).gameObject.activeSelf, Is.EqualTo(preset.Type == 2));
                Assert.That(Quaternion.Angle(sun.transform.rotation,
                    Quaternion.Euler(41.93118f,59.92017f,344.25909f)), Is.LessThan(.01f));
                definition.Apply(root, 0);
                Assert.That(sun.intensity, Is.EqualTo(1.5f));
                Assert.That(sun.color.r, Is.EqualTo(.87662196f).Within(.00001f));
                foreach (var preset in definition.Presets)
                    foreach (var value in preset.VisiblePaths)
                        Assert.That(BuildPath(root.transform, value).gameObject.activeSelf, Is.EqualTo(preset.Type == 0));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Transform BuildPath(Transform root, string path)
        {
            int position = 0;
            while (position < path.Length)
            {
                int colon = path.IndexOf(':', position);
                int length = int.Parse(path.Substring(position, colon - position));
                string name = path.Substring(colon + 1, length);
                position = colon + 1 + length + 1;
                int end = path.IndexOf(';', position);
                int occurrence = int.Parse(path.Substring(position, end - position));
                Transform child = null; int match = 0;
                foreach (Transform candidate in root)
                    if (candidate.name == name && match++ == occurrence) { child = candidate; break; }
                if (child == null) { child = new GameObject(name).transform; child.SetParent(root); }
                root = child; position = end + 1;
            }
            return root;
        }

        private static void Configure(ImportedSceneRenderContext context, Light sun) =>
            context.Configure(new ImportedSceneLight(Color.white,1,Vector3.zero,false),
                default,null,0,Array.Empty<Renderer>(),worldMainLight:sun,ownsSceneLighting:true);

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("Lighting scope test");
            public readonly Light Host;
            private readonly Light originalSun = RenderSettings.sun;
            private readonly ImportedSceneRenderBridge bridge;
            public Fixture()
            {
                Host = AddLight("Editor sun",LightType.Directional);
                RenderSettings.sun = Host;
                bridge = Root.AddComponent<ImportedSceneRenderBridge>();
            }
            public ImportedSceneRenderContext AddContext(string name)
            {
                var owner = new GameObject(name); owner.transform.SetParent(Root.transform);
                return owner.AddComponent<ImportedSceneRenderContext>();
            }
            public Light AddLight(string name, LightType type, bool enabled = true, Transform parent = null)
            {
                var owner = new GameObject(name); owner.transform.SetParent(parent != null ? parent : Root.transform);
                var light = owner.AddComponent<Light>(); light.type = type; light.enabled = enabled;
                return light;
            }
            public void Tick() => Invoke("LateUpdate");
            public void DisableBridge() => Invoke("OnDisable");
            public void CameraEvent(string name, Camera camera) => typeof(ImportedSceneRenderBridge)
                .GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,new object[]{default(ScriptableRenderContext),camera});
            private void Invoke(string name) => typeof(ImportedSceneRenderBridge)
                .GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bridge,null);
            public void Dispose()
            {
                DisableBridge(); UnityEngine.Object.DestroyImmediate(Root); RenderSettings.sun = originalSun;
            }
        }

        private sealed class EnvironmentSnapshot
        {
            private readonly AmbientMode mode = RenderSettings.ambientMode;
            private readonly float intensity = RenderSettings.ambientIntensity;
            private readonly Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor;
            private readonly SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            public void Restore()
            {
                RenderSettings.ambientMode = mode; RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground; RenderSettings.ambientProbe = probe;
            }
        }
    }
}
