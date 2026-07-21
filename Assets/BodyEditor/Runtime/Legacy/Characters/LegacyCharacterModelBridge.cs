using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using BodyEditor.ReferenceModels;
using UnityEngine;

namespace BodyEditor.Characters.Legacy
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ReferenceModelImportController))]
    public sealed class LegacyCharacterModelBridge :
        MonoBehaviour,
        ICharacterModelSource
    {
        private static readonly IReadOnlyList<ICharacterModel> emptyCharacters =
            Array.Empty<ICharacterModel>();

        private ReferenceModelImportController importController;
        private IReferenceModelInstance currentImport;

        public event Action CharactersChanged;

        public IReadOnlyList<ICharacterModel> CharacterModels { get; private set; } =
            emptyCharacters;

        private void OnEnable()
        {
            importController = GetComponent<ReferenceModelImportController>();
            importController.StateChanged += HandleImportStateChanged;
            SynchronizeCharacter();
        }

        private void HandleImportStateChanged()
        {
            SynchronizeCharacter();
        }

        private void SynchronizeCharacter()
        {
            var nextImport = importController?.Current;
            if (ReferenceEquals(currentImport, nextImport))
            {
                return;
            }

            currentImport = nextImport;
            if (nextImport is ICharacterModelCollection collection)
            {
                CharacterModels = collection.CharacterModels ?? emptyCharacters;
            }
            else if (nextImport is ICharacterModel character)
            {
                CharacterModels = Array.AsReadOnly(new[] { character });
            }
            else
            {
                CharacterModels = emptyCharacters;
            }

            CharactersChanged?.Invoke();
        }

        private void OnDisable()
        {
            if (importController != null)
            {
                importController.StateChanged -= HandleImportStateChanged;
            }

            importController = null;
            currentImport = null;
            if (CharacterModels.Count > 0)
            {
                CharacterModels = emptyCharacters;
                CharactersChanged?.Invoke();
            }
        }

        private void OnDestroy()
        {
            CharactersChanged = null;
        }
    }
}
