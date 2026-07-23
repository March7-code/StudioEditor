using System;
using System.Collections.Generic;
using BodyEditor.ReferenceModels;
using UnityEngine;

namespace BodyEditor.UI
{
    internal interface IEditableSceneTimelineController :
        IReferenceModelTimelineController
    {
        event Action StructureChanged;

        int AddTrack(Transform target, ReferenceTimelineTrackKind kind);

        bool AddOrUpdateKeyframe(int trackIndex);

        bool DeleteKeyframe(int trackIndex);

        bool DeleteTrack(int trackIndex);

        void SetDuration(float duration);
    }

    [DisallowMultipleComponent]
    public sealed class SceneTimelineController :
        MonoBehaviour,
        IEditableSceneTimelineController
    {
        private sealed class AuthoredKeyframe
        {
            public float Time;
            public Vector3 VectorValue;
            public Quaternion RotationValue;
        }

        private sealed class AuthoredTrack
        {
            public Transform Target;
            public ReferenceTimelineTrackKind Kind;
            public bool Enabled = true;
            public readonly List<AuthoredKeyframe> Keyframes =
                new List<AuthoredKeyframe>();
        }

        private readonly List<AuthoredTrack> authoredTracks =
            new List<AuthoredTrack>();
        private readonly List<ReferenceTimelineTrack> tracks =
            new List<ReferenceTimelineTrack>();
        private float duration = 10f;
        private float playbackSpeed = 1f;
        private bool loop;

        public event Action StateChanged;
        public event Action StructureChanged;

        public float Duration => duration;

        public float CurrentTime { get; private set; }

        public float PlaybackSpeed
        {
            get => playbackSpeed;
            set
            {
                var next = Mathf.Clamp(value, 0.05f, 8f);
                if (Mathf.Approximately(playbackSpeed, next))
                {
                    return;
                }

                playbackSpeed = next;
                StateChanged?.Invoke();
            }
        }

        public bool IsPlaying { get; private set; }

        public bool Loop
        {
            get => loop;
            set
            {
                if (loop == value)
                {
                    return;
                }

                loop = value;
                StateChanged?.Invoke();
            }
        }

        public IReadOnlyList<ReferenceTimelineTrack> Tracks => tracks;

        public void Play()
        {
            if (duration <= 0f)
            {
                return;
            }

            if (CurrentTime >= duration - 0.000001f)
            {
                Seek(0f);
            }

            if (!IsPlaying)
            {
                IsPlaying = true;
                StateChanged?.Invoke();
            }
        }

        public void Pause()
        {
            if (!IsPlaying)
            {
                return;
            }

            IsPlaying = false;
            StateChanged?.Invoke();
        }

        public void Stop()
        {
            IsPlaying = false;
            Seek(0f);
        }

        public void Seek(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, duration);
            Sample(CurrentTime);
            StateChanged?.Invoke();
        }

        public void SetTrackEnabled(int trackIndex, bool enabled)
        {
            if (!TryGetTrack(trackIndex, out var track) ||
                track.Enabled == enabled)
            {
                return;
            }

            track.Enabled = enabled;
            RebuildDescriptors();
            Sample(CurrentTime);
        }

        public int AddTrack(
            Transform target,
            ReferenceTimelineTrackKind kind)
        {
            if (target == null ||
                (kind != ReferenceTimelineTrackKind.Position &&
                 kind != ReferenceTimelineTrackKind.Rotation &&
                 kind != ReferenceTimelineTrackKind.Scale))
            {
                return -1;
            }

            for (var index = 0; index < authoredTracks.Count; index++)
            {
                if (authoredTracks[index].Target == target &&
                    authoredTracks[index].Kind == kind)
                {
                    return index;
                }
            }

            var track = new AuthoredTrack
            {
                Target = target,
                Kind = kind,
            };
            authoredTracks.Add(track);
            CaptureKeyframe(track, CurrentTime);
            RebuildDescriptors();
            return authoredTracks.Count - 1;
        }

        public bool AddOrUpdateKeyframe(int trackIndex)
        {
            if (!TryGetTrack(trackIndex, out var track) ||
                track.Target == null)
            {
                return false;
            }

            CaptureKeyframe(track, CurrentTime);
            RebuildDescriptors();
            return true;
        }

        public bool DeleteKeyframe(int trackIndex)
        {
            if (!TryGetTrack(trackIndex, out var track) ||
                track.Keyframes.Count == 0)
            {
                return false;
            }

            var nearestIndex = -1;
            var nearestDistance = float.MaxValue;
            for (var index = 0; index < track.Keyframes.Count; index++)
            {
                var distance = Mathf.Abs(
                    track.Keyframes[index].Time - CurrentTime);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }
            }

            var tolerance = Mathf.Max(0.001f, duration / 10000f);
            if (nearestIndex < 0 || nearestDistance > tolerance)
            {
                return false;
            }

            track.Keyframes.RemoveAt(nearestIndex);
            RebuildDescriptors();
            return true;
        }

        public bool DeleteTrack(int trackIndex)
        {
            if (trackIndex < 0 || trackIndex >= authoredTracks.Count)
            {
                return false;
            }

            authoredTracks.RemoveAt(trackIndex);
            RebuildDescriptors();
            return true;
        }

        public void SetDuration(float value)
        {
            var minimum = 0.1f;
            for (var trackIndex = 0;
                 trackIndex < authoredTracks.Count;
                 trackIndex++)
            {
                var keys = authoredTracks[trackIndex].Keyframes;
                if (keys.Count > 0)
                {
                    minimum = Mathf.Max(
                        minimum,
                        keys[keys.Count - 1].Time);
                }
            }

            var next = Mathf.Clamp(value, minimum, 3600f);
            if (Mathf.Approximately(duration, next))
            {
                return;
            }

            duration = next;
            CurrentTime = Mathf.Clamp(CurrentTime, 0f, duration);
            StructureChanged?.Invoke();
            StateChanged?.Invoke();
        }

        private void Update()
        {
            if (!IsPlaying || duration <= 0f)
            {
                return;
            }

            var next = CurrentTime + Time.deltaTime * playbackSpeed;
            if (next < duration)
            {
                CurrentTime = next;
            }
            else if (loop)
            {
                CurrentTime = Mathf.Repeat(next, duration);
            }
            else
            {
                CurrentTime = duration;
                IsPlaying = false;
            }

            Sample(CurrentTime);
            StateChanged?.Invoke();
        }

        private void CaptureKeyframe(AuthoredTrack track, float time)
        {
            var keyframe = FindKeyframe(track, time);
            if (keyframe == null)
            {
                keyframe = new AuthoredKeyframe
                {
                    Time = time,
                };
                track.Keyframes.Add(keyframe);
                track.Keyframes.Sort(
                    (left, right) => left.Time.CompareTo(right.Time));
            }

            switch (track.Kind)
            {
                case ReferenceTimelineTrackKind.Position:
                    keyframe.VectorValue = track.Target.localPosition;
                    break;
                case ReferenceTimelineTrackKind.Rotation:
                    keyframe.RotationValue = track.Target.localRotation;
                    break;
                case ReferenceTimelineTrackKind.Scale:
                    keyframe.VectorValue = track.Target.localScale;
                    break;
            }
        }

        private void Sample(float time)
        {
            for (var index = 0; index < authoredTracks.Count; index++)
            {
                var track = authoredTracks[index];
                if (!track.Enabled ||
                    track.Target == null ||
                    track.Keyframes.Count == 0)
                {
                    continue;
                }

                FindSegment(track.Keyframes, time, out var left, out var right);
                var leftKey = track.Keyframes[left];
                var rightKey = track.Keyframes[right];
                var amount = left == right
                    ? 0f
                    : Mathf.InverseLerp(leftKey.Time, rightKey.Time, time);
                switch (track.Kind)
                {
                    case ReferenceTimelineTrackKind.Position:
                        track.Target.localPosition = Vector3.LerpUnclamped(
                            leftKey.VectorValue,
                            rightKey.VectorValue,
                            amount);
                        break;
                    case ReferenceTimelineTrackKind.Rotation:
                        track.Target.localRotation = Quaternion.SlerpUnclamped(
                            leftKey.RotationValue,
                            rightKey.RotationValue,
                            amount);
                        break;
                    case ReferenceTimelineTrackKind.Scale:
                        track.Target.localScale = Vector3.LerpUnclamped(
                            leftKey.VectorValue,
                            rightKey.VectorValue,
                            amount);
                        break;
                }
            }
        }

        private void RebuildDescriptors()
        {
            tracks.Clear();
            for (var index = 0; index < authoredTracks.Count; index++)
            {
                var source = authoredTracks[index];
                var times = new float[source.Keyframes.Count];
                for (var keyIndex = 0;
                     keyIndex < source.Keyframes.Count;
                     keyIndex++)
                {
                    times[keyIndex] = source.Keyframes[keyIndex].Time;
                }

                tracks.Add(new ReferenceTimelineTrack(
                    index,
                    KindLabel(source.Kind),
                    BuildTargetPath(source.Target),
                    source.Kind,
                    times,
                    source.Enabled,
                    source.Target != null,
                    source.Target == null
                        ? "The authored target no longer exists."
                        : "Editable scene track."));
            }

            StructureChanged?.Invoke();
            StateChanged?.Invoke();
        }

        private bool TryGetTrack(int index, out AuthoredTrack track)
        {
            if (index >= 0 && index < authoredTracks.Count)
            {
                track = authoredTracks[index];
                return true;
            }

            track = null;
            return false;
        }

        private static AuthoredKeyframe FindKeyframe(
            AuthoredTrack track,
            float time)
        {
            for (var index = 0; index < track.Keyframes.Count; index++)
            {
                if (Mathf.Abs(track.Keyframes[index].Time - time) < 0.0001f)
                {
                    return track.Keyframes[index];
                }
            }

            return null;
        }

        private static void FindSegment(
            List<AuthoredKeyframe> keyframes,
            float time,
            out int left,
            out int right)
        {
            if (time <= keyframes[0].Time)
            {
                left = right = 0;
                return;
            }

            var last = keyframes.Count - 1;
            if (time >= keyframes[last].Time)
            {
                left = right = last;
                return;
            }

            right = 1;
            while (right < keyframes.Count &&
                   keyframes[right].Time < time)
            {
                right++;
            }

            left = right - 1;
        }

        private static string KindLabel(ReferenceTimelineTrackKind kind)
        {
            switch (kind)
            {
                case ReferenceTimelineTrackKind.Position:
                    return "Position";
                case ReferenceTimelineTrackKind.Rotation:
                    return "Rotation";
                case ReferenceTimelineTrackKind.Scale:
                    return "Scale";
                default:
                    return kind.ToString();
            }
        }

        private static string BuildTargetPath(Transform target)
        {
            if (target == null)
            {
                return "Missing target";
            }

            var path = target.name;
            var current = target.parent;
            while (current != null)
            {
                path = current.name + " / " + path;
                current = current.parent;
            }

            return path;
        }
    }
}
