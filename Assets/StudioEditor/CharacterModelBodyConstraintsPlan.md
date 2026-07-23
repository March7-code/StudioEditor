# Character Model Body Constraint System Plan

## Status

- Document type: architecture and implementation plan
- Implementation status: Phase 2 core pose pipeline and kinematic controls
  implemented; preliminary passive body-collision slice implemented
- First supported character source: Koikatsu
- Runtime target: imported character models inside Studio Editor
- Explicitly excluded from the first version: clothing collision and soft-body simulation

Current foundation slice:

- Added the independent `StudioEditor.Characters` runtime assembly.
- Added format-independent character model, semantic skeleton, anatomy geometry, and
  character collection contracts.
- Koikatsu card imports now expose a native character model capability.
- Koikatsu Studio scene imports expose every imported character as a model collection.
- Added a temporary Legacy bridge from the current import lifecycle.
- Added a full-skeleton `CharacterPoseBuffer` with local and world-space access.
- Added ordered pose modifiers and a persistent `CharacterPoseLayer` intended for
  action editing.
- Every Koikatsu character now owns a `CharacterPoseCoordinator`.
- Koikatsu Studio FK and IK now run as ordered pose modifiers.
- Timeline tracks targeting character transforms now run in the Timeline pose stage;
  ordinary scene objects and IK targets remain direct Timeline bindings.
- Action-editing layers run after imported animation, FK, IK, and Timeline.
- Added format-independent kinematic control targets for hips, chest, head, wrists,
  and feet, plus elbow and knee pole targets. Shoulder targets are drafted but remain
  disabled until pose inference and clavicle limits are available. Wrist targets solve
  the upper/lower arm chain only; finger bones remain owned by the imported pose.
- Selected positional controls expose world-space XYZ movement axes. Hips, chest,
  head, wrist, and foot targets also expose world-space XYZ rotation rings.
- Eye-look control is intentionally deferred. Anime pupils are a source-specific
  appearance capability and must not be implemented by rotating the head control.
- Control targets follow the imported pose until first manipulation, then become
  world-space Hold targets. Clearing a target returns its affected bones to the pose
  captured when that target was activated.
- Added an analytic body-collision modifier in the `BodyConstraints` stage. It fits
  conservative head, torso, and pelvis volumes, then projects elbow, hand, knee, foot,
  and limb-segment samples out of those volumes before writing corrected limb IK.
- Control-point IK and body-collision correction now share one two-bone solver and one
  humanoid chain configuration. Elbows and knees keep their bind-pose bend side and
  use hard bend ranges of `2-165` and `2-155` degrees respectively.
- The preliminary collision slice creates no Rigidbody, Joint, or Unity Collider and
  remains deterministic during Timeline seeking. Clothing, hair, fingers, the floor,
  limb-to-limb pairs, and inter-character collision remain excluded.

## Decision

The VaM design is suitable as a reference, but the implementation should not be
attached to the Koikatsu adapter or the legacy `ReferenceModel` API.

The new system will be built around a format-independent `CharacterModel` runtime.
An importer creates a character model and supplies optional source-specific profile
data. Pose evaluation, body constraints, collision proxies, controller behavior, and
lifecycle management belong to the character-model layer.

This keeps the important dependency direction:

```text
Character workspace and UI
        |
        v
CharacterModel runtime
        |
        +--> Pose pipeline
        +--> Body constraint runtime
        +--> Secondary physics runtime
        |
        v
Unity animation, physics, and rendering

Import adapters ------> construct CharacterModel and provide optional profile data
Koikatsu adapter -----> never becomes a dependency of the generic runtime
```

The existing `ReferenceModel` implementation is treated as a migration source only.
New public contracts and new feature names must not use `ReferenceModel`.

## Why This Is Feasible

Anime characters are a good fit for a reduced VaM-style body constraint system:

- The useful body skeleton normally has a small, stable set of semantic bones.
- Limbs can be represented effectively by capsules and spheres.
- The goal is visually plausible separation, not medical soft-tissue accuracy.
- Imported body shape is mostly static, so collision proxies can be fitted once when
  the character model is created.
- Clothing, hair, fingers, and soft tissue can be left outside the first solver.

The main difficulty is not collider generation. It is creating one authoritative pose
pipeline. Direct `Transform` writes from Animator overrides, IK, Timeline, and user
tools must not fight the body constraint result in different `LateUpdate` methods.

## Goals

1. Prevent common limb-to-body and limb-to-limb interpenetration.
2. Support future VaM-like hand, foot, head, chest, and hip controllers.
3. Preserve Animator, imported FK/IK, Timeline, and manual pose workflows.
4. Keep the feature optional. Disabled mode must reproduce the current pose output.
5. Keep character-format knowledge out of the generic runtime.
6. Fit collision proxies to stylized proportions instead of assuming realistic ratios.
7. Make model replacement, Timeline seeking, pause, reset, and disposal predictable.
8. Avoid per-frame mesh reads and managed allocations after the body rig is built.

## Non-Goals For The First Version

- Clothing collision or cloth deformation.
- Breast, glute, belly, or other soft-body simulation.
- Hair and accessory collision.
- Finger collision and finger physics.
- Inter-character collision.
- Environment interaction beyond an optional floor collider.
- Ragdoll and fall simulation.
- Porting VaM's DAZ skinning or GPU physics stack.
- Reusing Koikatsu's original body physics components after import.

## Naming

The new APIs should use `CharacterModel` consistently.

Recommended terms:

| Concept | Name |
| --- | --- |
| Imported editable character | `CharacterModel` |
| Semantic skeleton access | `CharacterSkeleton` |
| One evaluated pose | `CharacterPoseBuffer` |
| Ordered pose evaluation | `CharacterPosePipeline` |
| Body collision and joint owner | `CharacterBodyConstraintController` |
| Runtime proxy skeleton | `CharacterBodyRig` |
| Collider and joint configuration | `CharacterBodyProfile` |
| Interactive hand/foot/head target | `CharacterEffectorController` |
| Hair, cloth, spring bones | `CharacterSecondaryPhysicsController` |
| Active-model application owner | `CharacterWorkspaceController` |

Avoid naming a component simply `CharacterController`, because Unity already defines
`UnityEngine.CharacterController`.

Suggested namespaces:

```text
StudioEditor.Characters
StudioEditor.Characters.Pose
StudioEditor.Characters.Constraints
StudioEditor.Characters.SecondaryPhysics
StudioEditor.Characters.Import
```

## Ownership Boundaries

### CharacterModel

Owns the imported character's runtime identity and lifetime:

- Root object.
- Semantic body skeleton.
- Body surface renderers.
- Animator and pose-related components.
- Optional source-specific character profile.
- Capability flags.
- Disposal of imported resources.

It does not implement IK, collision solving, UI, or adapter-specific loading.

### CharacterPosePipeline

Owns the ordered production of a target pose:

1. Capture the base Animator pose.
2. Apply imported FK overrides.
3. Apply imported IK.
4. Apply Timeline tracks.
5. Apply editor and user-controller targets.
6. Publish an immutable target pose for the constraint stage.

Pose producers must write to a `CharacterPoseBuffer`, not directly compete over the
render skeleton. During migration, legacy writers may be wrapped as pipeline stages.

The pipeline contains no Rigidbody, Collider, or importer logic.

### CharacterBodyConstraintController

Owns one character's body-constraint lifecycle:

- Validates whether the character has the required semantic bones.
- Requests or generates a `CharacterBodyProfile`.
- Builds and disposes the proxy body rig.
- Drives the proxy rig from the target pose.
- Applies the resolved pose back to the render skeleton.
- Handles enable, disable, reset, seek, and model replacement.
- Exposes controller modes and high-level settings to the application.

It does not parse character files and does not know Koikatsu bone names.

### CharacterBodyRigBuilder

Builds runtime physics objects from a skeleton and profile:

- Dynamic segment rigidbodies.
- Kinematic targets where required.
- `ConfigurableJoint` limits and drives.
- Capsule, sphere, and occasional box colliders.
- Collision-ignore pairs.
- Mass, drag, solver, and collision-detection settings.

It performs construction only. Frame-by-frame behavior remains in
`CharacterBodyRig`.

### CharacterColliderFitter

Generates a model-specific profile from body geometry. It is a pure geometry service
and must not own UI or physics objects.

Inputs:

- Semantic skeleton.
- Body `SkinnedMeshRenderer` instances only.
- Bind-pose mesh data and bone weights.
- Optional source-profile overrides.

Output:

- Segment axes.
- Local collider centers.
- Capsule lengths and radii.
- Joint anchors.
- Confidence and fallback information.

### Import Adapters

Import adapters construct the model and may supply profile hints:

- Semantic bone mapping.
- Identification of actual body renderers, excluding clothes and hair.
- Bone-axis corrections.
- Recommended joint limits.
- Per-segment collider multipliers.
- Known collision-ignore pairs.

The Koikatsu adapter may provide `KoikatsuCharacterBodyProfileHints`, but the generic
runtime only consumes the common profile contract.

## Pose And Simulation Order

The target frame order is:

```text
Animator samples base animation
        |
CharacterPosePipeline applies FK, IK, Timeline, and editor targets
        |
Target CharacterPoseBuffer is published
        |
FixedUpdate drives CharacterBodyRig joints and effectors
        |
PhysX solves joint limits and self-collision
        |
LateUpdate copies the resolved proxy pose to the render skeleton
        |
Secondary physics updates hair, accessories, and optional cloth
        |
Render
```

The coordinator must define this order explicitly. Correctness must not rely on the
unspecified ordering of unrelated `LateUpdate` methods.

Timeline seeking and animation replacement are discontinuities. On a discontinuity,
the body rig must:

1. Copy the new target pose immediately.
2. Clear linear and angular velocities.
3. Reset controller error accumulation.
4. Skip historical simulation unless a future warm-up option is explicitly enabled.

## Physical Body Rig

The first rig should use major body segments only:

- Pelvis.
- Lower torso and chest.
- Head.
- Left and right upper arms.
- Left and right lower arms.
- Left and right hands.
- Left and right upper legs.
- Left and right lower legs.
- Left and right feet.

Shoulders, neck, and toes can remain non-colliding helper joints initially. Fingers
remain purely animated.

Each physical segment has:

- One proxy transform outside the animated skin hierarchy.
- One dynamic `Rigidbody`.
- One parent `ConfigurableJoint` with anatomical angular limits.
- One capsule, sphere, or box collider.
- A mapping back to one semantic render bone.

Desired local bone rotation becomes the joint drive target. The render skeleton is
never used as the dynamic Rigidbody hierarchy, which prevents Animator and PhysX from
writing to the same transforms.

Only hips/root translation should normally be copied back as a position. Limb bone
lengths must remain fixed, so limbs are resolved primarily through rotation.

## Collision Proxy Generation

Static realistic ratios are insufficient for stylized characters. The preferred
approach is mesh fitting with semantic fallbacks.

### Primary fitting path

1. Select only the canonical nude/body skin renderers supplied by `CharacterModel`.
2. Read bind-pose vertices and bone weights once.
3. Assign vertices to semantic body segments using dominant and secondary weights.
4. Transform each vertex cloud into its segment's local bind-pose space.
5. Fit the collider axis to the parent-child bone direction.
6. Use robust percentile bounds rather than absolute minimum and maximum values.
7. Generate radius, length, center, and endpoint padding.
8. Apply profile multipliers and clamps.

Robust bounds are important because stylized meshes can contain isolated vertices,
genital geometry, eyelashes, or other geometry that should not enlarge a limb proxy.

### Fallback path

When body mesh data is missing or bone weights are unsuitable:

- Use parent-child bone length for capsule length.
- Use per-segment radius-to-length ratios.
- Apply model-scale normalization.
- Mark the generated segment as low-confidence for debug display.

### Refit policy

The proxy is fitted once when the character is created. Outfit changes do not trigger
a refit because clothing is excluded. A future live body-shape editor must raise an
explicit body-shape-changed event before the profile is regenerated.

## Self-Collision Policy

Allowing every collider pair to collide produces unstable joints. Collision policy
must be explicit and data-driven.

Default ignored pairs:

- Direct parent and child segments.
- Upper arm with its own lower arm.
- Lower arm with its own hand.
- Upper leg with its own lower leg.
- Lower leg with its own foot.
- Neighboring torso segments.

Important enabled pairs:

- Hands and forearms against chest, torso, pelvis, and head.
- Left and right arms against each other where useful.
- Thighs against pelvis and the opposite thigh.
- Lower legs and feet against the opposite leg.
- Head against hands and forearms.

The pair table belongs to `CharacterBodyProfile`, not scattered calls to
`Physics.IgnoreCollision`.

## Controller Behavior

Interactive effectors should be layered on top of the body rig rather than built into
the pose pipeline.

Initial effectors:

- Hips.
- Chest.
- Head.
- Left and right hands.
- Left and right feet.

Recommended controller states, inspired by VaM:

| State | Behavior |
| --- | --- |
| `Off` | No effector drive. Animation and natural joints control the body. |
| `Hold` | Follow the target using ordinary spring and damping. |
| `Lock` | Use stronger drive and tighter error limits. |
| `Comply` | Use softer drive and move the target back toward the solved body when blocked. |

`Comply` is important for avoiding persistent penetration pressure. When collision
prevents a hand or foot from reaching its requested target, the visible controller
target gradually follows the actual solved body after a configurable position or
rotation threshold.

All modes require force, torque, velocity, and error clamps. A controller target must
never generate unbounded drive against the torso.

## Settings And Profiles

`CharacterBodyProfile` should be serializable data containing:

- Required semantic bones.
- Segment definitions.
- Collider types and fitted dimensions.
- Joint anchors and angular limits.
- Drive spring, damping, and force limits.
- Segment masses and drag.
- Collision-ignore pairs.
- Effector defaults.
- Scale and fitting clamps.

Profile precedence:

```text
Generic anime defaults
        < source-format hints
        < generated per-model measurements
        < explicit user overrides
```

Generated profiles may remain in memory initially. Asset persistence should be added
only when manual tuning and reuse justify it.

## Separation From Secondary Physics

Body self-collision and secondary physics must remain separate capabilities.

`CharacterBodyConstraintController` owns:

- Major-body rigidbodies.
- Anatomical joints.
- Self-collision proxies.
- Interactive body effectors.

`CharacterSecondaryPhysicsController` owns:

- Hair spring bones.
- Accessory spring bones.
- Bust spring bones if retained.
- Unity Cloth or later cloth systems.

The UI should eventually expose separate toggles such as `Body Constraints` and
`Secondary Physics`. The existing generic `Physics` toggle should not be expanded to
silently control both systems.

## Legacy ReferenceModel Migration

The new system should not be implemented by adding more interfaces to
`IReferenceModelInstance`.

Recommended migration:

1. Introduce `CharacterModel` contracts in a new runtime namespace.
2. Add a temporary adapter that wraps the currently imported legacy model as a
   `CharacterModel`.
3. Move model presentation and Timeline ownership to the character workspace in
   independent steps.
4. Update Koikatsu and PMX importers to return native character models.
5. Remove the bridge after all active workflows use the new model layer.

Dependency rule during migration:

```text
Legacy bridge may depend on ReferenceModel and CharacterModel.
CharacterModel runtime must never depend on ReferenceModel.
```

The first body-constraint implementation should target Koikatsu character models.
PMX can opt in later because PMX imports may already contain body rigidbodies and
joints that would conflict with a second body solver.

## Proposed File Layout

```text
Assets/StudioEditor/Runtime/Characters/
    CharacterModel.cs
    CharacterSkeleton.cs
    CharacterWorkspaceController.cs

Assets/StudioEditor/Runtime/Characters/Pose/
    CharacterPoseBuffer.cs
    CharacterPosePipeline.cs
    CharacterPoseCoordinator.cs
    ICharacterPoseModifier.cs

Assets/StudioEditor/Runtime/Characters/Constraints/
    CharacterBodyConstraintController.cs
    CharacterBodyProfile.cs
    CharacterBodyRig.cs
    CharacterBodyRigBuilder.cs
    CharacterColliderFitter.cs
    CharacterEffectorController.cs
    CharacterConstraintDebugView.cs

Assets/StudioEditor/Runtime/Characters/Import/
    ICharacterImporter.cs
    ICharacterBodyProfileProvider.cs

Assets/StudioEditor/Runtime/Legacy/Characters/
    LegacyCharacterModelBridge.cs

Assets/StudioEditor/Adapters/Koikatsu/Characters/
    KoikatsuCharacterBodyProfileProvider.cs
```

Exact file names can change during implementation, but the ownership boundaries
should remain.

## Delivery Phases

### Phase 1: CharacterModel foundation

- Add the new character-model contracts and workspace lifecycle.
- Add the temporary legacy bridge.
- Keep all current visual and animation behavior unchanged.
- Establish capability names for skeleton, body geometry, Timeline, and secondary
  physics.

### Phase 2: Authoritative pose pipeline

- Introduce `CharacterPoseBuffer` and ordered pose modifiers.
- Move Koikatsu FK, IK, and Timeline writes into explicit pipeline stages.
- Retain a direct-output mode that matches current results.
- Add discontinuity handling for seek, animation replacement, and model reset.

This phase is required before body physics. Adding Rigidbody constraints while several
components still overwrite bones in `LateUpdate` would be unstable by design.

### Phase 3: Body profile fitting and debug view

- Identify body-only renderers.
- Fit major segment colliders from bind-pose mesh and bone weights.
- Add semantic fallbacks and Koikatsu-specific hints.
- Display proxy colliders, axes, confidence, and ignored pairs.
- Do not enable collision solving yet.

The preliminary runtime currently uses semantic measurements plus head-renderer bounds
for conservative analytic volumes. Bone-weight fitting and the editable debug view are
still required before these values can replace the fallback anime profile.

### Phase 4: Passive body constraints

- Build the proxy Rigidbody and joint hierarchy.
- Drive it from animation target poses.
- Enable joint limits and self-collision.
- Apply the solved rotations back to the render skeleton.
- Add reset and velocity clearing.

Before the Rigidbody proxy exists, the current deterministic modifier provides a
limited passive constraint for limbs against head, torso, and pelvis. It validates the
pose-stage ownership and controller interaction, but it is not a substitute for the
full joint and self-collision solver described above.

### Phase 5: Interactive effectors

- Add hands, feet, hips, chest, and head targets.
- Add elbow and knee pole targets for stable bend-plane editing.
- Implement `Off`, `Hold`, `Lock`, and `Comply`.
- Add force and error clamps.
- Integrate editor selection and manipulation without adapter dependencies.

Current kinematic slice implements `Off`/follow and world-space `Hold` without a
physics proxy. It includes world-space movement axes, rotation rings, and limb pole
targets. `Lock`, `Comply`, finger posing, and pupil-texture eye look remain later
Phase 5 work.

### Phase 6: Tuning and expansion

- Tune anime-default profiles across multiple body shapes.
- Add optional floor collision.
- Decide whether PMX models can opt in safely.
- Consider live body-shape refitting.
- Consider soft-body or clothing work only as separate future modules.

## Manual Validation Checklist

The initial development cycle will use manual visual validation rather than automated
render probes.

- Disabled mode matches the current imported animation and Timeline pose.
- A hand dragged into the chest stops or slides instead of entering the torso.
- A forearm cannot pass through the head during an extreme IK target.
- Thighs and lower legs do not collapse through the pelvis or opposite leg.
- Parent-child collider pairs do not jitter against each other.
- The body returns to a stable pose after the controller is released.
- `Comply` targets retreat when blocked instead of building unlimited force.
- Timeline seek and stop snap cleanly without residual velocity.
- Replacing or clearing a model leaves no proxy objects or active physics callbacks.
- Two character models can own independent rigs and settings.
- Secondary hair and accessory physics can be toggled independently.
- Extreme anime proportions still receive conservative, editable colliders.

## Architecture Acceptance Criteria

The feature is considered correctly integrated only when:

- `StudioEditor.Runtime` contains no dependency on a Koikatsu type or assembly.
- New code does not add a body-constraint responsibility to `ReferenceModel` APIs.
- Import adapters provide data and construction, not frame-by-frame body solving.
- Pose evaluation has one explicit coordinator and one final render-pose write stage.
- Body constraints can be disabled without changing the target pose.
- The proxy rig is owned and disposed with exactly one `CharacterModel`.
- Collision fitting excludes clothing and other secondary meshes.
- No body mesh is baked or read back every frame.
- Collision-ignore policy and joint limits are profile data, not hard-coded throughout
  MonoBehaviours.

## Primary Risks

### Multiple pose writers

Current FK, IK, and Timeline components directly modify transforms. This must be
centralized before physics is enabled.

### Physics determinism

Interactive physics is history-dependent, while Timeline scrubbing is random access.
Seeking must reset the rig instead of pretending previous simulation history exists.

### Exaggerated proportions

Large heads, narrow shoulders, wide hips, and unusually thin limbs can defeat fixed
ratios. Mesh fitting plus per-source clamps is required.

### Over-constrained joints

Too many drive joints and collider pairs can create jitter or solver explosions. The
first implementation should use one anatomical parent joint per segment and add
effectors only to selected body endpoints.

### Existing source physics

PMX and future formats may already contain body rigidbodies. Character capability data
must declare whether the generic body rig is compatible, replaces source body physics,
or is unsupported.

## Recommended First Implementation Slice

The safest first vertical slice is deliberately small:

1. Native `CharacterModel` and pose-pipeline contracts.
2. Koikatsu model exposed through those contracts.
3. Body renderer identification.
4. Debug-only fitted proxies for pelvis, chest, head, arms, hands, legs, and feet.
5. No active Rigidbody simulation until the proxy fit can be inspected across several
   characters.

An analytic, Rigidbody-free limb constraint now runs as an interim vertical slice.
The recommendation against active Rigidbody simulation remains unchanged.

This validates the new ownership model and stylized-body fitting assumptions before
the project commits to physics behavior or controller UI.
