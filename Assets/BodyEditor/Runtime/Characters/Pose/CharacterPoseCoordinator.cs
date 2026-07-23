using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.Characters
{
    public static class CharacterPoseStages
    {
        public const int ImportedFk = 100;
        public const int ImportedIk = 200;
        public const int Timeline = 300;
        public const int ActionEditing = 1000;
        public const int BodyConstraints = 2000;
        public const int EyeLook = 3000;
    }

    public interface ICharacterPoseModifier
    {
        int Order { get; }

        bool Enabled { get; }

        void Evaluate(CharacterPoseBuffer pose);
    }

    public interface ICharacterPosePipeline
    {
        event Action EvaluationStarting;

        event Action<CharacterPoseBuffer> PoseEvaluated;

        CharacterSkeleton Skeleton { get; }

        CharacterPoseBuffer Pose { get; }

        int ModifierCount { get; }

        bool IsInitialized { get; }

        void RegisterModifier(ICharacterPoseModifier modifier);

        CharacterPoseLayer CreateLayer(
            int order = CharacterPoseStages.ActionEditing,
            string name = null);

        void UnregisterModifier(ICharacterPoseModifier modifier);

        void EvaluateNow();
    }

    [DefaultExecutionOrder(30000)]
    [DisallowMultipleComponent]
    public sealed class CharacterPoseCoordinator :
        MonoBehaviour,
        ICharacterPosePipeline
    {
        private readonly List<ModifierEntry> modifiers =
            new List<ModifierEntry>();
        private CharacterSkeleton skeleton;
        private CharacterPoseBuffer pose;
        private long nextSequence;
        private bool evaluating;

        public event Action<CharacterPoseBuffer> PoseEvaluated;

        public event Action EvaluationStarting;

        public CharacterSkeleton Skeleton => skeleton;

        public CharacterPoseBuffer Pose => pose;

        public int ModifierCount => modifiers.Count;

        public bool IsInitialized => pose != null;

        public static CharacterPoseCoordinator Attach(
            GameObject root,
            CharacterSkeleton skeleton)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            var coordinator = root.GetComponent<CharacterPoseCoordinator>();
            if (coordinator == null)
            {
                coordinator = root.AddComponent<CharacterPoseCoordinator>();
            }

            coordinator.Initialize(skeleton);
            return coordinator;
        }

        public void Initialize(CharacterSkeleton value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (ReferenceEquals(skeleton, value) && pose != null)
            {
                return;
            }

            skeleton = value;
            pose = new CharacterPoseBuffer(value);
            modifiers.Clear();
            nextSequence = 0;
        }

        public void RegisterModifier(ICharacterPoseModifier modifier)
        {
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            for (var index = 0; index < modifiers.Count; index++)
            {
                if (ReferenceEquals(modifiers[index].Modifier, modifier))
                {
                    return;
                }
            }

            modifiers.Add(new ModifierEntry(modifier, nextSequence++));
            modifiers.Sort(CompareModifiers);
        }

        public CharacterPoseLayer CreateLayer(
            int order = CharacterPoseStages.ActionEditing,
            string name = null)
        {
            if (skeleton == null)
            {
                throw new InvalidOperationException(
                    "Pose coordinator has not been initialized.");
            }

            var layer = new CharacterPoseLayer(skeleton, order, name);
            RegisterModifier(layer);
            return layer;
        }

        public void UnregisterModifier(ICharacterPoseModifier modifier)
        {
            if (modifier == null)
            {
                return;
            }

            for (var index = modifiers.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(modifiers[index].Modifier, modifier))
                {
                    modifiers.RemoveAt(index);
                }
            }
        }

        public void EvaluateNow()
        {
            if (pose == null || evaluating)
            {
                return;
            }

            evaluating = true;
            try
            {
                EvaluationStarting?.Invoke();
                pose.Capture();
                for (var index = 0; index < modifiers.Count; index++)
                {
                    var modifier = modifiers[index].Modifier;
                    if (!modifier.Enabled)
                    {
                        continue;
                    }

                    try
                    {
                        modifier.Evaluate(pose);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                    }
                }

                pose.Apply();
                PoseEvaluated?.Invoke(pose);
            }
            finally
            {
                evaluating = false;
            }
        }

        private void LateUpdate()
        {
            if (modifiers.Count > 0 || EvaluationStarting != null ||
                PoseEvaluated != null)
            {
                EvaluateNow();
            }
        }

        private void OnDestroy()
        {
            modifiers.Clear();
            skeleton = null;
            pose = null;
            PoseEvaluated = null;
            EvaluationStarting = null;
        }

        private static int CompareModifiers(
            ModifierEntry left,
            ModifierEntry right)
        {
            var order = left.Modifier.Order.CompareTo(right.Modifier.Order);
            return order != 0
                ? order
                : left.Sequence.CompareTo(right.Sequence);
        }

        private readonly struct ModifierEntry
        {
            public ModifierEntry(ICharacterPoseModifier modifier, long sequence)
            {
                Modifier = modifier;
                Sequence = sequence;
            }

            public ICharacterPoseModifier Modifier { get; }

            public long Sequence { get; }
        }
    }
}
