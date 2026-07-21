using UnityEngine;

namespace BodyEditor.Rendering
{
    public enum CharacterRenderSurfaceRole
    {
        Unknown,
        Skin,
        Face,
        Hair,
        Clothes,
        Accessory,
    }

    public readonly struct CharacterRenderMaterialContext
    {
        public CharacterRenderMaterialContext(
            Material sourceMaterial,
            CharacterRenderSurfaceRole role,
            string materialKey,
            string materialName,
            Color baseColor,
            Color? requestedOutlineColor)
        {
            SourceMaterial = sourceMaterial;
            Role = role;
            MaterialKey = materialKey ?? string.Empty;
            MaterialName = materialName ?? string.Empty;
            BaseColor = baseColor;
            RequestedOutlineColor = requestedOutlineColor;
        }

        public Material SourceMaterial { get; }

        public CharacterRenderSurfaceRole Role { get; }

        public string MaterialKey { get; }

        public string MaterialName { get; }

        public Color BaseColor { get; }

        public Color? RequestedOutlineColor { get; }
    }

    public interface ICharacterRenderScheme
    {
        string Id { get; }

        Material CreateMaterial(CharacterRenderMaterialContext context);
    }
}
