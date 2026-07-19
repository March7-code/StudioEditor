using BodyEditor.ReferenceModels;
using BodyEditor.Editing;
using BodyEditor.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    internal static class BodyEditorRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeUi()
        {
            if (Object.FindAnyObjectByType<BodyEditorTopBar>() != null)
            {
                return;
            }

            var root = new GameObject("Body Editor Runtime");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Body Editor Runtime Panel";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1440, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 100f;
            panelSettings.themeStyleSheet =
                Resources.Load<ThemeStyleSheet>("BodyEditorRuntimeTheme");

            var document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = 100f;

            var controller = root.AddComponent<ReferenceModelImportController>();
            controller.RegisterAdapter(new PmxReferenceModelAdapter());
            root.AddComponent<ReferenceModelPresentationController>();
            root.AddComponent<EditableSkeletonController>();

            var lifetime = root.AddComponent<BodyEditorRuntimeLifetime>();
            lifetime.PanelSettings = panelSettings;
            root.AddComponent<BodyEditorViewport>();
            root.AddComponent<BodyEditorTopBar>();
            root.SetActive(true);
        }
    }

    internal sealed class BodyEditorRuntimeLifetime : MonoBehaviour
    {
        public PanelSettings PanelSettings { private get; set; }

        private void OnDestroy()
        {
            if (PanelSettings != null)
            {
                Destroy(PanelSettings);
            }
        }
    }
}
