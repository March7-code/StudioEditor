using System;
using StudioEditor.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal sealed class ViewportAxisGizmo : VisualElement
    {
        private const float Center = 48f;
        private const float AxisLength = 29f;
        private const float HandleRadius = 11f;

        private readonly StudioEditorViewport viewport;
        private readonly AxisHandle[] handles;

        public ViewportAxisGizmo(StudioEditorViewport viewport)
        {
            this.viewport = viewport;
            name = "studio-editor-axis-gizmo";
            pickingMode = PickingMode.Position;
            AddToClassList("axis-gizmo");
            generateVisualContent += Draw;

            handles = new[]
            {
                CreateHandle(ViewportAxis.X, "X", "axis-gizmo__handle--x"),
                CreateHandle(ViewportAxis.Y, "Y", "axis-gizmo__handle--y"),
                CreateHandle(ViewportAxis.Z, "Z", "axis-gizmo__handle--z"),
            };

            schedule.Execute(Refresh).Every(16);
        }

        private AxisHandle CreateHandle(
            ViewportAxis axis,
            string label,
            string colorClass)
        {
            var button = new Button(() => viewport.AlignToAxis(axis))
            {
                text = label,
                tooltip = $"Align to {label} axis; click again to flip",
                pickingMode = PickingMode.Position,
            };
            button.AddToClassList("axis-gizmo__handle");
            button.AddToClassList(colorClass);
            Add(button);
            return new AxisHandle(axis, button);
        }

        private void Refresh()
        {
            var inverseRotation = Quaternion.Inverse(viewport.ViewRotation);
            for (var index = 0; index < handles.Length; index++)
            {
                var handle = handles[index];
                var cameraDirection = inverseRotation * GetDirection(handle.Axis);
                handle.Offset = new Vector2(
                    cameraDirection.x,
                    -cameraDirection.y) * AxisLength;
                handle.Depth = cameraDirection.z;
                handle.Button.style.left = Center + handle.Offset.x - HandleRadius;
                handle.Button.style.top = Center + handle.Offset.y - HandleRadius;
                handle.Button.style.opacity = Mathf.Lerp(0.68f, 1f,
                    Mathf.InverseLerp(-1f, 1f, handle.Depth));
            }

            Array.Sort(handles, (left, right) => left.Depth.CompareTo(right.Depth));
            for (var index = 0; index < handles.Length; index++)
            {
                handles[index].Button.BringToFront();
            }

            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var center = new Vector2(Center, Center);

            painter.BeginPath();
            painter.fillColor = new Color(0.105f, 0.115f, 0.125f, 0.92f);
            painter.Arc(center, 40f, Angle.Degrees(0f), Angle.Degrees(360f),
                ArcDirection.Clockwise);
            painter.ClosePath();
            painter.Fill();

            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Round;
            for (var index = 0; index < handles.Length; index++)
            {
                var handle = handles[index];
                painter.strokeColor = GetColor(handle.Axis);
                painter.BeginPath();
                painter.MoveTo(center - handle.Offset);
                painter.LineTo(center + handle.Offset);
                painter.Stroke();
            }

            painter.BeginPath();
            painter.fillColor = new Color(0.42f, 0.45f, 0.48f, 1f);
            painter.Arc(center, 4f, Angle.Degrees(0f), Angle.Degrees(360f),
                ArcDirection.Clockwise);
            painter.ClosePath();
            painter.Fill();
        }

        private static Vector3 GetDirection(ViewportAxis axis)
        {
            switch (axis)
            {
                case ViewportAxis.X:
                    return Vector3.right;
                case ViewportAxis.Y:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }

        private static Color GetColor(ViewportAxis axis)
        {
            switch (axis)
            {
                case ViewportAxis.X:
                    return new Color(0.82f, 0.25f, 0.25f, 1f);
                case ViewportAxis.Y:
                    return new Color(0.25f, 0.68f, 0.34f, 1f);
                default:
                    return new Color(0.28f, 0.47f, 0.86f, 1f);
            }
        }

        private sealed class AxisHandle
        {
            public AxisHandle(ViewportAxis axis, Button button)
            {
                Axis = axis;
                Button = button;
            }

            public ViewportAxis Axis { get; }
            public Button Button { get; }
            public Vector2 Offset { get; set; }
            public float Depth { get; set; }
        }
    }
}
