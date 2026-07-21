using System.Collections.Generic;
using BodyEditor.ReferenceModels;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyEditor.Viewport
{
    public enum ViewportAxis
    {
        X,
        Y,
        Z,
    }

    [RequireComponent(typeof(ReferenceModelImportController))]
    public sealed class BodyEditorViewport : MonoBehaviour
    {
        private const float MinDistance = 0.05f;
        private const float MaxDistance = 2000f;

        private ReferenceModelImportController importController;
        [SerializeField] private ViewportControlSettings controls = new ViewportControlSettings();
        private Camera viewportCamera;
        private ViewportGrid grid;
        private Vector3 focus = new Vector3(0f, 1f, 0f);
        private float distance = 8f;
        private float yaw = 25f;
        private float pitch = 12f;
        private float roll;
        private Vector2 cameraOffset;
        private ViewportAxis? alignedAxis;
        private int alignedAxisSign = 1;

        public ViewportControlSettings Controls => controls;

        public Quaternion ViewRotation => Quaternion.Euler(pitch, yaw, roll);

        public bool TryCreatePointerRay(
            Vector2 normalizedPanelPosition,
            out Ray ray)
        {
            if (!EnsureCamera())
            {
                ray = default;
                return false;
            }

            var pixelRect = viewportCamera.pixelRect;
            var screenPosition = new Vector3(
                pixelRect.x + Mathf.Clamp01(normalizedPanelPosition.x) * pixelRect.width,
                pixelRect.y + (1f - Mathf.Clamp01(normalizedPanelPosition.y)) * pixelRect.height,
                0f);
            ray = viewportCamera.ScreenPointToRay(screenPosition);
            return true;
        }

        private void OnEnable()
        {
            importController = GetComponent<ReferenceModelImportController>();
            importController.StateChanged += HandleImportStateChanged;
            grid = new ViewportGrid(transform);
            EnsureCamera();
        }

        private void LateUpdate()
        {
            if (EnsureCamera())
            {
                ApplyCameraPose();
                grid?.Refresh(focus, distance);
            }
        }

        public void Orbit(Vector2 pointerDelta)
        {
            if (EnsureCamera())
            {
                viewportCamera.orthographic = false;
            }

            alignedAxis = null;
            yaw += pointerDelta.x * 0.25f;
            pitch = Mathf.Clamp(pitch - pointerDelta.y * 0.2f, -80f, 80f);
        }

        public void Pan(Vector2 pointerDelta)
        {
            if (!EnsureCamera())
            {
                return;
            }

            var viewportHeight = Mathf.Max(1f, Screen.height - 48f);
            var unitsPerPixel = 2f * distance *
                                Mathf.Tan(viewportCamera.fieldOfView * 0.5f *
                                          Mathf.Deg2Rad) /
                                viewportHeight;
            focus += (-viewportCamera.transform.right * pointerDelta.x +
                      viewportCamera.transform.up * pointerDelta.y) * unitsPerPixel;
        }

        public void Zoom(float wheelDelta)
        {
            distance = Mathf.Clamp(
                distance * Mathf.Exp(wheelDelta * 0.08f),
                MinDistance,
                MaxDistance);
        }

        public void AlignToAxis(ViewportAxis axis)
        {
            if (!EnsureCamera())
            {
                return;
            }

            if (viewportCamera.orthographic && alignedAxis == axis)
            {
                alignedAxisSign *= -1;
            }
            else
            {
                alignedAxis = axis;
                alignedAxisSign = 1;
            }

            switch (axis)
            {
                case ViewportAxis.X:
                    pitch = 0f;
                    yaw = alignedAxisSign > 0 ? -90f : 90f;
                    break;
                case ViewportAxis.Y:
                    pitch = alignedAxisSign > 0 ? 90f : -90f;
                    yaw = 0f;
                    break;
                default:
                    pitch = 0f;
                    yaw = alignedAxisSign > 0 ? 180f : 0f;
                    break;
            }

            roll = 0f;
            cameraOffset = Vector2.zero;

            viewportCamera.orthographic = true;
            ApplyCameraPose();
        }

        private bool EnsureCamera()
        {
            if (viewportCamera != null)
            {
                return true;
            }

            viewportCamera = Camera.main;
            if (viewportCamera == null)
            {
                viewportCamera = FindAnyObjectByType<Camera>();
            }

            if (viewportCamera == null)
            {
                return false;
            }

            viewportCamera.clearFlags = CameraClearFlags.SolidColor;
            viewportCamera.backgroundColor = new Color(0.105f, 0.115f, 0.125f, 1f);
            viewportCamera.orthographic = false;
            ApplyCameraPose();
            return true;
        }

        private void ApplyCameraPose()
        {
            var rotation = Quaternion.Euler(pitch, yaw, roll);
            viewportCamera.transform.SetPositionAndRotation(
                focus + rotation * new Vector3(
                    cameraOffset.x,
                    cameraOffset.y,
                    -distance),
                rotation);
            if (viewportCamera.orthographic)
            {
                viewportCamera.orthographicSize = distance *
                                                  Mathf.Tan(viewportCamera.fieldOfView *
                                                            0.5f * Mathf.Deg2Rad);
            }

            viewportCamera.nearClipPlane = Mathf.Clamp(distance * 0.001f, 0.01f, 0.3f);
            viewportCamera.farClipPlane = Mathf.Max(1000f, distance * 20f);
        }

        private void HandleImportStateChanged()
        {
            if (importController.Status != ReferenceModelImportStatus.Ready ||
                importController.Current?.Root == null)
            {
                return;
            }

            if (importController.Current is IReferenceModelCameraProvider provider &&
                provider.TryGetCamera(out var pose))
            {
                ApplyReferenceCamera(pose);
                return;
            }

            Frame(importController.Current.Root);
        }

        private void ApplyReferenceCamera(ReferenceModelCameraPose pose)
        {
            if (!EnsureCamera())
            {
                return;
            }

            focus = pose.Target;
            pitch = pose.EulerAngles.x;
            yaw = pose.EulerAngles.y;
            roll = pose.EulerAngles.z;
            cameraOffset = new Vector2(pose.Distance.x, pose.Distance.y);
            distance = Mathf.Clamp(
                -pose.Distance.z,
                MinDistance,
                MaxDistance);
            viewportCamera.fieldOfView = Mathf.Clamp(
                pose.FieldOfView,
                1f,
                179f);
            viewportCamera.orthographic = false;
            alignedAxis = null;
            ApplyCameraPose();
        }

        public void Frame(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || !EnsureCamera())
            {
                return;
            }

            roll = 0f;
            cameraOffset = Vector2.zero;

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            focus = bounds.center;
            var aspect = Mathf.Max(0.1f, viewportCamera.aspect);
            var visibleHalfSize = Mathf.Max(
                bounds.extents.y,
                bounds.extents.x / aspect,
                bounds.extents.z * 0.5f);
            var halfFieldOfView = viewportCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            distance = Mathf.Clamp(
                visibleHalfSize * 1.25f / Mathf.Tan(halfFieldOfView) +
                bounds.extents.z,
                MinDistance,
                MaxDistance);
        }

        private void OnDisable()
        {
            if (importController != null)
            {
                importController.StateChanged -= HandleImportStateChanged;
            }

            grid?.Dispose();
            grid = null;
        }
    }

    internal sealed class ViewportGrid
    {
        private const int HalfLineCount = 50;
        private const int MajorInterval = 5;

        private readonly GridLayer minorLines;
        private readonly GridLayer majorLines;
        private readonly GridLayer xAxis;
        private readonly GridLayer zAxis;
        private float currentStep = -1f;
        private Vector2 currentCenter = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

        public ViewportGrid(Transform parent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("Body Editor viewport grid could not find an unlit shader.");
                return;
            }

            minorLines = new GridLayer(parent, "Minor Grid", shader,
                new Color(0.19f, 0.205f, 0.22f, 1f));
            majorLines = new GridLayer(parent, "Major Grid", shader,
                new Color(0.285f, 0.305f, 0.325f, 1f));
            xAxis = new GridLayer(parent, "X Axis", shader,
                new Color(0.68f, 0.22f, 0.22f, 1f));
            zAxis = new GridLayer(parent, "Z Axis", shader,
                new Color(0.22f, 0.39f, 0.68f, 1f));
        }

        public void Refresh(Vector3 focus, float cameraDistance)
        {
            if (minorLines == null)
            {
                return;
            }

            var step = CalculateStep(cameraDistance);
            var center = new Vector2(
                Mathf.Round(focus.x / step) * step,
                Mathf.Round(focus.z / step) * step);
            if (Mathf.Approximately(step, currentStep) && center == currentCenter)
            {
                return;
            }

            currentStep = step;
            currentCenter = center;
            Rebuild(step, center);
        }

        public void Dispose()
        {
            minorLines?.Dispose();
            majorLines?.Dispose();
            xAxis?.Dispose();
            zAxis?.Dispose();
        }

        private void Rebuild(float step, Vector2 center)
        {
            var minor = new List<Vector3>();
            var major = new List<Vector3>();
            var halfSize = step * HalfLineCount;
            var minX = center.x - halfSize;
            var maxX = center.x + halfSize;
            var minZ = center.y - halfSize;
            var maxZ = center.y + halfSize;
            var majorStep = step * MajorInterval;

            for (var offset = -HalfLineCount; offset <= HalfLineCount; offset++)
            {
                var x = center.x + offset * step;
                AddLine(IsMajor(x, majorStep) ? major : minor,
                    new Vector3(x, 0f, minZ), new Vector3(x, 0f, maxZ));

                var z = center.y + offset * step;
                AddLine(IsMajor(z, majorStep) ? major : minor,
                    new Vector3(minX, 0f, z), new Vector3(maxX, 0f, z));
            }

            minorLines.SetVertices(minor);
            majorLines.SetVertices(major);
            xAxis.SetVertices(new List<Vector3>
            {
                new Vector3(minX, 0.002f, 0f),
                new Vector3(maxX, 0.002f, 0f),
            });
            zAxis.SetVertices(new List<Vector3>
            {
                new Vector3(0f, 0.002f, minZ),
                new Vector3(0f, 0.002f, maxZ),
            });
        }

        private static float CalculateStep(float distance)
        {
            var rawStep = Mathf.Max(0.001f, distance / 12f);
            var exponent = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rawStep)));
            var normalized = rawStep / exponent;
            var multiplier = normalized < 2f ? 1f : normalized < 5f ? 2f : 5f;
            return exponent * multiplier;
        }

        private static bool IsMajor(float coordinate, float majorStep)
        {
            return Mathf.Abs(coordinate / majorStep -
                             Mathf.Round(coordinate / majorStep)) < 0.01f;
        }

        private static void AddLine(List<Vector3> vertices, Vector3 start, Vector3 end)
        {
            vertices.Add(start);
            vertices.Add(end);
        }
    }

    internal sealed class GridLayer
    {
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Material material;

        public GridLayer(Transform parent, string name, Shader shader, Color color)
        {
            root = new GameObject(name);
            root.transform.SetParent(parent, false);

            mesh = new Mesh
            {
                name = name + " Mesh",
                hideFlags = HideFlags.DontSave,
            };

            material = new Material(shader)
            {
                name = name + " Material",
                color = color,
                hideFlags = HideFlags.DontSave,
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        public void SetVertices(List<Vector3> vertices)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            var indices = new int[vertices.Count];
            for (var index = 0; index < indices.Length; index++)
            {
                indices[index] = index;
            }

            mesh.SetIndices(indices, MeshTopology.Lines, 0);
        }

        public void Dispose()
        {
            Object.Destroy(root);
            Object.Destroy(mesh);
            Object.Destroy(material);
        }
    }
}
