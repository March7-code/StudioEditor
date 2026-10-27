These tiny serialized-file fixtures contain a synthetic 2 x 1 RGBA texture,
including a zero mip count to exercise the legacy conversion path.

`StrippedUnity56Texture.bytes` has no embedded type tree.
`Unity56TextureReference.bytes` has the corresponding native Unity 5.6.2f1
Texture2D schema. Both contain only generated pixels and no game artwork.

The independent fixture writer is
`Diagnostics/LegacyBundleTypeTreeReview/generate_fixture.py`.
