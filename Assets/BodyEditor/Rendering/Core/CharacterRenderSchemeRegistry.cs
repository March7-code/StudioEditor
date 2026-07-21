using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.Rendering
{
    public static class CharacterRenderSchemeRegistry
    {
        private static readonly Dictionary<string, ICharacterRenderScheme> schemes =
            new Dictionary<string, ICharacterRenderScheme>(StringComparer.Ordinal);

        private static string defaultSchemeId;

        public static string DefaultSchemeId => defaultSchemeId ?? string.Empty;

        public static void Register(
            ICharacterRenderScheme scheme,
            bool makeDefault = false)
        {
            if (scheme == null)
            {
                throw new ArgumentNullException(nameof(scheme));
            }

            if (string.IsNullOrWhiteSpace(scheme.Id))
            {
                throw new ArgumentException(
                    "A character render scheme must have a non-empty ID.",
                    nameof(scheme));
            }

            if (schemes.TryGetValue(scheme.Id, out var existing))
            {
                if (existing.GetType() != scheme.GetType())
                {
                    throw new InvalidOperationException(
                        $"Character render scheme ID '{scheme.Id}' is already registered by " +
                        $"{existing.GetType().FullName}.");
                }

                if (makeDefault)
                {
                    defaultSchemeId = scheme.Id;
                }

                return;
            }

            schemes.Add(scheme.Id, scheme);
            if (makeDefault || string.IsNullOrEmpty(defaultSchemeId))
            {
                defaultSchemeId = scheme.Id;
            }
        }

        public static bool TryGet(
            string id,
            out ICharacterRenderScheme scheme)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                scheme = null;
                return false;
            }

            return schemes.TryGetValue(id, out scheme);
        }

        public static ICharacterRenderScheme GetDefault()
        {
            if (string.IsNullOrEmpty(defaultSchemeId) ||
                !schemes.TryGetValue(defaultSchemeId, out var scheme))
            {
                throw new InvalidOperationException(
                    "No default character render scheme has been registered.");
            }

            return scheme;
        }

        public static IReadOnlyList<ICharacterRenderScheme> GetAll()
        {
            var values = new List<ICharacterRenderScheme>(schemes.Values);
            return values.AsReadOnly();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            schemes.Clear();
            defaultSchemeId = null;
        }
    }
}
