using System;
using UnityEngine;

namespace StudioEditor.Viewport
{
    public enum ViewportPointerButton
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }

    public enum ViewportNavigationAction
    {
        None,
        Orbit,
        Pan,
    }

    [Serializable]
    public sealed class ViewportControlSettings
    {
        [SerializeField] private ViewportPointerButton orbitButton =
            ViewportPointerButton.Right;
        [SerializeField] private ViewportPointerButton panButton =
            ViewportPointerButton.Middle;
        [SerializeField, Min(0.01f)] private float orbitSensitivity = 1f;
        [SerializeField, Min(0.01f)] private float panSensitivity = 1f;
        [SerializeField, Min(0.01f)] private float zoomSensitivity = 1f;

        public ViewportPointerButton OrbitButton
        {
            get => orbitButton;
            set => orbitButton = value;
        }

        public ViewportPointerButton PanButton
        {
            get => panButton;
            set => panButton = value;
        }

        public float OrbitSensitivity
        {
            get => orbitSensitivity;
            set => orbitSensitivity = Mathf.Max(0.01f, value);
        }

        public float PanSensitivity
        {
            get => panSensitivity;
            set => panSensitivity = Mathf.Max(0.01f, value);
        }

        public float ZoomSensitivity
        {
            get => zoomSensitivity;
            set => zoomSensitivity = Mathf.Max(0.01f, value);
        }

        public ViewportNavigationAction Resolve(int pointerButton)
        {
            if (pointerButton == (int)panButton)
            {
                return ViewportNavigationAction.Pan;
            }

            return pointerButton == (int)orbitButton
                ? ViewportNavigationAction.Orbit
                : ViewportNavigationAction.None;
        }
    }

    internal sealed class StudioEditorViewportInputController
    {
        private readonly StudioEditorViewport viewport;
        private readonly ViewportControlSettings settings;
        private ViewportNavigationAction activeAction;
        private int activePointerId = -1;
        private int activeButtonMask;

        public StudioEditorViewportInputController(
            StudioEditorViewport viewport,
            ViewportControlSettings settings)
        {
            this.viewport = viewport;
            this.settings = settings;
        }

        public bool BeginPointer(int pointerId, int pointerButton)
        {
            var action = settings.Resolve(pointerButton);
            if (action == ViewportNavigationAction.None)
            {
                return false;
            }

            activePointerId = pointerId;
            activeButtonMask = 1 << pointerButton;
            activeAction = action;
            return true;
        }

        public bool MovePointer(int pointerId, int pressedButtons, Vector2 delta)
        {
            if (pointerId != activePointerId ||
                (pressedButtons & activeButtonMask) == 0)
            {
                return false;
            }

            switch (activeAction)
            {
                case ViewportNavigationAction.Orbit:
                    viewport.Orbit(delta * settings.OrbitSensitivity);
                    return true;
                case ViewportNavigationAction.Pan:
                    viewport.Pan(delta * settings.PanSensitivity);
                    return true;
                default:
                    return false;
            }
        }

        public bool EndPointer(int pointerId)
        {
            if (pointerId != activePointerId)
            {
                return false;
            }

            activePointerId = -1;
            activeButtonMask = 0;
            activeAction = ViewportNavigationAction.None;
            return true;
        }

        public void Zoom(float wheelDelta)
        {
            viewport.Zoom(wheelDelta * settings.ZoomSensitivity);
        }
    }
}
