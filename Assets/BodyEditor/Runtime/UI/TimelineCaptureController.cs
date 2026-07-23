using System;
using System.Collections;
using System.IO;
using BodyEditor.ReferenceModels;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class TimelineCaptureController : MonoBehaviour
    {
        [Serializable]
        private sealed class CaptureManifest
        {
            public string format = "BodyEditor.TimelineCapture";
            public int version = 1;
            public string state;
            public int outputFps;
            public int width;
            public int height;
            public float duration;
            public int expectedFrames;
            public int committedFrames;
            public string startedUtc;
            public string completedUtc;
            public bool complete;
            public bool cancelled;
            public string error;
        }

        private sealed class SettingsSnapshot
        {
            private readonly int captureFramerate = Time.captureFramerate;
            private readonly float maximumDeltaTime = Time.maximumDeltaTime;
            private readonly float timeScale = Time.timeScale;
            private readonly int vSyncCount = QualitySettings.vSyncCount;
            private readonly int targetFrameRate = Application.targetFrameRate;
            private readonly bool runInBackground = Application.runInBackground;

            public void Apply(int outputFps)
            {
                Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.timeScale = 1f;
                Time.captureFramerate = outputFps;
                Time.maximumDeltaTime = Mathf.Max(
                    maximumDeltaTime,
                    1f / outputFps + 0.001f);
            }

            public void Restore()
            {
                Time.captureFramerate = captureFramerate;
                Time.maximumDeltaTime = maximumDeltaTime;
                Time.timeScale = timeScale;
                QualitySettings.vSyncCount = vSyncCount;
                Application.targetFrameRate = targetFrameRate;
                Application.runInBackground = runInBackground;
            }
        }

        private static readonly WaitForEndOfFrame EndOfFrame =
            new WaitForEndOfFrame();

        private IReferenceModelTimelineController timeline;
        private SettingsSnapshot settings;
        private CaptureManifest manifest;
        private Coroutine routine;
        private Texture2D frameTexture;
        private VisualElement uiRoot;
        private StyleEnum<DisplayStyle> uiDisplay;
        private float originalTime;
        private float originalSpeed;
        private bool originalLoop;
        private bool originalPlaying;
        private bool cancelRequested;

        public event Action StateChanged;

        public bool IsCapturing => routine != null;

        public string Status { get; private set; } = string.Empty;

        public string OutputPath { get; private set; } = string.Empty;

        public int CommittedFrames => manifest?.committedFrames ?? 0;

        public int ExpectedFrames => manifest?.expectedFrames ?? 0;

        public bool StartCapture(
            IReferenceModelTimelineController targetTimeline,
            int outputFps)
        {
            if (IsCapturing)
            {
                return false;
            }

            if (targetTimeline == null || targetTimeline.Duration <= 0f)
            {
                SetStatus("No playable timeline to capture.");
                return false;
            }

            outputFps = Mathf.Clamp(outputFps, 1, 240);
            var width = Screen.width;
            var height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                SetStatus("The game view has no captureable size.");
                return false;
            }

            try
            {
                var captureId = DateTime.UtcNow.ToString(
                    "yyyyMMdd_HHmmss_fff");
                OutputPath = Path.Combine(
                    Application.persistentDataPath,
                    "BodyEditor",
                    "Captures",
                    captureId);
                Directory.CreateDirectory(Path.Combine(OutputPath, "frames"));

                timeline = targetTimeline;
                originalTime = timeline.CurrentTime;
                originalSpeed = timeline.PlaybackSpeed;
                originalLoop = timeline.Loop;
                originalPlaying = timeline.IsPlaying;
                timeline.Pause();
                timeline.Loop = false;
                timeline.PlaybackSpeed = 1f;
                timeline.Seek(0f);

                manifest = new CaptureManifest
                {
                    state = "capturing",
                    outputFps = outputFps,
                    width = width,
                    height = height,
                    duration = timeline.Duration,
                    expectedFrames = Mathf.FloorToInt(
                        timeline.Duration * outputFps) + 1,
                    committedFrames = 0,
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    complete = false,
                    cancelled = false,
                    error = string.Empty,
                };
                WriteManifest();
                WriteFfmpegCommand();

                settings = new SettingsSnapshot();
                settings.Apply(outputFps);
                uiRoot = GetComponent<UIDocument>().rootVisualElement;
                uiDisplay = uiRoot.style.display;
                uiRoot.style.display = DisplayStyle.None;
                EnsureTexture(width, height);
                cancelRequested = false;
                Status = $"Capture 0/{manifest.expectedFrames}";
                routine = StartCoroutine(RunGuarded());
                StateChanged?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                Finish(false, false, exception.Message);
                return false;
            }
        }

        public void Cancel()
        {
            if (!IsCapturing)
            {
                return;
            }

            cancelRequested = true;
            SetStatus("Cancelling after the current frame...");
        }

        private void Update()
        {
            if (IsCapturing &&
                Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cancel();
            }
        }

        private IEnumerator RunGuarded()
        {
            var capture = CaptureFrames();
            Exception failure = null;
            while (true)
            {
                object current = null;
                var hasNext = false;
                try
                {
                    hasNext = capture.MoveNext();
                    if (hasNext)
                    {
                        current = capture.Current;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null || !hasNext)
                {
                    break;
                }

                yield return current;
            }

            if (failure != null)
            {
                Finish(false, cancelRequested, failure.Message);
            }
            else if (cancelRequested)
            {
                Finish(false, true, "Capture cancelled.");
            }
            else
            {
                Finish(true, false, string.Empty);
            }
        }

        private IEnumerator CaptureFrames()
        {
            for (var frameIndex = 0;
                 frameIndex < manifest.expectedFrames;
                 frameIndex++)
            {
                if (cancelRequested)
                {
                    yield break;
                }

                var frameTime = Mathf.Min(
                    frameIndex / (float)manifest.outputFps,
                    timeline.Duration);
                timeline.Seek(frameTime);
                yield return EndOfFrame;

                if (cancelRequested)
                {
                    yield break;
                }

                CaptureFrame(frameIndex);
                manifest.committedFrames = frameIndex + 1;
                WriteManifest();
                Status =
                    $"Capture {manifest.committedFrames}/{manifest.expectedFrames}";
                StateChanged?.Invoke();
            }
        }

        private void CaptureFrame(int frameIndex)
        {
            if (Screen.width != manifest.width ||
                Screen.height != manifest.height)
            {
                throw new InvalidOperationException(
                    "The game view size changed during capture.");
            }

            var previousTarget = RenderTexture.active;
            byte[] png;
            try
            {
                RenderTexture.active = null;
                frameTexture.ReadPixels(
                    new Rect(0f, 0f, manifest.width, manifest.height),
                    0,
                    0,
                    false);
                frameTexture.Apply(false, false);
                png = frameTexture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousTarget;
            }

            if (png == null || png.Length == 0)
            {
                throw new InvalidOperationException(
                    "Unity returned an empty PNG frame.");
            }

            var framePath = Path.Combine(
                OutputPath,
                "frames",
                $"frame_{frameIndex:000000}.png");
            File.WriteAllBytes(framePath, png);
        }

        private void EnsureTexture(int width, int height)
        {
            if (frameTexture != null &&
                frameTexture.width == width &&
                frameTexture.height == height)
            {
                return;
            }

            DestroyTexture();
            frameTexture = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);
        }

        private void Finish(bool complete, bool cancelled, string error)
        {
            routine = null;
            if (manifest != null)
            {
                manifest.state = complete
                    ? "complete"
                    : cancelled ? "cancelled" : "failed";
                manifest.complete = complete;
                manifest.cancelled = cancelled;
                manifest.error = error ?? string.Empty;
                manifest.completedUtc = DateTime.UtcNow.ToString("O");
                TryWriteManifest();
            }

            settings?.Restore();
            settings = null;
            if (uiRoot != null)
            {
                uiRoot.style.display = uiDisplay;
                uiRoot = null;
            }

            if (timeline != null)
            {
                timeline.Pause();
                timeline.PlaybackSpeed = originalSpeed;
                timeline.Loop = originalLoop;
                timeline.Seek(originalTime);
                if (originalPlaying)
                {
                    timeline.Play();
                }
            }

            Status = complete
                ? $"Saved {manifest?.committedFrames ?? 0} frames"
                : cancelled
                    ? $"Cancelled at {manifest?.committedFrames ?? 0} frames"
                    : string.IsNullOrWhiteSpace(error)
                        ? string.Empty
                        : "Capture failed: " + error;
            timeline = null;
            cancelRequested = false;
            StateChanged?.Invoke();
        }

        private void WriteManifest()
        {
            File.WriteAllText(
                Path.Combine(OutputPath, "capture.json"),
                JsonUtility.ToJson(manifest, true));
        }

        private void TryWriteManifest()
        {
            try
            {
                WriteManifest();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void WriteFfmpegCommand()
        {
            var command =
                $"ffmpeg -framerate {manifest.outputFps} " +
                "-i \"frames/frame_%06d.png\" " +
                "-c:v libx264 -preset slow -crf 15 " +
                "-pix_fmt yuv420p \"output.mp4\"";
            File.WriteAllText(
                Path.Combine(OutputPath, "ffmpeg-command.txt"),
                command);
        }

        private void SetStatus(string value)
        {
            Status = value ?? string.Empty;
            StateChanged?.Invoke();
        }

        private void OnDisable()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                Finish(false, true, "Capture stopped with the editor UI.");
            }
        }

        private void OnDestroy()
        {
            DestroyTexture();
        }

        private void DestroyTexture()
        {
            if (frameTexture != null)
            {
                Destroy(frameTexture);
                frameTexture = null;
            }
        }
    }
}
