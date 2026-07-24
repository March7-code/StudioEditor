using StudioEditor.Characters.Legacy;
using StudioEditor.Editing;
using StudioEditor.ReferenceModels;
using StudioEditor.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal static class StudioEditorRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeUi()
        {
            if (Object.FindAnyObjectByType<StudioEditorTopBar>() != null)
            {
                return;
            }

            var root = new GameObject("Studio Editor Runtime");
            root.SetActive(false);
            Object.DontDestroyOnLoad(root);

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Studio Editor Runtime Panel";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1440, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 100f;
            panelSettings.themeStyleSheet =
                Resources.Load<ThemeStyleSheet>("StudioEditorRuntimeTheme");

            var document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = 100f;

            var controller = root.AddComponent<SceneContentController>();
            var adapters = ReferenceModelAdapterRegistry.CreateAdapters();
            for (var index = 0; index < adapters.Count; index++)
            {
                controller.RegisterAdapter(adapters[index]);
            }
            root.AddComponent<ReferenceModelPresentationController>();
            root.AddComponent<LegacyCharacterModelBridge>();
            root.AddComponent<CharacterControlPointController>();
            root.AddComponent<CharacterBodyConstraintController>();
            root.AddComponent<TimelineCaptureController>();
            root.AddComponent<ScreenshotCaptureController>();
            root.AddComponent<SceneTimelineController>();

            var lifetime = root.AddComponent<StudioEditorRuntimeLifetime>();
            lifetime.PanelSettings = panelSettings;
            root.AddComponent<StudioEditorViewport>();
            root.AddComponent<StudioEditorTopBar>();
            root.SetActive(true);
        }
    }

    internal sealed class StudioEditorRuntimeLifetime : MonoBehaviour
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
