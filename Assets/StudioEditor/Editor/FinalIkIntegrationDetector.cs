using StudioEditor.ReferenceModels;
using UnityEditor;

namespace StudioEditor.Editor
{
    internal static class FinalIkIntegrationDetector
    {
        private const string MenuPath =
            "Tools/Studio Editor/Check Final IK";

        [MenuItem(MenuPath)]
        private static void CheckFromMenu()
        {
            KoikatsuFinalIkRuntime.RefreshAvailability();
            var available = KoikatsuFinalIkRuntime.TryGetStatus(
                out var status);
            EditorUtility.DisplayDialog(
                available ? "Final IK Available" : "Final IK Unavailable",
                status,
                "OK");
        }
    }
}
