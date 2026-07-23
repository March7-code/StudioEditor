using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using BodyEditor.ReferenceModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    internal sealed class BodyEditorSidebar : VisualElement
    {
        private static readonly List<string> FilterChoices = new List<string>
        {
            "All",
            "Characters",
            "Objects",
            "Lights",
            "Cameras",
            "Collections",
        };

        private readonly SceneContentController importController;
        private readonly Action<bool> collapseChanged;
        private readonly Action<Camera> cameraActivated;
        private readonly Action<IReferenceSceneNode> selectionChanged;
        private readonly HashSet<string> expandedNodes =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly VisualElement header;
        private readonly Label title;
        private readonly TextField searchField;
        private readonly PopupField<string> filterField;
        private readonly VisualElement filters;
        private readonly ScrollView tree;
        private readonly Label objectCountLabel;
        private readonly Label selectionLabel;
        private readonly VisualElement footer;
        private readonly Button collapseButton;

        private IReferenceModelInstance currentImport;
        private IReferenceSceneNode sceneRoot;
        private IReferenceSceneNode selectedNode;
        private bool collapsed;

        public BodyEditorSidebar(
            SceneContentController importController,
            Action<bool> collapseChanged = null,
            Action<Camera> cameraActivated = null,
            Action<IReferenceSceneNode> selectionChanged = null)
        {
            this.importController = importController ??
                throw new ArgumentNullException(nameof(importController));
            this.collapseChanged = collapseChanged;
            this.cameraActivated = cameraActivated;
            this.selectionChanged = selectionChanged;

            name = "body-editor-sidebar";
            pickingMode = PickingMode.Position;
            AddToClassList("editor-sidebar");
            AddToClassList("scene-outliner");

            header = new VisualElement();
            header.AddToClassList("scene-outliner__header");
            title = new Label("Scene");
            title.AddToClassList("scene-outliner__title");
            header.Add(title);
            objectCountLabel = new Label("0 objects");
            objectCountLabel.AddToClassList("scene-outliner__count");
            header.Add(objectCountLabel);
            collapseButton = new Button(ToggleCollapsed)
            {
                text = ">",
                tooltip = "Collapse scene outliner",
            };
            collapseButton.AddToClassList("scene-outliner__collapse");
            header.Add(collapseButton);
            Add(header);

            filters = new VisualElement();
            filters.AddToClassList("scene-outliner__filters");
            searchField = new TextField("Filter")
            {
                tooltip = "Filter scene objects by name",
            };
            searchField.AddToClassList("scene-outliner__search");
            searchField.RegisterValueChangedCallback(HandleSearchChanged);
            filters.Add(searchField);

            filterField = new PopupField<string>(FilterChoices, 0)
            {
                tooltip = "Filter scene objects by type",
            };
            filterField.AddToClassList("scene-outliner__type-filter");
            filterField.RegisterValueChangedCallback(HandleFilterChanged);
            filters.Add(filterField);
            Add(filters);

            tree = new ScrollView(ScrollViewMode.Vertical);
            tree.AddToClassList("scene-outliner__tree");
            Add(tree);

            footer = new VisualElement();
            footer.AddToClassList("scene-outliner__footer");
            selectionLabel = new Label("Nothing selected");
            selectionLabel.AddToClassList("scene-outliner__selection");
            footer.Add(selectionLabel);
            Add(footer);

            importController.StateChanged += Refresh;
            RegisterCallback<DetachFromPanelEvent>(HandleDetached);
            Refresh();
        }

        private void ToggleCollapsed()
        {
            collapsed = !collapsed;
            EnableInClassList("scene-outliner--collapsed", collapsed);
            title.style.display = collapsed
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            objectCountLabel.style.display = collapsed
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            filters.style.display = collapsed
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            tree.style.display = collapsed
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            footer.style.display = collapsed
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            collapseButton.text = collapsed ? "<" : ">";
            collapseButton.tooltip = collapsed
                ? "Expand scene outliner"
                : "Collapse scene outliner";
            collapseChanged?.Invoke(collapsed);
        }

        private void HandleSearchChanged(ChangeEvent<string> changeEvent)
        {
            RebuildTree();
        }

        private void HandleFilterChanged(ChangeEvent<string> changeEvent)
        {
            RebuildTree();
        }

        private void Refresh()
        {
            var nextImport = importController.Current;
            var nextHierarchy = importController.SceneHierarchy;
            if (!ReferenceEquals(currentImport, nextImport) ||
                !ReferenceEquals(sceneRoot, nextHierarchy))
            {
                currentImport = nextImport;
                sceneRoot = nextHierarchy;
                selectedNode = null;
                selectionChanged?.Invoke(null);
                expandedNodes.Clear();
                if (sceneRoot != null)
                {
                    expandedNodes.Add(sceneRoot.Id);
                }
            }

            objectCountLabel.text = sceneRoot == null
                ? "0 objects"
                : $"{CountObjects(sceneRoot)} objects";
            RefreshSelection();
            RebuildTree();
        }

        private void RebuildTree()
        {
            tree.Clear();
            if (sceneRoot == null)
            {
                var empty = new Label("No objects in the scene");
                empty.AddToClassList("scene-outliner__empty");
                tree.Add(empty);
                return;
            }

            var cameras = new List<IReferenceSceneNode>();
            CollectCameras(sceneRoot, cameras);
            for (var index = 0; index < cameras.Count; index++)
            {
                if (Matches(cameras[index]))
                {
                    AddNode(cameras[index], 0, false, false, false);
                }
            }

            AddNode(sceneRoot, 0, true, true, true);
        }

        private void AddNode(
            IReferenceSceneNode node,
            int depth,
            bool forceVisible = false,
            bool skipCameras = false,
            bool includeChildren = true)
        {
            if (skipCameras &&
                node.Kind == ReferenceSceneObjectKind.Camera)
            {
                return;
            }

            if (!forceVisible && !ContainsMatch(node, skipCameras))
            {
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("scene-outliner__row");
            row.EnableInClassList(
                "scene-outliner__row--selected",
                ReferenceEquals(selectedNode, node));

            var indent = new VisualElement();
            indent.AddToClassList("scene-outliner__indent");
            indent.style.width = depth * 14;
            row.Add(indent);

            var hasChildren = node.Children.Count > 0;
            var expanded = expandedNodes.Contains(node.Id);
            var disclosure = new Button(() => ToggleExpanded(node))
            {
                text = hasChildren ? expanded ? "v" : ">" : string.Empty,
                tooltip = hasChildren
                    ? expanded ? "Collapse" : "Expand"
                    : string.Empty,
            };
            disclosure.AddToClassList("scene-outliner__disclosure");
            disclosure.SetEnabled(hasChildren);
            row.Add(disclosure);

            var kind = new Label(GetKindLabel(node.Kind));
            kind.AddToClassList("scene-outliner__kind");
            kind.AddToClassList(
                $"scene-outliner__kind--{GetKindClass(node.Kind)}");
            row.Add(kind);

            var name = new Button(() => SelectNode(node))
            {
                text = string.IsNullOrWhiteSpace(node.DisplayName)
                    ? node.Kind.ToString()
                    : node.DisplayName,
                tooltip = node.DisplayName,
            };
            name.AddToClassList("scene-outliner__name");
            row.Add(name);

            if (node.Kind == ReferenceSceneObjectKind.Camera)
            {
                AddCameraSelector(row, node);
            }
            else
            {
                var visibility = new Toggle
                {
                    tooltip = node.Root != null
                        ? "Show or hide this object"
                        : "This scene group has no visibility state",
                };
                visibility.AddToClassList("scene-outliner__visibility");
                visibility.SetValueWithoutNotify(node.IsVisible);
                visibility.SetEnabled(node.Root != null);
                visibility.RegisterValueChangedCallback(
                    changeEvent => node.SetVisible(changeEvent.newValue));
                row.Add(visibility);
            }

            tree.Add(row);

            var revealChildren = includeChildren && hasChildren &&
                                 (expanded || IsFiltering);
            if (!revealChildren)
            {
                return;
            }

            for (var index = 0; index < node.Children.Count; index++)
            {
                AddNode(
                    node.Children[index],
                    depth + 1,
                    false,
                    skipCameras,
                    true);
            }
        }

        private void AddCameraSelector(
            VisualElement row,
            IReferenceSceneNode node)
        {
            var provider = currentImport as IReferenceSceneCameraProvider;
            var radio = new RadioButton
            {
                tooltip = provider != null
                    ? "Use this camera as the main view"
                    : "This camera cannot control the main view",
            };
            radio.AddToClassList("scene-outliner__camera-radio");
            radio.SetValueWithoutNotify(
                provider != null &&
                string.Equals(
                    provider.ActiveCameraId,
                    node.Id,
                    StringComparison.Ordinal));
            radio.SetEnabled(provider != null);
            radio.RegisterValueChangedCallback(changeEvent =>
            {
                if (!changeEvent.newValue)
                {
                    radio.SetValueWithoutNotify(
                        provider != null &&
                        string.Equals(
                            provider.ActiveCameraId,
                            node.Id,
                            StringComparison.Ordinal));
                    return;
                }

                if (provider == null ||
                    !provider.TryActivateCamera(node.Id, out var camera))
                {
                    radio.SetValueWithoutNotify(false);
                    return;
                }

                cameraActivated?.Invoke(camera);
                RebuildTree();
            });
            row.Add(radio);
        }

        private static void CollectCameras(
            IReferenceSceneNode node,
            ICollection<IReferenceSceneNode> cameras)
        {
            if (node.Kind == ReferenceSceneObjectKind.Camera)
            {
                cameras.Add(node);
            }

            for (var index = 0; index < node.Children.Count; index++)
            {
                CollectCameras(node.Children[index], cameras);
            }
        }

        private bool ContainsMatch(
            IReferenceSceneNode node,
            bool skipCameras = false)
        {
            if (skipCameras &&
                node.Kind == ReferenceSceneObjectKind.Camera)
            {
                return false;
            }

            if (Matches(node))
            {
                return true;
            }

            for (var index = 0; index < node.Children.Count; index++)
            {
                if (ContainsMatch(node.Children[index], skipCameras))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Matches(IReferenceSceneNode node)
        {
            var query = searchField.value?.Trim();
            if (!string.IsNullOrEmpty(query) &&
                (node.DisplayName == null ||
                 node.DisplayName.IndexOf(
                     query,
                     StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            switch (filterField.value)
            {
                case "Characters":
                    return node.Kind == ReferenceSceneObjectKind.Character;
                case "Objects":
                    return node.Kind == ReferenceSceneObjectKind.Object;
                case "Lights":
                    return node.Kind == ReferenceSceneObjectKind.Light;
                case "Cameras":
                    return node.Kind == ReferenceSceneObjectKind.Camera;
                case "Collections":
                    return node.Kind == ReferenceSceneObjectKind.Collection;
                default:
                    return true;
            }
        }

        private bool IsFiltering =>
            !string.IsNullOrWhiteSpace(searchField.value) ||
            !string.Equals(
                filterField.value,
                "All",
                StringComparison.Ordinal);

        private void ToggleExpanded(IReferenceSceneNode node)
        {
            if (!expandedNodes.Add(node.Id))
            {
                expandedNodes.Remove(node.Id);
            }

            RebuildTree();
        }

        private void SelectNode(IReferenceSceneNode node)
        {
            selectedNode = node;
            selectionChanged?.Invoke(node);
            RefreshSelection();
            RebuildTree();
        }

        private void RefreshSelection()
        {
            if (selectedNode == null)
            {
                selectionLabel.text = "Nothing selected";
                selectionLabel.tooltip = string.Empty;
                return;
            }

            selectionLabel.text =
                $"{selectedNode.Kind}  {selectedNode.DisplayName}";
            selectionLabel.tooltip = selectedNode.DisplayName;
        }

        private static int CountObjects(IReferenceSceneNode node)
        {
            var count = node.Kind == ReferenceSceneObjectKind.Scene ? 0 : 1;
            for (var index = 0; index < node.Children.Count; index++)
            {
                count += CountObjects(node.Children[index]);
            }

            return count;
        }

        private static string GetKindLabel(ReferenceSceneObjectKind kind)
        {
            switch (kind)
            {
                case ReferenceSceneObjectKind.Scene:
                    return "SCN";
                case ReferenceSceneObjectKind.Character:
                    return "CHR";
                case ReferenceSceneObjectKind.Light:
                    return "LGT";
                case ReferenceSceneObjectKind.Camera:
                    return "CAM";
                case ReferenceSceneObjectKind.Collection:
                    return "COL";
                default:
                    return "OBJ";
            }
        }

        private static string GetKindClass(ReferenceSceneObjectKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }

        private void HandleDetached(DetachFromPanelEvent detachEvent)
        {
            importController.StateChanged -= Refresh;
        }
    }
}
