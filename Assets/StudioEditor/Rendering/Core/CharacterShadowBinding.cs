using UnityEngine;

namespace StudioEditor.Rendering
{
    // Explicit ownership keeps clothing shadows scoped to the imported character,
    // including outfits replaced or hidden after loading the scene card.
    public sealed class CharacterShadowBinding : MonoBehaviour { }
}
