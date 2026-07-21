using System;
using System.Collections.Generic;
using System.IO;
using BodyEditor.Editing;
using BodyEditor.ReferenceModels;
using BodyEditor.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(ReferenceModelImportController))]
    [RequireComponent(typeof(ReferenceModelPresentationController))]
    public sealed class BodyEditorTopBar : MonoBehaviour
    {
        private const string StatusReadyClass = "topbar__status--ready";
        private const string StatusLoadingClass = "topbar__status--loading";
        private const string StatusErrorClass = "topbar__status--error";
        private const string TopologyModeSelectedClass =
            "topbar__topology-mode--selected";

        private ReferenceModelImportController controller;
        private ReferenceModelPresentationController presentation;
        private EditableSkeletonController editableSkeleton;
        private CharacterControlPointController characterControls;
        private BodyEditorViewport viewport;
        private BodyEditorViewportInputController inputController;
        private bool skeletonPointerActive;
        private bool controlPointPointerActive;
        private VisualElement uiRoot;
        private VisualElement viewportInput;
        private Button importButton;
        private Button sceneImportButton;
        private Button topologyEdgesButton;
        private Button topologyRingsButton;
        private Button topologyBothButton;
        private IntegerField sectionRingCountField;
        private Toggle topologyToggle;
        private Toggle physicsToggle;
        private Label modelLabel;
        private Label statusLabel;

        private void OnEnable()
        {
            controller = GetComponent<ReferenceModelImportController>();
            presentation = GetComponent<ReferenceModelPresentationController>();
            editableSkeleton = GetComponent<EditableSkeletonController>();
            characterControls = GetComponent<CharacterControlPointController>();
            viewport = GetComponent<BodyEditorViewport>();
            if (viewport != null)
            {
                inputController = new BodyEditorViewportInputController(
                    viewport,
                    viewport.Controls);
            }

            controller.StateChanged += RefreshState;
            presentation.StateChanged += RefreshTopologyState;
            BuildUi(GetComponent<UIDocument>().rootVisualElement);
            RefreshState();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.StateChanged -= RefreshState;
            }

            if (presentation != null)
            {
                presentation.StateChanged -= RefreshTopologyState;
            }

            if (importButton != null)
            {
                importButton.clicked -= PickAndImport;
            }

            if (sceneImportButton != null)
            {
                sceneImportButton.clicked -= PickAndImportScene;
            }

            if (physicsToggle != null)
            {
                physicsToggle.UnregisterValueChangedCallback(HandlePhysicsChanged);
            }

            if (topologyToggle != null)
            {
                topologyToggle.UnregisterValueChangedCallback(
                    HandleTopologyChanged);
            }

            if (sectionRingCountField != null)
            {
                sectionRingCountField.UnregisterValueChangedCallback(
                    HandleSectionRingCountChanged);
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
            characterControls?.EndPointerDrag();
            skeletonPointerActive = false;
            controlPointPointerActive = false;
            inputController = null;
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

            var topBar = new VisualElement
            {
                name = "body-editor-topbar",
                pickingMode = PickingMode.Position,
            };
            topBar.AddToClassList("topbar");

            var brand = new Label("Body Editor");
            brand.AddToClassList("topbar__brand");
            topBar.Add(brand);

            var divider = new VisualElement();
            divider.AddToClassList("topbar__divider");
            topBar.Add(divider);

            importButton = new Button
            {
                text = "Import Model",
                tooltip = "Import a model from disk",
            };
            importButton.AddToClassList("topbar__import");
            importButton.clicked += PickAndImport;
            topBar.Add(importButton);

            sceneImportButton = new Button
            {
                text = "Import Scene",
                tooltip = "Import a Koikatsu Studio scene card",
            };
            sceneImportButton.AddToClassList("topbar__scene-import");
            sceneImportButton.clicked += PickAndImportScene;
            topBar.Add(sceneImportButton);

            modelLabel = new Label("No model");
            modelLabel.AddToClassList("topbar__model");
            topBar.Add(modelLabel);

            topologyToggle = new Toggle("Topology")
            {
                tooltip = "Show only the imported reference mesh topology",
            };
            topologyToggle.AddToClassList("topbar__topology");
            topologyToggle.RegisterValueChangedCallback(HandleTopologyChanged);
            topBar.Add(topologyToggle);

            var topologyModes = new VisualElement
            {
                tooltip = "Choose triangle edges, source-vertex rings, or both",
            };
            topologyModes.AddToClassList("topbar__topology-modes");
            topologyEdgesButton = CreateTopologyModeButton(
                "Edges",
                ReferenceTopologyDisplayMode.Edges,
                "Show the imported triangle topology");
            topologyEdgesButton.AddToClassList(
                "topbar__topology-mode--first");
            topologyRingsButton = CreateTopologyModeButton(
                "Rings",
                ReferenceTopologyDisplayMode.Rings,
                "Orange source observations with the green recovered section field");
            topologyBothButton = CreateTopologyModeButton(
                "Both",
                ReferenceTopologyDisplayMode.Both,
                "Show triangle topology and captured source-vertex rings");
            topologyBothButton.AddToClassList(
                "topbar__topology-mode--last");
            topologyModes.Add(topologyEdgesButton);
            topologyModes.Add(topologyRingsButton);
            topologyModes.Add(topologyBothButton);
            topBar.Add(topologyModes);

            sectionRingCountField = new IntegerField("Count")
            {
                tooltip = "Source-vertex ring samples per semantic segment (1-100)",
                isDelayed = true,
            };
            sectionRingCountField.AddToClassList("topbar__ring-count");
            sectionRingCountField.RegisterValueChangedCallback(
                HandleSectionRingCountChanged);
            topBar.Add(sectionRingCountField);

            physicsToggle = new Toggle("Physics")
            {
                tooltip = "Enable model physics",
            };
            physicsToggle.AddToClassList("topbar__physics");
            physicsToggle.RegisterValueChangedCallback(HandlePhysicsChanged);
            topBar.Add(physicsToggle);

            statusLabel = new Label("Ready");
            statusLabel.AddToClassList("topbar__status");
            topBar.Add(statusLabel);

            root.Add(topBar);

            if (viewport != null)
            {
                root.Add(new ViewportAxisGizmo(viewport));
            }

            root.Add(new ReferenceTimelinePanel(controller, viewportInput));

            root.Add(new BodyEditorSidebar(
                editableSkeleton,
                controller,
                presentation));
            uiRoot.Focus();
        }

        private void HandlePhysicsChanged(ChangeEvent<bool> changeEvent)
        {
            if (controller.Current is IReferenceModelPhysicsController physics &&
                physics.SupportsPhysics &&
                !presentation.TopologyMode)
            {
                physics.SetPhysicsEnabled(changeEvent.newValue);
            }

            RefreshPhysicsState();
        }

        private void HandleTopologyChanged(ChangeEvent<bool> changeEvent)
        {
            presentation.SetTopologyMode(changeEvent.newValue);
            RefreshTopologyState();
            RefreshPhysicsState();
        }

        private Button CreateTopologyModeButton(
            string text,
            ReferenceTopologyDisplayMode mode,
            string tooltip)
        {
            var button = new Button(() =>
                presentation.SetTopologyDisplayMode(mode))
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList("topbar__topology-mode");
            return button;
        }

        private void HandleSectionRingCountChanged(ChangeEvent<int> changeEvent)
        {
            presentation.SetSectionRingCount(changeEvent.newValue);
            sectionRingCountField.SetValueWithoutNotify(
                presentation.SectionRingCount);
        }

        private void HandleKeyDown(KeyDownEvent keyEvent)
        {
            if (!skeletonPointerActive && !controlPointPointerActive &&
                (keyEvent.keyCode == KeyCode.Delete ||
                 keyEvent.keyCode == KeyCode.Backspace) &&
                characterControls?.ClearSelectedControlPoint() == true)
            {
                keyEvent.StopImmediatePropagation();
                return;
            }

            if (editableSkeleton == null ||
                skeletonPointerActive ||
                controlPointPointerActive ||
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
            if (characterControls != null &&
                pointerEvent.button == (int)ViewportPointerButton.Left &&
                TryCreatePointerRay(pointerEvent.position, out var controlRay) &&
                characterControls.BeginPointerDrag(
                    controlRay,
                    viewport.ViewRotation * Vector3.forward))
            {
                controlPointPointerActive = true;
                viewportInput.CapturePointer(pointerEvent.pointerId);
                pointerEvent.StopPropagation();
                return;
            }

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
            if (controlPointPointerActive &&
                viewportInput.HasPointerCapture(pointerEvent.pointerId))
            {
                if (TryCreatePointerRay(pointerEvent.position, out var controlRay))
                {
                    characterControls?.UpdatePointerDrag(controlRay);
                }

                pointerEvent.StopPropagation();
                return;
            }

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
            if (controlPointPointerActive)
            {
                characterControls?.EndPointerDrag();
                controlPointPointerActive = false;
            }

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
            var extensions = CollectExtensions();
            if (!WindowsModelFilePicker.TryPick(
                    extensions,
                    out var filePath,
                    out var pickerError))
            {
                if (!string.IsNullOrEmpty(pickerError))
                {
                    SetStatus("File dialog failed", StatusErrorClass, pickerError);
                    Debug.LogError($"[Body Editor] {pickerError}", this);
                }

                return;
            }

            modelLabel.text = Path.GetFileName(filePath);
            await controller.ImportAsync(filePath);
        }

        private async void PickAndImportScene()
        {
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
                    SetStatus("File dialog failed", StatusErrorClass, pickerError);
                    Debug.LogError($"[Body Editor] {pickerError}", this);
                }

                return;
            }

            modelLabel.text = Path.GetFileName(filePath);
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
            sceneImportButton?.SetEnabled(
                controller.Status != ReferenceModelImportStatus.Loading &&
                CollectExtensions(true).Count > 0);
            RefreshPhysicsState();
            RefreshTopologyState();

            switch (controller.Status)
            {
                case ReferenceModelImportStatus.Loading:
                    SetStatus("Loading...", StatusLoadingClass);
                    break;
                case ReferenceModelImportStatus.Ready:
                    modelLabel.text = controller.Current?.DisplayName ?? "Model";
                    SetStatus("Ready", StatusReadyClass);
                    break;
                case ReferenceModelImportStatus.Failed:
                    SetStatus("Import failed", StatusErrorClass, controller.Error);
                    break;
                default:
                    modelLabel.text = "No model";
                    SetStatus("Ready");
                    break;
            }
        }

        private void RefreshPhysicsState()
        {
            if (physicsToggle == null)
            {
                return;
            }

            var physics = controller.Current as IReferenceModelPhysicsController;
            var supported = physics?.SupportsPhysics == true;
            physicsToggle.SetEnabled(supported &&
                                     !presentation.TopologyMode &&
                                     controller.Status != ReferenceModelImportStatus.Loading);
            physicsToggle.SetValueWithoutNotify(supported && physics.PhysicsEnabled);
        }

        private void RefreshTopologyState()
        {
            if (topologyToggle == null)
            {
                return;
            }

            var supported = presentation.SupportsTopologyMode &&
                            controller.Status != ReferenceModelImportStatus.Loading;
            topologyToggle.SetEnabled(supported);
            topologyToggle.SetValueWithoutNotify(
                supported && presentation.TopologyMode);
            var topologyActive = supported && presentation.TopologyMode;
            SetTopologyModeButtonState(
                topologyEdgesButton,
                ReferenceTopologyDisplayMode.Edges,
                topologyActive);
            SetTopologyModeButtonState(
                topologyRingsButton,
                ReferenceTopologyDisplayMode.Rings,
                topologyActive && presentation.SupportsSectionRings);
            SetTopologyModeButtonState(
                topologyBothButton,
                ReferenceTopologyDisplayMode.Both,
                topologyActive && presentation.SupportsSectionRings);
            if (sectionRingCountField != null)
            {
                sectionRingCountField.SetEnabled(
                    topologyActive && presentation.SupportsSectionRings);
                sectionRingCountField.SetValueWithoutNotify(
                    presentation.SectionRingCount);
            }
            editableSkeleton?.SetVisible(!presentation.TopologyMode);
        }

        private void SetTopologyModeButtonState(
            Button button,
            ReferenceTopologyDisplayMode mode,
            bool enabled)
        {
            if (button == null)
            {
                return;
            }

            button.SetEnabled(enabled);
            button.EnableInClassList(
                TopologyModeSelectedClass,
                presentation.TopologyDisplayMode == mode);
        }

        private void SetStatus(
            string text,
            string modifierClass = null,
            string tooltip = null)
        {
            statusLabel.RemoveFromClassList(StatusReadyClass);
            statusLabel.RemoveFromClassList(StatusLoadingClass);
            statusLabel.RemoveFromClassList(StatusErrorClass);

            if (!string.IsNullOrEmpty(modifierClass))
            {
                statusLabel.AddToClassList(modifierClass);
            }

            statusLabel.text = text;
            statusLabel.tooltip = tooltip ?? string.Empty;
        }
    }
}
