using System;
using System.Collections.Generic;
using BodyEditor.ReferenceModels;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    internal sealed class ReferenceModelSidebar : VisualElement
    {
        private readonly ReferenceModelImportController importController;
        private readonly ReferenceModelPresentationController presentation;
        private readonly Label modelName;
        private readonly Label sourceName;
        private readonly VisualElement variantRow;
        private readonly Label variantLabel;
        private readonly DropdownField variantField;
        private readonly Toggle bodyBonesOnly;
        private readonly ScrollView tree;
        private GroupView meshGroup;
        private GroupView skeletonGroup;
        private int displayedRevision = -1;
        private bool updatingVariant;

        public ReferenceModelSidebar(
            ReferenceModelImportController importController,
            ReferenceModelPresentationController presentation)
        {
            this.importController = importController;
            this.presentation = presentation;
            name = "reference-model-sidebar";
            AddToClassList("reference-sidebar");

            var heading = new Label("Reference Model");
            heading.AddToClassList("reference-sidebar__heading");
            Add(heading);

            modelName = new Label("No model");
            modelName.AddToClassList("reference-sidebar__model");
            Add(modelName);

            var sourceRow = new VisualElement();
            sourceRow.AddToClassList("reference-sidebar__metadata-row");
            var sourceLabel = new Label("Source");
            sourceLabel.AddToClassList("reference-sidebar__metadata-label");
            sourceRow.Add(sourceLabel);
            sourceName = new Label("-");
            sourceName.AddToClassList("reference-sidebar__metadata-value");
            sourceRow.Add(sourceName);
            Add(sourceRow);

            variantRow = new VisualElement();
            variantRow.AddToClassList("reference-sidebar__metadata-row");
            variantLabel = new Label("Outfit");
            variantLabel.AddToClassList("reference-sidebar__metadata-label");
            variantRow.Add(variantLabel);
            variantField = new DropdownField
            {
                choices = new List<string>(),
            };
            variantField.AddToClassList("reference-sidebar__variant");
            variantField.RegisterValueChangedCallback(HandleVariantChanged);
            variantRow.Add(variantField);
            Add(variantRow);

            bodyBonesOnly = new Toggle("Body bones only")
            {
                tooltip = "Show only the configured body-reference skeleton",
            };
            bodyBonesOnly.AddToClassList("reference-sidebar__body-filter");
            bodyBonesOnly.RegisterValueChangedCallback(
                value => presentation.SetBodyBonesOnly(value.newValue));
            Add(bodyBonesOnly);

            var columns = new VisualElement();
            columns.AddToClassList("reference-sidebar__columns");
            columns.Add(CreateColumnLabel("Item", "reference-sidebar__column--item"));
            columns.Add(CreateColumnLabel("Highlight", "reference-sidebar__column--highlight"));
            columns.Add(CreateColumnLabel("Visible", "reference-sidebar__column--visible"));
            Add(columns);

            tree = new ScrollView(ScrollViewMode.Vertical);
            tree.AddToClassList("reference-sidebar__tree");
            Add(tree);

            presentation.StateChanged += Refresh;
            importController.StateChanged += Refresh;
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            Refresh();
        }

        private void Refresh()
        {
            modelName.text = presentation.HasModel
                ? importController.Current?.DisplayName ?? "Reference model"
                : "No model";
            sourceName.text = presentation.HasModel &&
                              !string.IsNullOrEmpty(
                                  importController.CurrentFormatName)
                ? importController.CurrentFormatName
                : "-";
            RefreshVariant();
            bodyBonesOnly.SetEnabled(
                presentation.HasModel && presentation.SupportsBodyBoneView);
            bodyBonesOnly.SetValueWithoutNotify(presentation.BodyBonesOnly);

            if (displayedRevision != presentation.Revision)
            {
                RebuildTree();
                displayedRevision = presentation.Revision;
            }

            UpdateGroup(
                meshGroup,
                presentation.MeshHighlighted,
                presentation.MeshHighlightMixed,
                presentation.MeshVisible,
                presentation.MeshVisibilityMixed,
                presentation.MeshItems,
                false);
            UpdateGroup(
                skeletonGroup,
                presentation.SkeletonHighlighted,
                presentation.SkeletonHighlightMixed,
                presentation.SkeletonVisible,
                presentation.SkeletonVisibilityMixed,
                presentation.BoneItems,
                presentation.BodyBonesOnly);
            meshGroup.Root.SetEnabled(presentation.HasModel);
            skeletonGroup.Root.SetEnabled(presentation.HasModel);
        }

        private void RefreshVariant()
        {
            var variants = importController.Current as
                IReferenceModelVariantProvider;
            var visible = presentation.HasModel &&
                          variants != null &&
                          variants.VariantNames.Count != 0;
            variantRow.style.display = visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            variantLabel.text = string.IsNullOrEmpty(variants.VariantLabel)
                ? "Variant"
                : variants.VariantLabel;
            var choices = new List<string>(variants.VariantNames);
            var index = variants.ActiveVariantIndex;
            if (index < 0 || index >= choices.Count)
            {
                index = 0;
            }

            updatingVariant = true;
            variantField.choices = choices;
            variantField.SetValueWithoutNotify(choices[index]);
            updatingVariant = false;
            variantField.SetEnabled(
                importController.Status != ReferenceModelImportStatus.Loading);
        }

        private async void HandleVariantChanged(ChangeEvent<string> changeEvent)
        {
            if (updatingVariant ||
                importController.Status == ReferenceModelImportStatus.Loading)
            {
                return;
            }

            var index = variantField.choices.IndexOf(changeEvent.newValue);
            if (index >= 0)
            {
                await importController.SelectVariantAsync(index);
            }
        }

        private void RebuildTree()
        {
            var meshExpanded = meshGroup?.Expanded ?? false;
            var skeletonExpanded = skeletonGroup?.Expanded ?? false;
            tree.Clear();

            meshGroup = CreateGroup(
                "Mesh",
                presentation.MeshItems,
                meshExpanded,
                presentation.SetMeshHighlighted,
                presentation.SetMeshVisible,
                presentation.SetMeshItemHighlighted,
                presentation.SetMeshItemVisible);
            skeletonGroup = CreateGroup(
                "Skeleton",
                presentation.BoneItems,
                skeletonExpanded,
                presentation.SetSkeletonHighlighted,
                presentation.SetSkeletonVisible,
                presentation.SetBoneItemHighlighted,
                presentation.SetBoneItemVisible);
            tree.Add(meshGroup.Root);
            tree.Add(skeletonGroup.Root);
        }

        private static GroupView CreateGroup(
            string title,
            IReadOnlyList<ReferenceModelPartState> items,
            bool expanded,
            Action<bool> setAllHighlighted,
            Action<bool> setAllVisible,
            Action<int, bool> setItemHighlighted,
            Action<int, bool> setItemVisible)
        {
            var group = new GroupView(title, items.Count, expanded);
            group.Highlight.RegisterValueChangedCallback(
                value => setAllHighlighted(value.newValue));
            group.Visible.RegisterValueChangedCallback(
                value => setAllVisible(value.newValue));

            for (var index = 0; index < items.Count; index++)
            {
                var itemIndex = index;
                var item = items[index];
                var row = new PartRow(item.Name, item.Path);
                row.Highlight.RegisterValueChangedCallback(
                    value => setItemHighlighted(itemIndex, value.newValue));
                row.Visible.RegisterValueChangedCallback(
                    value => setItemVisible(itemIndex, value.newValue));
                group.Items.Add(row);
                group.Children.Add(row.Root);
            }

            return group;
        }

        private static void UpdateGroup(
            GroupView group,
            bool highlighted,
            bool highlightMixed,
            bool visible,
            bool visibilityMixed,
            IReadOnlyList<ReferenceModelPartState> states,
            bool bodyBonesOnly)
        {
            group.Count.text = states.Count.ToString();
            SetToggle(group.Highlight, highlighted, highlightMixed);
            SetToggle(group.Visible, visible, visibilityMixed);
            for (var index = 0; index < group.Items.Count && index < states.Count; index++)
            {
                SetToggle(group.Items[index].Highlight, states[index].Highlighted, false);
                SetToggle(group.Items[index].Visible, states[index].Visible, false);
                group.Items[index].Root.SetEnabled(
                    !bodyBonesOnly || states[index].IsBodyBone);
            }
        }

        private static void SetToggle(Toggle toggle, bool value, bool mixed)
        {
            toggle.SetValueWithoutNotify(value);
            toggle.showMixedValue = mixed;
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            presentation.StateChanged -= Refresh;
            importController.StateChanged -= Refresh;
            variantField.UnregisterValueChangedCallback(HandleVariantChanged);
        }

        private static Toggle CreateToggle(string columnClass)
        {
            var toggle = new Toggle();
            toggle.AddToClassList("reference-sidebar__toggle");
            toggle.AddToClassList(columnClass);
            return toggle;
        }

        private static Label CreateColumnLabel(string text, string columnClass)
        {
            var label = new Label(text);
            label.AddToClassList("reference-sidebar__column");
            label.AddToClassList(columnClass);
            return label;
        }

        private sealed class GroupView
        {
            private readonly Button disclosure;

            public GroupView(string title, int count, bool expanded)
            {
                Root = new VisualElement();
                Root.AddToClassList("reference-sidebar__group");

                var header = new VisualElement();
                header.AddToClassList("reference-sidebar__row");
                var item = new VisualElement();
                item.AddToClassList("reference-sidebar__item");
                disclosure = new Button(ToggleExpanded)
                {
                    tooltip = "Expand or collapse",
                };
                disclosure.AddToClassList("reference-sidebar__disclosure");
                var name = new Label(title);
                name.AddToClassList("reference-sidebar__item-name");
                Count = new Label(count.ToString());
                Count.AddToClassList("reference-sidebar__count");
                item.Add(disclosure);
                item.Add(name);
                item.Add(Count);
                header.Add(item);

                Highlight = CreateToggle("reference-sidebar__toggle--highlight");
                Visible = CreateToggle("reference-sidebar__toggle--visible");
                Highlight.tooltip = "Highlight all";
                Visible.tooltip = "Show all";
                header.Add(Highlight);
                header.Add(Visible);

                Children = new VisualElement();
                Children.AddToClassList("reference-sidebar__children");
                Root.Add(header);
                Root.Add(Children);
                SetExpanded(expanded);
            }

            public VisualElement Root { get; }
            public VisualElement Children { get; }
            public Label Count { get; }
            public Toggle Highlight { get; }
            public Toggle Visible { get; }
            public List<PartRow> Items { get; } = new List<PartRow>();
            public bool Expanded { get; private set; }

            private void ToggleExpanded()
            {
                SetExpanded(!Expanded);
            }

            private void SetExpanded(bool expanded)
            {
                Expanded = expanded;
                disclosure.text = expanded ? "v" : ">";
                Children.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private sealed class PartRow
        {
            public PartRow(string name, string path)
            {
                Root = new VisualElement();
                Root.AddToClassList("reference-sidebar__row");
                Root.AddToClassList("reference-sidebar__row--child");

                var label = new Label(name)
                {
                    tooltip = path,
                };
                label.AddToClassList("reference-sidebar__child-name");
                Root.Add(label);

                Highlight = CreateToggle("reference-sidebar__toggle--highlight");
                Visible = CreateToggle("reference-sidebar__toggle--visible");
                Highlight.tooltip = "Highlight item";
                Visible.tooltip = "Show item";
                Root.Add(Highlight);
                Root.Add(Visible);
            }

            public VisualElement Root { get; }
            public Toggle Highlight { get; }
            public Toggle Visible { get; }
        }
    }
}
