using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.Characters
{
    [DisallowMultipleComponent]
    public sealed class CascadeurAnimationPlayer :
        MonoBehaviour,
        ICharacterPoseModifier
    {
        private readonly List<BoneBinding> bindings = new List<BoneBinding>();
        private ICharacterPosePipeline pipeline;
        private AnimationClip clip;
        private GameObject sampleRoot;
        private float time;

        public int Order => CharacterPoseStages.ActionEditing;

        public bool Enabled { get; set; } = true;

        public bool IsPlaying { get; private set; }

        public bool Loop { get; set; } = true;

        public float PlaybackSpeed { get; set; } = 1f;

        public float CurrentTime => time;

        public float Duration => clip != null ? clip.length : 0f;

        public string ClipName => clip != null ? clip.name : string.Empty;

        public int BoundBoneCount => bindings.Count;

        public void Initialize(
            ICharacterModel target,
            AnimationClip animationClip,
            GameObject sourcePrefab,
            string sourceCharacterPath,
            IReadOnlyDictionary<string, CharacterPoseChannels> animatedPaths)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (animationClip == null)
            {
                throw new ArgumentNullException(nameof(animationClip));
            }

            if (sourcePrefab == null)
            {
                throw new ArgumentNullException(nameof(sourcePrefab));
            }

            var nextPipeline = target.Controls?.Pose?.Pipeline;
            if (nextPipeline == null || target.Skeleton == null)
            {
                throw new InvalidOperationException(
                    "The target character has no pose pipeline.");
            }

            Release();
            pipeline = nextPipeline;
            clip = animationClip;
            sampleRoot = Instantiate(sourcePrefab);
            sampleRoot.name = $"Cascadeur Sample - {target.DisplayName}";
            sampleRoot.hideFlags = HideFlags.HideAndDontSave;
            DisableSampleRenderers(sampleRoot);

            var sourceCharacter = ResolveSourceCharacter(
                sampleRoot.transform,
                sourceCharacterPath,
                target.Root != null ? target.Root.name : string.Empty);
            BuildBindings(
                target,
                sourceCharacter,
                animatedPaths ??
                new Dictionary<string, CharacterPoseChannels>());
            if (bindings.Count == 0)
            {
                Release();
                throw new InvalidOperationException(
                    "No animated Cascadeur bones matched the target character.");
            }

            time = 0f;
            IsPlaying = false;
            pipeline.RegisterModifier(this);
            pipeline.EvaluateNow();
        }

        public void Play()
        {
            if (clip == null)
            {
                return;
            }

            if (time >= Duration - 0.000001f)
            {
                time = 0f;
            }

            IsPlaying = true;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void Stop()
        {
            IsPlaying = false;
            time = 0f;
            pipeline?.EvaluateNow();
        }

        public void Seek(float value)
        {
            time = Mathf.Clamp(value, 0f, Duration);
            pipeline?.EvaluateNow();
        }

        public void Evaluate(CharacterPoseBuffer pose)
        {
            if (clip == null || sampleRoot == null)
            {
                return;
            }

            clip.SampleAnimation(sampleRoot, time);
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding.Source == null)
                {
                    continue;
                }

                if ((binding.Channels & CharacterPoseChannels.Position) != 0)
                {
                    pose.SetLocalPosition(
                        binding.TargetBoneIndex,
                        binding.Source.localPosition);
                }

                if ((binding.Channels & CharacterPoseChannels.Rotation) != 0)
                {
                    pose.SetLocalRotation(
                        binding.TargetBoneIndex,
                        binding.Source.localRotation);
                }

                if ((binding.Channels & CharacterPoseChannels.Scale) != 0)
                {
                    pose.SetLocalScale(
                        binding.TargetBoneIndex,
                        binding.Source.localScale);
                }
            }
        }

        private void Update()
        {
            if (!IsPlaying || clip == null || Duration <= 0f)
            {
                return;
            }

            var next = time + Time.deltaTime * PlaybackSpeed;
            if (Loop)
            {
                time = Mathf.Repeat(next, Duration);
            }
            else if (next >= Duration)
            {
                time = Duration;
                IsPlaying = false;
            }
            else
            {
                time = Mathf.Max(0f, next);
            }
        }

        private void BuildBindings(
            ICharacterModel target,
            Transform sourceCharacter,
            IReadOnlyDictionary<string, CharacterPoseChannels> animatedPaths)
        {
            bindings.Clear();
            var targetRoot = target.Root.transform;
            var sourceRoot = sampleRoot.transform;
            var sourceByName = BuildUniqueNameMap(sourceCharacter);
            for (var index = 0; index < target.Skeleton.BoneCount; index++)
            {
                var targetBone = target.Skeleton.Bones[index];
                var relativePath = GetRelativePath(
                    targetRoot,
                    targetBone.Transform);
                var source = string.IsNullOrEmpty(relativePath)
                    ? sourceCharacter
                    : sourceCharacter.Find(relativePath);
                if (source == null)
                {
                    sourceByName.TryGetValue(targetBone.Name, out source);
                }

                if (source == null)
                {
                    continue;
                }

                var sourcePath = GetRelativePath(sourceRoot, source);
                if (!animatedPaths.TryGetValue(sourcePath, out var channels) ||
                    channels == CharacterPoseChannels.None)
                {
                    continue;
                }

                bindings.Add(new BoneBinding(index, source, channels));
            }
        }

        private static Transform ResolveSourceCharacter(
            Transform root,
            string path,
            string targetName)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var exact = root.Find(path);
                if (exact != null)
                {
                    return exact;
                }
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (string.Equals(
                    transforms[index].name,
                    targetName,
                    StringComparison.Ordinal))
                {
                    return transforms[index];
                }
            }

            return root;
        }

        private static Dictionary<string, Transform> BuildUniqueNameMap(
            Transform root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                var name = transforms[index].name;
                if (duplicates.Contains(name))
                {
                    continue;
                }

                if (result.ContainsKey(name))
                {
                    result.Remove(name);
                    duplicates.Add(name);
                }
                else
                {
                    result.Add(name, transforms[index]);
                }
            }

            return result;
        }

        private static string GetRelativePath(Transform root, Transform value)
        {
            if (ReferenceEquals(root, value))
            {
                return string.Empty;
            }

            var names = new List<string>();
            var current = value;
            while (current != null && !ReferenceEquals(current, root))
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current == null)
            {
                return string.Empty;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static void DisableSampleRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = false;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private void Release()
        {
            pipeline?.UnregisterModifier(this);
            pipeline = null;
            bindings.Clear();
            clip = null;
            IsPlaying = false;
            if (sampleRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(sampleRoot);
                }
                else
                {
                    DestroyImmediate(sampleRoot);
                }

                sampleRoot = null;
            }
        }

        private void OnDestroy()
        {
            Release();
        }

        private readonly struct BoneBinding
        {
            public BoneBinding(
                int targetBoneIndex,
                Transform source,
                CharacterPoseChannels channels)
            {
                TargetBoneIndex = targetBoneIndex;
                Source = source;
                Channels = channels;
            }

            public int TargetBoneIndex { get; }

            public Transform Source { get; }

            public CharacterPoseChannels Channels { get; }
        }
    }
}
