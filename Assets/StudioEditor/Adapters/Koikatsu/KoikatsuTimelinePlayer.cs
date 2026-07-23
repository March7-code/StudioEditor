using System;
using System.Collections.Generic;
using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.ReferenceModels
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
            EyesPattern,
            EyesOpenMax,
            EyebrowPattern,
            EyebrowOpenMax,
            MouthPattern,
            MouthOpenRate,
            LeftHandPose,
            RightHandPose,
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
            public ICharacterModel Character;
            public ICharacterPosePipeline CharacterPosePipeline;
            public ICharacterPatternController PatternController;
            public ICharacterMouthController MouthController;
            public ICharacterHandPoseController HandPoseController;
        }

        private readonly List<TrackBinding> bindings =
            new List<TrackBinding>();
        private readonly List<ReferenceTimelineTrack> tracks =
            new List<ReferenceTimelineTrack>();
        private readonly Dictionary<CharacterPoseCoordinator, TimelinePoseModifier>
            poseModifiers =
                new Dictionary<CharacterPoseCoordinator, TimelinePoseModifier>();
        private readonly List<KoikatsuStudioItemPose> itemPoses =
            new List<KoikatsuStudioItemPose>();

        private IReadOnlyList<ReferenceTimelineTrack> readOnlyTracks;
        private IReadOnlyList<ICharacterModel> characterModels =
            Array.Empty<ICharacterModel>();
        private KoikatsuStudioNodeConstraints nodeConstraints;
        private Func<ICharacterModel, int, int> resolveEyePattern;
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
            IReadOnlyList<GameObject> objectsByTimelineIndex,
            IReadOnlyList<ICharacterModel> characterModels = null,
            Func<ICharacterModel, int, int> eyePatternResolver = null)
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
            player.Initialize(
                timeline,
                objectsByTimelineIndex,
                characterModels ?? Array.Empty<ICharacterModel>(),
                eyePatternResolver);
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
            if (binding.PoseCoordinator == null && enabled)
            {
                SampleBinding(binding, CurrentTime);
            }

            EvaluatePosePipelines();

            StateChanged?.Invoke();
        }

        private void Initialize(
            KoikatsuTimelineScene timeline,
            IReadOnlyList<GameObject> objectsByTimelineIndex,
            IReadOnlyList<ICharacterModel> characterModels,
            Func<ICharacterModel, int, int> eyePatternResolver)
        {
            resolveEyePattern = eyePatternResolver;
            this.characterModels = characterModels ??
                Array.Empty<ICharacterModel>();
            nodeConstraints = GetComponent<KoikatsuStudioNodeConstraints>();
            var importedItemPoses = GetComponentsInChildren<
                KoikatsuStudioItemPose>(true);
            itemPoses.AddRange(importedItemPoses);
            Duration = Mathf.Max(0f, timeline.Duration);
            playbackSpeed = Mathf.Clamp(
                timeline.TimeScale <= 0f ? 1f : timeline.TimeScale,
                0.05f,
                8f);

            for (var index = 0; index < timeline.Tracks.Count; index++)
            {
                AddBinding(
                    index,
                    timeline.Tracks[index],
                    objectsByTimelineIndex,
                    characterModels);
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
            IReadOnlyList<GameObject> objectsByTimelineIndex,
            IReadOnlyList<ICharacterModel> characterModels)
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
            else if (IsCharacterChannel(binding.Channel) &&
                     (!TryResolveCharacter(
                          source,
                          objectsByTimelineIndex,
                          characterModels,
                          out binding.Character,
                          out status) ||
                      !TryBindCharacterController(binding, out status)))
            {
                // Resolution provides the status shown by the timeline panel.
            }
            else if (!IsCharacterChannel(binding.Channel) &&
                     !TryResolveTarget(
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
            var keyframeTimes = new float[source.Keyframes.Count];
            for (var keyIndex = 0;
                 keyIndex < source.Keyframes.Count;
                 keyIndex++)
            {
                keyframeTimes[keyIndex] = source.Keyframes[keyIndex].Time;
            }

            tracks.Add(new ReferenceTimelineTrack(
                index,
                name,
                targetLabel,
                supported ? kind : ReferenceTimelineTrackKind.Unsupported,
                keyframeTimes,
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
            if (string.Equals(source.Owner, "Timeline", StringComparison.Ordinal))
            {
                switch (source.Id)
                {
                    case "characterEyes":
                        return Channel.EyesPattern;
                    case "characterEyesOpen":
                        return Channel.EyesOpenMax;
                    case "characterEyebrows":
                        return Channel.EyebrowPattern;
                    case "characterEyebrowsOpen":
                        return Channel.EyebrowOpenMax;
                    case "characterMouth":
                        return Channel.MouthPattern;
                    case "characterMouthOpen":
                        return Channel.MouthOpenRate;
                    case "characterLeftHand":
                        return Channel.LeftHandPose;
                    case "characterRightHand":
                        return Channel.RightHandPose;
                }
            }

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
                case Channel.EyesPattern:
                case Channel.EyesOpenMax:
                case Channel.EyebrowPattern:
                case Channel.EyebrowOpenMax:
                case Channel.MouthPattern:
                case Channel.MouthOpenRate:
                case Channel.LeftHandPose:
                case Channel.RightHandPose:
                    return ReferenceTimelineTrackKind.Value;
                default:
                    return ReferenceTimelineTrackKind.Unsupported;
            }
        }

        private static bool IsCharacterChannel(Channel channel)
        {
            return channel >= Channel.EyesPattern &&
                   channel <= Channel.RightHandPose;
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

        private static bool TryResolveCharacter(
            KoikatsuTimelineTrack source,
            IReadOnlyList<GameObject> objectsByTimelineIndex,
            IReadOnlyList<ICharacterModel> characterModels,
            out ICharacterModel character,
            out string status)
        {
            character = null;
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

            for (var index = 0; index < characterModels.Count; index++)
            {
                var candidate = characterModels[index];
                var root = candidate?.Root;
                if (root == null)
                {
                    continue;
                }

                if (ReferenceEquals(root, targetObject) ||
                    root.transform.IsChildOf(targetObject.transform) ||
                    targetObject.transform.IsChildOf(root.transform))
                {
                    character = candidate;
                    status = string.Empty;
                    return true;
                }
            }

            status = $"Scene object index {objectIndex} is not a character.";
            return false;
        }

        private static bool TryBindCharacterController(
            TrackBinding binding,
            out string status)
        {
            status = string.Empty;
            var controls = binding.Character?.Controls;
            if (controls == null)
            {
                status = "Character has no control interface.";
                return false;
            }

            switch (binding.Channel)
            {
                case Channel.EyesPattern:
                case Channel.EyesOpenMax:
                    binding.PatternController = controls.Eyes?.Open;
                    break;
                case Channel.EyebrowPattern:
                case Channel.EyebrowOpenMax:
                    binding.PatternController = controls.Eyebrows;
                    break;
                case Channel.MouthPattern:
                case Channel.MouthOpenRate:
                    binding.MouthController = controls.Mouth;
                    binding.PatternController = binding.MouthController;
                    break;
                case Channel.LeftHandPose:
                case Channel.RightHandPose:
                    binding.HandPoseController = controls.Hands;
                    break;
            }

            binding.CharacterPosePipeline = controls.Pose?.Pipeline;

            if (binding.PatternController == null &&
                binding.HandPoseController == null)
            {
                status = "Character does not expose the requested control.";
                return false;
            }

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
                var valid = HasCompatibleValue(binding, binding.Keyframes[index]);
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

        private static bool HasCompatibleValue(
            TrackBinding binding,
            KoikatsuTimelineKeyframe keyframe)
        {
            switch (binding.Channel)
            {
                case Channel.Rotation:
                    return keyframe.TryGetQuaternion("value", out _);
                case Channel.Position:
                case Channel.Scale:
                    return keyframe.TryGetVector3("value", out _);
                case Channel.EyesPattern:
                case Channel.EyebrowPattern:
                case Channel.MouthPattern:
                case Channel.LeftHandPose:
                case Channel.RightHandPose:
                    return keyframe.TryGetInt("value", out _);
                case Channel.EyesOpenMax:
                case Channel.EyebrowOpenMax:
                case Channel.MouthOpenRate:
                    return keyframe.TryGetSingle("value", out _);
                default:
                    return false;
            }
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
            nodeConstraints?.EvaluateNow();
            for (var index = 0; index < itemPoses.Count; index++)
            {
                if (itemPoses[index] != null)
                {
                    itemPoses[index].EvaluateAfterTimeline();
                }
            }
        }

        private void Sample(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, Duration);
            SampleDirectBindings(CurrentTime);
            EvaluatePosePipelines();
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

        private void EvaluatePosePipelines()
        {
            nodeConstraints?.EvaluateNow();
            var evaluated = new HashSet<ICharacterPosePipeline>();

            for (var index = 0; index < characterModels.Count; index++)
            {
                var pipeline = characterModels[index]?.Controls?.Pose?.Pipeline;
                if (pipeline != null && evaluated.Add(pipeline))
                {
                    pipeline.EvaluateNow();
                }
            }

            foreach (var pair in poseModifiers)
            {
                if (pair.Key != null && evaluated.Add(pair.Key))
                {
                    pair.Key.EvaluateNow();
                }
            }

            for (var index = 0; index < itemPoses.Count; index++)
            {
                if (itemPoses[index] != null)
                {
                    itemPoses[index].EvaluateNow();
                }
            }
        }

        private bool SampleBinding(
            TrackBinding binding,
            float time,
            CharacterPoseBuffer pose = null)
        {
            if (binding.Keyframes == null || binding.Keyframes.Length == 0)
            {
                return false;
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

            if (IsCharacterChannel(binding.Channel))
            {
                return SampleCharacterBinding(
                    binding,
                    leftKey,
                    rightKey,
                    factor);
            }

            if (binding.Target == null)
            {
                return false;
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

                return true;
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

            return true;
        }

        private bool SampleCharacterBinding(
            TrackBinding binding,
            KoikatsuTimelineKeyframe leftKey,
            KoikatsuTimelineKeyframe rightKey,
            float factor)
        {
            switch (binding.Channel)
            {
                case Channel.EyesPattern:
                    if (!leftKey.TryGetInt("value", out var eyeSetId))
                    {
                        return false;
                    }

                    return ApplyPattern(
                        binding.PatternController,
                        resolveEyePattern != null
                            ? resolveEyePattern(binding.Character, eyeSetId)
                            : eyeSetId);
                case Channel.EyebrowPattern:
                case Channel.MouthPattern:
                    return leftKey.TryGetInt("value", out var pattern) &&
                           ApplyPattern(binding.PatternController, pattern);
                case Channel.EyesOpenMax:
                case Channel.EyebrowOpenMax:
                    return TrySampleFloat(
                               leftKey,
                               rightKey,
                               factor,
                               out var openMax) &&
                           ApplyOpenMax(binding.PatternController, openMax);
                case Channel.MouthOpenRate:
                    return TrySampleFloat(
                               leftKey,
                               rightKey,
                               factor,
                               out var openRate) &&
                           ApplyFixedOpenRate(
                               binding.MouthController,
                               openRate);
                case Channel.LeftHandPose:
                    return leftKey.TryGetInt("value", out var leftPose) &&
                           ApplyHandPose(
                               binding.HandPoseController,
                               CharacterHand.Left,
                               leftPose);
                case Channel.RightHandPose:
                    return leftKey.TryGetInt("value", out var rightPose) &&
                           ApplyHandPose(
                               binding.HandPoseController,
                               CharacterHand.Right,
                               rightPose);
                default:
                    return false;
            }
        }

        private static bool TrySampleFloat(
            KoikatsuTimelineKeyframe leftKey,
            KoikatsuTimelineKeyframe rightKey,
            float factor,
            out float value)
        {
            value = 0f;
            if (!leftKey.TryGetSingle("value", out var leftValue) ||
                !rightKey.TryGetSingle("value", out var rightValue))
            {
                return false;
            }

            value = Mathf.LerpUnclamped(leftValue, rightValue, factor);
            return true;
        }

        private static bool ApplyPattern(
            ICharacterPatternController controller,
            int pattern)
        {
            if (controller == null || controller.PatternCount == 0)
            {
                return false;
            }

            controller.SetPattern(
                Mathf.Clamp(pattern, 0, controller.PatternCount - 1),
                false);
            return true;
        }

        private static bool ApplyOpenMax(
            ICharacterPatternController controller,
            float value)
        {
            if (controller == null)
            {
                return false;
            }

            controller.SetOpenMax(value);
            return true;
        }

        private static bool ApplyFixedOpenRate(
            ICharacterMouthController controller,
            float value)
        {
            if (controller == null)
            {
                return false;
            }

            controller.SetFixedOpenRate(value);
            return true;
        }

        private static bool ApplyHandPose(
            ICharacterHandPoseController controller,
            CharacterHand hand,
            int pose)
        {
            if (controller == null)
            {
                return false;
            }

            if (pose < 0)
            {
                controller.ClearPose(hand);
                return true;
            }

            var poseCount = controller.GetPoseCount(hand);
            if (poseCount == 0)
            {
                return false;
            }

            controller.SetPose(hand, Mathf.Clamp(pose, 0, poseCount - 1));
            return true;
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
                        owner.SampleBinding(
                            poseBindings[index],
                            owner.CurrentTime,
                            pose);
                    }
                }
            }
        }
    }
}
