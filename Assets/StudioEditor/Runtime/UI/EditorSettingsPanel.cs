using System;
using System.Collections.Generic;
using System.IO;
using StudioEditor.ReferenceModels;
using StudioEditor.Settings;
using StudioEditor.Viewport;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal sealed class EditorSettingsPanel : VisualElement
    {
        private static readonly List<string> PointerButtonChoices =
            new List<string> { "Left", "Right", "Middle" };

        private readonly SceneContentController importController;
        private readonly Button launcher;
        private readonly VisualElement menu;
        private readonly Toggle physicsToggle;
        private readonly Button editorSettingsButton;
        private readonly VisualElement editorSurface;
        private readonly TextField koikatsuDirectoryField;
        private readonly Label directoryStatus;
        private readonly Slider uiScaleSlider;
        private readonly DropdownField orbitButtonField;
        private readonly DropdownField panButtonField;

        private bool isOpen;
        private bool editorSettingsOpen;
        private bool physicsEnabled;
        private bool refreshingEditorSettings;

        public EditorSettingsPanel(
            SceneContentController importController,
            VisualElement launcherParent)
        {
            this.importController = importController ??
                throw new ArgumentNullException(nameof(importController));
            if (launcherParent == null)
            {
                throw new ArgumentNullException(nameof(launcherParent));
            }

            name = "editor-settings-host";
            pickingMode = PickingMode.Ignore;
            AddToClassList("editor-settings-host");

            launcher = new Button(ToggleMenu)
            {
                text = "S",
                tooltip = "Open settings",
                pickingMode = PickingMode.Position,
            };
            launcher.AddToClassList("settings-launcher");
            launcher.AddToClassList("workspace-tools__button");
            launcherParent.Add(launcher);

            menu = new VisualElement
            {
                pickingMode = PickingMode.Position,
            };
            menu.AddToClassList("editor-settings-menu");

            physicsToggle = new Toggle("Physics")
            {
                tooltip = "Enable physics for supported scene objects",
            };
            physicsToggle.AddToClassList("editor-settings-menu__physics");
            physicsToggle.RegisterValueChangedCallback(HandlePhysicsChanged);
            menu.Add(physicsToggle);

            editorSettingsButton = new Button(() => SetEditorSettingsOpen(true))
            {
                text = "Editor Settings  >",
                tooltip = "Open editor settings",
            };
            editorSettingsButton.AddToClassList("editor-settings-menu__item");
            menu.Add(editorSettingsButton);
            Add(menu);

            editorSurface = new VisualElement
            {
                pickingMode = PickingMode.Position,
            };
            editorSurface.AddToClassList("editor-settings-panel");

            var header = new VisualElement();
            header.AddToClassList("editor-settings-panel__header");
            header.Add(CreateHeaderButton(
                "<",
                "Back",
                () => SetEditorSettingsOpen(false)));
            var title = new Label("Editor Settings");
            title.AddToClassList("editor-settings-panel__title");
            header.Add(title);
            header.Add(CreateHeaderButton(
                "x",
                "Close",
                () => SetMenuOpen(false)));
            editorSurface.Add(header);

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.AddToClassList("editor-settings-panel__content");

            var koikatsuSection = CreateSection("Koikatsu");
            var directoryRow = new VisualElement();
            directoryRow.AddToClassList("editor-settings-panel__path-row");
            koikatsuDirectoryField = new TextField("Directory")
            {
                isDelayed = true,
                tooltip = "Koikatsu directory containing abdata",
            };
            koikatsuDirectoryField.AddToClassList(
                "editor-settings-panel__path-field");
            koikatsuDirectoryField.RegisterValueChangedCallback(
                HandleKoikatsuDirectoryChanged);
            directoryRow.Add(koikatsuDirectoryField);
            var browseButton = new Button(PickKoikatsuDirectory)
            {
                text = "...",
                tooltip = "Select Koikatsu directory",
            };
            browseButton.AddToClassList(
                "editor-settings-panel__browse-button");
            directoryRow.Add(browseButton);
            koikatsuSection.Add(directoryRow);
            directoryStatus = new Label();
            directoryStatus.AddToClassList(
                "editor-settings-panel__status");
            koikatsuSection.Add(directoryStatus);
            content.Add(koikatsuSection);

            var interfaceSection = CreateSection("Interface");
            uiScaleSlider = new Slider("UI Scale", 0.75f, 1.5f)
            {
                showInputField = true,
                tooltip = "Scale the complete editor interface",
            };
            uiScaleSlider.AddToClassList("editor-settings-panel__field");
            uiScaleSlider.RegisterValueChangedCallback(HandleUiScaleChanged);
            interfaceSection.Add(uiScaleSlider);
            content.Add(interfaceSection);

            var mouseSection = CreateSection("Mouse");
            orbitButtonField = CreatePointerButtonField(
                "Orbit",
                HandleOrbitButtonChanged);
            panButtonField = CreatePointerButtonField(
                "Pan",
                HandlePanButtonChanged);
            mouseSection.Add(orbitButtonField);
            mouseSection.Add(panButtonField);
            content.Add(mouseSection);

            editorSurface.Add(content);
            Add(editorSurface);

            importController.StateChanged += RefreshPhysics;
            StudioEditorSettings.Changed += RefreshEditorSettings;
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            SetMenuOpen(false);
            RefreshPhysics();
            RefreshEditorSettings();
        }

        private static VisualElement CreateSection(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("editor-settings-panel__section");
            var heading = new Label(title);
            heading.AddToClassList("editor-settings-panel__section-title");
            section.Add(heading);
            return section;
        }

        private static Button CreateHeaderButton(
            string text,
            string tooltip,
            Action action)
        {
            var button = new Button(action)
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList("editor-settings-panel__header-button");
            return button;
        }

        private static DropdownField CreatePointerButtonField(
            string label,
            EventCallback<ChangeEvent<string>> callback)
        {
            var field = new DropdownField(label, PointerButtonChoices, 0);
            field.AddToClassList("editor-settings-panel__field");
            field.RegisterValueChangedCallback(callback);
            return field;
        }

        private void ToggleMenu()
        {
            SetMenuOpen(!isOpen);
        }

        private void SetMenuOpen(bool value)
        {
            isOpen = value;
            menu.style.display = isOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            launcher.EnableInClassList("settings-launcher--open", isOpen);
            if (!isOpen)
            {
                SetEditorSettingsOpen(false);
            }
        }

        private void SetEditorSettingsOpen(bool value)
        {
            editorSettingsOpen = value && isOpen;
            editorSurface.style.display = editorSettingsOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            editorSettingsButton.EnableInClassList(
                "editor-settings-menu__item--open",
                editorSettingsOpen);
            if (editorSettingsOpen)
            {
                RefreshEditorSettings();
            }
        }

        private void HandlePhysicsChanged(ChangeEvent<bool> changeEvent)
        {
            physicsEnabled = changeEvent.newValue;
            ApplyPhysicsState();
            RefreshPhysics();
        }

        private void HandleKoikatsuDirectoryChanged(
            ChangeEvent<string> changeEvent)
        {
            if (refreshingEditorSettings)
            {
                return;
            }

            ApplyKoikatsuDirectory(changeEvent.newValue);
        }

        private void PickKoikatsuDirectory()
        {
            if (!WindowsModelFilePicker.TryPickDirectory(
                    out var directory,
                    out var error,
                    "Select Koikatsu Directory",
                    StudioEditorSettings.KoikatsuGameRoot))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    SetDirectoryError(error);
                }

                return;
            }

            ApplyKoikatsuDirectory(directory);
        }

        private void ApplyKoikatsuDirectory(string directory)
        {
            if (!StudioEditorSettings.TrySetKoikatsuGameRoot(
                    directory,
                    out var error))
            {
                SetDirectoryError(error);
                return;
            }

            RefreshEditorSettings();
        }

        private void HandleUiScaleChanged(ChangeEvent<float> changeEvent)
        {
            if (refreshingEditorSettings)
            {
                return;
            }

            StudioEditorSettings.SetUiScale(changeEvent.newValue);
        }

        private void HandleOrbitButtonChanged(ChangeEvent<string> changeEvent)
        {
            if (!refreshingEditorSettings &&
                TryParsePointerButton(changeEvent.newValue, out var button))
            {
                StudioEditorSettings.SetOrbitButton(button);
            }
        }

        private void HandlePanButtonChanged(ChangeEvent<string> changeEvent)
        {
            if (!refreshingEditorSettings &&
                TryParsePointerButton(changeEvent.newValue, out var button))
            {
                StudioEditorSettings.SetPanButton(button);
            }
        }

        private void RefreshPhysics()
        {
            var supportsPhysics = ApplyPhysicsState();
            physicsToggle.SetValueWithoutNotify(physicsEnabled);
            physicsToggle.SetEnabled(supportsPhysics);
        }

        private void RefreshEditorSettings()
        {
            refreshingEditorSettings = true;
            try
            {
                var directory = StudioEditorSettings.KoikatsuGameRoot;
                koikatsuDirectoryField.SetValueWithoutNotify(directory);
                uiScaleSlider.SetValueWithoutNotify(StudioEditorSettings.UiScale);
                orbitButtonField.SetValueWithoutNotify(
                    GetPointerButtonName(StudioEditorSettings.OrbitButton));
                panButtonField.SetValueWithoutNotify(
                    GetPointerButtonName(StudioEditorSettings.PanButton));

                var directoryAvailable = string.IsNullOrWhiteSpace(directory) ||
                    Directory.Exists(Path.Combine(directory, "abdata"));
                SetDirectoryError(directoryAvailable
                    ? string.Empty
                    : "Configured directory is unavailable");
            }
            finally
            {
                refreshingEditorSettings = false;
            }
        }

        private void SetDirectoryError(string error)
        {
            var hasError = !string.IsNullOrWhiteSpace(error);
            directoryStatus.text = hasError ? error : string.Empty;
            directoryStatus.style.display = hasError
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            koikatsuDirectoryField.EnableInClassList(
                "editor-settings-panel__path-field--error",
                hasError);
        }

        private bool ApplyPhysicsState()
        {
            var supportsPhysics = false;
            var imports = importController.ManagedImports;
            for (var index = 0; index < imports.Count; index++)
            {
                if (!(imports[index] is IReferenceModelPhysicsController physics) ||
                    !physics.SupportsPhysics)
                {
                    continue;
                }

                supportsPhysics = true;
                if (physics.PhysicsEnabled != physicsEnabled)
                {
                    physics.SetPhysicsEnabled(physicsEnabled);
                }
            }

            return supportsPhysics;
        }

        private static bool TryParsePointerButton(
            string value,
            out ViewportPointerButton button)
        {
            return Enum.TryParse(value, true, out button);
        }

        private static string GetPointerButtonName(
            ViewportPointerButton button)
        {
            return button.ToString();
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            importController.StateChanged -= RefreshPhysics;
            StudioEditorSettings.Changed -= RefreshEditorSettings;
            UnregisterCallback<DetachFromPanelEvent>(HandleDetach);
        }
    }
}
