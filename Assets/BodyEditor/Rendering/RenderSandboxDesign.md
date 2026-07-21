# Render Sandbox Design

## Purpose

The rendering layer is an authoring boundary for temporary or user-owned Unity
content. It does not try to normalize every imported material into one universal
shader model. Import adapters keep their source-specific data and provide a
baseline that is visible and reversible.

The sandbox adds scoped overrides on top of that baseline. A character, scene
object, environment root, or light rig can be edited independently. A missing
override leaves the imported object unchanged.

## Directory Layout

```text
Assets/BodyEditor/Rendering/
  Core/
    BodyEditor.Rendering.asmdef
    CharacterRenderMaterialContext.cs
    CharacterRenderSchemeRegistry.cs
    MaterialRenderUtility.cs
  RenderSchemes/
    DefaultAnime/
      BodyEditor.RenderScheme.DefaultAnime.asmdef
      DefaultAnimeCharacterRenderScheme.cs
      Resources/Shaders/BodyEditorAnimeCharacter.shader
```

`Core` contains small contracts and common material state helpers. Each render
scheme is isolated in its own directory and assembly. Community schemes can be
added without putting their shader-specific logic in an import adapter.

## Current Vertical Slice

The first slice extracts the existing Koikatsu character material style into
`DefaultAnimeCharacterRenderScheme`. The Koikatsu adapter still owns source
classification, card colors, baked eye textures, clothing textures, and bundle
lifetime. It asks the registry for the default scheme when it needs a character
material. The resulting shader and parameter defaults remain visually equivalent
to the previous implementation.

Non-character Koikatsu conversion remains on its existing fallback path until a
scene-object sandbox processor is added.

## Sandbox Application Model

1. Import or register a root GameObject.
2. Classify the root as character, scene object, environment, or light rig.
3. Scan renderers and material slots and capture original shared materials.
4. Apply ordered, scoped overrides. Exact slot rules take precedence over group
   rules; unmatched rules are reported and never guessed.
5. Own cloned working materials and restore the captured materials when the root
   is unloaded or the sandbox is reset.

Persistent scene projects record source object identity, transforms, category,
and override asset references. Unity Material assets hold shader parameters;
the project does not duplicate every shader property in its own format.

## External And Manual Content

Imported PMX and Koikatsu roots enter through the adapter load lifecycle. A user
owned FBX or prefab can enter through an explicit sandbox root component or a
selection command. Both paths produce the same scanned surface list.

Bindings use the strongest available identity: adapter material index, prefab or
asset identity, relative transform path, renderer index, and material slot. A
binding that no longer matches is surfaced for repair.

## Non-Goals

- No universal PBR or anime material schema.
- No runtime Shader Graph compiler.
- No automatic guessing when a model changes shape or naming.
- No global character style that mutates unrelated scene objects.
