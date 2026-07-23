using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BodyEditor.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BodyEditor.ReferenceModels
{
    internal sealed class KoikatsuAssetRequest
    {
        public KoikatsuAssetRequest(
            int category,
            int slot,
            string property,
            int defaultSlot = -1)
        {
            Category = category;
            Slot = slot;
            Property = property ?? string.Empty;
            DefaultSlot = defaultSlot;
        }

        public int Category { get; }

        public int Slot { get; }

        public string Property { get; }

        public int DefaultSlot { get; }
    }

    internal sealed class KoikatsuResolvedAsset
    {
        public KoikatsuResolvedAsset(
            IReadOnlyList<KoikatsuBundleSource> bundleSources,
            string assetName,
            string manifestName,
            int category,
            int requestedSlot,
            int resolvedSlot,
            string property,
            string source,
            KoikatsuListEntry listEntry = null)
        {
            BundleSources = bundleSources ??
                throw new ArgumentNullException(nameof(bundleSources));
            if (BundleSources.Count == 0)
            {
                throw new ArgumentException(
                    "At least one AssetBundle source is required.",
                    nameof(bundleSources));
            }
            AssetName = assetName ?? string.Empty;
            ManifestName = manifestName ?? string.Empty;
            Category = category;
            RequestedSlot = requestedSlot;
            ResolvedSlot = resolvedSlot;
            Property = property ?? string.Empty;
            Source = source ?? string.Empty;
            ListEntry = listEntry;
        }

        public IReadOnlyList<KoikatsuBundleSource> BundleSources { get; }

        public KoikatsuBundleSource BundleSource => BundleSources[0];

        public string BundlePath => BundleSource.FilePath;

        public string AssetName { get; }

        public string ManifestName { get; }

        public int Category { get; }

        public int RequestedSlot { get; }

        public int ResolvedSlot { get; }

        public string Property { get; }

        public string Source { get; }

        public KoikatsuListEntry ListEntry { get; }

        public bool IsDummy =>
            string.Equals(AssetName, "p_dummy", StringComparison.Ordinal);
    }

    internal interface IKoikatsuAssetResolver
    {
        bool TryResolve(
            KoikatsuAssetRequest request,
            out KoikatsuResolvedAsset asset);
    }

    internal sealed class KoikatsuVanillaAssetResolver :
        IKoikatsuAssetResolver
    {
        private readonly string abdataRoot;
        private readonly KoikatsuListCatalog catalog;
        private readonly KoikatsuCard card;

        public KoikatsuVanillaAssetResolver(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            KoikatsuCard card = null)
        {
            this.abdataRoot = Path.GetFullPath(
                abdataRoot ?? throw new ArgumentNullException(nameof(abdataRoot)));
            this.catalog = catalog ??
                throw new ArgumentNullException(nameof(catalog));
            this.card = card;
        }

        public bool TryResolve(
            KoikatsuAssetRequest request,
            out KoikatsuResolvedAsset asset)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var resolvedSlot = request.Slot;
            var modGuid = card?.FindSideloaderGuid(
                request.Property,
                request.Category,
                request.Slot);
            if (!catalog.TryGet(
                    request.Category,
                    resolvedSlot,
                    modGuid,
                    out var entry))
            {
                var missingDescription =
                    $"card '{card?.SourcePath ?? "(unknown)"}', " +
                    $"property '{request.Property}', category " +
                    $"{request.Category}, slot {request.Slot}";
                if (request.DefaultSlot < 0)
                {
                    Debug.LogError(
                        "Koikatsu resource is missing for " +
                        missingDescription +
                        (string.IsNullOrEmpty(modGuid)
                            ? "."
                            : $", zipmod GUID '{modGuid}'."));
                    asset = null;
                    return false;
                }

                Debug.LogWarning(
                    "Koikatsu resource is missing for " +
                    missingDescription +
                    (string.IsNullOrEmpty(modGuid)
                        ? string.Empty
                        : $", zipmod GUID '{modGuid}'") +
                    $". Falling back to slot {request.DefaultSlot}.");
                resolvedSlot = request.DefaultSlot;
                if (!catalog.TryGet(request.Category, resolvedSlot, out entry))
                {
                    resolvedSlot = 0;
                    if (!catalog.TryGet(request.Category, resolvedSlot, out entry))
                    {
                        asset = null;
                        return false;
                    }
                }
            }

            var assetName = entry.Get("MainData");
            var mainAb = entry.Get("MainAB");
            if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(mainAb))
            {
                asset = null;
                return false;
            }

            asset = new KoikatsuResolvedAsset(
                catalog.ResolveBundleCandidates(
                    abdataRoot,
                    mainAb,
                    entry.Archive),
                assetName,
                entry.Get("MainManifest"),
                request.Category,
                request.Slot,
                resolvedSlot,
                request.Property,
                string.IsNullOrEmpty(entry.ModGuid)
                    ? "vanilla"
                    : $"zipmod {entry.ModGuid}",
                entry);
            return true;
        }
    }

    internal static class KoikatsuAssetPath
    {
        public static string ResolveAbdataPath(
            string abdataRoot,
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidDataException(
                    "Koikatsu list entry has no AssetBundle path.");
            }

            relativePath = relativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
            const string prefix = "abdata";
            if (relativePath.StartsWith(
                    prefix + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Substring(prefix.Length + 1);
            }

            var root = Path.GetFullPath(abdataRoot)
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var result = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Koikatsu asset path escapes abdata: {relativePath}");
            }

            return result;
        }
    }

    internal enum KoikatsuSkinningMode
    {
        None,
        Body,
        Head,
    }

    internal sealed class KoikatsuPartLoadOptions
    {
        public Transform Parent { get; set; }

        public string ObjectName { get; set; }

        public Vector3 LocalPosition { get; set; }

        public Vector3 LocalEulerAngles { get; set; }

        public Vector3 LocalScale { get; set; } = Vector3.one;

        public KoikatsuSkinningMode SkinningMode { get; set; }

        public IReadOnlyDictionary<string, Transform> TargetBones { get; set; }

        public Transform SharedRootBone { get; set; }

        public KoikatsuCardHairPart HairMaterial { get; set; }

        public Texture2D HairGlossTexture { get; set; }

        public KoikatsuTextureLoader TextureLoader { get; set; }

        public int MaterialEditorObjectType { get; set; } = -1;

        public int MaterialEditorCoordinateIndex { get; set; } = -1;

        public int MaterialEditorSlot { get; set; } = -1;

        public KoikatsuCardClothesPart ClothesMaterial { get; set; }

        public KoikatsuCardAccessory AccessoryMaterial { get; set; }

        public KoikatsuCardHairPart AccessoryHairMaterial { get; set; }

        public KoikatsuTextureSet Textures { get; set; }

        public KoikatsuBakedClothesTextures BakedClothesTextures { get; set; }

        public bool PhysicsAllowed
        {
            get
            {
                if (HairMaterial != null)
                {
                    return !HairMaterial.NoShake;
                }

                if (AccessoryMaterial != null)
                {
                    return !AccessoryMaterial.NoShake;
                }

                return true;
            }
        }
    }

    [DefaultExecutionOrder(31000)]
    internal sealed class KoikatsuBoneProxyFollower : MonoBehaviour
    {
        private Binding[] bindings = Array.Empty<Binding>();

        public static bool RequiresProxy(
            Transform root,
            IReadOnlyDictionary<string, Transform> targetBones)
        {
            if (root == null || targetBones == null)
            {
                return false;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (!targetBones.ContainsKey(transforms[index].name))
                {
                    return true;
                }
            }

            return false;
        }

        public void Configure(
            Transform sourceRoot,
            IReadOnlyDictionary<string, Transform> targetBones)
        {
            if (sourceRoot == null)
            {
                throw new ArgumentNullException(nameof(sourceRoot));
            }

            if (targetBones == null)
            {
                throw new ArgumentNullException(nameof(targetBones));
            }

            var values = new List<Binding>();
            var sources = sourceRoot.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index];
                if (targetBones.TryGetValue(source.name, out var target) &&
                    target != null && target != source)
                {
                    values.Add(new Binding(source, target));
                }
            }

            bindings = values.ToArray();
            Synchronize();
        }

        private void LateUpdate()
        {
            Synchronize();
        }

        private void Synchronize()
        {
            for (var index = 0; index < bindings.Length; index++)
            {
                var binding = bindings[index];
                if (binding.Source == null || binding.Target == null)
                {
                    continue;
                }

                binding.Source.localPosition = binding.Target.localPosition;
                binding.Source.localRotation = binding.Target.localRotation;
                binding.Source.localScale = binding.Target.localScale;
            }
        }

        private readonly struct Binding
        {
            public Binding(Transform source, Transform target)
            {
                Source = source;
                Target = target;
            }

            public Transform Source { get; }

            public Transform Target { get; }
        }
    }

    internal sealed class KoikatsuPartLoader
    {
        private static readonly Bounds CharacterBounds = new Bounds(
            new Vector3(0f, -0.2f, 0f),
            new Vector3(2f, 2f, 2f));

        private readonly IKoikatsuAssetResolver resolver;
        private readonly List<KoikatsuAssetBundleLease> leases;
        private readonly List<Material> runtimeMaterials;

        public KoikatsuPartLoader(
            IKoikatsuAssetResolver resolver,
            List<KoikatsuAssetBundleLease> leases,
            List<Material> runtimeMaterials)
        {
            this.resolver = resolver ??
                throw new ArgumentNullException(nameof(resolver));
            this.leases = leases ??
                throw new ArgumentNullException(nameof(leases));
            this.runtimeMaterials = runtimeMaterials ??
                throw new ArgumentNullException(nameof(runtimeMaterials));
        }

        public GameObject Load(
            KoikatsuAssetRequest request,
            KoikatsuPartLoadOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Parent == null)
            {
                throw new ArgumentException(
                    "A parent transform is required.",
                    nameof(options));
            }

            if (!resolver.TryResolve(request, out var asset) || asset.IsDummy)
            {
                return null;
            }

            if (!asset.BundleSources.Any(
                    source => File.Exists(source.FilePath)))
            {
                throw new FileNotFoundException(
                    $"Koikatsu {asset.Source} AssetBundle was not found for " +
                    $"category {asset.Category}, slot {asset.ResolvedSlot}.",
                    asset.BundleSource.DisplayName);
            }

            KoikatsuAssetBundleLease lease = null;
            KoikatsuBundleSource loadedSource = null;
            GameObject instance = null;
            var materialStart = runtimeMaterials.Count;
            try
            {
                GameObject prefab = null;
                for (var sourceIndex = 0;
                     sourceIndex < asset.BundleSources.Count;
                     sourceIndex++)
                {
                    var candidate = asset.BundleSources[sourceIndex];
                    if (!File.Exists(candidate.FilePath))
                    {
                        continue;
                    }

                    KoikatsuAssetBundleLease candidateLease = null;
                    try
                    {
                        candidateLease =
                            KoikatsuAssetBundleCache.Acquire(candidate);
                        if (!candidateLease.Bundle.Contains(asset.AssetName))
                        {
                            candidateLease.Dispose();
                            continue;
                        }

                        prefab = candidateLease.Bundle.LoadAsset<GameObject>(
                            asset.AssetName);
                        if (prefab == null)
                        {
                            candidateLease.Dispose();
                            continue;
                        }
                    }
                    catch (Exception exception) when (
                        IsCandidateFailure(exception))
                    {
                        candidateLease?.Dispose();
                        continue;
                    }

                    lease = candidateLease;
                    loadedSource = candidate;
                    break;
                }

                if (prefab == null)
                {
                    throw new InvalidDataException(
                        "No Koikatsu Sideloader AssetBundle candidate contains " +
                        $"prefab '{asset.AssetName}'.");
                }

                instance = Object.Instantiate(prefab, options.Parent, false);
                instance.name = string.IsNullOrEmpty(options.ObjectName)
                    ? asset.AssetName
                    : options.ObjectName;
                instance.transform.localPosition = options.LocalPosition;
                instance.transform.localRotation = Quaternion.Euler(
                    options.LocalEulerAngles);
                instance.transform.localScale = options.LocalScale;

                var clothesRendererMap = options.ClothesMaterial != null
                    ? KoikatsuClothesRendererMapLoader.TryCreate(
                        loadedSource,
                        asset.AssetName,
                        instance)
                    : null;

                if (options.SkinningMode != KoikatsuSkinningMode.None)
                {
                    RebindSkinning(instance, options);
                }

                options.TextureLoader?.ApplyMaterialEditorProperties(
                    instance,
                    options.MaterialEditorObjectType,
                    options.MaterialEditorCoordinateIndex,
                    options.MaterialEditorSlot);

                if (options.HairMaterial != null)
                {
                    KoikatsuMaterialConverter.ConvertHair(
                        instance,
                        options.HairMaterial,
                        options.HairGlossTexture,
                        runtimeMaterials);
                }
                else if (options.ClothesMaterial != null)
                {
                    KoikatsuMaterialConverter.ConvertClothes(
                        instance,
                        options.ClothesMaterial,
                        options.Textures,
                        options.BakedClothesTextures,
                        clothesRendererMap,
                        runtimeMaterials);
                }
                else if (options.AccessoryMaterial != null)
                {
                    var accessoryRendererMap =
                        KoikatsuAccessoryRendererMapLoader.TryCreate(
                            loadedSource,
                            asset.AssetName,
                            instance);
                    KoikatsuMaterialConverter.ConvertAccessory(
                        instance,
                        options.AccessoryMaterial,
                        options.AccessoryHairMaterial,
                        accessoryRendererMap,
                        runtimeMaterials);
                }
                else
                {
                    KoikatsuMaterialConverter.Convert(
                        instance,
                        runtimeMaterials);
                }

                // A baked map may have replaced MainTex during conversion;
                // MaterialEditor overrides are the final source of truth.
                options.TextureLoader?.ApplyMaterialEditorProperties(
                    instance,
                    options.MaterialEditorObjectType,
                    options.MaterialEditorCoordinateIndex,
                    options.MaterialEditorSlot);

                KoikatsuSpringBoneMetadataLoader.Attach(
                    loadedSource,
                    asset.AssetName,
                    instance,
                    options.PhysicsAllowed);

                KoikatsuVer02MetadataLoader.Attach(
                    loadedSource,
                    asset.AssetName,
                    instance,
                    options.PhysicsAllowed);

                leases.Add(lease);
                lease = null;
                return instance;
            }
            catch
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(instance);
                for (var index = runtimeMaterials.Count - 1;
                     index >= materialStart;
                     index--)
                {
                    KoikatsuCharacterAssembler.DestroyRuntimeObject(
                        runtimeMaterials[index]);
                    runtimeMaterials.RemoveAt(index);
                }

                lease?.Dispose();
                throw;
            }
        }

        private static bool IsCandidateFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is InvalidDataException ||
                   exception is NotSupportedException ||
                   exception is UnityException;
        }

        private static void RebindSkinning(
            GameObject model,
            KoikatsuPartLoadOptions options)
        {
            if (options.TargetBones == null || options.SharedRootBone == null)
            {
                throw new InvalidOperationException(
                    "Skinning rebind requires target bones and a root bone.");
            }

            var duplicateRootName = options.SkinningMode ==
                                    KoikatsuSkinningMode.Body
                ? "cf_j_root"
                : "cf_J_N_FaceRoot";
            var duplicateRoot = FindByName(model.transform, duplicateRootName);
            var preserveLocalSkeleton = KoikatsuBoneProxyFollower.RequiresProxy(
                duplicateRoot,
                options.TargetBones);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var sourceBones = renderer.bones;
                var reboundBones = new Transform[sourceBones.Length];
                for (var boneIndex = 0;
                     boneIndex < sourceBones.Length;
                     boneIndex++)
                {
                    var sourceBone = sourceBones[boneIndex];
                    if (sourceBone != null && options.TargetBones.TryGetValue(
                            sourceBone.name,
                            out var targetBone))
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
                    renderer.rootBone = options.SharedRootBone;
                }
                else if (renderer.rootBone != null &&
                         options.TargetBones.TryGetValue(
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
                        options.TargetBones);
                }
                else
                {
                    duplicateRoot.SetParent(null, false);
                    KoikatsuCharacterAssembler.DestroyRuntimeObject(
                        duplicateRoot.gameObject);
                }
            }
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
    }

    internal sealed class KoikatsuTextureSet
    {
        public Texture2D Main { get; set; }

        public Texture2D Main02 { get; set; }

        public Texture2D Main03 { get; set; }

        public Texture2D ColorMask { get; set; }

        public Texture2D ColorMask02 { get; set; }

        public Texture2D ColorMask03 { get; set; }

        public Texture2D Select(KoikatsuClothesTextureSlot slot)
        {
            switch (slot)
            {
                case KoikatsuClothesTextureSlot.Main:
                    return Main;
                case KoikatsuClothesTextureSlot.Main02:
                    return Main02;
                case KoikatsuClothesTextureSlot.Main03:
                    return Main03;
                default:
                    return null;
            }
        }

        public Texture2D SelectForMaterial(
            Texture sourceTexture,
            string rendererName,
            string materialName)
        {
            if (IsSameTexture(sourceTexture, Main03))
            {
                return Main03;
            }

            if (IsSameTexture(sourceTexture, Main02))
            {
                return Main02;
            }

            if (IsSameTexture(sourceTexture, Main))
            {
                return Main;
            }

            var candidates = new[] { Main, Main02, Main03 };
            var availableCount = 0;
            for (var index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] != null)
                {
                    availableCount++;
                }
            }

            if (availableCount == 0)
            {
                return null;
            }

            if (availableCount == 1)
            {
                for (var index = 0; index < candidates.Length; index++)
                {
                    if (candidates[index] != null)
                    {
                        return candidates[index];
                    }
                }
            }

            var materialTokens = Tokenize(
                (rendererName ?? string.Empty) + "_" +
                (materialName ?? string.Empty));
            var bestIndex = -1;
            var bestScore = 0f;
            for (var index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] == null)
                {
                    continue;
                }

                var score = Similarity(
                    materialTokens,
                    Tokenize(candidates[index].name));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            return bestIndex >= 0 ? candidates[bestIndex] : Main;
        }

        private static bool IsSameTexture(Texture left, Texture right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return ReferenceEquals(left, right) ||
                   string.Equals(
                       left.name,
                       right.name,
                       StringComparison.Ordinal);
        }

        private static HashSet<string> Tokenize(string value)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var token = new StringBuilder();
            for (var index = 0; index <= value.Length; index++)
            {
                var character = index < value.Length ? value[index] : '_';
                if (char.IsLetterOrDigit(character))
                {
                    token.Append(char.ToLowerInvariant(character));
                    continue;
                }

                AddToken(result, token);
                token.Length = 0;
            }

            return result;
        }

        private static void AddToken(
            ISet<string> tokens,
            StringBuilder token)
        {
            if (token.Length == 0)
            {
                return;
            }

            var value = token.ToString();
            switch (value)
            {
                case "cf":
                case "cm":
                case "m":
                case "o":
                case "t":
                case "mat":
                    return;
                default:
                    tokens.Add(value);
                    return;
            }
        }

        private static float Similarity(
            ISet<string> left,
            ISet<string> right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return 0f;
            }

            var intersection = 0;
            foreach (var token in left)
            {
                if (right.Contains(token))
                {
                    intersection++;
                }
            }

            return 2f * intersection / (left.Count + right.Count);
        }
    }

    internal sealed class KoikatsuFaceTextures
    {
        public Texture2D Eyebrow { get; set; }

        public Texture2D Nose { get; set; }

        public Texture2D EyelineUp { get; set; }

        public Texture2D EyelineShadow { get; set; }

        public Texture2D EyelineDown { get; set; }
    }

    internal sealed class KoikatsuTextureLoader
    {
        // MaterialEditor leaves uploaded textures at Unity's repeat default;
        // modded meshes can intentionally address them with negative UVs.
        private const TextureWrapMode MaterialEditorTextureWrapMode =
            TextureWrapMode.Repeat;

        private readonly string abdataRoot;
        private readonly KoikatsuListCatalog catalog;
        private readonly List<KoikatsuAssetBundleLease> leases;
        private readonly ICollection<Texture2D> runtimeTextures;
        private readonly KoikatsuCard card;
        private readonly KoikatsuMaterialEditorData materialEditorData;
        private readonly KoikatsuSkinOverlayData skinOverlayData;
        private readonly Dictionary<string, KoikatsuAssetBundleLease> bundles =
            new Dictionary<string, KoikatsuAssetBundleLease>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> looseTextures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public KoikatsuTextureLoader(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            List<KoikatsuAssetBundleLease> leases,
            KoikatsuCard card,
            ICollection<Texture2D> runtimeTextures)
        {
            this.abdataRoot = Path.GetFullPath(
                abdataRoot ?? throw new ArgumentNullException(nameof(abdataRoot)));
            this.catalog = catalog ??
                throw new ArgumentNullException(nameof(catalog));
            this.leases = leases ??
                throw new ArgumentNullException(nameof(leases));
            this.card = card;
            materialEditorData = KoikatsuMaterialEditorData.Read(
                card?.Blocks,
                card?.MaterialEditorSharedTextures);
            skinOverlayData = KoikatsuSkinOverlayData.Read(card?.Blocks);
            this.runtimeTextures = runtimeTextures ??
                throw new ArgumentNullException(nameof(runtimeTextures));
        }

        public string AbdataRoot => abdataRoot;

        public Texture2D LoadMaterialEditorCharacterTexture(
            string materialName,
            string propertyName)
        {
            var cacheKey = "MaterialEditor\n" + materialName + "\n" +
                           propertyName;
            if (looseTextures.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            if (materialEditorData == null ||
                !materialEditorData.TryGetCharacterTexture(
                    materialName,
                    propertyName,
                    out var bytes))
            {
                return null;
            }

            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"Koikatsu MaterialEditor {materialName} {propertyName}",
                filterMode = FilterMode.Bilinear,
                wrapMode = MaterialEditorTextureWrapMode,
            };
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(texture);
                Debug.LogWarning(
                    $"Could not decode the Koikatsu MaterialEditor texture " +
                    $"for material '{materialName}', property '{propertyName}'.");
                return null;
            }

            looseTextures.Add(cacheKey, texture);
            runtimeTextures.Add(texture);
            return texture;
        }

        public Texture2D LoadSkinOverlayTexture(
            int coordinateIndex,
            KoikatsuSkinOverlayType type)
        {
            var cacheKey = "KSOX\n" + coordinateIndex + "\n" + (int)type;
            if (looseTextures.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            if (skinOverlayData == null ||
                !skinOverlayData.TryGetTexture(
                    coordinateIndex,
                    type,
                    out var bytes))
            {
                return null;
            }

            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"Koikatsu KSOX {(int)type} ({coordinateIndex})",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(texture);
                Debug.LogWarning(
                    $"Could not decode KSOX skin overlay type {(int)type} " +
                    $"for coordinate {coordinateIndex}.");
                return null;
            }

            looseTextures.Add(cacheKey, texture);
            runtimeTextures.Add(texture);
            return texture;
        }

        public void ApplyMaterialEditorProperties(
            GameObject model,
            int objectType,
            int coordinateIndex = -1,
            int slot = -1)
        {
            if (model == null || materialEditorData == null || objectType < 0)
            {
                return;
            }

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                var materials = renderer.materials;
                var appliedTexture = false;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    foreach (var property in materialEditorData.GetTextureProperties(
                                 objectType,
                                 coordinateIndex,
                                 slot,
                                 material.name))
                    {
                        if (!materialEditorData.TryGetTextureData(
                                property.TextureId.Value,
                                out var bytes))
                        {
                            continue;
                        }

                        var texture = DecodeMaterialEditorTexture(
                            material.name,
                            property.Property,
                            property.TextureId.Value,
                            bytes);
                        if (texture != null)
                        {
                            SetMaterialTexture(
                                material,
                                property.Property,
                                texture);
                            if (IsFaceDetailRenderer(renderer.name))
                            {
                                // ApplyFaceTexture may have hidden an empty
                                // layer with an alpha-zero base color. A
                                // MaterialEditor replacement is authoritative
                                // and must restore its alpha. Preserve the
                                // card-authored RGB for visible eye lines and
                                // brows; replacing every face-detail color
                                // with white erases eyelineColor and the
                                // skin-colored shadow material.
                                RestoreFaceDetailBaseColorAlpha(material);
                            }
                            appliedTexture = true;
                        }
                    }

                    foreach (var property in materialEditorData.GetColorProperties(
                                 objectType,
                                 coordinateIndex,
                                 slot,
                                 material.name))
                    {
                        SetMaterialColor(
                            material,
                            property.Property,
                            property.Value.ToColor(Color.white));
                    }

                    foreach (var property in materialEditorData.GetFloatProperties(
                                 objectType,
                                 coordinateIndex,
                                 slot,
                                 material.name))
                    {
                        if (float.TryParse(
                                property.Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var value))
                        {
                            SetMaterialFloat(
                                material,
                                property.Property,
                                value);
                        }
                    }

                }

                // Face-detail renderers can be disabled when their card
                // texture is explicitly empty. A MaterialEditor override is
                // an authoritative replacement and must make the layer
                // visible again.
                if (appliedTexture)
                {
                    // The converted material may have been created while the
                    // card layer was empty, so its copied render state is
                    // opaque even though the MaterialEditor replacement is
                    // an alpha texture. Restore the render state used by the
                    // original face-detail pass before enabling it.
                    if (IsFaceDetailRenderer(renderer.name))
                    {
                        for (var materialIndex = 0;
                             materialIndex < materials.Length;
                             materialIndex++)
                        {
                            MaterialRenderUtility.ConfigureTransparent(
                                materials[materialIndex]);
                        }
                    }

                    renderer.enabled = true;
                }
            }
        }

        private static bool IsFaceDetailRenderer(string rendererName)
        {
            var key = (rendererName ?? string.Empty).ToLowerInvariant();
            return key == "cf_o_eyeline" ||
                   key == "cf_o_eyeline_low" ||
                   key == "cf_o_mayuge" ||
                   key == "cf_o_noseline";
        }

        private static void RestoreFaceDetailBaseColorAlpha(Material material)
        {
            if (material == null)
            {
                return;
            }

            var color = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.color;
            if (color.a > 0f)
            {
                return;
            }

            color.a = 1f;
            MaterialRenderUtility.SetBaseColor(material, color);
        }

        private Texture2D DecodeMaterialEditorTexture(
            string materialName,
            string propertyName,
            int textureId,
            byte[] bytes)
        {
            var cacheKey = "MaterialEditor\n" + materialName + "\n" +
                           propertyName + "\n" + textureId;
            if (looseTextures.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"Koikatsu MaterialEditor {materialName} {propertyName}",
                filterMode = FilterMode.Bilinear,
                wrapMode = MaterialEditorTextureWrapMode,
            };
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(texture);
                Debug.LogWarning(
                    $"Could not decode the Koikatsu MaterialEditor texture " +
                    $"for material '{materialName}', property '{propertyName}'.");
                return null;
            }

            DilateTransparentFaceDetailBorder(
                texture,
                materialName,
                propertyName);

            looseTextures.Add(cacheKey, texture);
            runtimeTextures.Add(texture);
            return texture;
        }

        private static void DilateTransparentFaceDetailBorder(
            Texture2D texture,
            string materialName,
            string propertyName)
        {
            if (texture == null ||
                !IsFaceDetailMainTexture(materialName, propertyName))
            {
                return;
            }

            // Uploaded face PNGs often store white RGB below zero alpha.
            // Extend visible edge colors so bilinear filtering cannot reveal it.
            var source = texture.GetPixels32();
            var width = texture.width;
            var height = texture.height;
            if (source == null || source.Length != width * height ||
                width <= 0 || height <= 0)
            {
                return;
            }

            var dilated = (Color32[])source.Clone();
            var changed = false;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (source[index].a != 0)
                    {
                        continue;
                    }

                    uint red = 0;
                    uint green = 0;
                    uint blue = 0;
                    uint weight = 0;
                    for (var offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (var offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (offsetX == 0 && offsetY == 0)
                            {
                                continue;
                            }

                            var neighborX = (x + offsetX + width) % width;
                            var neighborY = (y + offsetY + height) % height;
                            var neighbor = source[neighborY * width + neighborX];
                            if (neighbor.a == 0)
                            {
                                continue;
                            }

                            red += (uint)(neighbor.r * neighbor.a);
                            green += (uint)(neighbor.g * neighbor.a);
                            blue += (uint)(neighbor.b * neighbor.a);
                            weight += neighbor.a;
                        }
                    }

                    if (weight == 0)
                    {
                        continue;
                    }

                    dilated[index] = new Color32(
                        (byte)(red / weight),
                        (byte)(green / weight),
                        (byte)(blue / weight),
                        0);
                    changed = true;
                }
            }

            if (changed)
            {
                texture.SetPixels32(dilated);
                texture.Apply(false, false);
            }
        }

        private static bool IsFaceDetailMainTexture(
            string materialName,
            string propertyName)
        {
            var property = (propertyName ?? string.Empty).TrimStart('_');
            if (!string.Equals(
                    property,
                    "MainTex",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    property,
                    "BaseMap",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var material = materialName ?? string.Empty;
            return material.IndexOf(
                       "mayuge",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   material.IndexOf(
                       "eyeline",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   material.IndexOf(
                       "noseline",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void SetMaterialTexture(
            Material material,
            string propertyName,
            Texture texture)
        {
            var applied = false;
            foreach (var property in ResolveMaterialProperties(
                         material,
                         propertyName))
            {
                material.SetTexture(property, texture);
                applied = true;
            }

            if (applied)
            {
                SetMaterialEditorOverrideTag(material, propertyName);
            }
        }

        internal static void SetMaterialColor(
            Material material,
            string propertyName,
            Color color)
        {
            var applied = false;
            foreach (var property in ResolveMaterialProperties(
                         material,
                         propertyName))
            {
                material.SetColor(property, color);
                applied = true;
            }

            if (applied)
            {
                SetMaterialEditorOverrideTag(material, propertyName);
            }
        }

        private static void SetMaterialEditorOverrideTag(
            Material material,
            string propertyName)
        {
            if (material == null || string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            material.SetOverrideTag(
                "BodyEditor.MaterialEditor." +
                propertyName.TrimStart('_'),
                "1");
        }

        private static void SetMaterialFloat(
            Material material,
            string propertyName,
            float value)
        {
            foreach (var property in ResolveMaterialProperties(
                         material,
                         propertyName))
            {
                material.SetFloat(property, value);
            }
        }

        private static IEnumerable<string> ResolveMaterialProperties(
            Material material,
            string propertyName)
        {
            if (material == null || string.IsNullOrEmpty(propertyName))
            {
                yield break;
            }

            var candidates = new[]
            {
                propertyName,
                propertyName == "MainTex" ? "_BaseMap" : null,
                propertyName == "Color" || propertyName == "_Color"
                    ? "_BaseColor"
                    : null,
                propertyName == "NormalMap" ? "_NormalMap" : null,
                propertyName == "AlphaMask" ? "_AlphaMask" : null,
                propertyName == "ColorMask" ? "_ColorMask" : null,
                "_" + propertyName,
                propertyName.Length > 0
                    ? char.ToUpperInvariant(propertyName[0]) +
                      propertyName.Substring(1)
                    : propertyName,
            };
            for (var index = 0; index < candidates.Length; index++)
            {
                if (!string.IsNullOrEmpty(candidates[index]) &&
                    material.HasProperty(candidates[index]))
                {
                    yield return candidates[index];
                }
            }
        }

        public KoikatsuTextureSet LoadPartTextures(
            int category,
            int slot,
            string property = null)
        {
            var modGuid = card?.FindSideloaderGuid(
                property ?? string.Empty,
                category,
                slot);
            return catalog.TryGet(category, slot, modGuid, out var entry)
                ? LoadPartTextures(entry)
                : new KoikatsuTextureSet();
        }

        public Material LoadVanillaMaterial(
            string bundleName,
            string assetName)
        {
            if (string.IsNullOrWhiteSpace(bundleName) ||
                string.IsNullOrWhiteSpace(assetName))
            {
                return null;
            }

            var sources = catalog.ResolveBundleCandidates(
                abdataRoot,
                bundleName);
            for (var sourceIndex = 0;
                 sourceIndex < sources.Count;
                 sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (!File.Exists(source.FilePath))
                {
                    continue;
                }

                try
                {
                    if (!bundles.TryGetValue(source.CacheKey, out var lease))
                    {
                        lease = KoikatsuAssetBundleCache.Acquire(source);
                        bundles.Add(source.CacheKey, lease);
                        leases.Add(lease);
                    }

                    if (!lease.Bundle.Contains(assetName))
                    {
                        continue;
                    }

                    var material = lease.Bundle.LoadAsset<Material>(assetName);
                    if (material != null)
                    {
                        return material;
                    }
                }
                catch (Exception exception) when (
                    IsTextureCandidateFailure(exception))
                {
                    continue;
                }
            }

            return null;
        }

        public KoikatsuTextureSet LoadPartTextures(KoikatsuListEntry entry)
        {
            if (entry == null)
            {
                return new KoikatsuTextureSet();
            }

            var mainBundle = entry.Get("MainAB");
            return new KoikatsuTextureSet
            {
                Main = Load(entry, "MainTexAB", "MainTex", mainBundle),
                Main02 = Load(entry, "MainTex02AB", "MainTex02", mainBundle),
                Main03 = Load(entry, "MainTex03AB", "MainTex03", mainBundle),
                ColorMask = Load(
                    entry,
                    "ColorMaskAB",
                    "ColorMaskTex",
                    mainBundle),
                ColorMask02 = Load(
                    entry,
                    "ColorMask02AB",
                    "ColorMask02Tex",
                    mainBundle),
                ColorMask03 = Load(
                    entry,
                    "ColorMask03AB",
                    "ColorMask03Tex",
                    mainBundle),
            };
        }

        public Texture2D LoadCatalogTexture(
            int category,
            int slot,
            string bundleKey,
            string textureKey,
            string property = null,
            string textureNameSuffix = null)
        {
            var modGuid = card?.FindSideloaderGuid(
                property ?? string.Empty,
                category,
                slot);
            if (!catalog.TryGet(category, slot, modGuid, out var entry))
            {
                Debug.LogWarning(
                    "Koikatsu texture list entry is missing for card " +
                    $"'{card?.SourcePath ?? "(unknown)"}', property " +
                    $"'{property ?? string.Empty}', category {category}, " +
                    $"slot {slot}" +
                    (string.IsNullOrEmpty(modGuid)
                        ? "."
                        : $", zipmod GUID '{modGuid}'."));
                return null;
            }

            return Load(
                entry,
                bundleKey,
                textureKey,
                entry.Get("MainAB"),
                textureNameSuffix);
        }

        public Texture2D LoadCatalogTextureForGuid(
            int category,
            int slot,
            string modGuid,
            string bundleKey,
            string textureKey)
        {
            if (!catalog.TryGet(category, slot, modGuid, out var entry))
            {
                Debug.LogWarning(
                    "Koikatsu texture list entry is missing for category " +
                    $"{category}, slot {slot}" +
                    (string.IsNullOrWhiteSpace(modGuid)
                        ? "."
                        : $", zipmod GUID '{modGuid}'."));
                return null;
            }

            return Load(
                entry,
                bundleKey,
                textureKey,
                entry.Get("MainAB"));
        }

        private Texture2D Load(
            KoikatsuListEntry entry,
            string bundleKey,
            string textureKey,
            string fallbackBundle,
            string textureNameSuffix = null)
        {
            var textureName = entry.Get(textureKey);
            if (string.IsNullOrWhiteSpace(textureName) || textureName == "0")
            {
                return null;
            }

            if (IsExplicitEmptyTexture(textureName))
            {
                return null;
            }

            var bundleName = entry.Get(bundleKey);
            if (string.IsNullOrWhiteSpace(bundleName) || bundleName == "0")
            {
                bundleName = fallbackBundle;
            }

            if (string.IsNullOrWhiteSpace(bundleName) || bundleName == "0")
            {
                return null;
            }

            if (!string.IsNullOrEmpty(textureNameSuffix))
            {
                var variantBundleName = GetHeadVariantBundleName(bundleName);
                var variant = TryLoadTexture(
                    entry,
                    variantBundleName,
                    textureName + textureNameSuffix,
                    false);
                if (variant != null)
                {
                    return variant;
                }
            }

            return TryLoadTexture(entry, bundleName, textureName, true);
        }

        private Texture2D TryLoadTexture(
            KoikatsuListEntry entry,
            string bundleName,
            string textureName,
            bool required)
        {
            var looseTexture = TryLoadLooseTexture(
                entry,
                bundleName,
                textureName,
                required);
            if (looseTexture != null)
            {
                return looseTexture;
            }

            var sources = catalog.ResolveBundleCandidates(
                abdataRoot,
                bundleName,
                entry.Archive);
            Texture2D texture = null;
            for (var sourceIndex = 0;
                 sourceIndex < sources.Count;
                 sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (!File.Exists(source.FilePath))
                {
                    continue;
                }

                try
                {
                    if (!bundles.TryGetValue(source.CacheKey, out var lease))
                    {
                        lease = KoikatsuAssetBundleCache.Acquire(source);
                        bundles.Add(source.CacheKey, lease);
                        leases.Add(lease);
                    }

                    if (!lease.Bundle.Contains(textureName))
                    {
                        continue;
                    }

                    texture = lease.Bundle.LoadAsset<Texture2D>(textureName);
                    if (texture != null)
                    {
                        break;
                    }
                }
                catch (Exception exception) when (
                    IsTextureCandidateFailure(exception))
                {
                    continue;
                }
            }

            if (texture == null && required &&
                !IsExplicitEmptyTexture(textureName))
            {
                Debug.LogWarning(
                    "Koikatsu texture asset is missing for card " +
                    $"'{card?.SourcePath ?? "(unknown)"}', category " +
                    $"{entry.Category}, slot {entry.Id}, texture " +
                    $"'{textureName}', virtual bundle '{bundleName}'.");
            }

            return texture;
        }

        private static bool IsTextureCandidateFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is InvalidDataException ||
                   exception is NotSupportedException ||
                   exception is UnityException;
        }

        private static bool IsExplicitEmptyTexture(string textureName)
        {
            return string.Equals(
                       textureName,
                       "none",
                       StringComparison.OrdinalIgnoreCase) ||
                   (textureName ?? string.Empty).EndsWith(
                       "_none",
                       StringComparison.OrdinalIgnoreCase);
        }

        private Texture2D TryLoadLooseTexture(
            KoikatsuListEntry entry,
            string bundleName,
            string textureName,
            bool required)
        {
            var cacheKey = entry.ModGuid + "\n" + bundleName + "\n" + textureName;
            if (looseTextures.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            byte[] bytes;
            string archiveEntryName;
            string archivePath;
            if (!catalog.TryReadVirtualLooseTexture(
                    bundleName,
                    textureName,
                    out bytes,
                    out archiveEntryName,
                    out archivePath) &&
                (entry.Archive == null ||
                 !entry.Archive.TryReadLooseTexture(
                     bundleName,
                     textureName,
                     out bytes,
                     out archiveEntryName)))
            {
                return null;
            }

            if (string.IsNullOrEmpty(archivePath))
            {
                archivePath = entry.Archive?.ArchivePath ?? string.Empty;
            }

            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, false))
                {
                    KoikatsuCharacterAssembler.DestroyRuntimeObject(texture);
                    if (required)
                    {
                        Debug.LogWarning(
                            $"Loose Koikatsu texture '{archiveEntryName}' in " +
                            $"zipmod '{archivePath}' is invalid.");
                    }

                    return null;
                }

                looseTextures.Add(cacheKey, texture);
                runtimeTextures.Add(texture);
                return texture;
            }
            catch
            {
                KoikatsuCharacterAssembler.DestroyRuntimeObject(texture);
                throw;
            }
        }

        private static string GetHeadVariantBundleName(string bundleName)
        {
            const string extension = ".unity3d";
            if (string.IsNullOrEmpty(bundleName) ||
                !bundleName.EndsWith(
                    extension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return bundleName;
            }

            var stem = bundleName.Substring(
                0,
                bundleName.Length - extension.Length);
            return stem.Length >= 2
                ? stem.Substring(0, stem.Length - 2) + "50" + extension
                : bundleName;
        }
    }
}
