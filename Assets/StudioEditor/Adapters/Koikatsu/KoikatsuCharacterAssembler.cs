using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using StudioEditor.Characters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuCharacterAssembler
    {
        public const string BaseBundleFileName = "oo_base.unity3d";
        private const string HeadBundleFileName = "bo_head_00.unity3d";
        private static readonly int[] HairCategories =
        {
            101,
            102,
            103,
            104,
        };
        private static readonly int[] HairDefaultSlots = { 0, 1, 0, 0 };
        private static readonly string[] HairObjectNames =
        {
            "ct_hairB",
            "ct_hairF",
            "ct_hairS",
            "ct_hairO_01",
        };
        private static readonly string[] HairProperties =
        {
            "ChaFileHair.HairBack",
            "ChaFileHair.HairFront",
            "ChaFileHair.HairSide",
            "ChaFileHair.HairOption",
        };
        private static readonly int[] ClothesCategories =
        {
            105,
            106,
            107,
            108,
            109,
            110,
            111,
            112,
            112,
        };
        private static readonly string[] ClothesObjectNames =
        {
            "ct_clothesTop",
            "ct_clothesBot",
            "ct_bra",
            "ct_shorts",
            "ct_gloves",
            "ct_panst",
            "ct_socks",
            "ct_shoes_inner",
            "ct_shoes_outer",
        };
        private static readonly string[] ClothesProperties =
        {
            "ClothesTop",
            "ClothesBot",
            "ClothesBra",
            "ClothesShorts",
            "ClothesGloves",
            "ClothesPants",
            "ClothesSocks",
            "ClothesShoesInner",
            "ClothesShoesOuter",
        };
        private static readonly string[] CoordinateNames =
        {
            "School 1",
            "School 2",
            "Gym",
            "Swim",
            "Club",
            "Casual",
            "Pajamas",
        };
        private static readonly Bounds CharacterBounds = new Bounds(
            new Vector3(0f, -0.2f, 0f),
            new Vector3(2f, 2f, 2f));
        public static KoikatsuReferenceModelInstance BuildFemaleBase(
            string baseBundlePath,
            Transform parent,
            CancellationToken cancellationToken)
        {
            var headBundlePath = Path.Combine(
                Path.GetDirectoryName(baseBundlePath) ?? string.Empty,
                HeadBundleFileName);
            return BuildCharacter(
                baseBundlePath,
                Array.AsReadOnly(new[]
                {
                    new KoikatsuBundleSource(headBundlePath),
                }),
                "p_cf_head_00",
                "Koikatsu Female Base (Imported)",
                parent,
                cancellationToken,
                true);
        }

        public static KoikatsuReferenceModelInstance BuildFromCard(
            KoikatsuCard card,
            string abdataRoot,
            string modsRoot,
            Transform parent,
            CancellationToken cancellationToken,
            int coordinateIndex = 0)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            if (card.Parameter == null || card.Parameter.Sex > 1)
            {
                throw new NotSupportedException(
                    "The Koikatsu card has an unsupported character sex.");
            }

            if (coordinateIndex < 0 ||
                (card.Coordinates.Count != 0 &&
                 coordinateIndex >= card.Coordinates.Count))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coordinateIndex),
                    coordinateIndex,
                    "The Koikatsu outfit slot is outside the card's Coordinate list.");
            }

            var catalog = KoikatsuListCatalog.Load(abdataRoot, modsRoot);
            var headModGuid = card.FindSideloaderGuid(
                "ChaFileFace.headId",
                100,
                card.Face.HeadId);
            if (!catalog.TryGet(
                    100,
                    card.Face.HeadId,
                    headModGuid,
                    out var headEntry))
            {
                throw new InvalidDataException(
                    string.IsNullOrWhiteSpace(headModGuid)
                        ? $"Head ID {card.Face.HeadId} was not found in category 100."
                        : $"Head ID {card.Face.HeadId} from zipmod GUID " +
                          $"'{headModGuid}' was not found in category 100.");
            }

            var baseBundlePath = Path.Combine(
                abdataRoot,
                "chara",
                BaseBundleFileName);
            var headBundleSources = catalog.ResolveBundleCandidates(
                abdataRoot,
                headEntry.Get("MainAB"),
                headEntry.Archive);
            var headAssetName = headEntry.Get("MainData");
            if (string.IsNullOrEmpty(headAssetName))
            {
                throw new InvalidDataException(
                    $"Head ID {card.Face.HeadId} has no MainData entry.");
            }

            return BuildCharacter(
                baseBundlePath,
                headBundleSources,
                headAssetName,
                card.DisplayName + " (Koikatsu)",
                parent,
                cancellationToken,
                card.Parameter.Sex == 1,
                card,
                abdataRoot,
                headEntry,
                catalog,
                coordinateIndex);
        }

        private static KoikatsuReferenceModelInstance BuildCharacter(
            string baseBundlePath,
            IReadOnlyList<KoikatsuBundleSource> headBundleSources,
            string headAssetName,
            string displayName,
            Transform parent,
            CancellationToken cancellationToken,
            bool female,
            KoikatsuCard card = null,
            string abdataRoot = null,
            KoikatsuListEntry headEntry = null,
            KoikatsuListCatalog catalog = null,
            int coordinateIndex = 0)
        {
            var leases = new List<KoikatsuAssetBundleLease>();
            var runtimeMaterials = new List<Material>();
            var runtimeTextures = new List<Texture2D>();
            GameObject container = null;

            try
            {
                var baseBundle = KoikatsuAssetBundleCache.Acquire(baseBundlePath);
                leases.Add(baseBundle);
                cancellationToken.ThrowIfCancellationRequested();

                var headBundle = AcquireAssetBundle(
                    headBundleSources,
                    headAssetName,
                    out var loadedHeadSource);
                leases.Add(headBundle);
                cancellationToken.ThrowIfCancellationRequested();

                // The legacy cf_m_face_create shader is a Built-in Render
                // Pipeline material. Its UV/color interpretation is not
                // portable to URP, so do not execute it directly here.
                Material faceCreateMaterial = null;

                container = new GameObject(displayName);
                container.transform.SetParent(parent, false);

                var bodyTop = new GameObject("BodyTop");
                bodyTop.transform.SetParent(container.transform, false);

                var bodySkeleton = InstantiateRequired(
                    baseBundle.Bundle,
                    "p_cf_body_bone",
                    bodyTop.transform);
                bodySkeleton.name = "p_cf_body_bone";
                var bodyRoot = FindRequired(bodySkeleton.transform, "cf_j_root");
                var headParent = FindRequired(bodyRoot, "cf_s_head");
                var sharedRootBone = FindRequired(bodyRoot, "cf_j_hips");
                var bodyBones = BuildNameMap(bodyRoot);

                var headSkeleton = InstantiateRequired(
                    baseBundle.Bundle,
                    "p_cf_head_bone",
                    headParent);
                headSkeleton.name = "p_cf_head_bone";
                var faceRoot = FindRequired(
                    headSkeleton.transform,
                    "cf_J_N_FaceRoot");
                var headBones = BuildNameMap(headSkeleton.transform);

                var bodyAssetName = female
                    ? "p_cf_body_00"
                    : "p_cm_body_00";
                var bodyModel = InstantiateRequired(
                    baseBundle.Bundle,
                    bodyAssetName,
                    bodyTop.transform);
                bodyModel.name = bodyAssetName;
                RebindSkinning(
                    bodyModel,
                    bodyBones,
                    sharedRootBone,
                    "cf_j_root");

                cancellationToken.ThrowIfCancellationRequested();
                var headModel = InstantiateRequired(
                    headBundle.Bundle,
                    headAssetName,
                    headSkeleton.transform);
                headModel.name = headAssetName;
                CopySameNameLocalTransforms(
                    headSkeleton.transform,
                    headModel.transform);
                RebindSkinning(
                    headModel,
                    headBones,
                    sharedRootBone,
                    "cf_J_N_FaceRoot");

                if (card != null)
                {
                    KoikatsuShapeApplicator.Apply(
                        card,
                        abdataRoot,
                        baseBundle.Bundle,
                        headBundle.Bundle,
                        headEntry,
                        catalog,
                        bodySkeleton.transform,
                        headSkeleton.transform);

                    var textureLoader = new KoikatsuTextureLoader(
                        abdataRoot,
                        catalog,
                        leases,
                        card,
                        runtimeTextures);
                    KoikatsuMaterialConverter.ApplyMaterialEditorMainTextures(
                        bodyModel,
                        textureLoader,
                        4);
                    KoikatsuMaterialConverter.ApplyMaterialEditorMainTextures(
                        headModel,
                        textureLoader,
                        4);
                    LoadHair(
                        card,
                        abdataRoot,
                        catalog,
                        headSkeleton.transform,
                        leases,
                        runtimeMaterials,
                        textureLoader,
                        coordinateIndex,
                        cancellationToken);
                    var clothesLoadResult = default(KoikatsuClothesLoadResult);
                    try
                    {
                        LoadClothes(
                            card,
                            catalog,
                            bodyTop.transform,
                            bodyBones,
                            sharedRootBone,
                            leases,
                            runtimeMaterials,
                            runtimeTextures,
                            textureLoader,
                            ref clothesLoadResult,
                            coordinateIndex,
                            cancellationToken);
                    }
                    catch (Exception exception) when (
                        IsOptionalPartFailure(exception))
                    {
                        Debug.LogWarning(
                            $"Could not load all Koikatsu clothes for " +
                            $"'{card.DisplayName}': {exception.Message}");
                    }

                    try
                    {
                        LoadAccessories(
                            card,
                            abdataRoot,
                            catalog,
                            bodyTop.transform,
                            bodyBones,
                            headBones,
                            sharedRootBone,
                            leases,
                            runtimeMaterials,
                            textureLoader,
                            coordinateIndex,
                            cancellationToken);
                    }
                    catch (Exception exception) when (
                        IsOptionalPartFailure(exception))
                    {
                        Debug.LogWarning(
                            $"Could not load all Koikatsu accessories for " +
                            $"'{card.DisplayName}': {exception.Message}");
                    }

                    var bodyTexture = baseBundle.Bundle.LoadAsset<Texture2D>(
                        female ? "cf_body_00_t" : "cm_body_00_t");
                    bodyTexture = KoikatsuOverlayTextureBaker.Composite(
                        bodyTexture,
                        textureLoader.LoadSkinOverlayTexture(
                            coordinateIndex,
                            KoikatsuSkinOverlayType.BodyUnder),
                        runtimeTextures,
                        "Koikatsu Body (KSOX underlay)");
                    bodyTexture = KoikatsuBodyMaskBaker.Bake(
                        bodyTexture,
                        clothesLoadResult.BodyAlphaMask,
                        runtimeTextures);
                    bodyTexture = KoikatsuOverlayTextureBaker.Composite(
                        bodyTexture,
                        textureLoader.LoadSkinOverlayTexture(
                            coordinateIndex,
                            KoikatsuSkinOverlayType.BodyOver),
                        runtimeTextures,
                        "Koikatsu Body (KSOX overlay)");
                    if (!female)
                    {
                        ApplyCoveredMaleBodyState(
                            bodyModel,
                            clothesLoadResult.CoversGroin);
                    }

                    var headTextures = textureLoader.LoadPartTextures(headEntry);
                    var faceBaseTexture = textureLoader
                        .LoadMaterialEditorCharacterTexture(
                            "cf_m_face_00",
                            "MainTex") ?? headTextures.Main;
                    faceBaseTexture = KoikatsuOverlayTextureBaker.Composite(
                        faceBaseTexture,
                        textureLoader.LoadSkinOverlayTexture(
                            coordinateIndex,
                            KoikatsuSkinOverlayType.FaceUnder),
                        runtimeTextures,
                        "Koikatsu Face (KSOX underlay)");
                    var faceTexture = KoikatsuFaceTextureBaker.Bake(
                        faceCreateMaterial,
                        faceBaseTexture,
                        headTextures.ColorMask,
                        card,
                        textureLoader,
                        coordinateIndex,
                        runtimeTextures);
                    faceTexture = KoikatsuOverlayTextureBaker.Composite(
                        faceTexture,
                        textureLoader.LoadSkinOverlayTexture(
                            coordinateIndex,
                            KoikatsuSkinOverlayType.FaceOver),
                        runtimeTextures,
                        "Koikatsu Face (KSOX overlay)");
                    var eyeTextures = KoikatsuEyeTextureBaker.Bake(
                        card.Face,
                        textureLoader,
                        coordinateIndex,
                        runtimeTextures);
                    var faceTextures = LoadFaceTextures(
                        card,
                        textureLoader);
                    KoikatsuMaterialConverter.ConvertSkin(
                        bodyModel,
                        card.Body.Appearance.SkinMainColor,
                        bodyTexture,
                        "o_body",
                        clothesLoadResult.BodyAlphaMask != null,
                        runtimeMaterials);
                    KoikatsuMaterialConverter.ConvertFace(
                        headModel,
                        card.Body.Appearance.SkinMainColor,
                        faceTexture,
                        eyeTextures,
                        card.Face.Appearance,
                        faceTextures,
                        runtimeMaterials);
                    // Face-detail textures are applied after conversion so
                    // MaterialEditor remains the final source of truth for
                    // eye lines, brows, nose lines, and other face layers.
                    // Character properties are card-global; coordinate
                    // filtering only applies to clothes and accessories.
                    textureLoader.ApplyMaterialEditorProperties(
                        headModel,
                        4,
                        -1,
                        -1);
                    KoikatsuMaterialConverter.ConfigureFaceRenderQueues(
                        headModel,
                        card.Face.Appearance);
                    KoikatsuFaceNormalProxyBuilder.Attach(
                        headModel,
                        faceRoot);
                }
                else
                {
                    KoikatsuMaterialConverter.Convert(
                        bodyModel,
                        runtimeMaterials);
                    KoikatsuMaterialConverter.Convert(
                        headModel,
                        runtimeMaterials);
                }

                KoikatsuVer02MetadataLoader.Attach(
                    new KoikatsuBundleSource(baseBundlePath),
                    "p_cf_body_bone",
                    bodySkeleton);
                KoikatsuMorphControllerLoader.AttachEyebrow(
                    loadedHeadSource,
                    headAssetName,
                    headModel);
                KoikatsuMorphControllerLoader.AttachMouth(
                    loadedHeadSource,
                    headAssetName,
                    headModel);
                KoikatsuMorphControllerLoader.AttachEyeOpen(
                    loadedHeadSource,
                    headAssetName,
                    headModel);

                var bones = KoikatsuBodyBoneProfile.Build(bodyRoot);
                var characterSkeleton =
                    KoikatsuBodyBoneProfile.BuildCharacterSkeleton(
                        container.transform,
                        bones);
                var characterGeometry = new CharacterGeometry(
                    bodyModel.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                    headModel.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                var character = new KoikatsuReferenceModelInstance(
                    container,
                    bones,
                    characterSkeleton,
                    characterGeometry,
                    leases,
                    runtimeMaterials,
                    runtimeTextures,
                    card,
                    card != null
                        ? BuildCoordinateNames(card.Coordinates.Count)
                        : Array.Empty<string>(),
                    coordinateIndex);
                character.AttachMorphControllers(
                    abdataRoot,
                    catalog,
                    new KoikatsuBundleSource(baseBundlePath));
                ApplyImportedExpression(character, card?.Status, catalog);
                return character;
            }
            catch
            {
                DestroyRuntimeObject(container);
                for (var index = 0; index < runtimeMaterials.Count; index++)
                {
                    DestroyRuntimeObject(runtimeMaterials[index]);
                }

                for (var index = 0; index < runtimeTextures.Count; index++)
                {
                    DestroyRuntimeObject(runtimeTextures[index]);
                }

                for (var index = leases.Count - 1; index >= 0; index--)
                {
                    leases[index].Dispose();
                }

                throw;
            }
        }

        internal static void ApplyImportedExpression(
            KoikatsuReferenceModelInstance character,
            KoikatsuCardStatus status,
            KoikatsuListCatalog catalog)
        {
            if (character?.Root == null || status == null)
            {
                return;
            }

            ApplyPattern(
                character.Controls?.Eyebrows,
                status.EyebrowPattern,
                status.EyebrowOpenMax);
            ApplyPattern(
                character.Controls?.Eyes?.Open,
                ResolveEyeMorphPattern(catalog, status.EyesPattern),
                status.EyesOpenMax);

            var mouth = character.Controls?.Mouth;
            if (mouth != null && mouth.PatternCount > 0)
            {
                mouth.SetPattern(
                    Mathf.Clamp(
                        status.MouthPattern,
                        0,
                        mouth.PatternCount - 1),
                    false);
            }
        }

        private static void ApplyPattern(
            ICharacterPatternController controller,
            int pattern,
            float openMax)
        {
            if (controller == null || controller.PatternCount == 0)
            {
                return;
            }

            controller.SetPattern(
                Mathf.Clamp(pattern, 0, controller.PatternCount - 1),
                false);
            // Koikatsu stores this field as the controller's maximum opening,
            // not as the current blend progress. The original loader keeps the
            // current opening at the fully-open blink state and changes OpenMax.
            controller.SetOpenMax(openMax);
            controller.SetOpenRate(1f);
        }

        internal static int ResolveEyeMorphPattern(
            KoikatsuListCatalog catalog,
            int eyeSetId)
        {
            if (catalog == null)
            {
                return 0;
            }

            if (!catalog.TryGet(2, eyeSetId, out var entry) &&
                !catalog.TryGet(2, 0, out entry))
            {
                return 0;
            }

            return int.TryParse(entry.Get("EyesPtn"), out var pattern)
                ? Math.Max(pattern, 0)
                : 0;
        }

        private static bool IsOptionalPartFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is InvalidDataException ||
                   exception is NotSupportedException ||
                   exception is UnityException;
        }

        private static KoikatsuAssetBundleLease AcquireAssetBundle(
            IReadOnlyList<KoikatsuBundleSource> sources,
            string assetName,
            out KoikatsuBundleSource loadedSource)
        {
            loadedSource = null;
            if (sources == null || sources.Count == 0)
            {
                throw new InvalidDataException(
                    $"No Koikatsu AssetBundle source can provide '{assetName}'.");
            }

            for (var index = 0; index < sources.Count; index++)
            {
                if (!File.Exists(sources[index].FilePath))
                {
                    continue;
                }

                var lease = KoikatsuAssetBundleCache.Acquire(sources[index]);
                if (lease.Bundle.Contains(assetName))
                {
                    loadedSource = sources[index];
                    return lease;
                }

                lease.Dispose();
            }

            throw new InvalidDataException(
                $"No Koikatsu Sideloader candidate contains '{assetName}'.");
        }

        private static KoikatsuFaceTextures LoadFaceTextures(
            KoikatsuCard card,
            KoikatsuTextureLoader textureLoader)
        {
            var appearance = card.Face.Appearance;
            var suffix = card.Face.HeadId == 0
                ? string.Empty
                : $"_{card.Face.HeadId}";
            return new KoikatsuFaceTextures
            {
                Eyebrow = textureLoader.LoadCatalogTexture(
                    406,
                    appearance.EyebrowId,
                    "MainAB",
                    "EyebrowTex",
                    "ChaFileFace.eyebrowId"),
                Nose = textureLoader.LoadCatalogTexture(
                    414,
                    appearance.NoseId,
                    "MainAB",
                    "NoseTex",
                    "ChaFileFace.noseId"),
                EyelineUp = textureLoader.LoadCatalogTexture(
                    412,
                    appearance.EyelineUpId,
                    "MainAB",
                    "EyelineUpTex",
                    "ChaFileFace.eyelineUpId",
                    suffix),
                EyelineShadow = textureLoader.LoadCatalogTexture(
                    412,
                    appearance.EyelineUpId,
                    "MainAB",
                    "EyelineShadowTex",
                    "ChaFileFace.eyelineUpId",
                    suffix),
                EyelineDown = textureLoader.LoadCatalogTexture(
                    413,
                    appearance.EyelineDownId,
                    "MainAB",
                    "EyelineDownTex",
                    "ChaFileFace.eyelineDownId",
                    suffix),
            };
        }

        private static void LoadHair(
            KoikatsuCard card,
            string abdataRoot,
            KoikatsuListCatalog catalog,
            Transform headSkeleton,
            List<KoikatsuAssetBundleLease> leases,
            List<Material> runtimeMaterials,
            KoikatsuTextureLoader textureLoader,
            int coordinateIndex,
            CancellationToken cancellationToken)
        {
            if (card.Hair == null || catalog == null)
            {
                return;
            }

            var hairParent = FindRequired(headSkeleton, "cf_J_FaceUp_ty");
            var loader = new KoikatsuPartLoader(
                new KoikatsuVanillaAssetResolver(abdataRoot, catalog, card),
                leases,
                runtimeMaterials);
            var hairGloss = textureLoader.LoadCatalogTexture(
                439,
                card.Hair.GlossId,
                "MainTexAB",
                "MainTex",
                "ChaFileHair.glossId");
            for (var index = 0; index < KoikatsuCardHair.PartCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var part = card.Hair.Parts[index];
                loader.Load(
                    new KoikatsuAssetRequest(
                        HairCategories[index],
                        part.Id,
                        HairProperties[index],
                        HairDefaultSlots[index]),
                    new KoikatsuPartLoadOptions
                    {
                        Parent = hairParent,
                        ObjectName = HairObjectNames[index],
                        LocalPosition = part.Position,
                        LocalEulerAngles = part.Rotation,
                        LocalScale = part.Scale,
                        HairMaterial = part,
                        HairGlossTexture = hairGloss,
                        TextureLoader = textureLoader,
                        // MaterialEditor scopes hair independently from
                        // accessories and keys each part by its hair slot.
                        MaterialEditorObjectType = 3,
                        MaterialEditorCoordinateIndex = 0,
                        MaterialEditorSlot = index,
                    });
            }
        }

        private static void LoadClothes(
            KoikatsuCard card,
            KoikatsuListCatalog catalog,
            Transform bodyTop,
            IReadOnlyDictionary<string, Transform> bodyBones,
            Transform sharedRootBone,
            List<KoikatsuAssetBundleLease> leases,
            List<Material> runtimeMaterials,
            List<Texture2D> runtimeTextures,
            KoikatsuTextureLoader textureLoader,
            ref KoikatsuClothesLoadResult loadResult,
            int coordinateIndex,
            CancellationToken cancellationToken)
        {
            if (card.Coordinates == null || card.Coordinates.Count == 0)
            {
                return;
            }

            var coordinate = card.Coordinates[coordinateIndex];
            var resolver = new KoikatsuVanillaAssetResolver(
                textureLoader.AbdataRoot,
                catalog,
                card);
            var loader = new KoikatsuPartLoader(
                resolver,
                leases,
                runtimeMaterials);
            var topPart = GetClothesPart(coordinate, 0);
            var topProperty =
                $"outfit{coordinateIndex}.ChaFileClothes.{ClothesProperties[0]}";
            catalog.TryGet(
                105,
                topPart.Id,
                card.FindSideloaderGuid(topProperty, 105, topPart.Id),
                out var topEntry);
            var suppressBottom = topEntry?.Get("Coordinate") == "2";
            var suppressBra = topEntry?.Get("NotBra") == "1";
            var braPart = GetClothesPart(coordinate, 2);
            var braProperty =
                $"outfit{coordinateIndex}.ChaFileClothes.{ClothesProperties[2]}";
            catalog.TryGet(
                107,
                braPart.Id,
                card.FindSideloaderGuid(braProperty, 107, braPart.Id),
                out var braEntry);
            var suppressShorts = !suppressBra &&
                                 braEntry?.Get("Coordinate") == "2";
            var bodyAlphaMask = textureLoader.LoadCatalogTexture(
                105,
                topPart.Id,
                "OverBodyMaskAB",
                "OverBodyMask",
                topProperty);
            var coversGroin = suppressBottom || suppressShorts;
            loadResult = new KoikatsuClothesLoadResult(
                bodyAlphaMask,
                coversGroin);

            GameObject topObject = null;
            GameObject innerShoes = null;
            GameObject outerShoes = null;
            for (var index = 0; index < ClothesCategories.Length; index++)
            {
                if ((index == 1 && suppressBottom) ||
                    (index == 2 && suppressBra) ||
                    (index == 3 && suppressShorts))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var part = GetClothesPart(coordinate, index);
                var property =
                    $"outfit{coordinateIndex}.ChaFileClothes." +
                    ClothesProperties[index];
                var textures = textureLoader.LoadPartTextures(
                    ClothesCategories[index],
                    part.Id,
                    property);
                var bakedTextures = KoikatsuClothesTextureBaker.Bake(
                    textures,
                    part,
                    0,
                    textureLoader,
                    runtimeTextures);
                var instance = loader.Load(
                    new KoikatsuAssetRequest(
                        ClothesCategories[index],
                        part.Id,
                        property,
                        0),
                    new KoikatsuPartLoadOptions
                    {
                        Parent = bodyTop,
                        ObjectName = ClothesObjectNames[index],
                        SkinningMode = KoikatsuSkinningMode.Body,
                        TargetBones = bodyBones,
                        SharedRootBone = sharedRootBone,
                        ClothesMaterial = part,
                        Textures = textures,
                        BakedClothesTextures = bakedTextures,
                        TextureLoader = textureLoader,
                        MaterialEditorObjectType = 1,
                        MaterialEditorCoordinateIndex = coordinateIndex,
                        MaterialEditorSlot = index,
                    });
                ApplyWornClothesState(instance);
                if (index == 1 || index == 3)
                {
                    coversGroin |= instance != null;
                    loadResult = new KoikatsuClothesLoadResult(
                        bodyAlphaMask,
                        coversGroin);
                }

                if (index == 0)
                {
                    topObject = instance;
                }
                else if (index == 7)
                {
                    innerShoes = instance;
                }
                else if (index == 8)
                {
                    outerShoes = instance;
                }
            }

            if (topObject == null)
            {
                topObject = new GameObject(ClothesObjectNames[0]);
                topObject.transform.SetParent(bodyTop, false);
            }

            var topPartsBodyMask = LoadTopParts(
                coordinate,
                topPart,
                topEntry,
                catalog,
                topObject.transform,
                bodyBones,
                sharedRootBone,
                loader,
                textureLoader,
                runtimeTextures,
                coordinateIndex,
                cancellationToken);
            if (topPartsBodyMask != null)
            {
                bodyAlphaMask = topPartsBodyMask;
                loadResult = new KoikatsuClothesLoadResult(
                    bodyAlphaMask,
                    coversGroin);
            }

            if (innerShoes != null && outerShoes != null)
            {
                innerShoes.SetActive(false);
            }

        }

        private static Texture2D LoadTopParts(
            KoikatsuCardCoordinate coordinate,
            KoikatsuCardClothesPart topPart,
            KoikatsuListEntry topEntry,
            KoikatsuListCatalog catalog,
            Transform parent,
            IReadOnlyDictionary<string, Transform> bodyBones,
            Transform sharedRootBone,
            KoikatsuPartLoader loader,
            KoikatsuTextureLoader textureLoader,
            List<Texture2D> runtimeTextures,
            int coordinateIndex,
            CancellationToken cancellationToken)
        {
            if (topEntry == null ||
                !int.TryParse(topEntry.Get("Kind"), out var topType) ||
                (topType != 1 && topType != 2))
            {
                return null;
            }

            var categoryBase = topType == 1 ? 200 : 210;
            var defaults = topType == 1
                ? new[] { 0, 0, 1 }
                : new[] { 0, 1, 1 };
            var objectNames = new[]
            {
                "ct_top_parts_A",
                "ct_top_parts_B",
                "ct_top_parts_C",
            };
            Texture2D bodyAlphaMask = null;
            for (var index = 0; index < objectNames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = index < coordinate.SubPartsIds.Count
                    ? coordinate.SubPartsIds[index]
                    : defaults[index];
                var category = categoryBase + index;
                var subPartName = topType == 1
                    ? new[]
                    {
                        "ClothesJacketSubA",
                        "ClothesJacketSubB",
                        "ClothesJacketSubC",
                    }[index]
                    : new[]
                    {
                        "ClothesSailorSubA",
                        "ClothesSailorSubB",
                        "ClothesSailorSubC",
                    }[index];
                var property =
                    $"outfit{coordinateIndex}.ChaFileClothes.{subPartName}";
                var textures = textureLoader.LoadPartTextures(
                    category,
                    id,
                    property);
                if (index == 0)
                {
                    bodyAlphaMask = textureLoader.LoadCatalogTexture(
                        category,
                        id,
                        "OverBodyMaskAB",
                        "OverBodyMask",
                        property);
                }

                var bakedTextures = KoikatsuClothesTextureBaker.Bake(
                    textures,
                    topPart,
                    index == 2 ? 3 : 0,
                    textureLoader,
                    runtimeTextures);
                var instance = loader.Load(
                    new KoikatsuAssetRequest(
                        category,
                        id,
                        property,
                        defaults[index]),
                    new KoikatsuPartLoadOptions
                    {
                        Parent = parent,
                        ObjectName = objectNames[index],
                        SkinningMode = KoikatsuSkinningMode.Body,
                        TargetBones = bodyBones,
                        SharedRootBone = sharedRootBone,
                        ClothesMaterial = topPart,
                        Textures = textures,
                        BakedClothesTextures = bakedTextures,
                        TextureLoader = textureLoader,
                        MaterialEditorObjectType = 1,
                        MaterialEditorCoordinateIndex = coordinateIndex,
                        MaterialEditorSlot = topType == 1 ? 0 : 2,
                    });
                ApplyWornClothesState(instance);
            }

            return bodyAlphaMask;
        }

        internal static void ApplyCoveredMaleBodyState(
            GameObject bodyModel,
            bool coversGroin)
        {
            if (bodyModel == null || !coversGroin)
            {
                return;
            }

            var sensitiveRoot = FindByName(bodyModel.transform, "n_dankon");
            if (sensitiveRoot != null)
            {
                sensitiveRoot.gameObject.SetActive(false);
            }
        }

        private static void ApplyWornClothesState(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            var transforms = instance.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                switch (transforms[index].name)
                {
                    case "n_top_a":
                    case "n_bot_a":
                    case "n_panst_a":
                        transforms[index].gameObject.SetActive(true);
                        break;
                    case "n_top_b":
                    case "n_top_c":
                    case "n_bot_b":
                    case "n_bot_c":
                    case "n_panst_b":
                    case "n_panst_c":
                        transforms[index].gameObject.SetActive(false);
                        break;
                }
            }
        }

        private static void LoadAccessories(
            KoikatsuCard card,
            string abdataRoot,
            KoikatsuListCatalog catalog,
            Transform bodyTop,
            IReadOnlyDictionary<string, Transform> bodyBones,
            IReadOnlyDictionary<string, Transform> headBones,
            Transform sharedRootBone,
            List<KoikatsuAssetBundleLease> leases,
            List<Material> runtimeMaterials,
            KoikatsuTextureLoader textureLoader,
            int coordinateIndex,
            CancellationToken cancellationToken)
        {
            if (card.Coordinates == null || card.Coordinates.Count == 0)
            {
                return;
            }

            var coordinate = card.Coordinates[coordinateIndex];
            var loader = new KoikatsuPartLoader(
                new KoikatsuVanillaAssetResolver(abdataRoot, catalog, card),
                leases,
                runtimeMaterials);
            var hair = card.Hair != null && card.Hair.Parts.Count != 0
                ? card.Hair.Parts[0]
                : null;
            for (var slot = 0; slot < coordinate.Accessories.Count; slot++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var accessory = coordinate.Accessories[slot];
                var property =
                    $"outfit{coordinateIndex}.accessory{slot}." +
                    "ChaFileAccessory.PartsInfo.id";
                if (accessory == null || accessory.Type < 121 ||
                    accessory.Type > 130 || accessory.Id < 0 ||
                    !catalog.TryGet(
                        accessory.Type,
                        accessory.Id,
                        card.FindSideloaderGuid(
                            property,
                            accessory.Type,
                            accessory.Id),
                        out var entry))
                {
                    continue;
                }

                var defaultParentKey = entry.Get("Parent");
                var isSkinned = string.Equals(
                    defaultParentKey,
                    "null",
                    StringComparison.Ordinal);
                var parentKey = string.IsNullOrEmpty(accessory.ParentKey)
                    ? defaultParentKey
                    : accessory.ParentKey;
                var parent = isSkinned
                    ? bodyTop
                    : ResolveAccessoryParent(
                        parentKey,
                        bodyTop,
                        bodyBones,
                        headBones);
                var instance = loader.Load(
                    new KoikatsuAssetRequest(
                        accessory.Type,
                        accessory.Id,
                        property),
                    new KoikatsuPartLoadOptions
                    {
                        Parent = parent,
                        ObjectName = $"ca_slot{slot:00}",
                        SkinningMode = isSkinned
                            ? KoikatsuSkinningMode.Head
                            : KoikatsuSkinningMode.None,
                        TargetBones = isSkinned ? headBones : null,
                        SharedRootBone = isSkinned ? sharedRootBone : null,
                        AccessoryMaterial = accessory,
                        AccessoryHairMaterial = hair,
                        TextureLoader = textureLoader,
                        MaterialEditorObjectType = 2,
                        MaterialEditorCoordinateIndex = coordinateIndex,
                        MaterialEditorSlot = slot,
                    });
                if (instance != null)
                {
                    ApplyAccessoryMoves(
                        instance.transform,
                        accessory.AdditionalMoves,
                        entry.Get("HideHair") == "1");
                }
            }
        }

        private static Transform ResolveAccessoryParent(
            string parentKey,
            Transform bodyTop,
            IReadOnlyDictionary<string, Transform> bodyBones,
            IReadOnlyDictionary<string, Transform> headBones)
        {
            if (string.IsNullOrEmpty(parentKey) ||
                string.Equals(parentKey, "0", StringComparison.Ordinal) ||
                string.Equals(parentKey, "none", StringComparison.Ordinal))
            {
                return bodyTop;
            }

            if (headBones.TryGetValue(parentKey, out var headParent))
            {
                return headParent;
            }

            return bodyBones.TryGetValue(parentKey, out var bodyParent)
                ? bodyParent
                : bodyTop;
        }

        private static void ApplyAccessoryMoves(
            Transform accessoryRoot,
            Vector3[,] moves,
            bool resetMoves)
        {
            if (moves == null)
            {
                return;
            }

            var moveNames = new[] { "N_move", "N_move2" };
            var moveCount = Math.Min(moveNames.Length, moves.GetLength(0));
            for (var index = 0; index < moveCount; index++)
            {
                var move = FindByName(accessoryRoot, moveNames[index]);
                if (move == null)
                {
                    continue;
                }

                if (resetMoves)
                {
                    move.localPosition = Vector3.zero;
                    move.localRotation = Quaternion.identity;
                    move.localScale = Vector3.one;
                    continue;
                }

                if (moves.GetLength(1) > 0)
                {
                    move.localPosition = moves[index, 0] * 0.01f;
                }

                if (moves.GetLength(1) > 1)
                {
                    move.localRotation = Quaternion.Euler(moves[index, 1]);
                }

                if (moves.GetLength(1) > 2)
                {
                    move.localScale = moves[index, 2];
                }
            }
        }

        private static KoikatsuCardClothesPart GetClothesPart(
            KoikatsuCardCoordinate coordinate,
            int index)
        {
            if (coordinate != null && index < coordinate.Clothes.Count)
            {
                return coordinate.Clothes[index];
            }

            return new KoikatsuCardClothesPart(
                0,
                new[]
                {
                    new KoikatsuCardClothesColor(
                        Color.white,
                        0,
                        Vector2.zero,
                        Color.white),
                },
                0,
                0,
                Array.Empty<bool>(),
                0);
        }

        private static IReadOnlyList<string> BuildCoordinateNames(int count)
        {
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            var names = new string[count];
            for (var index = 0; index < names.Length; index++)
            {
                names[index] = index < CoordinateNames.Length
                    ? CoordinateNames[index]
                    : $"Outfit {index + 1}";
            }

            return Array.AsReadOnly(names);
        }


        private static GameObject InstantiateRequired(
            AssetBundle bundle,
            string assetName,
            Transform parent)
        {
            var prefab = bundle.LoadAsset<GameObject>(assetName);
            if (prefab == null)
            {
                throw new InvalidDataException(
                    $"AssetBundle does not contain prefab '{assetName}'.");
            }

            return Object.Instantiate(prefab, parent, false);
        }

        private static Transform FindRequired(Transform root, string name)
        {
            var transform = FindByName(root, name);
            if (transform == null)
            {
                throw new InvalidDataException(
                    $"Koikatsu prefab is missing required transform '{name}'.");
            }

            return transform;
        }

        private static Transform FindByName(Transform root, string name)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (string.Equals(
                        transforms[index].name,
                        name,
                        StringComparison.Ordinal))
                {
                    return transforms[index];
                }
            }

            return null;
        }

        private static Dictionary<string, Transform> BuildNameMap(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (!result.ContainsKey(transforms[index].name))
                {
                    result.Add(transforms[index].name, transforms[index]);
                }
            }

            return result;
        }

        private static void CopySameNameLocalTransforms(
            Transform destinationRoot,
            Transform sourceRoot)
        {
            var source = new Dictionary<string, List<Transform>>(
                StringComparer.Ordinal);
            var sourceTransforms = sourceRoot.GetComponentsInChildren<Transform>(
                true);
            for (var index = 0; index < sourceTransforms.Length; index++)
            {
                var transform = sourceTransforms[index];
                if (!source.TryGetValue(transform.name, out var matches))
                {
                    matches = new List<Transform>();
                    source.Add(transform.name, matches);
                }

                matches.Add(transform);
            }

            var occurrence = new Dictionary<string, int>(
                StringComparer.Ordinal);
            var destination = destinationRoot.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < destination.Length; index++)
            {
                var transform = destination[index];
                if (!source.TryGetValue(transform.name, out var matches))
                {
                    continue;
                }

                occurrence.TryGetValue(transform.name, out var ordinal);
                occurrence[transform.name] = ordinal + 1;
                if (ordinal >= matches.Count)
                {
                    continue;
                }

                var match = matches[ordinal];
                transform.localPosition = match.localPosition;
                transform.localRotation = match.localRotation;
                transform.localScale = match.localScale;
            }
        }

        private static void RebindSkinning(
            GameObject model,
            IReadOnlyDictionary<string, Transform> targetBones,
            Transform sharedRootBone,
            string duplicateRootName)
        {
            var duplicateRoot = FindByName(model.transform, duplicateRootName);
            var preserveLocalSkeleton = KoikatsuBoneProxyFollower.RequiresProxy(
                duplicateRoot,
                targetBones);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var sourceBones = renderer.bones;
                var reboundBones = new Transform[sourceBones.Length];
                for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    var sourceBone = sourceBones[boneIndex];
                    if (sourceBone != null &&
                        targetBones.TryGetValue(sourceBone.name, out var targetBone))
                    {
                        reboundBones[boneIndex] = targetBone;
                    }
                    else
                    {
                        reboundBones[boneIndex] = sourceBone;
                    }
                }

                renderer.bones = reboundBones;
                renderer.localBounds = CharacterBounds;
                if (renderer.GetComponent<Cloth>() == null)
                {
                    renderer.rootBone = sharedRootBone;
                }
                else if (renderer.rootBone != null &&
                         targetBones.TryGetValue(
                             renderer.rootBone.name,
                             out var targetRoot))
                {
                    renderer.rootBone = targetRoot;
                }
            }

            if (duplicateRoot != null)
            {
                if (preserveLocalSkeleton)
                {
                    model.AddComponent<KoikatsuBoneProxyFollower>().Configure(
                        duplicateRoot,
                        targetBones);
                }
                else
                {
                    duplicateRoot.SetParent(null, false);
                    DestroyRuntimeObject(duplicateRoot.gameObject);
                }
            }
        }

        internal static void DestroyRuntimeObject(Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }

        private readonly struct KoikatsuClothesLoadResult
        {
            public KoikatsuClothesLoadResult(
                Texture2D bodyAlphaMask,
                bool coversGroin)
            {
                BodyAlphaMask = bodyAlphaMask;
                CoversGroin = coversGroin;
            }

            public Texture2D BodyAlphaMask { get; }

            public bool CoversGroin { get; }
        }
    }

    internal sealed class KoikatsuReferenceModelInstance :
        IReferenceModelInstance,
        IReferenceModelPhysicsController,
        IReferenceModelSkeletonProvider,
        IReferenceModelVariantProvider,
        ICharacterModel,
        ICharacterKinematicGroupController
    {
        private GameObject root;
        private IReadOnlyList<ReferenceModelBone> bones;
        private CharacterSkeleton characterSkeleton;
        private CharacterGeometry characterGeometry;
        private CharacterPoseCoordinator poseCoordinator;
        private CharacterMouthController mouthController;
        private CharacterEyeOpenController eyeOpenController;
        private CharacterEyebrowController eyebrowController;
        private CharacterHandPoseController handPoseController;
        private CharacterEyeLookController eyeLookController;
        private ICharacterControls controls;
        private List<KoikatsuAssetBundleLease> bundleLeases;
        private List<Material> runtimeMaterials;
        private List<Texture2D> runtimeTextures;
        private readonly List<Object> runtimeObjects = new List<Object>();
        private readonly KoikatsuCard sourceCard;
        private IReadOnlyList<string> variantNames;
        private bool physicsEnabled;

        public KoikatsuReferenceModelInstance(
            GameObject root,
            IReadOnlyList<ReferenceModelBone> bones,
            CharacterSkeleton characterSkeleton,
            CharacterGeometry characterGeometry,
            List<KoikatsuAssetBundleLease> bundleLeases,
            List<Material> runtimeMaterials,
            List<Texture2D> runtimeTextures,
            KoikatsuCard sourceCard,
            IReadOnlyList<string> variantNames,
            int activeVariantIndex)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.bones = bones ?? throw new ArgumentNullException(nameof(bones));
            this.characterSkeleton = characterSkeleton ??
                throw new ArgumentNullException(nameof(characterSkeleton));
            this.characterGeometry = characterGeometry ??
                throw new ArgumentNullException(nameof(characterGeometry));
            poseCoordinator = CharacterPoseCoordinator.Attach(
                root,
                characterSkeleton);
            controls = new CharacterControlSet(poseCoordinator, this);
            this.bundleLeases = bundleLeases ??
                throw new ArgumentNullException(nameof(bundleLeases));
            this.runtimeMaterials = runtimeMaterials ??
                throw new ArgumentNullException(nameof(runtimeMaterials));
            this.runtimeTextures = runtimeTextures ??
                throw new ArgumentNullException(nameof(runtimeTextures));
            this.sourceCard = sourceCard;
            this.variantNames = variantNames ??
                throw new ArgumentNullException(nameof(variantNames));
            ActiveVariantIndex = activeVariantIndex;
            SetPhysicsEnabled(false);
        }

        public string DisplayName => root != null ? root.name : string.Empty;

        public GameObject Root => root;

        internal KoikatsuCard SourceCard => sourceCard;

        public IReadOnlyList<ReferenceModelBone> Bones => bones;

        public CharacterSkeleton Skeleton => characterSkeleton;

        public CharacterGeometry Geometry => characterGeometry;

        public ICharacterControls Controls => controls;

        public CharacterKinematicModes SupportedKinematicModes
        {
            get
            {
                var pose = root != null
                    ? root.GetComponent<KoikatsuStudioCharacterPose>()
                    : null;
                return pose != null
                    ? pose.SupportedKinematicModes
                    : CharacterKinematicModes.None;
            }
        }

        public CharacterKinematicMode KinematicMode
        {
            get
            {
                var pose = root != null
                    ? root.GetComponent<KoikatsuStudioCharacterPose>()
                    : null;
                return pose != null
                    ? pose.KinematicMode
                    : CharacterKinematicMode.None;
            }
        }

        public CharacterKinematicModes ActiveKinematicModes =>
            GetStudioPose()?.ActiveKinematicModes ??
            CharacterKinematicModes.None;

        public void SetKinematicMode(CharacterKinematicMode mode)
        {
            var pose = root != null
                ? root.GetComponent<KoikatsuStudioCharacterPose>()
                : null;
            if (pose == null)
            {
                if (mode != CharacterKinematicMode.None)
                {
                    throw new InvalidOperationException(
                        "The character has no imported kinematic pose.");
                }

                return;
            }

            pose.SetKinematicMode(mode);
        }

        public void SetKinematicModeActive(
            CharacterKinematicMode mode,
            bool active)
        {
            var pose = GetStudioPose();
            if (pose == null)
            {
                if (active)
                {
                    throw new InvalidOperationException(
                        "The character has no imported kinematic pose.");
                }

                return;
            }

            pose.SetKinematicModeActive(mode, active);
        }

        public CharacterKinematicGroups GetSupportedGroups(
            CharacterKinematicMode mode)
        {
            return GetStudioPose()?.GetSupportedGroups(mode) ??
                   CharacterKinematicGroups.None;
        }

        public CharacterKinematicGroups GetActiveGroups(
            CharacterKinematicMode mode)
        {
            return GetStudioPose()?.GetActiveGroups(mode) ??
                   CharacterKinematicGroups.None;
        }

        public void SetGroupActive(
            CharacterKinematicMode mode,
            CharacterKinematicGroups group,
            bool active)
        {
            var pose = GetStudioPose();
            if (pose == null)
            {
                throw new InvalidOperationException(
                    "The character has no imported kinematic pose.");
            }

            pose.SetGroupActive(mode, group, active);
        }

        private KoikatsuStudioCharacterPose GetStudioPose()
        {
            return root != null
                ? root.GetComponent<KoikatsuStudioCharacterPose>()
                : null;
        }

        public CharacterModelFeatures Features
        {
            get
            {
                var result = CharacterModelFeatures.None;
                if (characterSkeleton.SemanticBoneCount > 0)
                {
                    result |= CharacterModelFeatures.SemanticSkeleton;
                }

                if (characterGeometry.HasAnatomyGeometry)
                {
                    result |= CharacterModelFeatures.AnatomyGeometry;
                }

                if (characterSkeleton.SupportsBodyConstraints &&
                    characterGeometry.HasAnatomyGeometry)
                {
                    result |= CharacterModelFeatures.BodyConstraints;
                }

                return result;
            }
        }

        public string VariantLabel => "Outfit";

        public IReadOnlyList<string> VariantNames => variantNames;

        public int ActiveVariantIndex { get; }

        public bool SupportsPhysics => KoikatsuPhysicsRuntime.Supports(root);

        public bool PhysicsEnabled => SupportsPhysics && physicsEnabled;

        public void SetPhysicsEnabled(bool enabled)
        {
            physicsEnabled = enabled && SupportsPhysics;
            KoikatsuPhysicsRuntime.SetEnabled(root, physicsEnabled);
        }

        internal void AddBundleLease(KoikatsuAssetBundleLease lease)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            if (root == null)
            {
                throw new ObjectDisposedException(
                    nameof(KoikatsuReferenceModelInstance));
            }

            bundleLeases.Add(lease);
        }

        internal void AddRuntimeObject(Object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (root == null)
            {
                throw new ObjectDisposedException(
                    nameof(KoikatsuReferenceModelInstance));
            }

            runtimeObjects.Add(value);
        }

        internal void AttachMorphControllers(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            KoikatsuBundleSource bodyBundleSource)
        {
            mouthController = root != null
                ? root.GetComponentInChildren<CharacterMouthController>(true)
                : null;
            eyeOpenController = root != null
                ? root.GetComponentInChildren<CharacterEyeOpenController>(true)
                : null;
            eyebrowController = root != null
                ? root.GetComponentInChildren<CharacterEyebrowController>(true)
                : null;
            handPoseController = KoikatsuMorphControllerLoader.CreateHands(
                abdataRoot,
                catalog,
                root,
                characterSkeleton,
                poseCoordinator);
            eyeLookController = KoikatsuMorphControllerLoader.AttachEyes(
                bodyBundleSource,
                "p_cf_head_bone",
                root,
                characterSkeleton,
                poseCoordinator);
            controls = new CharacterControlSet(
                poseCoordinator,
                this,
                mouthController,
                eyeOpenController,
                eyeLookController,
                handPoseController,
                eyebrowController);
        }

        public void Dispose()
        {
            if (root == null)
            {
                return;
            }

            KoikatsuCharacterAssembler.DestroyRuntimeObject(root);
            root = null;
            physicsEnabled = false;

            for (var index = 0; index < runtimeMaterials.Count; index++)
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(
                    runtimeMaterials[index]);
            }

            runtimeMaterials.Clear();
            for (var index = 0; index < runtimeTextures.Count; index++)
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(
                    runtimeTextures[index]);
            }

            runtimeTextures.Clear();
            for (var index = 0; index < runtimeObjects.Count; index++)
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(
                    runtimeObjects[index]);
            }

            runtimeObjects.Clear();
            variantNames = Array.Empty<string>();
            for (var index = bundleLeases.Count - 1; index >= 0; index--)
            {
                bundleLeases[index].Dispose();
            }

            bundleLeases.Clear();
            bones = Array.Empty<ReferenceModelBone>();
            characterSkeleton = CharacterSkeleton.Empty;
            characterGeometry = CharacterGeometry.Empty;
            poseCoordinator = null;
            mouthController = null;
            eyeOpenController = null;
            eyebrowController = null;
            handPoseController = null;
            eyeLookController = null;
            controls = null;
        }
    }
}
