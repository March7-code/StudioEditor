# Body Editor

This folder contains the first minimal body-editor foundation.

Architecture planning for the new format-independent character runtime and optional
body self-collision system is documented in
`Assets/BodyEditor/CharacterModelBodyConstraintsPlan.md`.

## Current template

- Prefab: `Assets/BodyEditor/Templates/DefaultHumanoidSkeleton.prefab`
- Pose: T-pose
- Scale: meters, approximately 1.8 m tall
- Bone semantics: Unity `HumanBodyBones`
- Included bones: 22 body bones covering torso, head, shoulders, arms, hands, legs, feet, and toes
- Deliberately omitted for now: fingers, eyes, jaw, mesh, avatar, and animation controller

The `HumanoidSkeleton` component stores the semantic bone mapping, validates required bones and hierarchy relationships, and draws the skeleton with scene gizmos.

## Editor commands

- `Tools > Body Editor > Rebuild Default Humanoid Skeleton`
- `GameObject > Body Editor > Default Humanoid Skeleton`

The first command rebuilds the template prefab. The second instantiates it in the current scene.

## Next implementation step

The runtime UI Toolkit top bar imports reference models directly from disk.
`IReferenceModelFormatAdapter` isolates each file format; the current PMX adapter uses
Unity MMD Tools and keeps the PMX skeleton, skinning, morphs, materials, IK, and MMD
runtime components intact.

## Koikatsu adapter: model assembly stage

The Koikatsu adapter accepts female character-card PNGs as its primary input. It
parses the PNG container, block table, Custom, Coordinate, and Parameter blocks while
retaining all other block payloads (including KKEx) for later resolvers.

Koikatsu installation roots are configured in:

`Assets/BodyEditor/Adapters/Koikatsu/Resources/KoikatsuAdapterConfig.json`

Each entry points to a directory containing `abdata`, `mods`, and `UserData`. The
adapter first checks this table and then falls back to finding the `UserData` ancestor
of the imported card. Game AssetBundles remain read-only. Before Unity loads a legacy
bundle, the adapter creates a source-fingerprinted compatibility copy under
`Library/BodyEditor/KoikatsuBundles`. Only invalid legacy zero-count mip fields are
normalized; valid single- and multi-level textures remain untouched. Modified bundles
are cached with LZ4 compression so Unity 6 does not send invalid mip uploads to the
graphics driver without expanding ordinary game bundles. A fingerprinted clean marker
skips repeat inspection of bundles that need no changes. These cached bundles can also
be used as the input for later
resource export. The list catalog resolves card category/slot IDs to their `MainAB`,
`MainData`, texture, color-mask, and manifest values directly from the configured
installation. The adapter then reproduces
shared-skeleton assembly, same-name head transform copy, skinning rebind, fixed
renderer bounds, and the original bone-driven interpolation for all 44 body and 52
face shape parameters. Unsupported Shader Forge materials are replaced with preview
materials so the model remains visible in URP.

Vanilla hair uses the same resolver and part loader intended for zipmods. The resolver
accepts category, slot, and property identity and returns a bundle/prefab descriptor;
the loader owns bundle leases, instantiation, parent attachment, local TRS, optional
body/head skinning rebind, and material conversion. All five original hair slots are
supported and old four-slot cards receive the original default fifth slot. Static hair
geometry, card color, and local transforms are applied. Original `DynamicBone` and
`ChaCustomHairComponent` behavior and hair gloss/length shader behavior remain later
stages.

Every card Coordinate can be selected from the reference-model sidebar and is
assembled through the same part loader. It supports all nine clothing slots,
jacket/sailor sub-parts, original fallback IDs, body-skeleton rebind,
combined-outfit suppression rules, indoor/outdoor shoes, and Coordinate accessories. Clothing and
head list entries load their `MainTex`, secondary textures, and color masks directly
from the source bundles; card colors are used by the preview materials. Full
multi-color mask/pattern composition, body/face paint composition, and clothing alpha
masks remain later stages. Eye previews are
baked from the card-selected iris, gradient, upper/lower highlight, and eye-white
entries, including separate left/right colors and the card's UV settings. Zipmods
are resolved from the configured installation's `mods` directory. The adapter reads
Sideloader's cache, activates one version per manifest GUID, and builds the same
ordered virtual AssetBundle namespace used by Sideloader 21.1.2. Pure resource
override zipmods are indexed even when they have no character or Studio CSV. Asset
lookup checks virtual bundle overlays by asset name before the vanilla bundle, and
loose PNG/JPEG replacements use an indexed `abdata/<bundle>/<asset>` namespace.
Character UAR metadata and Studio item, pattern, map, and character-animation
metadata select the matching GUID/original slot before virtual asset lookup. Manifest
migration rules (`Migrate`, `MigrateAll`, and `StripAll`) are applied to character
UAR references when the replacement manifest is active, matching Sideloader's card
resolution behavior.

## Koikatsu Studio scene timelines

Studio scene-card import also reads the ExtendedSave `timeline/sceneInfo` payload.
When Timeline data is present, a bottom panel exposes play, pause, stop, seeking,
looping, playback speed, and per-track enable controls. The panel reports every
source track, including tracks that could not be bound, instead of silently dropping
them.

The current playback stage supports Timeline guide-object position, quaternion
rotation, and scale tracks plus KKPE bone position, rotation, and scale tracks. It
binds `objectIndex` to Studio's dictionary-key-sorted object order and resolves nested character
paths against the assembled hierarchy. Keyframe interpolation uses the curve stored
on the source keyframe. Camera, visibility, face, material, light, constraint, and
other plugin-owned value tracks remain visible as unsupported and are later stages.
