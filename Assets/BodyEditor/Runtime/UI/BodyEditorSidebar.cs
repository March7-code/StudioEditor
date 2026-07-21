using BodyEditor.Editing;
using BodyEditor.ReferenceModels;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    internal sealed class BodyEditorSidebar : VisualElement
    {
        private readonly Button bodyTab;
        private readonly Button referenceTab;
        private readonly VisualElement bodyPanel;
        private readonly VisualElement referencePanel;

        public BodyEditorSidebar(
            EditableSkeletonController editableSkeleton,
            ReferenceModelImportController importController,
            ReferenceModelPresentationController presentation)
        {
            name = "body-editor-sidebar";
            AddToClassList("editor-sidebar");

            var tabs = new VisualElement();
            tabs.AddToClassList("editor-sidebar__tabs");
            referenceTab = CreateTab("Reference", () => ShowPanel(false));

            if (editableSkeleton != null)
            {
                bodyTab = CreateTab("Body", () => ShowPanel(true));
                tabs.Add(bodyTab);
                bodyPanel = new BodySkeletonSidebar(editableSkeleton);
            }
            else
            {
                bodyTab = null;
                bodyPanel = null;
            }

            tabs.Add(referenceTab);
            Add(tabs);

            referencePanel = new ReferenceModelSidebar(
                importController,
                presentation);
            if (bodyPanel != null)
            {
                Add(bodyPanel);
            }

            Add(referencePanel);
            ShowPanel(editableSkeleton != null);
        }

        private static Button CreateTab(string text, System.Action action)
        {
            var button = new Button(action)
            {
                text = text,
            };
            button.AddToClassList("editor-sidebar__tab");
            return button;
        }

        private void ShowPanel(bool showBody)
        {
            var displayBody = showBody && bodyPanel != null;
            if (bodyPanel != null)
            {
                bodyPanel.style.display = displayBody
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            referencePanel.style.display = displayBody
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            if (bodyTab != null)
            {
                bodyTab.EnableInClassList(
                    "editor-sidebar__tab--selected",
                    displayBody);
            }

            referenceTab.EnableInClassList(
                "editor-sidebar__tab--selected",
                !displayBody);
        }
    }
}
