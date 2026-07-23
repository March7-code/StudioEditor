using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.Characters
{
    public sealed class CharacterGeometry
    {
        public CharacterGeometry(
            IEnumerable<SkinnedMeshRenderer> bodyRenderers,
            IEnumerable<SkinnedMeshRenderer> headRenderers)
        {
            BodyRenderers = CopyRenderers(bodyRenderers);
            HeadRenderers = CopyRenderers(headRenderers);

            var anatomy = new List<SkinnedMeshRenderer>(
                BodyRenderers.Count + HeadRenderers.Count);
            var seen = new HashSet<SkinnedMeshRenderer>();
            AddUnique(BodyRenderers, anatomy, seen);
            AddUnique(HeadRenderers, anatomy, seen);
            AnatomyRenderers = anatomy.AsReadOnly();
        }

        public static CharacterGeometry Empty { get; } =
            new CharacterGeometry(
                Array.Empty<SkinnedMeshRenderer>(),
                Array.Empty<SkinnedMeshRenderer>());

        public IReadOnlyList<SkinnedMeshRenderer> BodyRenderers { get; }

        public IReadOnlyList<SkinnedMeshRenderer> HeadRenderers { get; }

        public IReadOnlyList<SkinnedMeshRenderer> AnatomyRenderers { get; }

        public bool HasAnatomyGeometry => AnatomyRenderers.Count > 0;

        private static IReadOnlyList<SkinnedMeshRenderer> CopyRenderers(
            IEnumerable<SkinnedMeshRenderer> source)
        {
            if (source == null)
            {
                return Array.Empty<SkinnedMeshRenderer>();
            }

            var result = new List<SkinnedMeshRenderer>();
            var seen = new HashSet<SkinnedMeshRenderer>();
            foreach (var renderer in source)
            {
                if (renderer != null && seen.Add(renderer))
                {
                    result.Add(renderer);
                }
            }

            return result.AsReadOnly();
        }

        private static void AddUnique(
            IReadOnlyList<SkinnedMeshRenderer> source,
            ICollection<SkinnedMeshRenderer> destination,
            ISet<SkinnedMeshRenderer> seen)
        {
            for (var index = 0; index < source.Count; index++)
            {
                if (seen.Add(source[index]))
                {
                    destination.Add(source[index]);
                }
            }
        }
    }
}
