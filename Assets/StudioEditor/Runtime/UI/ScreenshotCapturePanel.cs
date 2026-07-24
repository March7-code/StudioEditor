using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal sealed class ScreenshotCapturePanel : VisualElement
    {
        private readonly ScreenshotCaptureController captureController;
        private readonly Button launcher;
        private readonly VisualElement surface;
        private readonly IntegerField widthField;
        private readonly IntegerField heightField;
        private readonly Button captureButton;
        private readonly Label statusLabel;
        private readonly Label outputPathLabel;
        private bool isOpen;

        public ScreenshotCapturePanel(
            ScreenshotCaptureController captureController,
            VisualElement launcherParent)
        {
            this.captureController = captureController ??
                throw new ArgumentNullException(nameof(captureController));
            if (launcherParent == null)
            {
                throw new ArgumentNullException(nameof(launcherParent));
            }

            name = "screenshot-capture-host";
            pickingMode = PickingMode.Ignore;
            AddToClassList("screenshot-capture-host");

            launcher = new Button(ToggleOpen)
            {
                text = "PIC",
                tooltip = "Take a single screenshot from the active viewport camera",
                pickingMode = PickingMode.Position,
            };
            launcher.AddToClassList("screenshot-capture-launcher");
            launcher.AddToClassList("workspace-tools__button");
            launcherParent.Add(launcher);

            surface = new VisualElement
            {
                pickingMode = PickingMode.Position,
            };
            surface.AddToClassList("screenshot-capture-panel");

            var header = new VisualElement();
            header.AddToClassList("screenshot-capture-panel__header");
            var title = new Label("Take Screenshot");
            title.AddToClassList("screenshot-capture-panel__title");
            header.Add(title);
            var closeButton = new Button(() => SetOpen(false))
            {
                text = "x",
                tooltip = "Close screenshot controls",
            };
            closeButton.AddToClassList("screenshot-capture-panel__close");
            header.Add(closeButton);
            surface.Add(header);

            var content = new VisualElement();
            content.AddToClassList("screenshot-capture-panel__content");
            var resolutionLabel = new Label("Output resolution");
            resolutionLabel.AddToClassList("screenshot-capture-panel__label");
            content.Add(resolutionLabel);

            var resolutionRow = new VisualElement();
            resolutionRow.AddToClassList("screenshot-capture-panel__resolution");
            widthField = CreateDimensionField("Width", Screen.width);
            heightField = CreateDimensionField("Height", Screen.height);
            heightField.AddToClassList(
                "screenshot-capture-panel__field--last");
            resolutionRow.Add(widthField);
            resolutionRow.Add(heightField);
            content.Add(resolutionRow);

            captureButton = new Button(Capture)
            {
                text = "Capture PNG",
                tooltip = "Render the active viewport camera without editor UI",
            };
            captureButton.AddToClassList("screenshot-capture-panel__capture");
            content.Add(captureButton);

            statusLabel = new Label();
            statusLabel.AddToClassList("screenshot-capture-panel__status");
            content.Add(statusLabel);

            outputPathLabel = new Label();
            outputPathLabel.AddToClassList("screenshot-capture-panel__path");
            content.Add(outputPathLabel);
            surface.Add(content);
            Add(surface);

            captureController.StateChanged += RefreshState;
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            SetOpen(false);
            RefreshState();
        }

        private IntegerField CreateDimensionField(string label, int value)
        {
            var field = new IntegerField(label)
            {
                value = Mathf.Clamp(
                    value,
                    ScreenshotCaptureController.MinimumDimension,
                    ScreenshotCaptureController.MaximumDimension),
                isDelayed = true,
                tooltip = $"Screenshot {label.ToLowerInvariant()} in pixels",
            };
            field.AddToClassList("screenshot-capture-panel__field");
            field.RegisterValueChangedCallback(changeEvent =>
                field.SetValueWithoutNotify(Mathf.Clamp(
                    changeEvent.newValue,
                    ScreenshotCaptureController.MinimumDimension,
                    ScreenshotCaptureController.MaximumDimension)));
            return field;
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            captureController.StateChanged -= RefreshState;
            UnregisterCallback<DetachFromPanelEvent>(HandleDetach);
        }

        private void ToggleOpen()
        {
            SetOpen(!isOpen);
        }

        private void SetOpen(bool value)
        {
            isOpen = value;
            surface.style.display = isOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            launcher.EnableInClassList(
                "screenshot-capture-launcher--open",
                isOpen);
        }

        private void Capture()
        {
            captureController.Capture(widthField.value, heightField.value);
        }

        private void RefreshState()
        {
            captureButton.SetEnabled(!captureController.IsCapturing);
            statusLabel.text = captureController.Status;
            outputPathLabel.text = string.IsNullOrWhiteSpace(
                captureController.OutputPath)
                ? string.Empty
                : captureController.OutputPath;
            outputPathLabel.tooltip = outputPathLabel.text;
        }
    }
}
