using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using BodyEditor.Characters.Constraints;
using UnityEngine;

namespace BodyEditor.Editing
{
    [DisallowMultipleComponent]
    public sealed class CharacterBodyConstraintController : MonoBehaviour
    {
        private readonly List<CharacterBodyConstraintRig> rigs =
            new List<CharacterBodyConstraintRig>();
        private ICharacterModelSource characterSource;
        private bool enabledConstraints = true;

        public bool ConstraintsEnabled
        {
            get => enabledConstraints;
            set
            {
                if (enabledConstraints == value)
                {
                    return;
                }

                enabledConstraints = value;
                for (var index = 0; index < rigs.Count; index++)
                {
                    rigs[index].Enabled = value;
                    rigs[index].Model.PoseCoordinator?.EvaluateNow();
                }
            }
        }

        public int RigCount => rigs.Count;

        private void OnEnable()
        {
            TryBindCharacterSource();
            SynchronizeCharacters();
        }

        private void Update()
        {
            if (characterSource == null && TryBindCharacterSource())
            {
                SynchronizeCharacters();
            }
        }

        private bool TryBindCharacterSource()
        {
            var components = GetComponents<MonoBehaviour>();
            for (var index = 0; index < components.Length; index++)
            {
                if (!(components[index] is ICharacterModelSource source))
                {
                    continue;
                }

                BindCharacterSource(source);
                return true;
            }

            return false;
        }

        private void BindCharacterSource(ICharacterModelSource source)
        {
            if (ReferenceEquals(characterSource, source))
            {
                return;
            }

            if (characterSource != null)
            {
                characterSource.CharactersChanged -= SynchronizeCharacters;
            }

            characterSource = source;
            if (characterSource != null)
            {
                characterSource.CharactersChanged += SynchronizeCharacters;
            }
        }

        private void SynchronizeCharacters()
        {
            var models = characterSource?.CharacterModels ??
                         Array.Empty<ICharacterModel>();
            for (var index = rigs.Count - 1; index >= 0; index--)
            {
                if (ContainsModel(models, rigs[index].Model))
                {
                    continue;
                }

                rigs[index].Dispose();
                rigs.RemoveAt(index);
            }

            for (var index = 0; index < models.Count; index++)
            {
                var model = models[index];
                if (model == null || model.Root == null || ContainsRig(model) ||
                    (model.Features & CharacterModelFeatures.BodyConstraints) == 0 ||
                    !CharacterBodyConstraintRig.TryCreate(model, out var rig))
                {
                    continue;
                }

                rig.Enabled = enabledConstraints;
                rigs.Add(rig);
            }
        }

        private bool ContainsRig(ICharacterModel model)
        {
            for (var index = 0; index < rigs.Count; index++)
            {
                if (ReferenceEquals(rigs[index].Model, model))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsModel(
            IReadOnlyList<ICharacterModel> models,
            ICharacterModel model)
        {
            for (var index = 0; index < models.Count; index++)
            {
                if (ReferenceEquals(models[index], model))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDisable()
        {
            BindCharacterSource(null);
            for (var index = rigs.Count - 1; index >= 0; index--)
            {
                rigs[index].Dispose();
            }

            rigs.Clear();
        }
    }
}
