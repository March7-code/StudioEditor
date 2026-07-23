using System;
using System.Collections.Generic;
using System.IO;
using BodyEditor.Editing;
using BodyEditor.ReferenceModels;
using BodyEditor.Settings;
using BodyEditor.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(SceneContentController))]
    [RequireComponent(typeof(ReferenceModelPresentationController))]
    [RequireComponent(typeof(TimelineCaptureController))]
    [RequireComponent(typeof(SceneTimelineController))]
    public sealed class BodyEditorTopBar : MonoBehaviour
    {
        private SceneContentController controller;
        private TimelineCaptureController captureController;
        private SceneTimelineController sceneTimeline;
        private EditableSkeletonController editableSkeleton;
        private BodyEditorViewport viewport;
        private BodyEditorViewportInputController inputController;
        private PanelSettings panelSettings;
        private bool skeletonPointerActive;
        private bool addMenuOpen;
        private VisualElement uiRoot;
        private VisualElement viewportInput;
        private Button addButton;
        private VisualElement addMenu;
        private Button importButton;
        private Button sceneImportButton;

        private void OnEnable()
        {
            controller = GetComponent<SceneContentController>();
            captureController = GetComponent<TimelineCaptureController>();
            sceneTimeline = GetComponent<SceneTimelineController>();
            editableSkeleton = GetComponent<EditableSkeletonController>();

            viewport = GetComponent<BodyEditorViewport>();
            if (viewport != null)
            {
                inputController = new BodyEditorViewportInputController(
                    viewport,
                    viewport.Controls);
            }

            var document = GetComponent<UIDocument>();
            panelSettings = document.panelSettings;
            ApplyUiScale();
            BodyEditorSettings.Changed += ApplyUiScale;
            controller.StateChanged += RefreshState;
            BuildUi(document.rootVisualElement);
            RefreshState();
        }

        private void OnDisable()
        {
            BodyEditorSettings.Changed -= ApplyUiScale;
            panelSettings = null;
            if (controller != null)
            {
                controller.StateChanged -= RefreshState;
            }

            if (importButton != null)
            {
                importButton.clicked -= PickAndImport;
            }

            if (sceneImportButton != null)
            {
                sceneImportButton.clicked -= PickAndImportScene;
            }

            if (addButton != null)
            {
                addButton.clicked -= ToggleAddMenu;
            }

            UnregisterViewportInput();
            if (uiRoot != null)
            {
                uiRoot.UnregisterCallback<KeyDownEvent>(
                    HandleKeyDown,
                    TrickleDown.TrickleDown);
                uiRoot = null;
            }

            editableSkeleton?.EndPointerDrag();
            skeletonPointerActive = false;
            inputController = null;
        }

        private void ApplyUiScale()
        {
            if (panelSettings != null)
            {
                panelSettings.scale = BodyEditorSettings.UiScale;
            }
        }

        private void BuildUi(VisualElement root)
        {
            root.Clear();
            uiRoot = root;
            uiRoot.focusable = true;
            uiRoot.RegisterCallback<KeyDownEvent>(
                HandleKeyDown,
                TrickleDown.TrickleDown);
            root.AddToClassList("body-editor-ui");
            root.RemoveFromClassList(
                "body-editor-ui--sidebar-collapsed");
            root.pickingMode = PickingMode.Ignore;

            viewportInput = new VisualElement
            {
                name = "body-editor-viewport-input",
                pickingMode = PickingMode.Position,
                focusable = true,
            };
            viewportInput.AddToClassList("viewport-input");
            viewportInput.RegisterCallback<PointerDownEvent>(HandlePointerDown);
            viewportInput.RegisterCallback<PointerMoveEvent>(HandlePointerMove);
            viewportInput.RegisterCallback<PointerUpEvent>(HandlePointerUp);
            viewportInput.RegisterCallback<PointerCancelEvent>(HandlePointerCancel);
            viewportInput.RegisterCallback<WheelEvent>(HandleWheel);
            root.Add(viewportInput);

            var workspaceTools = new VisualElement
            {
                name = "body-editor-workspace-tools",
                pickingMode = PickingMode.Ignore,
            };
            workspaceTools.AddToClassList("workspace-tools");
            root.Add(workspaceTools);

            addButton = new Button
            {
                text = "+",
                tooltip = "Add to scene",
                pickingMode = PickingMode.Position,
            };
            addButton.AddToClassList("workspace-add");
            addButton.AddToClassList("workspace-tools__button");
            addButton.clicked += ToggleAddMenu;
            workspaceTools.Add(addButton);

            addMenu = new VisualElement
            {
                name = "body-editor-add-menu",
                pickingMode = PickingMode.Position,
            };
            addMenu.AddToClassList("add-menu");
            addMenu.style.display = DisplayStyle.None;

            importButton = new Button
            {
                text = "Character",
                tooltip = "Import a character from disk",
            };
            importButton.AddToClassList("add-menu__item");
            importButton.clicked += PickAndImport;
            addMenu.Add(importButton);

            sceneImportButton = new Button
            {
                text = "Scene",
                tooltip = "Import a Koikatsu Studio scene card",
            };
            sceneImportButton.AddToClassList("add-menu__item");
            sceneImportButton.clicked += PickAndImportScene;
            addMenu.Add(sceneImportButton);
            workspaceTools.Add(addMenu);

            if (viewport != null)
            {
                root.Add(new ViewportAxisGizmo(viewport));
            }

            var animationPanel = new CharacterAnimationPanel(
                controller,
                workspaceTools);
            root.Add(animationPanel);

            var timelinePanel = new ReferenceTimelinePanel(
                controller,
                captureController,
                sceneTimeline,
                editableSkeleton,
                viewportInput,
                workspaceTools);
            root.Add(timelinePanel);

            root.Add(new EditorSettingsPanel(controller, workspaceTools));

            root.Add(new BodyEditorSidebar(
                controller,
                collapsed => uiRoot?.EnableInClassList(
                    "body-editor-ui--sidebar-collapsed",
                    collapsed),
                camera => viewport?.ActivateReferenceCamera(camera),
                animationPanel.SetSelectedSceneNode));
            workspaceTools.BringToFront();
            addMenu.BringToFront();
            uiRoot.Focus();
        }

        private void ToggleAddMenu()
        {
            SetAddMenuVisible(!addMenuOpen);
        }

        private void SetAddMenuVisible(bool visible)
        {
            if (addMenu == null)
            {
                return;
            }

            addMenuOpen = visible;
            addMenu.style.display = addMenuOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            addButton?.EnableInClassList("workspace-add--open", addMenuOpen);
        }

        private void HandleKeyDown(KeyDownEvent keyEvent)
        {
            if (editableSkeleton == null ||
                skeletonPointerActive ||
                (!keyEvent.ctrlKey && !keyEvent.commandKey))
            {
                return;
            }

            var handled = false;
            if (keyEvent.keyCode == KeyCode.Z)
            {
                if (keyEvent.shiftKey)
                {
                    editableSkeleton.Redo();
                }
                else
                {
                    editableSkeleton.Undo();
                }

                handled = true;
            }
            else if (keyEvent.keyCode == KeyCode.Y)
            {
                editableSkeleton.Redo();
                handled = true;
            }

            if (handled)
            {
                keyEvent.StopImmediatePropagation();
            }
        }

        private void HandlePointerDown(PointerDownEvent pointerEvent)
        {
            viewportInput.Focus();
            if (editableSkeleton != null &&
                pointerEvent.button == (int)ViewportPointerButton.Left &&
                TryCreatePointerRay(pointerEvent.position, out var ray) &&
                editableSkeleton.BeginPointerDrag(
                    ray,
                    viewport.ViewRotation * Vector3.forward))
            {
                skeletonPointerActive = true;
                viewportInput.CapturePointer(pointerEvent.pointerId);
                pointerEvent.StopPropagation();
                return;
            }

            if (inputController == null ||
                !inputController.BeginPointer(
                    pointerEvent.pointerId,
                    pointerEvent.button))
            {
                return;
            }

            viewportInput.CapturePointer(pointerEvent.pointerId);
            pointerEvent.StopPropagation();
        }

        private void HandlePointerMove(PointerMoveEvent pointerEvent)
        {
            if (skeletonPointerActive &&
                viewportInput.HasPointerCapture(pointerEvent.pointerId))
            {
                if (TryCreatePointerRay(pointerEvent.position, out var ray))
                {
                    editableSkeleton?.UpdatePointerDrag(ray);
                }

                pointerEvent.StopPropagation();
                return;
            }

            if (inputController == null ||
                !viewportInput.HasPointerCapture(pointerEvent.pointerId))
            {
                return;
            }

            var delta = new Vector2(
                pointerEvent.deltaPosition.x,
                pointerEvent.deltaPosition.y);
            if (inputController.MovePointer(
                    pointerEvent.pointerId,
                    pointerEvent.pressedButtons,
                    delta))
            {
                pointerEvent.StopPropagation();
            }
        }

        private void HandlePointerUp(PointerUpEvent pointerEvent)
        {
            ReleasePointer(pointerEvent.pointerId);
        }

        private void HandlePointerCancel(PointerCancelEvent pointerEvent)
        {
            ReleasePointer(pointerEvent.pointerId);
        }

        private void HandleWheel(WheelEvent wheelEvent)
        {
            if (inputController == null)
            {
                return;
            }

            inputController.Zoom(wheelEvent.delta.y);
            wheelEvent.StopPropagation();
        }

        private void ReleasePointer(int pointerId)
        {
            if (skeletonPointerActive)
            {
                editableSkeleton?.EndPointerDrag();
                skeletonPointerActive = false;
            }

            inputController?.EndPointer(pointerId);
            if (viewportInput?.HasPointerCapture(pointerId) == true)
            {
                viewportInput.ReleasePointer(pointerId);
            }
        }

        private void UnregisterViewportInput()
        {
            if (viewportInput == null)
            {
                return;
            }

            viewportInput.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
            viewportInput.UnregisterCallback<PointerMoveEvent>(HandlePointerMove);
            viewportInput.UnregisterCallback<PointerUpEvent>(HandlePointerUp);
            viewportInput.UnregisterCallback<PointerCancelEvent>(HandlePointerCancel);
            viewportInput.UnregisterCallback<WheelEvent>(HandleWheel);
            viewportInput = null;
        }

        private bool TryCreatePointerRay(Vector2 panelPosition, out Ray ray)
        {
            var visualTree = viewportInput?.panel?.visualTree;
            if (viewport == null || visualTree == null)
            {
                ray = default;
                return false;
            }

            var bounds = visualTree.worldBound;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                ray = default;
                return false;
            }

            var normalizedPosition = new Vector2(
                (panelPosition.x - bounds.xMin) / bounds.width,
                (panelPosition.y - bounds.yMin) / bounds.height);
            return viewport.TryCreatePointerRay(normalizedPosition, out ray);
        }

        private async void PickAndImport()
        {
            SetAddMenuVisible(false);
            var extensions = CollectExtensions();
            if (!WindowsModelFilePicker.TryPick(
                    extensions,
                    out var filePath,
                    out var pickerError))
            {
                if (!string.IsNullOrEmpty(pickerError))
                {
                    SetAddTooltip(pickerError);
                    Debug.LogError($"[Body Editor] {pickerError}", this);
                }

                return;
            }

            SetAddTooltip($"Importing {Path.GetFileName(filePath)}");
            await controller.ImportAsync(filePath);
        }

        private async void PickAndImportScene()
        {
            SetAddMenuVisible(false);
            var extensions = CollectExtensions(true);
            if (!WindowsModelFilePicker.TryPick(
                    extensions,
                    out var filePath,
                    out var pickerError,
                    "Import Koikatsu Studio Scene",
                    "Koikatsu Studio Scenes"))
            {
                if (!string.IsNullOrEmpty(pickerError))
                {
                    SetAddTooltip(pickerError);
                    Debug.LogError($"[Body Editor] {pickerError}", this);
                }

                return;
            }

            SetAddTooltip($"Importing {Path.GetFileName(filePath)}");
            await controller.ImportSceneAsync(filePath);
        }

        private List<string> CollectExtensions(bool sceneAdapters = false)
        {
            var extensions = new List<string>();
            for (var adapterIndex = 0;
                 adapterIndex < controller.Adapters.Count;
                 adapterIndex++)
            {
                var adapter = controller.Adapters[adapterIndex];
                if ((adapter is IReferenceSceneFormatAdapter) != sceneAdapters)
                {
                    continue;
                }

                var adapterExtensions = adapter.FileExtensions;
                for (var extensionIndex = 0;
                     extensionIndex < adapterExtensions.Count;
                     extensionIndex++)
                {
                    var extension = adapterExtensions[extensionIndex];
                    if (!extensions.Contains(extension))
                    {
                        extensions.Add(extension);
                    }
                }
            }

            return extensions;
        }

        private void RefreshState()
        {
            if (importButton == null)
            {
                return;
            }

            importButton.SetEnabled(
                controller.Status != ReferenceModelImportStatus.Loading);
            addButton?.SetEnabled(
                controller.Status != ReferenceModelImportStatus.Loading);
            sceneImportButton?.SetEnabled(
                controller.Status != ReferenceModelImportStatus.Loading &&
                CollectExtensions(true).Count > 0);
            if (controller.Status == ReferenceModelImportStatus.Loading)
            {
                SetAddMenuVisible(false);
            }
            switch (controller.Status)
            {
                case ReferenceModelImportStatus.Loading:
                    SetAddTooltip("Loading...");
                    break;
                case ReferenceModelImportStatus.Ready:
                    SetAddTooltip("Add to scene");
                    break;
                case ReferenceModelImportStatus.Failed:
                    SetAddTooltip(controller.Error);
                    break;
                default:
                    SetAddTooltip("Add to scene");
                    break;
            }
        }

        private void SetAddTooltip(string tooltip)
        {
            if (addButton == null)
            {
                return;
            }

            addButton.tooltip = string.IsNullOrWhiteSpace(tooltip)
                ? "Add to scene"
                : tooltip;
        }
    }
}
