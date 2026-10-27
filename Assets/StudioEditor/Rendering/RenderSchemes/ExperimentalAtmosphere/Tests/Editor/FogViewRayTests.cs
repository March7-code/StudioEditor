using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StudioEditor.Rendering.Experimental.Tests
{
    public sealed class FogViewRayTests
    {
        [TestCase(false, 0f)]
        [TestCase(false, 32f)]
        [TestCase(true, 0f)]
        [TestCase(true, -27f)]
        public void GpuFogRaysReachVisibleWorldPointsAtBothScreenHeights(bool orthographic, float pitch)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/StudioEditor/Rendering/RenderSchemes/ExperimentalAtmosphere/Tests/Editor/FogViewRayTests.compute");
            Assert.That(shader, Is.Not.Null);
            var owner = new GameObject("Fog ray regression camera");
            try
            {
                var camera = owner.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(.5f, 1.2f, 2.1f), Quaternion.Euler(pitch, 155, 0));
                camera.orthographic = orthographic; camera.orthographicSize = 1;
                camera.fieldOfView = 44; camera.aspect = 1.6f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                var local = new[] { new Vector3(.2f,.3f,-2), new Vector3(-.2f,-.4f,-2),
                    new Vector3(.5f,.6f,-8), new Vector3(0,0,-2) };
                var expected = new Vector3[local.Length]; var inputs = new Vector4[local.Length];
                for (int i = 0; i < local.Length; i++)
                {
                    expected[i] = camera.cameraToWorldMatrix.MultiplyPoint3x4(local[i]);
                    var viewport = camera.WorldToViewportPoint(expected[i]);
                    inputs[i] = new Vector4(viewport.x, viewport.y, viewport.z, 0);
                }
                shader.SetVectorArray("_TestUVDepth", inputs);
                shader.SetMatrix("_FogInverseProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse);
                shader.SetMatrix("_FogCameraToWorld", camera.cameraToWorldMatrix);
                shader.SetFloat("_FogFarClipValue", SystemInfo.usesReversedZBuffer ? 0 : 1);
                shader.SetFloat("_FogOrthographic", orthographic ? 1 : 0);
                using var buffer = new ComputeBuffer(local.Length, 12);
                int kernel = shader.FindKernel("Reconstruct");
                shader.SetBuffer(kernel, "_TestWorld", buffer); shader.Dispatch(kernel, 1, 1, 1);
                var actual = new Vector3[local.Length]; buffer.GetData(actual);
                for (int i = 0; i < local.Length; i++)
                    Assert.That(Vector3.Distance(expected[i], actual[i]), Is.LessThan(.0001f),
                        $"Fog must reach the visible point, including off-centre UVs: {inputs[i]}, actual={actual[i]}, expected={expected[i]}");
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
