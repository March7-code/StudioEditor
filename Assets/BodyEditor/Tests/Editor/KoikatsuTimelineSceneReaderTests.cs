using System;
using System.IO;
using BodyEditor.ReferenceModels;
using NUnit.Framework;
using UnityEngine;

namespace BodyEditor.Tests
{
    public sealed class KoikatsuTimelineSceneReaderTests
    {
        private const string ExtendedPayloadBase64 =
            "gah0aW1lbGluZZIAgalzY2VuZUluZm/Z7Dxyb290IGR1cmF0aW9uPSIxIiBi" +
            "bG9ja0xlbmd0aD0iMTAiIGRpdmlzaW9ucz0iMTAiIHRpbWVTY2FsZT0iMSI+" +
            "PGludGVycG9sYWJsZSBvd25lcj0iVGltZWxpbmUiIGlkPSJndWlkZU9iamVj" +
            "dFBvcyIgb2JqZWN0SW5kZXg9IjAiIGd1aWRlT2JqZWN0UGF0aD0iY2Zfal9o" +
            "aXBzIj48a2V5ZnJhbWUgdGltZT0iMCIgdmFsdWVYPSIxIiB2YWx1ZVk9IjIi" +
            "IHZhbHVlWj0iMyIgLz48L2ludGVycG9sYWJsZT48L3Jvb3Q+";

        [Test]
        public void ParsesGuideRotationAndCurve()
        {
            const string xml =
                "<root duration=\"2.5\" blockLength=\"5\" divisions=\"20\" " +
                "timeScale=\"0.5\">" +
                "<interpolable enabled=\"true\" owner=\"Timeline\" " +
                "id=\"guideObjectRot\" objectIndex=\"3\" " +
                "guideObjectPath=\"BodyTop/cf_j_hips\" custom=\"kept\">" +
                "<keyframe time=\"1.25\" valueX=\"0.1\" valueY=\"0.2\" " +
                "valueZ=\"0.3\" valueW=\"0.9\">" +
                "<curveKeyframe time=\"0\" value=\"0\" inTangent=\"0\" " +
                "outTangent=\"1\" />" +
                "<curveKeyframe time=\"1\" value=\"1\" inTangent=\"1\" " +
                "outTangent=\"0\" />" +
                "</keyframe></interpolable></root>";

            var scene = KoikatsuTimelineSceneReader.ParseXml(xml);

            Assert.That(scene.Duration, Is.EqualTo(2.5f));
            Assert.That(scene.BlockLength, Is.EqualTo(5f));
            Assert.That(scene.Divisions, Is.EqualTo(20));
            Assert.That(scene.TimeScale, Is.EqualTo(0.5f));
            Assert.That(scene.Tracks, Has.Count.EqualTo(1));

            var track = scene.Tracks[0];
            Assert.That(track.Owner, Is.EqualTo("Timeline"));
            Assert.That(track.Id, Is.EqualTo("guideObjectRot"));
            Assert.That(track.ObjectIndex, Is.EqualTo(3));
            Assert.That(track.GuideObjectPath, Is.EqualTo("BodyTop/cf_j_hips"));
            Assert.That(track.GetAttribute("custom"), Is.EqualTo("kept"));

            var keyframe = track.Keyframes[0];
            Assert.That(keyframe.Time, Is.EqualTo(1.25f));
            Assert.That(keyframe.Curve, Has.Count.EqualTo(2));
            Assert.That(
                keyframe.TryGetQuaternion("value", out var rotation),
                Is.True);
            Assert.That(rotation.x, Is.EqualTo(0.1f));
            Assert.That(rotation.y, Is.EqualTo(0.2f));
            Assert.That(rotation.z, Is.EqualTo(0.3f));
            Assert.That(rotation.w, Is.EqualTo(0.9f));
        }

        [Test]
        public void ReadsTimelineFromExtendedSaveTail()
        {
            var path = Path.GetTempFileName();
            try
            {
                var payload = Convert.FromBase64String(
                    ExtendedPayloadBase64);
                using (var stream = new FileStream(
                           path,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(new byte[] { 1, 2, 3, 4 });
                    writer.Write("KKEx");
                    writer.Write(3);
                    writer.Write(payload.Length);
                    writer.Write(payload);
                }

                Assert.That(
                    KoikatsuTimelineSceneReader.TryRead(path, out var scene),
                    Is.True);
                Assert.That(scene.Duration, Is.EqualTo(1f));
                Assert.That(scene.Tracks, Has.Count.EqualTo(1));
                Assert.That(scene.Tracks[0].Id, Is.EqualTo("guideObjectPos"));
                Assert.That(scene.Tracks[0].GuideObjectPath, Is.EqualTo(
                    "cf_j_hips"));
                Assert.That(
                    scene.Tracks[0].Keyframes[0].TryGetVector3(
                        "value",
                        out var position),
                    Is.True);
                Assert.That(position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void PlayerSamplesBoundTransformTracksAndReportsUnsupportedTracks()
        {
            const string linearCurve =
                "<curveKeyframe time=\"0\" value=\"0\" " +
                "inTangent=\"1\" outTangent=\"1\" />" +
                "<curveKeyframe time=\"1\" value=\"1\" " +
                "inTangent=\"1\" outTangent=\"1\" />";
            var xml =
                "<root duration=\"2\" timeScale=\"1\">" +
                "<interpolable owner=\"Timeline\" id=\"guideObjectPos\" " +
                "objectIndex=\"1\" guideObjectPath=\"Arm/Hand\">" +
                "<keyframe time=\"0\" valueX=\"0\" valueY=\"0\" " +
                "valueZ=\"0\">" + linearCurve + "</keyframe>" +
                "<keyframe time=\"2\" valueX=\"10\" valueY=\"4\" " +
                "valueZ=\"-2\">" + linearCurve + "</keyframe>" +
                "</interpolable>" +
                "<interpolable owner=\"KKPE\" id=\"boneRot\" " +
                "objectIndex=\"1\" parameter=\"Arm/Hand\">" +
                "<keyframe time=\"0\" valueX=\"0\" valueY=\"0\" " +
                "valueZ=\"0\" valueW=\"1\">" + linearCurve +
                "</keyframe>" +
                "<keyframe time=\"2\" valueX=\"0\" valueY=\"1\" " +
                "valueZ=\"0\" valueW=\"0\">" + linearCurve +
                "</keyframe>" +
                "</interpolable>" +
                "<interpolable owner=\"Timeline\" id=\"objectEnabled\" " +
                "objectIndex=\"1\"><keyframe time=\"0\" value=\"true\" />" +
                "</interpolable>" +
                "</root>";

            var host = new GameObject("Scene");
            try
            {
                var unrelated = new GameObject("Earlier Studio Object");
                unrelated.transform.SetParent(host.transform, false);
                var targetObject = new GameObject("Character");
                targetObject.transform.SetParent(host.transform, false);
                var arm = new GameObject("Arm");
                arm.transform.SetParent(targetObject.transform, false);
                var hand = new GameObject("Hand");
                hand.transform.SetParent(arm.transform, false);

                var scene = KoikatsuTimelineSceneReader.ParseXml(xml);
                var player = KoikatsuTimelinePlayer.Attach(
                    host,
                    scene,
                    new[] { unrelated, targetObject });

                Assert.That(player.Tracks.Count, Is.EqualTo(3));
                Assert.That(player.Tracks[0].Supported, Is.True);
                Assert.That(player.Tracks[1].Supported, Is.True);
                Assert.That(player.Tracks[2].Supported, Is.False);

                player.Seek(1f);
                Assert.That(
                    hand.transform.localPosition.x,
                    Is.EqualTo(5f).Within(0.001f));
                Assert.That(
                    hand.transform.localPosition.y,
                    Is.EqualTo(2f).Within(0.001f));
                Assert.That(
                    hand.transform.localPosition.z,
                    Is.EqualTo(-1f).Within(0.001f));
                Assert.That(
                    Quaternion.Angle(
                        hand.transform.localRotation,
                        Quaternion.Euler(0f, 90f, 0f)),
                    Is.LessThan(0.01f));

                player.SetTrackEnabled(0, false);
                player.Seek(2f);
                Assert.That(
                    hand.transform.localPosition.x,
                    Is.EqualTo(5f).Within(0.001f));
                player.SetTrackEnabled(0, true);
                Assert.That(
                    hand.transform.localPosition.x,
                    Is.EqualTo(10f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PlayerMarksMissingSceneObjectsAsUnsupported()
        {
            const string xml =
                "<root duration=\"1\"><interpolable owner=\"Timeline\" " +
                "id=\"guideObjectPos\" objectIndex=\"99\">" +
                "<keyframe time=\"0\" valueX=\"1\" valueY=\"2\" " +
                "valueZ=\"3\" /></interpolable></root>";
            var host = new GameObject("Scene");
            try
            {
                var player = KoikatsuTimelinePlayer.Attach(
                    host,
                    KoikatsuTimelineSceneReader.ParseXml(xml),
                    Array.Empty<GameObject>());

                Assert.That(player.Tracks[0].Supported, Is.False);
                Assert.That(player.Tracks[0].Status, Does.Contain("99"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
