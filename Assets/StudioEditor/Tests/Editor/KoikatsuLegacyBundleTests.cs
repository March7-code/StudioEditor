using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using NUnit.Framework;
using StudioEditor.ReferenceModels;
using UnityEngine;
using Hash128 = AssetsTools.NET.Hash128;

namespace StudioEditor.Tests
{
    public sealed class KoikatsuLegacyBundleTests
    {
        private AssetsManager manager;
        private AssetsFileInstance assets;
        private AssetsFileInstance reference;
        private AssetFileInfo info;

        [SetUp]
        public void SetUp()
        {
            manager = new AssetsManager();
            assets = manager.LoadAssetsFile(Path.Combine(Application.dataPath,
                "StudioEditor/Tests/Editor/Fixtures/StrippedUnity56Texture.bytes"), false);
            info = assets.file.GetAssetsOfType(AssetClassID.Texture2D).Single();
            Assert.That(assets.file.Metadata.TypeTreeEnabled, Is.False);
            Assert.That(manager.GetTemplateBaseField(assets, info), Is.Null);
            reference = manager.LoadAssetsFile(Path.Combine(Application.dataPath,
                "StudioEditor/Tests/Editor/Fixtures/Unity56TextureReference.bytes"), false);
        }

        [TearDown]
        public void TearDown() => manager?.UnloadAll(true);

        [Test]
        public void StrippedTextureRoundTripsWithoutChangingPixelOrSamplerData()
        {
            RestoreTrees();
            var texture = KoikatsuLegacyBundleSanitizer.ReadTexture(manager, assets, info);
            Assert.That(texture["m_Name"].AsString, Is.EqualTo("synthetic-texture"));
            Assert.That(texture["m_ColorSpace"].AsInt, Is.EqualTo(1));
            Assert.That(texture["m_TextureSettings"]["m_MipBias"].AsFloat, Is.EqualTo(.25f));
            Assert.That(texture["image data"].AsByteArray,
                Is.EqualTo(new byte[] { 17, 31, 63, 255, 127, 191, 223, 255 }));
            assets.file.Reader.Position = info.GetAbsoluteByteOffset(assets.file);
            Assert.That(Serialize(texture), Is.EqualTo(assets.file.Reader.ReadBytes((int)info.ByteSize)));
        }

        [Test]
        public void StrippedZeroMipTextureIsRepairedAndCanBeReadAgain()
        {
            RestoreTrees();
            var texture = KoikatsuLegacyBundleSanitizer.ReadTexture(manager, assets, info);
            Assert.That(KoikatsuLegacyBundleSanitizer.TrySanitizeTexture(texture), Is.True);
            Assert.That(texture["m_MipCount"].AsInt, Is.EqualTo(1));
            Assert.That(texture["m_CompleteImageSize"].AsInt, Is.EqualTo(8));
            var pixels = texture["image data"].AsByteArray.ToArray();
            info.SetNewData(texture);
            using (var output = new MemoryStream())
            {
                using (var writer = new AssetsFileWriter(output))
                    assets.file.Write(writer);
                var restored = manager.LoadAssetsFile(new MemoryStream(output.ToArray()), "repaired.assets", false);
                var repaired = KoikatsuLegacyBundleSanitizer.ReadTexture(manager, restored,
                    restored.file.GetAssetsOfType(AssetClassID.Texture2D).Single());
                Assert.That(repaired["m_MipCount"].AsInt, Is.EqualTo(1));
                Assert.That(repaired["image data"].AsByteArray, Is.EqualTo(pixels));
                Assert.That(KoikatsuLegacyBundleSanitizer.TrySanitizeTexture(repaired), Is.False);
            }
        }

        [TestCase("5.5.3f1")]
        [TestCase("2017.4.40f1")]
        public void MissingTreeFromAnUnsupportedVersionReportsAnExplicitError(string version)
        {
            assets.file.Metadata.UnityVersion = version;
            KoikatsuLegacyTypeTreeRestorer.CopyMatchingTrees(assets.file.Metadata, reference.file.Metadata);
            var exception = Assert.Throws<InvalidDataException>(() =>
                KoikatsuLegacyBundleSanitizer.ReadTexture(manager, assets, info));
            Assert.That(exception.Message, Does.Contain(version).And.Contain("no type tree"));
        }

        [Test]
        public void MissingTreeFromAnEditorFileIsNotReadWithThePlayerLayout()
        {
            assets.file.Metadata.TargetPlatform = unchecked((uint)-2);
            KoikatsuLegacyTypeTreeRestorer.CopyMatchingTrees(assets.file.Metadata, reference.file.Metadata);
            Assert.Throws<InvalidDataException>(() =>
                KoikatsuLegacyBundleSanitizer.ReadTexture(manager, assets, info));
        }

        [Test]
        public void ATreeWithTheSameClassButADifferentLayoutHashIsRejected()
        {
            reference.file.Metadata.TypeTreeTypes[0].TypeHash = new Hash128(new byte[16]);
            KoikatsuLegacyTypeTreeRestorer.CopyMatchingTrees(assets.file.Metadata, reference.file.Metadata);
            Assert.Throws<InvalidDataException>(() =>
                KoikatsuLegacyBundleSanitizer.ReadTexture(manager, assets, info));
        }

        [Test]
        public void RestoringSchemasPreservesTheDestinationsScriptIdentityAndIndex()
        {
            var target = assets.file.Metadata.TypeTreeTypes[0];
            var donor = reference.file.Metadata.TypeTreeTypes[0];
            target.TypeId = donor.TypeId = 114;
            target.ScriptTypeIndex = 7;
            donor.ScriptTypeIndex = 2;
            target.ScriptIdHash = new Hash128(Enumerable.Repeat((byte)1, 16).ToArray());
            donor.ScriptIdHash = new Hash128(Enumerable.Repeat((byte)2, 16).ToArray());
            KoikatsuLegacyTypeTreeRestorer.CopyMatchingTrees(assets.file.Metadata, reference.file.Metadata);
            Assert.That(target.Nodes == null || target.Nodes.Count == 0, Is.True,
                "Different script identities must not be mixed.");
            donor.ScriptIdHash = new Hash128(Enumerable.Repeat((byte)1, 16).ToArray());
            RestoreTrees();
            Assert.That(target.ScriptTypeIndex, Is.EqualTo(7));
            Assert.That(target.ScriptIdHash.data, Is.EqualTo(Enumerable.Repeat((byte)1, 16).ToArray()));
        }

        private void RestoreTrees()
        {
            KoikatsuLegacyTypeTreeRestorer.CopyMatchingTrees(assets.file.Metadata, reference.file.Metadata);
            Assert.That(assets.file.Metadata.TypeTreeEnabled, Is.True);
            Assert.That(assets.file.Metadata.TypeTreeTypes.All(type => type.Nodes.Count > 0), Is.True);
        }

        [Test]
        public void CharacterPreloadKeepsRuntimeTexturesAndMasksButSkipsThumbnails()
        {
            var entry = new KoikatsuListEntry(123, 14284, new Dictionary<string, string>
            {
                ["MainAB"] = "chara/hokagehat.unity3d",
                ["MainTexAB"] = "chara/texture.unity3d",
                ["OverBodyMaskAB"] = "chara/mask.unity3d",
                ["ExtraAB"] = "chara/hokagehat.unity3d",
                ["ThumbAB"] = "chara/thumb.unity3d",
                ["ThumbTex"] = "hatthumb",
            }, null, "peter247.hokagehat");
            Assert.That(entry.RuntimeBundlePaths, Is.EquivalentTo(new[]
            {
                "chara/hokagehat.unity3d", "chara/texture.unity3d", "chara/mask.unity3d",
            }));
        }

        private static byte[] Serialize(AssetTypeValueField texture)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = new AssetsFileWriter(output)) texture.Write(writer);
                return output.ToArray();
            }
        }
    }
}
