using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public sealed class KoikatsuTimelinePlayer :
        MonoBehaviour,
        IReferenceModelTimelineController
    {
        private enum Channel
        {
            None,
            Position,
            Rotation,
            Scale,
        }

        private sealed class TrackBinding
        {
            public KoikatsuTimelineTrack Source;
            public Transform Target;
            public Channel Channel;
            public KoikatsuTimelineKeyframe[] Keyframes;
            public AnimationCurve[] Curves;
            public bool Enabled;
            public CharacterPoseCoordinator PoseCoordinator;
            public int PoseBoneIndex = -1;
        }

        private readonly List<TrackBinding> bindings =
            new List<TrackBinding>();
        private readonly List<ReferenceTimelineTrack> tracks =
            new List<ReferenceTimelineTrack>();
        private readonly Dictionary<CharacterPoseCoordinator, TimelinePoseModifier>
            poseModifiers =
                new Dictionary<CharacterPoseCoordinator, TimelinePoseModifier>();

        private IReadOnlyList<ReferenceTimelineTrack> readOnlyTracks;
        private float playbackSpeed = 1f;
        private bool loop;

        public event Action StateChanged;

        public float Duration { get; private set; }

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

        public IReadOnlyList<ReferenceTimelineTrack> Tracks =>
            readOnlyTracks ?? (readOnlyTracks = tracks.AsReadOnly());

        public static KoikatsuTimelinePlayer Attach(
            GameObject host,
            KoikatsuTimelineScene timeline,
            IReadOnlyList<GameObject> objectsByTimelineIndex)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            if (objectsByTimelineIndex == null)
            {
                throw new ArgumentNullException(nameof(objectsByTimelineIndex));
            }

            var player = host.AddComponent<KoikatsuTimelinePlayer>();
            player.Initialize(timeline, objectsByTimelineIndex);
            return player;
        }

        public void Play()
        {
            if (Duration <= 0f)
            {
                return;
            }

            if (CurrentTime >= Duration - 0.000001f)
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
            Sample(0f);
            StateChanged?.Invoke();
        }

        public void Seek(float time)
        {
            Sample(Mathf.Clamp(time, 0f, Duration));
            StateChanged?.Invoke();
        }

        public void SetTrackEnabled(int trackIndex, bool enabled)
        {
            if (trackIndex < 0 || trackIndex >= bindings.Count ||
                !tracks[trackIndex].Supported)
            {
                return;
            }

            bindings[trackIndex].Enabled = enabled;
            tracks[trackIndex].SetEnabled(enabled);
            var binding = bindings[trackIndex];
            if (binding.PoseCoordinator != null)
            {
                binding.PoseCoordinator.EvaluateNow();
            }
            else if (enabled)
            {
                SampleBinding(binding, CurrentTime);
            }

            StateChanged?.Invoke();
        }

        private void Initialize(
            KoikatsuTimelineScene timeline,
            IReadOnlyList<GameObject> objectsByTimelineIndex)
        {
            Duration = Mathf.Max(0f, timeline.Duration);
            playbackSpeed = Mathf.Clamp(
                timeline.TimeScale <= 0f ? 1f : timeline.TimeScale,
                0.05f,
                8f);

            for (var index = 0; index < timeline.Tracks.Count; index++)
            {
                AddBinding(index, timeline.Tracks[index], objectsByTimelineIndex);
            }

            Sample(0f);
            var supported = 0;
            for (var index = 0; index < tracks.Count; index++)
            {
                if (tracks[index].Supported)
                {
                    supported++;
                }
            }

            Debug.Log(
                $"Imported Koikatsu Timeline: {supported}/{tracks.Count} " +
                $"tracks bound, duration {Duration:0.###}s.",
                this);
        }

        private void AddBinding(
            int index,
            KoikatsuTimelineTrack source,
            IReadOnlyList<GameObject> objectsByTimelineIndex)
        {
            var binding = new TrackBinding
            {
                Source = source,
                Channel = Classify(source),
                Enabled = source.Enabled,
            };
            bindings.Add(binding);

            var kind = ToTrackKind(binding.Channel);
            var targetLabel = BuildTargetLabel(source);
            var supported = false;
            var status = string.Empty;

            if (binding.Channel == Channel.None)
            {
                status = $"Unsupported Koikatsu Timeline track: " +
                         $"{source.Owner}/{source.Id}.";
            }
            else if (source.Keyframes.Count == 0)
            {
                status = "Track has no keyframes.";
            }
            else if (!TryResolveTarget(
                         source,
                         objectsByTimelineIndex,
                         out binding.Target,
                         out status))
            {
                // Resolution provides the status shown by the timeline panel.
            }
            else if (!TryBuildKeyframes(binding, out status))
            {
                binding.Target = null;
            }
            else
            {
                BindPoseTarget(binding);
                supported = true;
                status = source.Enabled
                    ? "Imported from the scene card."
                    : "Disabled in the source scene card.";
            }

            var name = string.IsNullOrWhiteSpace(source.Alias)
                ? $"{source.Owner}/{source.Id}"
                : source.Alias;
            tracks.Add(new ReferenceTimelineTrack(
                index,
                name,
                targetLabel,
                supported ? kind : ReferenceTimelineTrackKind.Unsupported,
                source.Keyframes.Count,
                supported && source.Enabled,
                supported,
                status));
        }

        private void BindPoseTarget(TrackBinding binding)
        {
            var coordinator = binding.Target != null
                ? binding.Target.GetComponentInParent<CharacterPoseCoordinator>(true)
                : null;
            if (coordinator == null || coordinator.Skeleton == null ||
                !coordinator.Skeleton.TryGetBoneIndex(
                    binding.Target,
                    out var boneIndex))
            {
                return;
            }

            binding.PoseCoordinator = coordinator;
            binding.PoseBoneIndex = boneIndex;
            if (!poseModifiers.TryGetValue(coordinator, out var modifier))
            {
                modifier = new TimelinePoseModifier(this, coordinator);
                poseModifiers.Add(coordinator, modifier);
                coordinator.RegisterModifier(modifier);
            }

            modifier.Add(binding);
        }

        private static Channel Classify(KoikatsuTimelineTrack source)
        {
            switch (source.Id)
            {
                case "guideObjectPos":
                    return Channel.Position;
                case "guideObjectRot":
                    return Channel.Rotation;
                case "guideObjectScale":
                    return Channel.Scale;
            }

            if (!string.Equals(source.Owner, "KKPE", StringComparison.Ordinal))
            {
                return Channel.None;
            }

            switch (source.Id)
            {
                case "bonePos":
                    return Channel.Position;
                case "boneRot":
                    return Channel.Rotation;
                case "boneScale":
                    return Channel.Scale;
                default:
                    return Channel.None;
            }
        }

        private static ReferenceTimelineTrackKind ToTrackKind(Channel channel)
        {
            switch (channel)
            {
                case Channel.Position:
                    return ReferenceTimelineTrackKind.Position;
                case Channel.Rotation:
                    return ReferenceTimelineTrackKind.Rotation;
                case Channel.Scale:
                    return ReferenceTimelineTrackKind.Scale;
                default:
                    return ReferenceTimelineTrackKind.Unsupported;
            }
        }

        private static string BuildTargetLabel(KoikatsuTimelineTrack source)
        {
            var path = TrackPath(source);
            var objectLabel = source.ObjectIndex.HasValue
                ? source.ObjectIndex.Value.ToString()
                : "Scene";
            if (string.IsNullOrEmpty(path))
            {
                return objectLabel;
            }

            var separator = path.LastIndexOf('/');
            return $"{objectLabel}:" +
                   (separator >= 0 ? path.Substring(separator + 1) : path);
        }

        private static bool TryResolveTarget(
            KoikatsuTimelineTrack source,
            IReadOnlyList<GameObject> objectsByTimelineIndex,
            out Transform target,
            out string status)
        {
            target = null;
            if (!source.ObjectIndex.HasValue)
            {
                status = "Track has no scene object index.";
                return false;
            }

            var objectIndex = source.ObjectIndex.Value;
            if (objectIndex < 0 || objectIndex >= objectsByTimelineIndex.Count)
            {
                status = $"Scene object index {objectIndex} was not loaded " +
                         $"(object count: {objectsByTimelineIndex.Count}).";
                return false;
            }

            var targetObject = objectsByTimelineIndex[objectIndex];
            if (targetObject == null)
            {
                status = $"Scene object index {objectIndex} has no loaded object.";
                return false;
            }

            var path = TrackPath(source);
            target = ResolvePath(targetObject.transform, path);
            if (target == null)
            {
                status = $"Target path '{path}' was not found on scene object " +
                         $"{source.ObjectIndex.Value}.";
                return false;
            }

            status = string.Empty;
            return true;
        }

        private static string TrackPath(KoikatsuTimelineTrack source)
        {
            return string.Equals(source.Owner, "KKPE", StringComparison.Ordinal)
                ? source.GetAttribute("parameter")
                : source.GuideObjectPath;
        }

        private static Transform ResolvePath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return root;
            }

            path = path.Replace('\\', '/').Trim('/');
            var direct = root.Find(path);
            if (direct != null)
            {
                return direct;
            }

            var slash = path.IndexOf('/');
            if (slash >= 0 &&
                string.Equals(
                    path.Substring(0, slash),
                    root.name,
                    StringComparison.Ordinal))
            {
                direct = root.Find(path.Substring(slash + 1));
                if (direct != null)
                {
                    return direct;
                }
            }

            var candidates = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < candidates.Length; index++)
            {
                if (PathEndsAt(candidates[index], root, path))
                {
                    return candidates[index];
                }
            }

            return null;
        }

        private static bool PathEndsAt(
            Transform candidate,
            Transform root,
            string path)
        {
            var remaining = path.Length;
            var current = candidate;
            while (current != null && remaining > 0)
            {
                var name = current.name;
                var start = remaining - name.Length;
                if (start < 0 ||
                    !string.Equals(
                        path.Substring(start, name.Length),
                        name,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                remaining = start;
                if (remaining == 0)
                {
                    return true;
                }

                if (path[remaining - 1] != '/')
                {
                    return false;
                }

                remaining--;
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            return remaining == 0;
        }

        private static bool TryBuildKeyframes(
            TrackBinding binding,
            out string status)
        {
            binding.Keyframes = new KoikatsuTimelineKeyframe[
                binding.Source.Keyframes.Count];
            for (var index = 0; index < binding.Keyframes.Length; index++)
            {
                binding.Keyframes[index] = binding.Source.Keyframes[index];
                var valid = binding.Channel == Channel.Rotation
                    ? binding.Keyframes[index].TryGetQuaternion("value", out _)
                    : binding.Keyframes[index].TryGetVector3("value", out _);
                if (!valid)
                {
                    status = $"Keyframe {index} has no compatible value.";
                    binding.Keyframes = null;
                    return false;
                }
            }

            Array.Sort(
                binding.Keyframes,
                (left, right) => left.Time.CompareTo(right.Time));
            binding.Curves = new AnimationCurve[binding.Keyframes.Length];
            for (var index = 0; index < binding.Keyframes.Length; index++)
            {
                var sourceCurve = binding.Keyframes[index].Curve;
                if (sourceCurve.Count == 0)
                {
                    continue;
                }

                var curveKeys = new Keyframe[sourceCurve.Count];
                for (var curveIndex = 0;
                     curveIndex < sourceCurve.Count;
                     curveIndex++)
                {
                    var sourceKey = sourceCurve[curveIndex];
                    curveKeys[curveIndex] = new Keyframe(
                        sourceKey.Time,
                        sourceKey.Value,
                        sourceKey.InTangent,
                        sourceKey.OutTangent);
                }

                binding.Curves[index] = new AnimationCurve(curveKeys)
                {
                    preWrapMode = WrapMode.ClampForever,
                    postWrapMode = WrapMode.ClampForever,
                };
            }

            status = string.Empty;
            return true;
        }

        private void Update()
        {
            if (!IsPlaying || Duration <= 0f)
            {
                return;
            }

            var next = CurrentTime + Time.deltaTime * playbackSpeed;
            if (next < Duration)
            {
                Sample(next);
                StateChanged?.Invoke();
                return;
            }

            if (loop)
            {
                Sample(Mathf.Repeat(next, Duration));
            }
            else
            {
                Sample(Duration);
                IsPlaying = false;
            }

            StateChanged?.Invoke();
        }

        private void LateUpdate()
        {
            // Non-bone targets must update before the late pose coordinator uses
            // them as IK or controller targets.
            SampleDirectBindings(CurrentTime);
        }

        private void Sample(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, Duration);
            SampleDirectBindings(CurrentTime);
            EvaluatePoseCoordinators();
        }

        private void SampleDirectBindings(float time)
        {
            for (var index = 0; index < bindings.Count; index++)
            {
                if (bindings[index].Enabled &&
                    bindings[index].PoseCoordinator == null)
                {
                    SampleBinding(bindings[index], time);
                }
            }
        }

        private void EvaluatePoseCoordinators()
        {
            foreach (var pair in poseModifiers)
            {
                if (pair.Key != null)
                {
                    pair.Key.EvaluateNow();
                }
            }
        }

        private static void SampleBinding(
            TrackBinding binding,
            float time,
            CharacterPoseBuffer pose = null)
        {
            if (binding.Target == null || binding.Keyframes == null ||
                binding.Keyframes.Length == 0)
            {
                return;
            }

            FindSegment(binding.Keyframes, time, out var left, out var right);
            var leftKey = binding.Keyframes[left];
            var rightKey = binding.Keyframes[right];
            var factor = 0f;
            if (left != right)
            {
                factor = Mathf.InverseLerp(leftKey.Time, rightKey.Time, time);
                if (binding.Curves[left] != null)
                {
                    factor = binding.Curves[left].Evaluate(factor);
                }
            }

            if (binding.Channel == Channel.Rotation)
            {
                leftKey.TryGetQuaternion("value", out var leftValue);
                rightKey.TryGetQuaternion("value", out var rightValue);
                var rotationValue = Quaternion.SlerpUnclamped(
                    leftValue,
                    rightValue,
                    factor);
                if (pose != null)
                {
                    pose.SetLocalRotation(
                        binding.PoseBoneIndex,
                        rotationValue);
                }
                else
                {
                    binding.Target.localRotation = rotationValue;
                }

                return;
            }

            leftKey.TryGetVector3("value", out var leftVector);
            rightKey.TryGetVector3("value", out var rightVector);
            var value = Vector3.LerpUnclamped(leftVector, rightVector, factor);
            if (binding.Channel == Channel.Position)
            {
                if (pose != null)
                {
                    pose.SetLocalPosition(binding.PoseBoneIndex, value);
                }
                else
                {
                    binding.Target.localPosition = value;
                }
            }
            else if (pose != null)
            {
                pose.SetLocalScale(binding.PoseBoneIndex, value);
            }
            else
            {
                binding.Target.localScale = value;
            }
        }

        private static void FindSegment(
            KoikatsuTimelineKeyframe[] keys,
            float time,
            out int left,
            out int right)
        {
            if (time <= keys[0].Time)
            {
                left = 0;
                right = 0;
                return;
            }

            var last = keys.Length - 1;
            if (time >= keys[last].Time)
            {
                left = last;
                right = last;
                return;
            }

            var low = 0;
            var high = last;
            while (high - low > 1)
            {
                var middle = (low + high) / 2;
                if (keys[middle].Time <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            left = low;
            right = high;
        }

        private void OnDestroy()
        {
            IsPlaying = false;
            foreach (var pair in poseModifiers)
            {
                if (pair.Key != null)
                {
                    pair.Key.UnregisterModifier(pair.Value);
                }
            }

            poseModifiers.Clear();
            StateChanged = null;
        }

        private sealed class TimelinePoseModifier : ICharacterPoseModifier
        {
            private readonly KoikatsuTimelinePlayer owner;
            private readonly CharacterPoseCoordinator coordinator;
            private readonly List<TrackBinding> poseBindings =
                new List<TrackBinding>();

            public TimelinePoseModifier(
                KoikatsuTimelinePlayer owner,
                CharacterPoseCoordinator coordinator)
            {
                this.owner = owner;
                this.coordinator = coordinator;
            }

            public int Order => CharacterPoseStages.Timeline;

            public bool Enabled => owner != null && owner.isActiveAndEnabled;

            public void Add(TrackBinding binding)
            {
                poseBindings.Add(binding);
            }

            public void Evaluate(CharacterPoseBuffer pose)
            {
                if (coordinator == null ||
                    !ReferenceEquals(pose.Skeleton, coordinator.Skeleton))
                {
                    return;
                }

                for (var index = 0; index < poseBindings.Count; index++)
                {
                    if (poseBindings[index].Enabled)
                    {
                        SampleBinding(
                            poseBindings[index],
                            owner.CurrentTime,
                            pose);
                    }
                }
            }
        }
    }
}
