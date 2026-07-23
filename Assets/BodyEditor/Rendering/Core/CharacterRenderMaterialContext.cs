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
            Color? requestedOutlineColor,
            Color? secondaryColor = null,
            Color? tertiaryColor = null,
            Color? quaternaryColor = null,
            bool useVertexColorChannels = false,
            Texture hairGlossTexture = null,
            Texture toonRampTexture = null)
        {
            SourceMaterial = sourceMaterial;
            Role = role;
            MaterialKey = materialKey ?? string.Empty;
            MaterialName = materialName ?? string.Empty;
            BaseColor = baseColor;
            RequestedOutlineColor = requestedOutlineColor;
            SecondaryColor = secondaryColor;
            TertiaryColor = tertiaryColor;
            QuaternaryColor = quaternaryColor;
            UseVertexColorChannels = useVertexColorChannels;
            HairGlossTexture = hairGlossTexture;
            ToonRampTexture = toonRampTexture;
        }

        public Material SourceMaterial { get; }

        public CharacterRenderSurfaceRole Role { get; }

        public string MaterialKey { get; }

        public string MaterialName { get; }

        public Color BaseColor { get; }

        public Color? RequestedOutlineColor { get; }

        // Optional source color channels used by materials such as Koikatsu
        // hair, whose shader receives base/start/end colors separately.
        public Color? SecondaryColor { get; }

        public Color? TertiaryColor { get; }

        public Color? QuaternaryColor { get; }

        public bool UseVertexColorChannels { get; }

        public Texture HairGlossTexture { get; }

        // This texture must follow the BodyEditor toon-ramp layout. Source
        // shader properties with similar names are not necessarily compatible.
        public Texture ToonRampTexture { get; }
    }

    public interface ICharacterRenderScheme
    {
        string Id { get; }

        Material CreateMaterial(CharacterRenderMaterialContext context);
    }
}
