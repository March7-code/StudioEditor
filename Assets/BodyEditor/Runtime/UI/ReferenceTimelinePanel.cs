using System;
using BodyEditor.ReferenceModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace BodyEditor.UI
{
    internal sealed class ReferenceTimelinePanel : VisualElement
    {
        private readonly ReferenceModelImportController importController;
        private readonly VisualElement viewportInput;
        private readonly Button playPauseButton;
        private readonly Button stopButton;
        private readonly Slider scrubber;
        private readonly FloatField speedField;
        private readonly Toggle loopToggle;
        private readonly Label clockLabel;
        private readonly Label summaryLabel;
        private readonly ScrollView trackList;

        private IReferenceModelTimelineController timeline;

        public ReferenceTimelinePanel(
            ReferenceModelImportController importController,
            VisualElement viewportInput)
        {
            this.importController = importController ??
                throw new ArgumentNullException(nameof(importController));
            this.viewportInput = viewportInput ??
                throw new ArgumentNullException(nameof(viewportInput));

            name = "reference-timeline";
            pickingMode = PickingMode.Position;
            AddToClassList("reference-timeline");

            var transport = new VisualElement();
            transport.AddToClassList("reference-timeline__transport");

            var startButton = new Button(() => timeline?.Seek(0f))
            {
                text = "|<",
                tooltip = "Go to the first frame",
            };
            startButton.AddToClassList("reference-timeline__icon-button");
            transport.Add(startButton);

            playPauseButton = new Button(TogglePlayback)
            {
                text = ">",
                tooltip = "Play timeline",
            };
            playPauseButton.AddToClassList("reference-timeline__icon-button");
            transport.Add(playPauseButton);

            stopButton = new Button(() => timeline?.Stop())
            {
                text = "[]",
                tooltip = "Stop and return to the first frame",
            };
            stopButton.AddToClassList("reference-timeline__icon-button");
            transport.Add(stopButton);

            clockLabel = new Label("00:00.000 / 00:00.000");
            clockLabel.AddToClassList("reference-timeline__clock");
            transport.Add(clockLabel);

            speedField = new FloatField("Speed")
            {
                isDelayed = true,
                tooltip = "Timeline playback speed (0.05-8)",
            };
            speedField.AddToClassList("reference-timeline__speed");
            speedField.RegisterValueChangedCallback(HandleSpeedChanged);
            transport.Add(speedField);

            loopToggle = new Toggle("Loop");
            loopToggle.AddToClassList("reference-timeline__loop");
            loopToggle.RegisterValueChangedCallback(HandleLoopChanged);
            transport.Add(loopToggle);

            summaryLabel = new Label();
            summaryLabel.AddToClassList("reference-timeline__summary");
            transport.Add(summaryLabel);
            Add(transport);

            scrubber = new Slider(0f, 1f)
            {
                tooltip = "Seek the imported scene timeline",
            };
            scrubber.AddToClassList("reference-timeline__scrubber");
            scrubber.RegisterValueChangedCallback(HandleScrubberChanged);
            Add(scrubber);

            var columns = new VisualElement();
            columns.AddToClassList("reference-timeline__columns");
            columns.Add(CreateColumn("On", "reference-timeline__column--enabled"));
            columns.Add(CreateColumn("Track", "reference-timeline__column--track"));
            columns.Add(CreateColumn("Target", "reference-timeline__column--target"));
            columns.Add(CreateColumn("Keys", "reference-timeline__column--keys"));
            Add(columns);

            trackList = new ScrollView(ScrollViewMode.Vertical);
            trackList.AddToClassList("reference-timeline__tracks");
            Add(trackList);

            importController.StateChanged += RefreshTimeline;
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            schedule.Execute(RefreshPlaybackState).Every(33);
            RefreshTimeline();
        }

        private static Label CreateColumn(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            importController.StateChanged -= RefreshTimeline;
            BindTimeline(null);
        }

        private void RefreshTimeline()
        {
            IReferenceModelTimelineController currentTimeline = null;
            if (importController.Status == ReferenceModelImportStatus.Ready)
            {
                if (importController.Current is
                    IReferenceModelTimelineProvider provider)
                {
                    currentTimeline = provider.Timeline;
                }
                else
                {
                    currentTimeline = importController.Current as
                        IReferenceModelTimelineController;
                }
            }

            BindTimeline(currentTimeline);
        }

        private void BindTimeline(IReferenceModelTimelineController value)
        {
            if (ReferenceEquals(timeline, value))
            {
                RefreshPlaybackState();
                return;
            }

            if (timeline != null)
            {
                timeline.StateChanged -= RefreshPlaybackState;
            }

            timeline = value;
            if (timeline != null)
            {
                timeline.StateChanged += RefreshPlaybackState;
            }

            style.display = timeline == null
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            viewportInput.EnableInClassList(
                "viewport-input--timeline",
                timeline != null);
            RebuildTracks();
            RefreshPlaybackState();
        }

        private void TogglePlayback()
        {
            if (timeline == null)
            {
                return;
            }

            if (timeline.IsPlaying)
            {
                timeline.Pause();
            }
            else
            {
                timeline.Play();
            }
        }

        private void HandleScrubberChanged(ChangeEvent<float> changeEvent)
        {
            timeline?.Seek(changeEvent.newValue);
        }

        private void HandleSpeedChanged(ChangeEvent<float> changeEvent)
        {
            if (timeline == null)
            {
                return;
            }

            timeline.PlaybackSpeed = Mathf.Clamp(changeEvent.newValue, 0.05f, 8f);
            speedField.SetValueWithoutNotify(timeline.PlaybackSpeed);
        }

        private void HandleLoopChanged(ChangeEvent<bool> changeEvent)
        {
            if (timeline != null)
            {
                timeline.Loop = changeEvent.newValue;
            }
        }

        private void RebuildTracks()
        {
            trackList.Clear();
            if (timeline == null)
            {
                return;
            }

            for (var index = 0; index < timeline.Tracks.Count; index++)
            {
                var track = timeline.Tracks[index];
                var row = new VisualElement
                {
                    tooltip = track.Status,
                };
                row.AddToClassList("reference-timeline__track-row");
                if (!track.Supported)
                {
                    row.AddToClassList(
                        "reference-timeline__track-row--unsupported");
                }

                var toggle = new Toggle
                {
                    value = track.Enabled,
                };
                toggle.AddToClassList(
                    "reference-timeline__track-enabled");
                toggle.SetEnabled(track.Supported);
                var trackIndex = track.Index;
                toggle.RegisterValueChangedCallback(changeEvent =>
                    timeline?.SetTrackEnabled(
                        trackIndex,
                        changeEvent.newValue));
                row.Add(toggle);

                var nameLabel = new Label(track.Name);
                nameLabel.AddToClassList("reference-timeline__track-name");
                row.Add(nameLabel);

                var targetLabel = new Label(track.Target);
                targetLabel.AddToClassList("reference-timeline__track-target");
                row.Add(targetLabel);

                var keysLabel = new Label(track.KeyframeCount.ToString());
                keysLabel.AddToClassList("reference-timeline__track-keys");
                row.Add(keysLabel);
                trackList.Add(row);
            }
        }

        private void RefreshPlaybackState()
        {
            if (timeline == null)
            {
                return;
            }

            playPauseButton.text = timeline.IsPlaying ? "||" : ">";
            playPauseButton.tooltip = timeline.IsPlaying
                ? "Pause timeline"
                : "Play timeline";
            stopButton.SetEnabled(timeline.CurrentTime > 0f || timeline.IsPlaying);
            scrubber.lowValue = 0f;
            scrubber.highValue = Mathf.Max(0.0001f, timeline.Duration);
            scrubber.SetValueWithoutNotify(timeline.CurrentTime);
            speedField.SetValueWithoutNotify(timeline.PlaybackSpeed);
            loopToggle.SetValueWithoutNotify(timeline.Loop);
            clockLabel.text = $"{FormatTime(timeline.CurrentTime)} / " +
                              FormatTime(timeline.Duration);

            var supported = 0;
            for (var index = 0; index < timeline.Tracks.Count; index++)
            {
                if (timeline.Tracks[index].Supported)
                {
                    supported++;
                }
            }

            summaryLabel.text = $"{supported}/{timeline.Tracks.Count} tracks";
        }

        private static string FormatTime(float value)
        {
            value = Mathf.Max(0f, value);
            var span = TimeSpan.FromSeconds(value);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}." +
                   $"{span.Milliseconds:000}";
        }
    }
}
