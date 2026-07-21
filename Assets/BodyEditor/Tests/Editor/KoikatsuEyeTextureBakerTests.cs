using BodyEditor.ReferenceModels;
using NUnit.Framework;
using UnityEngine;

namespace BodyEditor.Tests
{
    public sealed class KoikatsuEyeTextureBakerTests
    {
        [Test]
        public void EyeWhiteRendererDoesNotReceiveIrisTexture()
        {
            var texture = new Texture2D(1, 1);
            try
            {
                var eyes = new KoikatsuBakedEyeTextures
                {
                    Left = new KoikatsuBakedEyeTexture(
                        texture,
                        Vector2.one,
                        Vector2.zero),
                };

                Assert.That(
                    eyes.TryGetIris(
                        "cf_ohitomi_l cf_m_sirome_00",
                        out _),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void IrisRendererReceivesMatchingEyeTexture()
        {
            var texture = new Texture2D(1, 1);
            try
            {
                var expected = new KoikatsuBakedEyeTexture(
                    texture,
                    Vector2.one,
                    Vector2.zero);
                var eyes = new KoikatsuBakedEyeTextures
                {
                    Left = expected,
                };

                Assert.That(
                    eyes.TryGetIris(
                        "cf_ohitomi_l02 cf_m_hitomi_00",
                        out var actual),
                    Is.True);
                Assert.That(actual, Is.SameAs(expected));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

    }
}
