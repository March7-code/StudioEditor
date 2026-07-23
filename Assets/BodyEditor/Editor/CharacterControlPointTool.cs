using System;
using BodyEditor.Characters.Controls;
using BodyEditor.Editing;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace BodyEditor.Editor
{
    [InitializeOnLoad]
    internal static class CharacterControlPointTool
    {
        private static readonly Color InactiveColor =
            new Color(0.12f, 0.92f, 0.24f, 1f);
        private static readonly Color ActiveColor =
            new Color(0.08f, 1f, 0.16f, 1f);
        private static readonly Color SelectedColor =
            new Color(0.55f, 1f, 0.05f, 1f);
        private static readonly Color LinkColor =
            new Color(0.08f, 1f, 0.2f, 1f);
        private static readonly Color OutlineColor =
            new Color(0.025f, 0.03f, 0.035f, 0.95f);

        private static bool enabled;

        public static event Action StateChanged;

        public static bool Enabled => enabled;

        static CharacterControlPointTool()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Disable;
        }

        public static void SetEnabled(bool value)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            if (enabled)
            {
                SceneView.duringSceneGui += DrawScene;
            }
            else
            {
                SceneView.duringSceneGui -= DrawScene;
            }

            SceneView.RepaintAll();
            StateChanged?.Invoke();
        }

        private static void Disable()
        {
            SetEnabled(false);
        }

        private static void DrawScene(SceneView sceneView)
        {
            if (!enabled || sceneView == null)
            {
                return;
            }

            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            try
            {
                var controllers = Resources.FindObjectsOfTypeAll<
                    CharacterControlPointController>();
                for (var index = 0; index < controllers.Length; index++)
                {
                    var controller = controllers[index];
                    if (controller == null ||
                        EditorUtility.IsPersistent(controller) ||
                        !controller.gameObject.scene.IsValid() ||
                        !controller.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    DrawController(controller, sceneView);
                }

                HandleKeyboard(controllers);
                HandlePointerDeselect(controllers);
            }
            finally
            {
                Handles.color = previousColor;
                Handles.zTest = previousZTest;
            }
        }

        private static void DrawController(
            CharacterControlPointController controller,
            SceneView sceneView)
        {
            if (controller == null || !controller.isActiveAndEnabled)
            {
                return;
            }

            var rigs = controller.Rigs;
            if (rigs.Count == 0)
            {
                controller.RefreshCharacters();
                rigs = controller.Rigs;
            }

            for (var rigIndex = 0; rigIndex < rigs.Count; rigIndex++)
            {
                var rig = rigs[rigIndex];
                if (rig?.Model?.Root == null || !rig.Model.Root.activeInHierarchy)
                {
                    continue;
                }

                DrawRig(controller, rig, sceneView, rigIndex);
            }
        }

        private static void DrawRig(
            CharacterControlPointController controller,
            CharacterControlRig rig,
            SceneView sceneView,
            int rigIndex)
        {
            var points = rig.ControlPoints;
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                if (!rig.TryGetControlPosition(point, out var position))
                {
                    continue;
                }

                var selected = ReferenceEquals(controller.SelectedRig, rig) &&
                               controller.SelectedControlPoint == point;
                var active = rig.IsActive(point);
                var pointColor = selected
                    ? SelectedColor
                    : active
                        ? ActiveColor
                        : InactiveColor;
                var size = HandleUtility.GetHandleSize(position) *
                           (selected ? 0.115f : 0.095f);
                var viewNormal = sceneView.camera != null
                    ? sceneView.camera.transform.forward
                    : Vector3.forward;
                Handles.color = OutlineColor;
                Handles.DrawWireDisc(
                    position,
                    viewNormal,
                    size * 1.15f,
                    2.5f);
                Handles.color = pointColor;
                DrawControlPoint(
                    controller,
                    rig,
                    point,
                    position,
                    size,
                    rigIndex);

                if (active &&
                    rig.TryGetAnchorPosition(point, out var anchor))
                {
                    Handles.color = LinkColor;
                    Handles.DrawLine(anchor, position, 3f);
                }
            }

            if (!ReferenceEquals(controller.SelectedRig, rig) ||
                !controller.SelectedControlPoint.HasValue)
            {
                return;
            }

            DrawSelectedHandle(
                rig,
                controller.SelectedControlPoint.Value);
        }

        private static void DrawControlPoint(
            CharacterControlPointController controller,
            CharacterControlRig rig,
            CharacterControlPoint point,
            Vector3 position,
            float size,
            int rigIndex)
        {
            var hint = (rigIndex + 1) * 397 ^
                       ((int)point + 1) * 7919;
            var controlId = GUIUtility.GetControlID(
                hint,
                FocusType.Passive);
            var current = Event.current;
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(
                        controlId,
                        HandleUtility.DistanceToCircle(position, size));
                    break;
                case EventType.Repaint:
                    Handles.SphereHandleCap(
                        controlId,
                        position,
                        Quaternion.identity,
                        size,
                        EventType.Repaint);
                    break;
                case EventType.MouseDown:
                    if (current.button != 0 || current.alt ||
                        HandleUtility.nearestControl != controlId)
                    {
                        break;
                    }

                    controller.SelectControlPoint(rig, point);
                    Selection.activeGameObject = rig.Model.Root;
                    current.Use();
                    SceneView.RepaintAll();
                    break;
            }
        }

        private static void DrawSelectedHandle(
            CharacterControlRig rig,
            CharacterControlPoint point)
        {
            if (!rig.TryGetControlPosition(point, out var position))
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            var nextPosition = Handles.PositionHandle(
                position,
                Quaternion.identity);
            if (EditorGUI.EndChangeCheck() && rig.SetTarget(point, nextPosition))
            {
                SceneView.RepaintAll();
            }

            if (!rig.SupportsRotation(point) ||
                !rig.TryGetControlRotation(point, out var rotation))
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            var nextRotation = Handles.RotationHandle(rotation, nextPosition);
            if (EditorGUI.EndChangeCheck() &&
                rig.SetTargetRotation(point, nextRotation))
            {
                SceneView.RepaintAll();
            }
        }

        private static void HandleKeyboard(
            CharacterControlPointController[] controllers)
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown)
            {
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                for (var index = 0; index < controllers.Length; index++)
                {
                    controllers[index]?.SelectControlPoint(null, null);
                }

                current.Use();
                return;
            }

            if (current.keyCode != KeyCode.Delete &&
                current.keyCode != KeyCode.Backspace)
            {
                return;
            }

            for (var index = 0; index < controllers.Length; index++)
            {
                if (controllers[index]?.ClearSelectedControlPoint() == true)
                {
                    current.Use();
                    SceneView.RepaintAll();
                    return;
                }
            }
        }

        private static void HandlePointerDeselect(
            CharacterControlPointController[] controllers)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown ||
                current.button != 0 ||
                current.alt)
            {
                return;
            }

            var changed = false;
            for (var index = 0; index < controllers.Length; index++)
            {
                var controller = controllers[index];
                if (controller == null ||
                    !controller.SelectedControlPoint.HasValue)
                {
                    continue;
                }

                controller.SelectControlPoint(null, null);
                changed = true;
            }

            if (changed)
            {
                SceneView.RepaintAll();
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class CharacterPoseControlsToggle : EditorToolbarToggle
    {
        public const string Id =
            "BodyEditor/Character Pose Controls";

        private bool listening;

        public CharacterPoseControlsToggle()
        {
            textIcon = "P";
            tooltip = "Character Pose Controls";
            this.RegisterValueChangedCallback(HandleValueChanged);
            RegisterCallback<AttachToPanelEvent>(HandleAttach);
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            RefreshValue();
        }

        private void HandleAttach(AttachToPanelEvent attachEvent)
        {
            if (listening)
            {
                return;
            }

            listening = true;
            CharacterControlPointTool.StateChanged += RefreshValue;
            RefreshValue();
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            if (!listening)
            {
                return;
            }

            listening = false;
            CharacterControlPointTool.StateChanged -= RefreshValue;
        }

        private void HandleValueChanged(ChangeEvent<bool> changeEvent)
        {
            CharacterControlPointTool.SetEnabled(changeEvent.newValue);
        }

        private void RefreshValue()
        {
            SetValueWithoutNotify(
                CharacterControlPointTool.Enabled);
        }
    }

    [Overlay(
        typeof(SceneView),
        "Character Pose",
        defaultDisplay: true,
        defaultDockZone = DockZone.LeftToolbar,
        defaultDockPosition = DockPosition.Bottom,
        defaultDockIndex = 10)]
    internal sealed class CharacterPoseControlsOverlay : ToolbarOverlay
    {
        public CharacterPoseControlsOverlay()
            : base(CharacterPoseControlsToggle.Id)
        {
        }
    }
}
