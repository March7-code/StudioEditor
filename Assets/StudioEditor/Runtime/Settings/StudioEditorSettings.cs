using System;
using System.IO;
using StudioEditor.Viewport;
using UnityEngine;

namespace StudioEditor.Settings
{
    public static class StudioEditorSettings
    {
        private const string KeyPrefix = "StudioEditor.Settings.";
        private const string KoikatsuRootKey = KeyPrefix + "KoikatsuRoot";
        private const string UiScaleKey = KeyPrefix + "UiScale";
        private const string OrbitButtonKey = KeyPrefix + "OrbitButton";
        private const string PanButtonKey = KeyPrefix + "PanButton";
        private const string KoikatsuConfigResource = "KoikatsuAdapterConfig";

        private static bool loaded;
        private static bool hasKoikatsuRootOverride;
        private static string koikatsuGameRoot;
        private static float uiScale;
        private static ViewportPointerButton orbitButton;
        private static ViewportPointerButton panButton;

        public static event Action Changed;

        public static bool HasKoikatsuGameRootOverride
        {
            get
            {
                EnsureLoaded();
                return hasKoikatsuRootOverride;
            }
        }

        public static string KoikatsuGameRoot
        {
            get
            {
                EnsureLoaded();
                return koikatsuGameRoot;
            }
        }

        public static float UiScale
        {
            get
            {
                EnsureLoaded();
                return uiScale;
            }
        }

        public static ViewportPointerButton OrbitButton
        {
            get
            {
                EnsureLoaded();
                return orbitButton;
            }
        }

        public static ViewportPointerButton PanButton
        {
            get
            {
                EnsureLoaded();
                return panButton;
            }
        }

        public static bool TrySetKoikatsuGameRoot(
            string value,
            out string error)
        {
            EnsureLoaded();
            error = string.Empty;
            var normalized = string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    normalized = Path.GetFullPath(value.Trim().Trim('"'));
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }

                if (!Directory.Exists(normalized))
                {
                    error = "Directory not found";
                    return false;
                }

                if (!Directory.Exists(Path.Combine(normalized, "abdata")))
                {
                    error = "The selected directory does not contain abdata";
                    return false;
                }
            }

            hasKoikatsuRootOverride = true;
            if (string.Equals(
                    koikatsuGameRoot,
                    normalized,
                    StringComparison.OrdinalIgnoreCase) &&
                PlayerPrefs.HasKey(KoikatsuRootKey))
            {
                return true;
            }

            koikatsuGameRoot = normalized;
            PlayerPrefs.SetString(KoikatsuRootKey, koikatsuGameRoot);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        public static void SetUiScale(float value)
        {
            EnsureLoaded();
            value = Mathf.Round(Mathf.Clamp(value, 0.75f, 1.5f) * 20f) / 20f;
            if (Mathf.Approximately(uiScale, value))
            {
                return;
            }

            uiScale = value;
            PlayerPrefs.SetFloat(UiScaleKey, uiScale);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void SetOrbitButton(ViewportPointerButton value)
        {
            EnsureLoaded();
            if (orbitButton == value)
            {
                return;
            }

            var previous = orbitButton;
            orbitButton = value;
            if (panButton == orbitButton)
            {
                panButton = previous;
            }

            SavePointerButtons();
        }

        public static void SetPanButton(ViewportPointerButton value)
        {
            EnsureLoaded();
            if (panButton == value)
            {
                return;
            }

            var previous = panButton;
            panButton = value;
            if (orbitButton == panButton)
            {
                orbitButton = previous;
            }

            SavePointerButtons();
        }

        public static void ApplyTo(ViewportControlSettings target)
        {
            if (target == null)
            {
                return;
            }

            EnsureLoaded();
            target.OrbitButton = orbitButton;
            target.PanButton = panButton;
        }

        private static void SavePointerButtons()
        {
            PlayerPrefs.SetInt(OrbitButtonKey, (int)orbitButton);
            PlayerPrefs.SetInt(PanButtonKey, (int)panButton);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            hasKoikatsuRootOverride = PlayerPrefs.HasKey(KoikatsuRootKey);
            koikatsuGameRoot = hasKoikatsuRootOverride
                ? PlayerPrefs.GetString(KoikatsuRootKey, string.Empty)
                : ReadProjectKoikatsuRoot();
            uiScale = Mathf.Round(Mathf.Clamp(
                PlayerPrefs.GetFloat(UiScaleKey, 1f),
                0.75f,
                1.5f) * 20f) / 20f;
            orbitButton = ReadPointerButton(
                OrbitButtonKey,
                ViewportPointerButton.Right);
            panButton = ReadPointerButton(
                PanButtonKey,
                ViewportPointerButton.Middle);
            if (orbitButton == panButton)
            {
                panButton = orbitButton == ViewportPointerButton.Middle
                    ? ViewportPointerButton.Right
                    : ViewportPointerButton.Middle;
            }
        }

        private static ViewportPointerButton ReadPointerButton(
            string key,
            ViewportPointerButton fallback)
        {
            var value = PlayerPrefs.GetInt(key, (int)fallback);
            return Enum.IsDefined(typeof(ViewportPointerButton), value)
                ? (ViewportPointerButton)value
                : fallback;
        }

        private static string ReadProjectKoikatsuRoot()
        {
            var asset = Resources.Load<TextAsset>(KoikatsuConfigResource);
            if (asset == null)
            {
                return string.Empty;
            }

            var table = JsonUtility.FromJson<KoikatsuConfigurationTable>(
                asset.text);
            if (table?.gameRoots == null)
            {
                return string.Empty;
            }

            for (var index = 0; index < table.gameRoots.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(table.gameRoots[index]))
                {
                    return table.gameRoots[index].Trim();
                }
            }

            return string.Empty;
        }

        [Serializable]
        private sealed class KoikatsuConfigurationTable
        {
            public string[] gameRoots = Array.Empty<string>();
        }
    }
}
