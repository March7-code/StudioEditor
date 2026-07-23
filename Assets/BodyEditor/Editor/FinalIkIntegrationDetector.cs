using System;
using BodyEditor.ReferenceModels;
using UnityEditor;
using UnityEngine;

namespace BodyEditor.Editor
{
    [InitializeOnLoad]
    internal static class FinalIkIntegrationDetector
    {
        private const string MenuPath =
            "Tools/Body Editor/Integrations/Check Final IK";
        private const string SessionStatusKey =
            "BodyEditor.FinalIkIntegration.Status";

        private static bool refreshQueued;

        static FinalIkIntegrationDetector()
        {
            QueueRefresh();
        }

        [MenuItem(MenuPath)]
        private static void CheckFromMenu()
        {
            Refresh(true);
        }

        internal static void QueueRefresh()
        {
            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            EditorApplication.delayCall += RefreshDelayed;
        }

        private static void RefreshDelayed()
        {
            refreshQueued = false;
            Refresh(false);
        }

        private static void Refresh(bool requestedByUser)
        {
            KoikatsuFinalIkRuntime.RefreshAvailability();
            var available = KoikatsuFinalIkRuntime.TryGetStatus(
                out var status);
            Menu.SetChecked(MenuPath, available);

            var previous = SessionState.GetString(SessionStatusKey, string.Empty);
            SessionState.SetString(SessionStatusKey, status);
            if (requestedByUser ||
                available && !string.Equals(
                    previous,
                    status,
                    StringComparison.Ordinal))
            {
                Debug.Log("[Body Editor] " + status);
            }
            else if (!available &&
                     !status.StartsWith(
                         "Final IK is not installed",
                         StringComparison.Ordinal) &&
                     !string.Equals(previous, status, StringComparison.Ordinal))
            {
                Debug.LogWarning("[Body Editor] " + status);
            }
        }
    }

    internal sealed class FinalIkIntegrationAssetPostprocessor :
        AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            if (didDomainReload ||
                ContainsFinalIkAsset(importedAssets) ||
                ContainsFinalIkAsset(deletedAssets) ||
                ContainsFinalIkAsset(movedAssets) ||
                ContainsFinalIkAsset(movedFromAssetPaths))
            {
                FinalIkIntegrationDetector.QueueRefresh();
            }
        }

        private static bool ContainsFinalIkAsset(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                if (!string.IsNullOrEmpty(path) &&
                    (path.IndexOf(
                         "/RootMotion/",
                         StringComparison.OrdinalIgnoreCase) >= 0 ||
                     path.IndexOf(
                         "FinalIK",
                         StringComparison.OrdinalIgnoreCase) >= 0 ||
                     path.IndexOf(
                         "Final IK",
                         StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
