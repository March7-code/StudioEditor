# Body Editor

This folder contains the first minimal body-editor foundation.

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
