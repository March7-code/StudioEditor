using System;
using System.IO;
using NUnit.Framework;
using StudioEditor.Characters;
using StudioEditor.ReferenceModels;
using StudioEditor.Rendering;
using StudioEditor.Rendering.RenderSchemes;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Tests
{
    public sealed class ImportedRenderStateTests
    {
        [Test]
        public void ItemShadingReceivesSceneRampAndRestoresItsPropertyBlock()
        {
            var root = new GameObject("Item source shading test");
            var material = new Material(Shader.Find("StudioEditor/KoikatsuStudioItem"));
            var ramp = new Texture2D(2, 2);
            var originalSun = RenderSettings.sun;
            try
            {
                var item = new GameObject("Sword");item.transform.SetParent(root.transform);
                var renderer = item.AddComponent<MeshRenderer>();renderer.sharedMaterial = material;
                renderer.renderingLayerMask = 4;
                material.SetFloat("_SourceItemModel", 1);
                var original = new UnityEngine.MaterialPropertyBlock();
                original.SetVector("_SourceAmbientShadow", Vector4.one);
                renderer.SetPropertyBlock(original);
                var context = root.AddComponent<ImportedSceneRenderContext>();
                var light = new ImportedSceneLight(Color.white, 1, Vector3.zero, false);
                context.Configure(light, light, null, 0, Array.Empty<Renderer>());
                context.ConfigureCharacterShading(ramp, new Color(.1f,.2f,.3f,.4f));
                var bridge = root.AddComponent<ImportedSceneRenderBridge>();Tick(bridge);
                var actual = new UnityEngine.MaterialPropertyBlock();renderer.GetPropertyBlock(actual);
                Assert.That(actual.GetTexture("_SourceRamp"), Is.SameAs(ramp));
                Assert.That(actual.GetVector("_SourceAmbientShadow"), Is.EqualTo(new Vector4(.1f,.2f,.3f,.4f)));
                Assert.That(renderer.renderingLayerMask, Is.EqualTo(4u));
                DisableBridge(root);renderer.GetPropertyBlock(actual);
                Assert.That(actual.GetTexture("_SourceRamp"), Is.Null);
                Assert.That(actual.GetVector("_SourceAmbientShadow"), Is.EqualTo(Vector4.one));
            }
            finally
            {
                DisableBridge(root);UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(ramp);
                RenderSettings.sun = originalSun;
            }
        }

        [Test]
        public void SourceLidsTruncateOpeningBeforeDistributingShapeWeights()
        {
            var root = new GameObject("Source eyelids");var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] {Vector3.zero,Vector3.right,Vector3.up};
                var zero = new Vector3[3];
                mesh.AddBlendShapeFrame("Closed",100,zero,zero,zero);
                mesh.AddBlendShapeFrame("Open",100,zero,zero,zero);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh = mesh;
                var lids = root.AddComponent<CharacterEyeOpenController>();
                lids.QuantizeOpeningToPercent = true;
                lids.Configure(new[] {new CharacterMorphTarget(renderer,new[] {new CharacterMorphPair(0,1)})});
                lids.SetOpenRate(.7877289f);
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(78f).Within(.001f));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(22f).Within(.001f));
            }
            finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
        }

        [TestCase(false)] [TestCase(true)]
        public void SceneReaderPreservesLightingAfterAllCameraSlots(bool secondaryOverride)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                using (var stream = File.Create(path))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(new byte[] {137, 80, 78, 71, 13, 10, 26, 10});
                    writer.Write(0); writer.Write(new byte[] {73, 69, 78, 68}); writer.Write(0);
                    writer.Write("1.0.4"); writer.Write(0); // no objects
                    writer.Write(-1); Vector(writer, Vector3.zero); Vector(writer, Vector3.zero); Vector(writer, Vector3.one);
                    writer.Write(2); writer.Write(true); writer.Write(7); writer.Write(0.31f);
                    writer.Write(false); writer.Write("{}"); writer.Write(1f); // AO
                    writer.Write(false); writer.Write(1f); writer.Write(1f); writer.Write(0.5f); // bloom
                    writer.Write(false); writer.Write(1f); writer.Write(1f); writer.Write(false); // depth, vignette
                    writer.Write(false); writer.Write("{}"); writer.Write(0f); writer.Write(0f); // fog
                    writer.Write(false); writer.Write("{}"); writer.Write("{}"); writer.Write(0); // shafts
                    writer.Write(true); writer.Write(true); writer.Write(true); writer.Write(.22f);
                    writer.Write(JsonUtility.ToJson(new Color(0.1f, 0.24f, 0.375f, 1f)));
                    writer.Write(.68f); writer.Write(5); writer.Write(0.3f);
                    for (int index = 0; index < 11; index++)
                    {
                        writer.Write(2); Vector(writer, Vector3.zero); Vector(writer, Vector3.zero);
                        Vector(writer, new Vector3(0, 0, -1)); writer.Write(23f);
                    }
                    EnvironmentLight(writer, new Color(0.1f, 0.3f, 0.8f, 1), 1.4f, 28, 287, true);
                    EnvironmentLight(writer, Color.white, 0.7f, 12, 64, false); writer.Write((int)LightType.Directional);
                    if (secondaryOverride)
                    {
                        // ExtendedSave: {TwoLutPlugin: [version, {slot, guid}]}.
                        using var payload = new MemoryStream();
                        using (var pack = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
                        {
                            void Text(string text) {pack.Write((byte)(0xa0 + text.Length));pack.Write(System.Text.Encoding.UTF8.GetBytes(text));}
                            pack.Write((byte)0x81);Text("TwoLutPlugin");pack.Write((byte)0x92);pack.Write((byte)0);
                            pack.Write((byte)0x82);Text("slot");pack.Write((byte)3);Text("guid");Text("fixture.lut");
                        }
                        writer.Write(new byte[]{4,(byte)'K',(byte)'K',(byte)'E',(byte)'x'});
                        writer.Write(1);writer.Write((int)payload.Length);writer.Write(payload.ToArray());
                    }
                }
                var settings = KoikatsuSceneReader.Read(path).RenderSettings;
                Assert.That(settings.ColorGradingId, Is.EqualTo(7));
                Assert.That(settings.ColorGradingBlend, Is.EqualTo(0.31f));
                Assert.That(settings.ColorGradingSecondaryId, Is.EqualTo(secondaryOverride ? 3 : 0));
                Assert.That(settings.ColorGradingSecondaryGuid, Is.EqualTo(secondaryOverride ? "fixture.lut" : null));
                Assert.That(settings.SunLightType, Is.EqualTo(2));
                Assert.That(settings.HasCharacterShading, Is.True);
                Assert.That(settings.CharacterRampId, Is.EqualTo(5));
                Assert.That(settings.CharacterLineDepth, Is.EqualTo(.22f));
                Assert.That(settings.CharacterLineWidth, Is.EqualTo(.68f));
                Assert.That(settings.CharacterAmbientShadow.g, Is.EqualTo(0.24f).Within(0.0001f));
                Assert.That(settings.CharacterAmbientShadowStrength, Is.EqualTo(0.3f));
                Assert.That(settings.CharacterLight.Color.b, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(settings.CharacterLight.Intensity, Is.EqualTo(1.4f));
                Assert.That(settings.CharacterLight.EulerAngles, Is.EqualTo(new Vector3(28, 287, 0)));
                Assert.That(settings.CharacterLight.Shadows, Is.True);
                Assert.That(settings.MapLight.Intensity, Is.EqualTo(0.7f));
                Assert.That(settings.MapLight.Shadows, Is.False);
            }
            finally { File.Delete(path); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisplayScopesRestoreCameraInEitherReleaseOrder(bool importedFirst)
        {
            var owner = new GameObject("Scoped camera test");
            DisplayCameraScope imported = null, experiment = null;
            try
            {
                var camera = owner.AddComponent<Camera>();
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false; data.volumeLayerMask = 1 << 5;
                imported = new DisplayCameraScope(camera, 1 << 30);
                experiment = new DisplayCameraScope(camera, 1 << 31);
                Assert.That(data.volumeLayerMask.value, Is.EqualTo((1 << 5) | (1 << 30) | (1 << 31)));
                if (importedFirst) imported.Dispose(); else experiment.Dispose();
                Assert.That(data.renderPostProcessing, Is.True);
                Assert.That(data.volumeLayerMask.value, Is.EqualTo((1 << 5) | (importedFirst ? 1 << 31 : 1 << 30)));
                imported.Dispose(); experiment.Dispose();
                Assert.That(data.renderPostProcessing, Is.False);
                Assert.That(data.volumeLayerMask.value, Is.EqualTo(1 << 5));
            }
            finally { imported?.Dispose(); experiment?.Dispose(); UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void CharacterKeyKeepsMapSunAndRestoresShadowLayersWhenSceneIsDisabled()
        {
            var root = new GameObject("Imported render context test");
            var bridgeOwner = new GameObject("Bridge test");
            var originalSun = RenderSettings.sun;
            try
            {
                var renderer = root.AddComponent<MeshRenderer>(); renderer.renderingLayerMask = 1;
                var sunOwner = new GameObject("Map sun"); sunOwner.transform.SetParent(root.transform);
                var mapSun = sunOwner.AddComponent<Light>(); mapSun.type = LightType.Directional;
                var sunData = mapSun.GetUniversalAdditionalLightData();
                sunData.shadowRenderingLayers = 16u;
                var context = root.AddComponent<ImportedSceneRenderContext>();
                context.Configure(new ImportedSceneLight(Color.cyan, 1.4f, new Vector3(20, 30, 0), true),
                    default, null, 0, new[] { renderer }, worldMainLight: mapSun);
                var bridge = bridgeOwner.AddComponent<ImportedSceneRenderBridge>();
                Tick(bridge);
                Assert.That(renderer.renderingLayerMask, Is.EqualTo(2u));
                Assert.That(RenderSettings.sun, Is.SameAs(mapSun));
                var key = root.GetComponentInChildren<ImportedSceneKeyLight>().GetComponent<Light>();
                Assert.That(key.color, Is.EqualTo(Color.cyan));
                Assert.That(key.intensity, Is.EqualTo(1.4f));
                Assert.That(key.GetUniversalAdditionalLightData().renderingLayers.value, Is.EqualTo(2u));
                Assert.That(sunData.renderingLayers.value, Is.EqualTo(1u));
                Assert.That(sunData.customShadowLayers, Is.True);
                Assert.That(sunData.shadowRenderingLayers.value, Is.EqualTo(19u));
                context.enabled = false; Tick(bridge);
                Assert.That(renderer.renderingLayerMask, Is.EqualTo(1u));
                Assert.That(RenderSettings.sun, Is.SameAs(originalSun));
                Assert.That(sunData.customShadowLayers, Is.False);
                Assert.That(sunData.shadowRenderingLayers.value, Is.EqualTo(16u));
                context.enabled = true; Tick(bridge);
                var replacement = root.AddComponent<ImportedSceneRenderContext>();
                replacement.Configure(default, default, null, 0, Array.Empty<Renderer>(), worldMainLight: mapSun);
                context.enabled = false; Tick(bridge);
                Assert.That(RenderSettings.sun, Is.SameAs(mapSun));
                replacement.enabled = false; Tick(bridge);
                Assert.That(RenderSettings.sun, Is.SameAs(originalSun));
                Assert.That(sunData.customShadowLayers, Is.False);
                Assert.That(sunData.shadowRenderingLayers.value, Is.EqualTo(16u));
            }
            finally { DisableBridge(bridgeOwner); UnityEngine.Object.DestroyImmediate(bridgeOwner); UnityEngine.Object.DestroyImmediate(root); RenderSettings.sun = originalSun; }
        }

        [Test]
        public void SourceLookupOwnsDisplayTransformAndRestoresCameraWhenTurnedOff()
        {
            var root = new GameObject("Imported color grade test");
            var bridgeOwner = new GameObject("Bridge test");
            var otherMainCameras = new System.Collections.Generic.List<Camera>();
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Camera>())
                if (existing.CompareTag("MainCamera"))
                { otherMainCameras.Add(existing); existing.tag = "Untagged"; }
            var cameraOwner = new GameObject("Display camera") { tag = "MainCamera" };
            var texture = new Texture2D(1024, 32, TextureFormat.RGB24, false, true);
            var originalFlag = ImportedRenderConfiguration.ApplySourceColorGrading;
            var originalSun = RenderSettings.sun;
            try
            {
                var camera = cameraOwner.AddComponent<Camera>();
                var data = camera.GetUniversalAdditionalCameraData();
                data.volumeLayerMask = 1 << 5;
                data.renderPostProcessing = false;
                var context = root.AddComponent<ImportedSceneRenderContext>();
                context.Configure(default, default, texture, 0.7f, Array.Empty<Renderer>());
                var bridge = bridgeOwner.AddComponent<ImportedSceneRenderBridge>();
                ImportedRenderConfiguration.ApplySourceColorGrading = false; Tick(bridge);
                Assert.That(context.ColorLookup, Is.SameAs(texture));
                Assert.That(context.ColorLookupBlend, Is.EqualTo(0.7f));
                Assert.That(data.renderPostProcessing, Is.False);
                Assert.That(bridgeOwner.GetComponentInChildren<UnityEngine.Rendering.Volume>(), Is.Null);
                ImportedRenderConfiguration.ApplySourceColorGrading = true; Tick(bridge);
                var volume = bridgeOwner.GetComponentInChildren<UnityEngine.Rendering.Volume>();
                Assert.That(volume, Is.Not.Null);
                Assert.That(volume.sharedProfile.TryGet<ColorLookup>(out var lookup), Is.True);
                Assert.That(lookup.texture.value, Is.SameAs(texture));
                Assert.That(lookup.contribution.value, Is.EqualTo(0.7f));
                Assert.That(volume.sharedProfile.TryGet<Tonemapping>(out var tone), Is.True);
                Assert.That(tone.mode.overrideState, Is.True);
                Assert.That(tone.mode.value, Is.EqualTo(TonemappingMode.None));
                Assert.That(volume.sharedProfile.TryGet<ColorAdjustments>(out var exposure), Is.True);
                Assert.That(exposure.postExposure.overrideState, Is.True);
                Assert.That(exposure.postExposure.value, Is.EqualTo(0f));
                Assert.That(data.renderPostProcessing, Is.True);
                ImportedRenderConfiguration.ApplySourceColorGrading = false; Tick(bridge);
                Assert.That(data.renderPostProcessing, Is.False);
                Assert.That(data.volumeLayerMask.value, Is.EqualTo(1 << 5));
                Assert.That(bridgeOwner.GetComponentInChildren<UnityEngine.Rendering.Volume>(), Is.Null);
            }
            finally
            {
                DisableBridge(bridgeOwner);
                UnityEngine.Object.DestroyImmediate(bridgeOwner); UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraOwner); UnityEngine.Object.DestroyImmediate(texture);
                foreach (var existing in otherMainCameras)
                    if (existing != null) existing.tag = "MainCamera";
                ImportedRenderConfiguration.ApplySourceColorGrading = originalFlag; RenderSettings.sun = originalSun;
            }
        }

        [Test]
        public void SceneLightsKeepTheirAudienceAndRestoreWhenContextChanges()
        {
            var root = new GameObject("Light audience test");
            var bridgeOwner = new GameObject("Bridge test");
            var originalSun = RenderSettings.sun;
            try
            {
                var renderer = root.AddComponent<MeshRenderer>();
                var lights = new ImportedLightTarget[4];
                for (int index = 0; index < lights.Length; index++)
                {
                    var owner = new GameObject("Source light"); owner.transform.SetParent(root.transform);
                    owner.AddComponent<Light>();
                    lights[index] = owner.AddComponent<ImportedLightTarget>();
                    lights[index].Audience = (ImportedLightAudience)index;
                }
                var context = root.AddComponent<ImportedSceneRenderContext>();
                context.Configure(new ImportedSceneLight(Color.white, 1, Vector3.zero, false),
                    default, null, 0, new[] { renderer }, lights);
                var bridge = bridgeOwner.AddComponent<ImportedSceneRenderBridge>(); Tick(bridge);
                Assert.That(lights[0].GetComponent<Light>().GetUniversalAdditionalLightData().renderingLayers.value, Is.EqualTo(3u));
                Assert.That(lights[1].GetComponent<Light>().GetUniversalAdditionalLightData().renderingLayers.value, Is.EqualTo(2u));
                Assert.That(lights[2].GetComponent<Light>().GetUniversalAdditionalLightData().renderingLayers.value, Is.EqualTo(1u));
                Assert.That(lights[3].GetComponent<Light>().GetUniversalAdditionalLightData().renderingLayers.value, Is.Zero);
                context.Configure(default, default, null, 0, Array.Empty<Renderer>());
                Tick(bridge);
                Assert.That(renderer.renderingLayerMask, Is.EqualTo(1u));
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_ImportedCharacterKey"), Is.Zero);
                foreach (var light in lights)
                    Assert.That(light.GetComponent<Light>().GetUniversalAdditionalLightData().renderingLayers.value, Is.EqualTo(1u));
            }
            finally { DisableBridge(bridgeOwner); UnityEngine.Object.DestroyImmediate(bridgeOwner); UnityEngine.Object.DestroyImmediate(root); RenderSettings.sun = originalSun; }
        }

        [Test]
        public void SecondaryLookupBlendsLinearDataAndUpdatesWithoutMixingUngradedImage()
        {
            var root = new GameObject("Dual lookup test");
            var cameraOwner = new GameObject("Dual lookup camera") {tag = "MainCamera"};
            var otherCameras = new System.Collections.Generic.List<Camera>();
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Camera>())
                if (existing.CompareTag("MainCamera")) {otherCameras.Add(existing);existing.tag = "Untagged";}
            var primary = new Texture2D(1024,32,TextureFormat.RGBAFloat,false,true);
            var secondary = new Texture2D(1024,32,TextureFormat.RGBAFloat,false,true);
            var readback = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var originalFlag = ImportedRenderConfiguration.ApplySourceColorGrading;
            var originalSun = RenderSettings.sun;
            var previous = RenderTexture.active;
            try
            {
                var a = new Color(.1f,.3f,.7f,1);var b = new Color(.8f,.5f,.2f,1);
                var pixels = new Color[1024*32];Array.Fill(pixels,a);primary.SetPixels(pixels);primary.Apply();
                Array.Fill(pixels,b);secondary.SetPixels(pixels);secondary.Apply();
                var camera = cameraOwner.AddComponent<Camera>();
                var data = camera.GetUniversalAdditionalCameraData();data.renderPostProcessing = false;
                var context = root.AddComponent<ImportedSceneRenderContext>();
                var bridge = root.AddComponent<ImportedSceneRenderBridge>();
                ImportedRenderConfiguration.ApplySourceColorGrading = true;
                RenderTexture cache = null;
                foreach (var amount in new[]{0f,.31f,1f})
                {
                    context.Configure(default,default,primary,1f,Array.Empty<Renderer>(),
                        colorLookupSecondary:secondary,colorLookupSecondaryBlend:amount);
                    Tick(bridge);
                    var volume = root.GetComponentInChildren<UnityEngine.Rendering.Volume>();
                    volume.sharedProfile.TryGet<ColorLookup>(out var lookup);
                    Assert.That(lookup.contribution.value,Is.EqualTo(1f));
                    Assert.That(lookup.texture.value,Is.TypeOf<RenderTexture>());
                    var current = (RenderTexture)lookup.texture.value;
                    if (cache != null) Assert.That(current,Is.SameAs(cache));
                    cache = current;
                    Assert.That(current.sRGB,Is.False);
                    RenderTexture.active = current;readback.ReadPixels(new Rect(512,16,1,1),0,0);readback.Apply();
                    var expected = Color.Lerp(a,b,amount);var actual = readback.GetPixel(0,0);
                    for(int channel=0;channel<3;channel++)
                        Assert.That(actual[channel],Is.EqualTo(expected[channel]).Within(1f/255),$"blend {amount}, channel {channel}");
                    Tick(bridge);
                    Assert.That(lookup.texture.value,Is.SameAs(cache));
                }
                RenderTexture.active = previous;
                context.Configure(default,default,primary,.69f,Array.Empty<Renderer>());Tick(bridge);
                Assert.That(cache == null,Is.True,"Removing the secondary lookup must release its cache.");
                var remaining = root.GetComponentInChildren<UnityEngine.Rendering.Volume>();
                remaining.sharedProfile.TryGet<ColorLookup>(out var single);
                Assert.That(single.texture.value,Is.SameAs(primary));
                Assert.That(single.contribution.value,Is.EqualTo(.69f));
                ImportedRenderConfiguration.ApplySourceColorGrading = false;Tick(bridge);
                Assert.That(data.renderPostProcessing,Is.False);
            }
            finally
            {
                RenderTexture.active = previous;DisableBridge(root);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraOwner);
                UnityEngine.Object.DestroyImmediate(primary);UnityEngine.Object.DestroyImmediate(secondary);UnityEngine.Object.DestroyImmediate(readback);
                foreach(var camera in otherCameras) if(camera != null) camera.tag = "MainCamera";
                ImportedRenderConfiguration.ApplySourceColorGrading = originalFlag;RenderSettings.sun = originalSun;
            }
        }

        [Test]
        public void FixedEyeLookKeepsPupilRatesAndCorrectsLidOpening()
        {
            var root = new GameObject("Eye expression test");
            var mesh = new Mesh();
            try
            {
                var left = new GameObject("Left eye").transform; left.SetParent(root.transform);
                var right = new GameObject("Right eye").transform; right.SetParent(root.transform);
                var skeleton = new CharacterSkeleton(new[] {
                    new CharacterBone("Root", root.transform, -1), new CharacterBone("Left", left, 0), new CharacterBone("Right", right, 0) });
                var pipeline = CharacterPoseCoordinator.Attach(root, skeleton);
                var look = root.AddComponent<CharacterEyeLookController>();
                look.Configure(pipeline, root.transform, left, right, Vector3.forward, Vector3.up);
                look.SetFixedLocalRotations(Quaternion.Euler(0, 12, 0), Quaternion.Euler(0, 8, 0), new Vector2(0.25f, -0.25f), new Vector2(0.16f, -0.16f));
                pipeline.EvaluateNow();
                Assert.That(look.GetLookRate(CharacterEye.Left), Is.EqualTo(new Vector2(0.25f, -0.25f)));
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                var zero = new Vector3[3];
                mesh.AddBlendShapeFrame("Closed", 100, zero, zero, zero);
                mesh.AddBlendShapeFrame("Open", 100, zero, zero, zero);
                var renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                var lids = root.AddComponent<CharacterEyeOpenController>();
                lids.Configure(new[] { new CharacterMorphTarget(renderer, new[] { new CharacterMorphPair(0, 1) }) }, openMax: 0.92f);
                lids.ConfigureLookCorrection(look, new Vector3(0, 0.33f, 0.17f));
                Tick(lids);
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(69f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(31f).Within(0.001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void HairMaskUsesSequentialBlendsOnGpu()
        {
            var mask = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            mask.SetPixel(0, 0, new Color(0.5f, 0.5f, 0, 1)); mask.Apply();
            var material = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                null, CharacterRenderSurfaceRole.Hair, "hair", "Hair mask test", Color.red, null,
                Color.green, Color.blue, colorMaskBlend: CharacterColorMaskBlend.Sequential));
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                material.SetTexture("_ColorMask", mask); material.SetFloat("_UseToon", 0);
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); image.Apply();
                var result = image.GetPixel(0, 0);
                Assert.That(result.r, Is.EqualTo(0.5f).Within(0.02f));
                Assert.That(result.g, Is.EqualTo(0.75f).Within(0.02f));
                Assert.That(result.b, Is.EqualTo(0.25f).Within(0.02f));
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        private static void Tick(Component component) => component.GetType().GetMethod("LateUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(component, null);
        private static void DisableBridge(GameObject owner)
        {
            var bridge = owner.GetComponent<ImportedSceneRenderBridge>();
            if (bridge != null) bridge.GetType().GetMethod("OnDisable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(bridge, null);
        }

        [Test]
        public void IrisExpressionUsesTextureAlphaForColorAndVisibilityOnGpu()
        {
            var expression = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            expression.SetPixel(0, 0, new Color(1, 0, 0, 0.5f)); expression.Apply();
            var baseMap = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            baseMap.SetPixel(0, 0, new Color(0, 0, 1, 0.2f)); baseMap.Apply();
            var material = new DefaultAnimeCharacterRenderScheme().CreateMaterial(new CharacterRenderMaterialContext(
                null, CharacterRenderSurfaceRole.Face, "hitomi", "Iris expression test", Color.white, null));
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                material.SetTexture("_IrisExpressionMap", expression); material.SetFloat("_IrisExpressionStrength", 1);
                material.SetTexture("_BaseMap", baseMap);
                material.SetFloat("_UseToon", 0);
                Graphics.Blit(baseMap, target, material, 0);
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); image.Apply();
                var result = image.GetPixel(0, 0);
                Assert.That(result.r, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(result.b, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(result.a, Is.EqualTo(0.5f).Within(0.01f));
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(expression); UnityEngine.Object.DestroyImmediate(baseMap);
            }
        }

        private static void Vector(BinaryWriter writer, Vector3 value)
        { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
        private static void EnvironmentLight(BinaryWriter writer, Color color, float intensity, float x, float y, bool shadows)
        { writer.Write(JsonUtility.ToJson(color)); writer.Write(intensity); writer.Write(x); writer.Write(y); writer.Write(shadows); }
    }
}
