using BodyEditor.ReferenceModels;
using NUnit.Framework;

namespace BodyEditor.Tests
{
    public sealed class KoikatsuLegacyBundleSanitizerTests
    {
        [TestCase(512, 512, 10, 131072)]
        [TestCase(1024, 512, 10, 262144)]
        [TestCase(512, 256, 12, 131072)]
        [TestCase(1024, 1024, 25, 1048576)]
        [TestCase(8, 8, 10, 32)]
        [TestCase(2, 1, 10, 8)]
        public void ComputesCompressedBaseLevelSize(
            int width,
            int height,
            int format,
            int expected)
        {
            Assert.That(
                KoikatsuLegacyBundleSanitizer.TryGetBaseLevelSize(
                    width,
                    height,
                    format,
                    out var actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void RejectsUnsupportedTextureFormat()
        {
            Assert.That(
                KoikatsuLegacyBundleSanitizer.TryGetBaseLevelSize(
                    512,
                    512,
                    999,
                    out _),
                Is.False);
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(10, false)]
        public void RepairsOnlyZeroMipCounts(int mipCount, bool expected)
        {
            Assert.That(
                KoikatsuLegacyBundleSanitizer.RequiresMipCountRepair(mipCount),
                Is.EqualTo(expected));
        }

        [TestCase("5.6.2f1", true)]
        [TestCase("2018.4.36f1", true)]
        [TestCase("2019.1.0f1", false)]
        [TestCase("6000.5.0f1", false)]
        [TestCase("", true)]
        public void DetectsLegacyEngineVersions(
            string version,
            bool expected)
        {
            Assert.That(
                KoikatsuLegacyBundleSanitizer.IsLegacyEngineVersion(version),
                Is.EqualTo(expected));
        }
    }
}
