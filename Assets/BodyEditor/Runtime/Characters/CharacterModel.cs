using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyEditor.Characters
{
    [Flags]
    public enum CharacterModelFeatures
    {
        None = 0,
        SemanticSkeleton = 1 << 0,
        AnatomyGeometry = 1 << 1,
        PosePipeline = 1 << 2,
        BodyConstraints = 1 << 3,
    }

    public interface ICharacterModel : IDisposable
    {
        string DisplayName { get; }

        GameObject Root { get; }

        CharacterSkeleton Skeleton { get; }

        CharacterGeometry Geometry { get; }

        CharacterPoseCoordinator PoseCoordinator { get; }

        CharacterModelFeatures Features { get; }
    }

    public interface ICharacterModelSource
    {
        event Action CharactersChanged;

        IReadOnlyList<ICharacterModel> CharacterModels { get; }
    }

    public interface ICharacterModelCollection
    {
        IReadOnlyList<ICharacterModel> CharacterModels { get; }
    }
}
