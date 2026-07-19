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
            bodyTab = CreateTab("Body", () => ShowPanel(true));
            referenceTab = CreateTab("Reference", () => ShowPanel(false));
            tabs.Add(bodyTab);
            tabs.Add(referenceTab);
            Add(tabs);

            bodyPanel = new BodySkeletonSidebar(editableSkeleton);
            referencePanel = new ReferenceModelSidebar(
                importController,
                presentation);
            Add(bodyPanel);
            Add(referencePanel);
            ShowPanel(true);
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
            bodyPanel.style.display = showBody
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            referencePanel.style.display = showBody
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            bodyTab.EnableInClassList("editor-sidebar__tab--selected", showBody);
            referenceTab.EnableInClassList(
                "editor-sidebar__tab--selected",
                !showBody);
        }
    }
}
