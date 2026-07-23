using System;
using System.Collections.Generic;
using BodyEditor.Characters;
using BodyEditor.ReferenceModels;
using UnityEngine;

namespace BodyEditor.Characters.Legacy
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SceneContentController))]
    public sealed class LegacyCharacterModelBridge :
        MonoBehaviour,
        ICharacterModelSource
    {
        private static readonly IReadOnlyList<ICharacterModel> emptyCharacters =
            Array.Empty<ICharacterModel>();

        private SceneContentController importController;
        public event Action CharactersChanged;

        public IReadOnlyList<ICharacterModel> CharacterModels { get; private set; } =
            emptyCharacters;

        private void OnEnable()
        {
            importController = GetComponent<SceneContentController>();
            importController.StateChanged += HandleImportStateChanged;
            SynchronizeCharacter();
        }

        private void HandleImportStateChanged()
        {
            SynchronizeCharacter();
        }

        private void SynchronizeCharacter()
        {
            CharacterModels = importController?.CharacterModels ??
                              emptyCharacters;

            CharactersChanged?.Invoke();
        }

        private void OnDisable()
        {
            if (importController != null)
            {
                importController.StateChanged -= HandleImportStateChanged;
            }

            importController = null;
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
