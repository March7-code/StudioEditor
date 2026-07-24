using System;
using System.Collections;
using System.IO;
using StudioEditor.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class ScreenshotCaptureController : MonoBehaviour
    {
        public const int MinimumDimension = 16;
        public const int MaximumDimension = 8192;
        private const long MaximumPixelCount = 64000000;
        private static readonly WaitForEndOfFrame EndOfFrame =
            new WaitForEndOfFrame();

        private Coroutine routine;

        public event Action StateChanged;

        public string Status { get; private set; } = "Ready to take a screenshot.";

        public string OutputPath { get; private set; } = string.Empty;

        public bool IsCapturing => routine != null;

        public bool Capture(int width, int height)
        {
            if (IsCapturing)
            {
                return false;
            }

            if (!ValidateDimensions(width, height, out var error))
            {
                SetStatus(error);
                return false;
            }

            var viewport = GetComponent<StudioEditorViewport>();
            var camera = viewport != null ? viewport.ViewportCamera : null;
            if (camera == null)
            {
                SetStatus("No active viewport camera is available.");
                return false;
            }

            routine = StartCoroutine(CaptureAtEndOfFrame(
                camera,
                width,
                height));
            SetStatus("Capturing screenshot...");
            return true;
        }

        private IEnumerator CaptureAtEndOfFrame(
            Camera camera,
            int width,
            int height)
        {
            yield return EndOfFrame;

            try
            {
                if (camera == null)
                {
                    throw new InvalidOperationException(
                        "The active viewport camera is no longer available.");
                }

                var outputPath = BuildOutputPath(width, height);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                RenderTexture renderTexture = null;
                Texture2D frameTexture = null;
                var previousTarget = camera.targetTexture;
                var previousAspect = camera.aspect;
                var previousRect = camera.rect;
                var previousActive = RenderTexture.active;
                var document = GetComponent<UIDocument>();
                var uiRoot = document != null ? document.rootVisualElement : null;
                var previousUiDisplay = uiRoot != null
                    ? uiRoot.style.display
                    : new StyleEnum<DisplayStyle>(DisplayStyle.Flex);

                try
                {
                    renderTexture = RenderTexture.GetTemporary(
                        width,
                        height,
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB);
                    frameTexture = new Texture2D(
                        width,
                        height,
                        TextureFormat.RGB24,
                        false);

                    if (uiRoot != null)
                    {
                        uiRoot.style.display = DisplayStyle.None;
                    }

                    camera.targetTexture = renderTexture;
                    camera.aspect = width / (float)height;
                    camera.rect = new Rect(0f, 0f, 1f, 1f);
                    camera.Render();

                    RenderTexture.active = renderTexture;
                    frameTexture.ReadPixels(
                        new Rect(0f, 0f, width, height),
                        0,
                        0,
                        false);
                    frameTexture.Apply(false, false);
                    var png = frameTexture.EncodeToPNG();
                    if (png == null || png.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "Unity returned an empty PNG screenshot.");
                    }

                    File.WriteAllBytes(outputPath, png);
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    camera.aspect = previousAspect;
                    camera.rect = previousRect;
                    RenderTexture.active = previousActive;
                    if (uiRoot != null)
                    {
                        uiRoot.style.display = previousUiDisplay;
                    }

                    if (renderTexture != null)
                    {
                        RenderTexture.ReleaseTemporary(renderTexture);
                    }

                    if (frameTexture != null)
                    {
                        Destroy(frameTexture);
                    }
                }

                OutputPath = outputPath;
                routine = null;
                SetStatus($"Saved {width}x{height} screenshot.");
            }
            catch (Exception exception)
            {
                routine = null;
                SetStatus("Screenshot failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }

        private static bool ValidateDimensions(
            int width,
            int height,
            out string error)
        {
            if (width < MinimumDimension || width > MaximumDimension ||
                height < MinimumDimension || height > MaximumDimension)
            {
                error = $"Resolution must be between {MinimumDimension} and " +
                        $"{MaximumDimension} pixels per side.";
                return false;
            }

            if ((long)width * height > MaximumPixelCount)
            {
                error = "The requested resolution is too large (64 megapixels maximum).";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static string BuildOutputPath(int width, int height)
        {
            var directory = Path.Combine(
                Application.persistentDataPath,
                "StudioEditor",
                "Screenshots");
            var name = "screenshot_" +
                       DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                       $"_{width}x{height}.png";
            return Path.Combine(directory, name);
        }

        private void SetStatus(string value)
        {
            Status = value ?? string.Empty;
            StateChanged?.Invoke();
        }

    }
}
