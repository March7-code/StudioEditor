using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class ExperimentalRenderSessionTests
    {
        private GameObject owner;
        private ExperimentalRenderSession session;
        private SceneRenderScheme originalScheme;
        private int originalQuality;

        [SetUp]
        public void SetUp()
        {
            originalScheme = ExperimentalRenderConfiguration.Scheme;
            originalQuality = QualitySettings.GetQualityLevel();
            owner = new GameObject("Rendering session test");
            session = owner.AddComponent<ExperimentalRenderSession>();
        }

        [TearDown]
        public void TearDown()
        {
            if (session != null) session.Restore();
            Object.DestroyImmediate(owner);
            ExperimentalRenderConfiguration.Scheme = originalScheme;
            QualitySettings.SetQualityLevel(originalQuality, false);
        }

        [Test]
        public void RepeatedSwitchesRestoreOriginalPipeline()
        {
            var original = QualitySettings.renderPipeline;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Assert.That(session.ApplyScheme(SceneRenderScheme.ExperimentalAtmosphere), Is.True);
                Assert.That(QualitySettings.renderPipeline, Is.Not.SameAs(original));
                Assert.That(session.IsActive, Is.True);
                Assert.That(session.ApplyScheme(SceneRenderScheme.DefaultAnime), Is.True);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(original));
                Assert.That(session.IsActive, Is.False);
                Assert.That(ExperimentalRenderSession.Current, Is.Null);
            }
        }

        [Test]
        public void RestorePreservesSelectedQualityAndRestoresVisitedLevels()
        {
            if (QualitySettings.names.Length < 2) Assert.Ignore("Requires two quality levels.");
            int other = (originalQuality + 1) % QualitySettings.names.Length;
            var firstPipeline = QualitySettings.renderPipeline;
            QualitySettings.SetQualityLevel(other, false);
            var otherPipeline = QualitySettings.renderPipeline;
            QualitySettings.SetQualityLevel(originalQuality, false);
            Assert.That(session.ApplyScheme(SceneRenderScheme.ExperimentalAtmosphere), Is.True);
            QualitySettings.SetQualityLevel(other, false);
            Assert.That(session.ApplyScheme(SceneRenderScheme.ExperimentalAtmosphere), Is.True);
            session.ApplyScheme(SceneRenderScheme.DefaultAnime);
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(other));
            Assert.That(QualitySettings.renderPipeline, Is.SameAs(otherPipeline));
            QualitySettings.SetQualityLevel(originalQuality, false);
            Assert.That(QualitySettings.renderPipeline, Is.SameAs(firstPipeline));
        }

        [Test]
        public void RestoreDestroysOwnedPipelineAndKeepsResourceTemplate()
        {
            var original = QualitySettings.renderPipeline;
            var template = Resources.Load<RenderPipelineAsset>(ExperimentalRenderSession.PipelineResource);
            Assert.That(session.ApplyScheme(SceneRenderScheme.ExperimentalAtmosphere), Is.True);
            var owned = QualitySettings.renderPipeline;
            Assert.That(owned, Is.Not.SameAs(template));
            session.Restore();
            Assert.That(QualitySettings.renderPipeline, Is.SameAs(original));
            Assert.That(owned == null, Is.True, "Owned runtime pipeline was not destroyed.");
            Assert.That(template != null, Is.True, "Resource template was destroyed.");
            Assert.That(ExperimentalRenderSession.Current, Is.Null);
        }

        [Test]
        public void RestorePreservesAnExternalPipelineOverride()
        {
            var original = QualitySettings.renderPipeline;
            Assert.That(session.ApplyScheme(SceneRenderScheme.ExperimentalAtmosphere), Is.True);
            var replacement = Object.Instantiate(GraphicsSettings.defaultRenderPipeline);
            try
            {
                QualitySettings.renderPipeline = replacement;
                session.ApplyScheme(SceneRenderScheme.DefaultAnime);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(replacement));
            }
            finally
            {
                QualitySettings.renderPipeline = original;
                Object.DestroyImmediate(replacement);
            }
        }
    }
}
