using System;
using StudioEditor.ReferenceModels;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal sealed class ModelLoadProgressPanel : VisualElement
    {
        private readonly SceneContentController controller;
        private readonly Label stage;
        private readonly Label detail;
        private readonly ProgressBar bar;
        private readonly Button cancel;

        public ModelLoadProgressPanel(SceneContentController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            name = "studio-editor-load-progress";
            AddToClassList("model-load-progress");
            stage = new Label("Preparing");
            stage.AddToClassList("model-load-progress__stage");
            Add(stage);
            bar = new ProgressBar { lowValue = 0f, highValue = 100f };
            Add(bar);
            detail = new Label();
            detail.AddToClassList("model-load-progress__detail");
            Add(detail);
            cancel = new Button(() =>
            {
                controller.CancelLoad();
                stage.text = "Cancelling…";
                cancel.SetEnabled(false);
            }) { text = "Cancel" };
            Add(cancel);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                controller.StateChanged += Refresh;
                controller.LoadProgressChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                controller.StateChanged -= Refresh;
                controller.LoadProgressChanged -= Refresh;
            });
            Refresh();
        }

        private void Refresh()
        {
            var loading = controller.Status == ReferenceModelImportStatus.Loading;
            style.display = loading ? DisplayStyle.Flex : DisplayStyle.None;
            if (!loading) { cancel.SetEnabled(true); return; }
            var progress = controller.LoadProgress;
            if (cancel.enabledSelf) stage.text = progress.Stage;
            bar.value = progress.Fraction * 100f;
            bar.title = $"{bar.value:F0}%";
            detail.text = progress.Detail;
        }
    }
}
