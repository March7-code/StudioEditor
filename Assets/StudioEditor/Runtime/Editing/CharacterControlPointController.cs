using System;
using System.Collections.Generic;
using StudioEditor.Characters;
using StudioEditor.Characters.Controls;
using UnityEngine;

namespace StudioEditor.Editing
{
    [DefaultExecutionOrder(31000)]
    [DisallowMultipleComponent]
    public sealed class CharacterControlPointController : MonoBehaviour
    {
        private readonly List<CharacterControlRig> rigs =
            new List<CharacterControlRig>();
        private ICharacterModelSource characterSource;
        private CharacterControlRig selectedRig;
        private CharacterControlPoint? selectedPoint;

        public event Action SelectionChanged;

        public IReadOnlyList<CharacterControlRig> Rigs => rigs;

        public CharacterControlRig SelectedRig => selectedRig;

        public ICharacterModel SelectedCharacter => selectedRig?.Model;

        public CharacterControlPoint? SelectedControlPoint => selectedPoint;

        private void OnEnable()
        {
            TryBindCharacterSource();
            SynchronizeCharacters();
        }

        private void LateUpdate()
        {
            if (characterSource == null && TryBindCharacterSource())
            {
                SynchronizeCharacters();
            }
        }

        public void RefreshCharacters()
        {
            if (characterSource == null)
            {
                TryBindCharacterSource();
            }

            SynchronizeCharacters();
        }

        public void SelectControlPoint(
            CharacterControlRig rig,
            CharacterControlPoint? point)
        {
            if (rig != null && !rigs.Contains(rig))
            {
                return;
            }

            if (ReferenceEquals(selectedRig, rig) && selectedPoint == point)
            {
                return;
            }

            selectedRig = rig;
            selectedPoint = rig != null ? point : null;
            SelectionChanged?.Invoke();
        }

        public bool ClearSelectedControlPoint()
        {
            if (selectedRig == null || !selectedPoint.HasValue)
            {
                return false;
            }

            return selectedRig.ClearTarget(selectedPoint.Value);
        }

        public void ClearAllControlPoints()
        {
            for (var index = 0; index < rigs.Count; index++)
            {
                rigs[index].ClearTargets();
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

                if (ReferenceEquals(selectedRig, rigs[index]))
                {
                    selectedRig = null;
                    selectedPoint = null;
                    SelectionChanged?.Invoke();
                }

                rigs[index].Dispose();
                rigs.RemoveAt(index);
            }

            for (var index = 0; index < models.Count; index++)
            {
                var model = models[index];
                if (model == null || model.Root == null ||
                    model.Controls?.Pose?.Pipeline == null ||
                    model.Skeleton == null ||
                    ContainsRig(model))
                {
                    continue;
                }

                try
                {
                    var rig = new CharacterControlRig(model);
                    if (rig.ControlPoints.Count == 0)
                    {
                        rig.Dispose();
                        continue;
                    }

                    rigs.Add(rig);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
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
            selectedRig = null;
            selectedPoint = null;
            for (var index = rigs.Count - 1; index >= 0; index--)
            {
                rigs[index].Dispose();
            }

            rigs.Clear();
        }

        private void OnDestroy()
        {
            SelectionChanged = null;
        }
    }
}
