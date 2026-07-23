using System;
using System.Collections.Generic;
using StudioEditor.Editing;
using StudioEditor.ReferenceModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioEditor.UI
{
    internal sealed class ReferenceTimelinePanel : VisualElement
    {
        private readonly SceneContentController importController;
        private readonly TimelineCaptureController captureController;
        private readonly SceneTimelineController sceneTimeline;
        private readonly EditableSkeletonController editableSkeleton;
        private readonly VisualElement viewportInput;
        private readonly Button launcher;
        private readonly VisualElement surface;
        private readonly Button playPauseButton;
        private readonly Button stopButton;
        private readonly Button captureButton;
        private readonly IntegerField captureFpsField;
        private readonly Toggle loopToggle;
        private readonly Label clockLabel;
        private readonly Label summaryLabel;
        private readonly Label captureStatusLabel;
        private readonly VisualElement editBar;
        private readonly FloatField durationField;
        private readonly Button addPositionButton;
        private readonly Button addRotationButton;
        private readonly Button addScaleButton;
        private readonly Button setKeyButton;
        private readonly Button deleteKeyButton;
        private readonly Button deleteTrackButton;
        private readonly VisualElement ruler;
        private readonly Label rulerHeading;
        private readonly TimelineLane rulerLane;
        private readonly Label keyHeading;
        private readonly ScrollView trackList;
        private readonly List<TimelineLane> trackLanes =
            new List<TimelineLane>();
        private readonly List<VisualElement> trackRows =
            new List<VisualElement>();

        private IReferenceModelTimelineController timeline;
        private VisualElement firstTrackRow;
        private TimelineLane firstTrackLane;
        private Label firstTrackKeys;
        private int selectedTrackIndex = -1;
        private bool isOpen;

        public ReferenceTimelinePanel(
            SceneContentController importController,
            TimelineCaptureController captureController,
            SceneTimelineController sceneTimeline,
            EditableSkeletonController editableSkeleton,
            VisualElement viewportInput,
            VisualElement launcherParent = null)
        {
            this.importController = importController ??
                throw new ArgumentNullException(nameof(importController));
            this.captureController = captureController ??
                throw new ArgumentNullException(nameof(captureController));
            this.sceneTimeline = sceneTimeline ??
                throw new ArgumentNullException(nameof(sceneTimeline));
            this.editableSkeleton = editableSkeleton;
            this.viewportInput = viewportInput ??
                throw new ArgumentNullException(nameof(viewportInput));

            name = "reference-timeline-host";
            pickingMode = PickingMode.Ignore;
            AddToClassList("reference-timeline-host");

            launcher = new Button(ToggleOpen)
            {
                text = "TL",
                tooltip = "Open scene timeline",
                pickingMode = PickingMode.Position,
            };
            launcher.AddToClassList("timeline-launcher");
            launcher.AddToClassList("workspace-tools__button");
            (launcherParent ?? this).Add(launcher);

            surface = new VisualElement
            {
                name = "reference-timeline",
                pickingMode = PickingMode.Position,
            };
            surface.AddToClassList("reference-timeline");
            Add(surface);

            var transport = new VisualElement();
            transport.AddToClassList("reference-timeline__transport");
            transport.Add(CreateIconButton(
                "|<",
                "Go to the first frame",
                () => timeline?.Seek(0f)));

            playPauseButton = CreateIconButton(
                ">",
                "Play timeline",
                TogglePlayback);
            transport.Add(playPauseButton);

            stopButton = CreateIconButton(
                "[]",
                "Stop and return to the first frame",
                () => timeline?.Stop());
            transport.Add(stopButton);

            clockLabel = new Label("00:00.000 / 00:00.000");
            clockLabel.AddToClassList("reference-timeline__clock");
            transport.Add(clockLabel);

            loopToggle = new Toggle("Loop");
            loopToggle.AddToClassList("reference-timeline__loop");
            loopToggle.RegisterValueChangedCallback(HandleLoopChanged);
            transport.Add(loopToggle);

            captureFpsField = new IntegerField("FPS")
            {
                value = 60,
                isDelayed = true,
                tooltip = "PNG capture frame rate",
            };
            captureFpsField.AddToClassList(
                "reference-timeline__capture-fps");
            captureFpsField.RegisterValueChangedCallback(changeEvent =>
                captureFpsField.SetValueWithoutNotify(
                    Mathf.Clamp(changeEvent.newValue, 1, 240)));
            transport.Add(captureFpsField);

            captureButton = new Button(ToggleCapture)
            {
                text = "REC",
                tooltip = "Capture the full timeline as a PNG sequence",
            };
            captureButton.AddToClassList("reference-timeline__capture");
            transport.Add(captureButton);

            summaryLabel = new Label();
            summaryLabel.AddToClassList("reference-timeline__summary");
            transport.Add(summaryLabel);

            var closeButton = CreateIconButton(
                "x",
                "Close timeline",
                () => SetOpen(false));
            closeButton.AddToClassList("reference-timeline__close");
            transport.Add(closeButton);
            surface.Add(transport);

            captureStatusLabel = new Label();
            captureStatusLabel.AddToClassList(
                "reference-timeline__capture-status");

            editBar = new VisualElement();
            editBar.AddToClassList("reference-timeline__edit-bar");
            durationField = new FloatField("Length")
            {
                value = sceneTimeline.Duration,
                isDelayed = true,
                tooltip = "Timeline duration in seconds",
            };
            durationField.AddToClassList(
                "reference-timeline__duration");
            durationField.RegisterValueChangedCallback(
                HandleDurationChanged);
            editBar.Add(durationField);

            addPositionButton = CreateEditButton(
                "+P",
                "Add a position track for the selected target",
                () => AddTrack(ReferenceTimelineTrackKind.Position));
            addRotationButton = CreateEditButton(
                "+R",
                "Add a rotation track for the selected target",
                () => AddTrack(ReferenceTimelineTrackKind.Rotation));
            addScaleButton = CreateEditButton(
                "+S",
                "Add a scale track for the selected target",
                () => AddTrack(ReferenceTimelineTrackKind.Scale));
            setKeyButton = CreateEditButton(
                "Key",
                "Write or overwrite a keyframe at the playhead",
                SetKeyframe);
            deleteKeyButton = CreateEditButton(
                "-Key",
                "Delete the selected track keyframe at the playhead",
                DeleteKeyframe);
            deleteTrackButton = CreateEditButton(
                "-Track",
                "Delete the selected track",
                DeleteTrack);
            editBar.Add(addPositionButton);
            editBar.Add(addRotationButton);
            editBar.Add(addScaleButton);
            editBar.Add(setKeyButton);
            editBar.Add(deleteKeyButton);
            editBar.Add(deleteTrackButton);
            editBar.Add(captureStatusLabel);
            surface.Add(editBar);

            ruler = new VisualElement();
            ruler.AddToClassList("reference-timeline__ruler");
            rulerHeading = new Label("TRACK / TARGET");
            rulerHeading.AddToClassList("reference-timeline__ruler-heading");
            ruler.Add(rulerHeading);
            rulerLane = new TimelineLane(SeekNormalized, true);
            ruler.Add(rulerLane);
            keyHeading = new Label("KEYS");
            keyHeading.AddToClassList("reference-timeline__key-heading");
            ruler.Add(keyHeading);
            surface.Add(ruler);

            trackList = new ScrollView(ScrollViewMode.Vertical);
            trackList.AddToClassList("reference-timeline__tracks");
            surface.Add(trackList);
            surface.RegisterCallback<GeometryChangedEvent>(
                HandleSurfaceGeometryChanged);

            importController.StateChanged += RefreshTimeline;
            captureController.StateChanged += RefreshCaptureState;
            if (editableSkeleton != null)
            {
                editableSkeleton.StateChanged += RefreshEditState;
            }
            RegisterCallback<DetachFromPanelEvent>(HandleDetach);
            schedule.Execute(RefreshPlaybackState).Every(33);
            SetOpen(false);
            RefreshTimeline();
        }

        private static Button CreateIconButton(
            string text,
            string tooltip,
            Action action)
        {
            var button = new Button(action)
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList("reference-timeline__icon-button");
            return button;
        }

        private static Button CreateEditButton(
            string text,
            string tooltip,
            Action action)
        {
            var button = new Button(action)
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList("reference-timeline__edit-button");
            return button;
        }

        private void HandleDetach(DetachFromPanelEvent detachEvent)
        {
            importController.StateChanged -= RefreshTimeline;
            captureController.StateChanged -= RefreshCaptureState;
            if (editableSkeleton != null)
            {
                editableSkeleton.StateChanged -= RefreshEditState;
            }
            BindTimeline(null);
        }

        private void ToggleOpen()
        {
            if (timeline != null)
            {
                SetOpen(!isOpen);
            }
        }

        private void SetOpen(bool value)
        {
            isOpen = value;
            surface.style.display = isOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            launcher.EnableInClassList("timeline-launcher--open", isOpen);
            viewportInput.EnableInClassList(
                "viewport-input--timeline",
                isOpen);
        }

        private void RefreshTimeline()
        {
            IReferenceModelTimelineController currentTimeline = sceneTimeline;
            if (importController.Status == ReferenceModelImportStatus.Ready)
            {
                if (importController.Current is
                    IReferenceModelTimelineProvider provider &&
                    provider.Timeline != null)
                {
                    currentTimeline = provider.Timeline;
                }
                else if (importController.Current is
                         IReferenceModelTimelineController controller)
                {
                    currentTimeline = controller;
                }
            }

            BindTimeline(currentTimeline);
        }

        private void BindTimeline(IReferenceModelTimelineController value)
        {
            if (ReferenceEquals(timeline, value))
            {
                RefreshPlaybackState();
                RefreshEditState();
                return;
            }

            if (timeline != null)
            {
                timeline.StateChanged -= RefreshPlaybackState;
                if (timeline is IEditableSceneTimelineController editable)
                {
                    editable.StructureChanged -= HandleTimelineStructureChanged;
                }
            }

            timeline = value;
            if (timeline != null)
            {
                timeline.StateChanged += RefreshPlaybackState;
                if (timeline is IEditableSceneTimelineController editable)
                {
                    editable.StructureChanged += HandleTimelineStructureChanged;
                }
            }

            selectedTrackIndex = -1;
            RebuildTracks();
            RefreshPlaybackState();
            RefreshEditState();
        }

        private void HandleTimelineStructureChanged()
        {
            RebuildTracks();
            RefreshPlaybackState();
            RefreshEditState();
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

        private void ToggleCapture()
        {
            if (captureController.IsCapturing)
            {
                captureController.Cancel();
                return;
            }

            if (timeline != null)
            {
                captureController.StartCapture(
                    timeline,
                    Mathf.Clamp(captureFpsField.value, 1, 240));
            }
        }

        private void HandleLoopChanged(ChangeEvent<bool> changeEvent)
        {
            if (timeline != null)
            {
                timeline.Loop = changeEvent.newValue;
            }
        }

        private void SeekNormalized(float normalizedTime)
        {
            if (timeline != null)
            {
                timeline.Seek(Mathf.Clamp01(normalizedTime) * timeline.Duration);
            }
        }

        private void HandleDurationChanged(ChangeEvent<float> changeEvent)
        {
            if (timeline is IEditableSceneTimelineController editable)
            {
                editable.SetDuration(changeEvent.newValue);
                durationField.SetValueWithoutNotify(timeline.Duration);
            }
        }

        private void AddTrack(ReferenceTimelineTrackKind kind)
        {
            if (!(timeline is IEditableSceneTimelineController editable))
            {
                return;
            }

            var target = ResolveAuthoringTarget();
            if (target == null)
            {
                captureStatusLabel.text = "No editable target";
                return;
            }

            var trackIndex = editable.AddTrack(target, kind);
            if (trackIndex >= 0)
            {
                selectedTrackIndex = trackIndex;
                RebuildTracks();
                RefreshEditState();
            }
        }

        private void SetKeyframe()
        {
            if (timeline is IEditableSceneTimelineController editable)
            {
                editable.AddOrUpdateKeyframe(selectedTrackIndex);
            }
        }

        private void DeleteKeyframe()
        {
            if (timeline is IEditableSceneTimelineController editable)
            {
                editable.DeleteKeyframe(selectedTrackIndex);
            }
        }

        private void DeleteTrack()
        {
            if (timeline is IEditableSceneTimelineController editable &&
                editable.DeleteTrack(selectedTrackIndex))
            {
                selectedTrackIndex = Mathf.Min(
                    selectedTrackIndex,
                    timeline.Tracks.Count - 1);
                RebuildTracks();
                RefreshEditState();
            }
        }

        private Transform ResolveAuthoringTarget()
        {
            if (editableSkeleton != null &&
                editableSkeleton.SelectedBone.HasValue &&
                editableSkeleton.Skeleton != null &&
                editableSkeleton.Skeleton.TryGetBone(
                    editableSkeleton.SelectedBone.Value,
                    out var selectedBone))
            {
                return selectedBone;
            }

            if (importController.Current?.Root != null)
            {
                return importController.Current.Root.transform;
            }

            return Camera.main != null ? Camera.main.transform : null;
        }

        private void SelectTrack(int trackIndex)
        {
            selectedTrackIndex = trackIndex;
            for (var index = 0; index < trackRows.Count; index++)
            {
                trackRows[index].EnableInClassList(
                    "reference-timeline__track-row--selected",
                    index == selectedTrackIndex);
            }

            RefreshEditState();
        }

        private void RebuildTracks()
        {
            trackList.Clear();
            trackLanes.Clear();
            trackRows.Clear();
            firstTrackRow = null;
            firstTrackLane = null;
            firstTrackKeys = null;
            if (timeline == null)
            {
                rulerLane.Configure(Array.Empty<float>(), 0f, 0f);
                return;
            }

            rulerLane.Configure(
                Array.Empty<float>(),
                timeline.Duration,
                timeline.CurrentTime);
            for (var index = 0; index < timeline.Tracks.Count; index++)
            {
                var track = timeline.Tracks[index];
                var row = new VisualElement
                {
                    tooltip = track.Status,
                };
                row.AddToClassList("reference-timeline__track-row");
                row.EnableInClassList(
                    "reference-timeline__track-row--selected",
                    index == selectedTrackIndex);
                var rowIndex = index;
                row.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    if (pointerEvent.button == 0)
                    {
                        SelectTrack(rowIndex);
                    }
                });
                if (!track.Supported)
                {
                    row.AddToClassList(
                        "reference-timeline__track-row--unsupported");
                }

                var metadata = new VisualElement();
                metadata.AddToClassList(
                    "reference-timeline__track-metadata");
                var toggle = new Toggle
                {
                    value = track.Enabled,
                    tooltip = "Enable this timeline track",
                };
                toggle.AddToClassList(
                    "reference-timeline__track-enabled");
                toggle.SetEnabled(track.Supported);
                var trackIndex = track.Index;
                toggle.RegisterValueChangedCallback(changeEvent =>
                    timeline?.SetTrackEnabled(
                        trackIndex,
                        changeEvent.newValue));
                metadata.Add(toggle);

                var labels = new VisualElement();
                labels.AddToClassList("reference-timeline__track-labels");
                var nameLabel = new Label(track.Name);
                nameLabel.AddToClassList(
                    "reference-timeline__track-name");
                labels.Add(nameLabel);
                var targetLabel = new Label(track.Target);
                targetLabel.AddToClassList(
                    "reference-timeline__track-target");
                labels.Add(targetLabel);
                metadata.Add(labels);
                row.Add(metadata);

                var lane = new TimelineLane(SeekNormalized, false);
                lane.Configure(
                    track.KeyframeTimes,
                    timeline.Duration,
                    timeline.CurrentTime,
                    track.Kind);
                trackLanes.Add(lane);
                row.Add(lane);

                var keysLabel = new Label(track.KeyframeCount.ToString());
                keysLabel.AddToClassList(
                    "reference-timeline__track-keys");
                row.Add(keysLabel);
                trackList.Add(row);
                trackRows.Add(row);
                if (firstTrackLane == null)
                {
                    firstTrackRow = row;
                    firstTrackLane = lane;
                    firstTrackKeys = keysLabel;
                    lane.RegisterCallback<GeometryChangedEvent>(
                        HandleFirstTrackGeometryChanged);
                }
            }

            schedule.Execute(SynchronizeRulerGeometry);
        }

        private void HandleSurfaceGeometryChanged(
            GeometryChangedEvent geometryEvent)
        {
            SynchronizeRulerGeometry();
        }

        private void HandleFirstTrackGeometryChanged(
            GeometryChangedEvent geometryEvent)
        {
            SynchronizeRulerGeometry();
        }

        private void SynchronizeRulerGeometry()
        {
            if (firstTrackRow == null ||
                firstTrackLane == null ||
                firstTrackKeys == null ||
                firstTrackLane.worldBound.width <= 0f ||
                rulerLane.worldBound.width <= 0f)
            {
                return;
            }

            var changed = false;
            var startError =
                firstTrackLane.worldBound.x - rulerLane.worldBound.x;
            var headingWidth =
                rulerHeading.resolvedStyle.width + startError;
            if (headingWidth < 0f)
            {
                return;
            }

            changed |= SetFixedWidth(rulerHeading, headingWidth);
            var laneWidthError =
                firstTrackLane.worldBound.width - rulerLane.worldBound.width;
            changed |= SetFixedWidth(
                rulerLane,
                rulerLane.resolvedStyle.width + laneWidthError);
            changed |= SetFixedWidth(
                keyHeading,
                firstTrackKeys.worldBound.width);
            if (changed)
            {
                schedule.Execute(SynchronizeRulerGeometry);
            }
        }

        private static bool SetFixedWidth(
            VisualElement element,
            float width)
        {
            width = Mathf.Max(0f, width);
            if (Mathf.Abs(element.resolvedStyle.width - width) < 0.1f)
            {
                return false;
            }

            element.style.width = width;
            element.style.minWidth = width;
            element.style.maxWidth = width;
            element.style.flexGrow = 0f;
            element.style.flexShrink = 0f;
            return true;
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
            stopButton.SetEnabled(
                timeline.CurrentTime > 0f || timeline.IsPlaying);
            loopToggle.SetValueWithoutNotify(timeline.Loop);
            clockLabel.text = $"{FormatTime(timeline.CurrentTime)} / " +
                              FormatTime(timeline.Duration);
            rulerLane.SetCurrentTime(timeline.CurrentTime, timeline.Duration);
            for (var index = 0; index < trackLanes.Count; index++)
            {
                trackLanes[index].SetCurrentTime(
                    timeline.CurrentTime,
                    timeline.Duration);
            }

            var supported = 0;
            var keys = 0;
            for (var index = 0; index < timeline.Tracks.Count; index++)
            {
                if (timeline.Tracks[index].Supported)
                {
                    supported++;
                }

                keys += timeline.Tracks[index].KeyframeCount;
            }

            summaryLabel.text =
                $"{supported}/{timeline.Tracks.Count} tracks  |  {keys} keys";
        }

        private void RefreshCaptureState()
        {
            var capturing = captureController.IsCapturing;
            captureButton.text = capturing ? "ESC" : "REC";
            captureButton.tooltip = capturing
                ? "Cancel capture after the current frame"
                : "Capture the full timeline as a PNG sequence";
            captureButton.EnableInClassList(
                "reference-timeline__capture--active",
                capturing);
            captureFpsField.SetEnabled(!capturing);
            captureStatusLabel.text = captureController.Status;
            captureStatusLabel.tooltip = captureController.OutputPath;
            playPauseButton.SetEnabled(!capturing);
            loopToggle.SetEnabled(!capturing);
        }

        private void RefreshEditState()
        {
            var editable = timeline is IEditableSceneTimelineController;
            durationField.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            addPositionButton.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            addRotationButton.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            addScaleButton.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            setKeyButton.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            deleteKeyButton.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            deleteTrackButton.style.display = editable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (!editable)
            {
                return;
            }

            durationField.SetValueWithoutNotify(timeline.Duration);
            var hasTarget = ResolveAuthoringTarget() != null;
            addPositionButton.SetEnabled(hasTarget);
            addRotationButton.SetEnabled(hasTarget);
            addScaleButton.SetEnabled(hasTarget);
            var hasSelection = selectedTrackIndex >= 0 &&
                               selectedTrackIndex < timeline.Tracks.Count;
            setKeyButton.SetEnabled(hasSelection);
            deleteKeyButton.SetEnabled(hasSelection);
            deleteTrackButton.SetEnabled(hasSelection);
        }

        private static string FormatTime(float value)
        {
            value = Mathf.Max(0f, value);
            var span = TimeSpan.FromSeconds(value);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}." +
                   $"{span.Milliseconds:000}";
        }

        private sealed class TimelineLane : VisualElement
        {
            private readonly Action<float> seek;
            private readonly bool ruler;
            private readonly VisualElement playhead;
            private bool dragging;

            public TimelineLane(Action<float> seek, bool ruler)
            {
                this.seek = seek;
                this.ruler = ruler;
                pickingMode = PickingMode.Position;
                AddToClassList("reference-timeline__lane");
                if (ruler)
                {
                    AddToClassList("reference-timeline__lane--ruler");
                }

                playhead = new VisualElement
                {
                    pickingMode = PickingMode.Ignore,
                };
                playhead.AddToClassList("reference-timeline__playhead");
                RegisterCallback<PointerDownEvent>(HandlePointerDown);
                RegisterCallback<PointerMoveEvent>(HandlePointerMove);
                RegisterCallback<PointerUpEvent>(HandlePointerUp);
                RegisterCallback<PointerCancelEvent>(HandlePointerCancel);
            }

            public void Configure(
                IReadOnlyList<float> keyframes,
                float duration,
                float currentTime,
                ReferenceTimelineTrackKind kind =
                    ReferenceTimelineTrackKind.Value)
            {
                Clear();
                if (ruler)
                {
                    BuildRuler(duration);
                }
                else if (keyframes != null && duration > 0f)
                {
                    for (var index = 0; index < keyframes.Count; index++)
                    {
                        var marker = new VisualElement
                        {
                            tooltip = FormatTime(keyframes[index]),
                            pickingMode = PickingMode.Ignore,
                        };
                        marker.AddToClassList("reference-timeline__keyframe");
                        marker.AddToClassList(KeyframeClass(kind));
                        marker.style.left = new Length(
                            Mathf.Clamp01(keyframes[index] / duration) * 100f,
                            LengthUnit.Percent);
                        Add(marker);
                    }
                }

                Add(playhead);
                SetCurrentTime(currentTime, duration);
            }

            public void SetCurrentTime(float time, float duration)
            {
                playhead.style.left = new Length(
                    duration <= 0f
                        ? 0f
                        : Mathf.Clamp01(time / duration) * 100f,
                    LengthUnit.Percent);
            }

            private void BuildRuler(float duration)
            {
                const int divisions = 8;
                for (var index = 0; index <= divisions; index++)
                {
                    var normalized = index / (float)divisions;
                    var tick = new VisualElement
                    {
                        pickingMode = PickingMode.Ignore,
                    };
                    tick.AddToClassList("reference-timeline__ruler-tick");
                    tick.style.left = new Length(
                        normalized * 100f,
                        LengthUnit.Percent);
                    Add(tick);

                    var label = new Label(FormatRulerTime(duration * normalized))
                    {
                        pickingMode = PickingMode.Ignore,
                    };
                    label.AddToClassList("reference-timeline__ruler-label");
                    if (index == divisions)
                    {
                        label.AddToClassList(
                            "reference-timeline__ruler-label--last");
                    }

                    label.style.left = new Length(
                        normalized * 100f,
                        LengthUnit.Percent);
                    Add(label);
                }
            }

            private void HandlePointerDown(PointerDownEvent pointerEvent)
            {
                if (pointerEvent.button != 0)
                {
                    return;
                }

                dragging = true;
                Seek(pointerEvent.localPosition.x);
                pointerEvent.StopPropagation();
            }

            private void HandlePointerMove(PointerMoveEvent pointerEvent)
            {
                if (dragging)
                {
                    Seek(pointerEvent.localPosition.x);
                    pointerEvent.StopPropagation();
                }
            }

            private void HandlePointerUp(PointerUpEvent pointerEvent)
            {
                if (!dragging || pointerEvent.button != 0)
                {
                    return;
                }

                dragging = false;
                Seek(pointerEvent.localPosition.x);
                pointerEvent.StopPropagation();
            }

            private void HandlePointerCancel(PointerCancelEvent pointerEvent)
            {
                dragging = false;
            }

            private void Seek(float x)
            {
                seek?.Invoke(
                    resolvedStyle.width <= 0f
                        ? 0f
                        : Mathf.Clamp01(x / resolvedStyle.width));
            }

            private static string KeyframeClass(
                ReferenceTimelineTrackKind kind)
            {
                switch (kind)
                {
                    case ReferenceTimelineTrackKind.Position:
                        return "reference-timeline__keyframe--position";
                    case ReferenceTimelineTrackKind.Rotation:
                        return "reference-timeline__keyframe--rotation";
                    case ReferenceTimelineTrackKind.Scale:
                        return "reference-timeline__keyframe--scale";
                    default:
                        return "reference-timeline__keyframe--value";
                }
            }

            private static string FormatRulerTime(float value)
            {
                if (value >= 60f)
                {
                    return $"{Mathf.FloorToInt(value / 60f)}:" +
                           $"{Mathf.FloorToInt(value % 60f):00}";
                }

                return value.ToString(value < 10f ? "0.0" : "0");
            }
        }
    }
}
