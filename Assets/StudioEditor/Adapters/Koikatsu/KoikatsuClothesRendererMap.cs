using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using StudioEditor.Rendering;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal enum KoikatsuClothesTextureSlot
    {
        None,
        Main,
        Main02,
        Main03,
    }

    internal sealed class KoikatsuClothesRendererMap
    {
        private readonly IReadOnlyDictionary<Renderer, KoikatsuClothesTextureSlot>
            slots;
        private readonly IReadOnlyList<GameObject> option01;
        private readonly IReadOnlyList<GameObject> option02;
        private readonly IReadOnlyList<GameObject> sleeves01;
        private readonly IReadOnlyList<GameObject> sleeves02;
        private readonly IReadOnlyList<GameObject> sleeves03;
        private readonly IReadOnlyList<Renderer> emblem01;
        private readonly IReadOnlyList<Renderer> emblem02;

        public KoikatsuClothesRendererMap(
            IReadOnlyDictionary<Renderer, KoikatsuClothesTextureSlot> slots,
            IReadOnlyList<GameObject> option01,
            IReadOnlyList<GameObject> option02,
            IReadOnlyList<GameObject> sleeves01,
            IReadOnlyList<GameObject> sleeves02,
            IReadOnlyList<GameObject> sleeves03,
            IReadOnlyList<Renderer> emblem01,
            IReadOnlyList<Renderer> emblem02)
        {
            this.slots = slots ??
                throw new ArgumentNullException(nameof(slots));
            this.option01 = option01 ?? Array.Empty<GameObject>();
            this.option02 = option02 ?? Array.Empty<GameObject>();
            this.sleeves01 = sleeves01 ?? Array.Empty<GameObject>();
            this.sleeves02 = sleeves02 ?? Array.Empty<GameObject>();
            this.sleeves03 = sleeves03 ?? Array.Empty<GameObject>();
            this.emblem01 = emblem01 ?? Array.Empty<Renderer>();
            this.emblem02 = emblem02 ?? Array.Empty<Renderer>();
        }

        public bool TryGet(
            Renderer renderer,
            out KoikatsuClothesTextureSlot slot)
        {
            return slots.TryGetValue(renderer, out slot);
        }

        public void ApplyOptions(bool showOption01, bool showOption02)
        {
            SetActive(option01, showOption01);
            SetActive(option02, showOption02);
        }

        public void ApplySleeves(int sleevesType)
        {
            if (sleevesType < 0)
            {
                return;
            }

            SetActive(sleeves01, sleevesType == 0);
            SetActive(sleeves02, sleevesType == 1);
            SetActive(sleeves03, sleevesType == 2);
        }

        public void ApplyEmblems(Texture2D texture01, Texture2D texture02)
        {
            SetMainTexture(emblem01, texture01);
            SetMainTexture(emblem02, texture02);
        }

        private static void SetActive(
            IReadOnlyList<GameObject> values,
            bool active)
        {
            for (var index = 0; index < values.Count; index++)
            {
                values[index]?.SetActive(active);
            }
        }

        private static void SetMainTexture(
            IReadOnlyList<Renderer> renderers,
            Texture texture)
        {
            for (var index = 0; index < renderers.Count; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.gameObject.SetActive(texture != null);
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                MaterialRenderUtility.SetMainTexture(materials[0], texture);
            }
        }
    }

    internal static class KoikatsuClothesRendererMapLoader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Metadata> Cache =
            new Dictionary<string, Metadata>(StringComparer.OrdinalIgnoreCase);

        public static KoikatsuClothesRendererMap TryCreate(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrEmpty(assetName) ||
                instance == null)
            {
                return null;
            }

            var cacheKey = CreateCacheKey(source, assetName);
            Metadata metadata;
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(cacheKey, out metadata))
                {
                    metadata = ParseSafely(source, assetName);
                    Cache.Add(cacheKey, metadata);
                }
            }

            if (metadata == null)
            {
                return null;
            }

            var assignments = new Dictionary<
                Renderer,
                KoikatsuClothesTextureSlot>();
            var emblem01 = new List<Renderer>();
            var emblem02 = new List<Renderer>();
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                var locator = CreateRuntimeLocator(
                    instance.transform,
                    renderer);
                if (locator != null && metadata.Slots.TryGetValue(
                        locator,
                        out var slot))
                {
                    assignments[renderer] = slot;
                }

                if (locator != null && metadata.Emblem01.Contains(locator))
                {
                    emblem01.Add(renderer);
                }
                if (locator != null && metadata.Emblem02.Contains(locator))
                {
                    emblem02.Add(renderer);
                }
            }

            if (metadata.Slots.Count != 0 && assignments.Count == 0)
            {
                Debug.LogWarning(
                    "Koikatsu clothes renderer groups were parsed but could " +
                    $"not be bound for prefab '{assetName}' in " +
                    $"'{source.DisplayName}'. Falling back to material matching.");
                return null;
            }

            if (assignments.Count != metadata.Slots.Count)
            {
                Debug.LogWarning(
                    $"Koikatsu clothes prefab '{assetName}' bound " +
                    $"{assignments.Count} of {metadata.Slots.Count} serialized " +
                    $"renderer groups from '{source.DisplayName}'. Unmatched " +
                    "renderers will keep their source textures.");
            }

            return new KoikatsuClothesRendererMap(
                assignments,
                BindObjects(instance.transform, metadata.Option01),
                BindObjects(instance.transform, metadata.Option02),
                BindObjects(instance.transform, metadata.Sleeves01),
                BindObjects(instance.transform, metadata.Sleeves02),
                BindObjects(instance.transform, metadata.Sleeves03),
                emblem01.AsReadOnly(),
                emblem02.AsReadOnly());
        }

        private static string CreateCacheKey(
            KoikatsuBundleSource source,
            string assetName)
        {
            var file = new FileInfo(source.FilePath);
            return source.CacheKey + "|" + assetName + "|" +
                   file.Length + "|" + file.LastWriteTimeUtc.Ticks;
        }

        private static Metadata ParseSafely(
            KoikatsuBundleSource source,
            string assetName)
        {
            try
            {
                var metadata = Parse(source, assetName);
                if (metadata == null)
                {
                    Debug.LogWarning(
                        "Koikatsu clothes renderer groups were not found for " +
                        $"prefab '{assetName}' in '{source.DisplayName}'. " +
                        "Falling back to material matching.");
                }

                return metadata;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Koikatsu clothes renderer groups could not be parsed for " +
                    $"prefab '{assetName}' in '{source.DisplayName}': " +
                    exception.Message);
                return null;
            }
        }

        private static Metadata Parse(
            KoikatsuBundleSource source,
            string assetName)
        {
            var manager = new AssetsManager();
            Stream ownedStream = null;
            try
            {
                var bundle = LoadBundle(manager, source, out ownedStream);
                var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                var context = new ParseContext(manager, assets);
                var rootPathId = FindRootGameObject(
                    context,
                    assetName);
                if (rootPathId == 0)
                {
                    return null;
                }

                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var index = 0; index < behaviours.Count; index++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[index],
                        AssetReadFlags.None);
                    if (behaviour["rendNormal01"].IsDummy ||
                        GetPathId(behaviour["m_GameObject"]) != rootPathId)
                    {
                        continue;
                    }

                    var metadata = new Metadata();
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendNormal01"],
                        KoikatsuClothesTextureSlot.Main);
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendAlpha01"],
                        KoikatsuClothesTextureSlot.Main);
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendNormal02"],
                        KoikatsuClothesTextureSlot.Main02);
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendNormal03"],
                        KoikatsuClothesTextureSlot.Main03);

                    // The original ChangeCustomClothes writes Main to this
                    // renderer after the three normal groups.
                    AddPointer(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendAccessory"],
                        KoikatsuClothesTextureSlot.Main);
                    AddObjectArray(
                        context,
                        metadata.Option01,
                        rootPathId,
                        behaviour["objOpt01"]);
                    AddObjectArray(
                        context,
                        metadata.Option02,
                        rootPathId,
                        behaviour["objOpt02"]);
                    AddObjectArray(
                        context,
                        metadata.Sleeves01,
                        rootPathId,
                        behaviour["objSleeves01"]);
                    AddObjectArray(
                        context,
                        metadata.Sleeves02,
                        rootPathId,
                        behaviour["objSleeves02"]);
                    AddObjectArray(
                        context,
                        metadata.Sleeves03,
                        rootPathId,
                        behaviour["objSleeves03"]);
                    AddRendererPointer(
                        context,
                        metadata.Emblem01,
                        rootPathId,
                        behaviour["rendEmblem01"]);
                    AddRendererPointer(
                        context,
                        metadata.Emblem01,
                        rootPathId,
                        behaviour["rendEmblem02"]);
                    AddRendererArray(
                        context,
                        metadata.Emblem01,
                        rootPathId,
                        behaviour["exRendEmblem01"]);
                    AddRendererArray(
                        context,
                        metadata.Emblem02,
                        rootPathId,
                        behaviour["exRendEmblem02"]);
                    return metadata;
                }

                return null;
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        internal static BundleFileInstance LoadBundle(
            AssetsManager manager,
            KoikatsuBundleSource source,
            out Stream ownedStream)
        {
            ownedStream = null;
            if (source.StreamOffset > 0)
            {
                var file = new FileStream(
                    source.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                var offsetStream = new OffsetReadStream(
                    file,
                    source.StreamOffset);
                ownedStream = offsetStream;
                return manager.LoadBundleFile(
                    offsetStream,
                    source.DisplayName,
                    true);
            }

            if (string.IsNullOrEmpty(source.ArchiveEntryName))
            {
                return manager.LoadBundleFile(source.FilePath, true);
            }

            var memory = new MemoryStream();
            using (var file = new FileStream(
                       source.FilePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite))
            using (var archive = new ZipArchive(
                       file,
                       ZipArchiveMode.Read,
                       false))
            {
                var entry = archive.GetEntry(source.ArchiveEntryName);
                if (entry == null)
                {
                    throw new InvalidDataException(
                        $"Zipmod '{source.FilePath}' does not contain " +
                        $"'{source.ArchiveEntryName}'.");
                }

                using (var input = entry.Open())
                {
                    input.CopyTo(memory);
                }
            }

            memory.Position = 0;
            ownedStream = memory;
            return manager.LoadBundleFile(
                memory,
                source.DisplayName,
                true);
        }

        internal static long FindRootGameObject(
            ParseContext context,
            string assetName)
        {
            var bundleAssets = context.Assets.file.GetAssetsOfType(
                AssetClassID.AssetBundle);
            for (var bundleIndex = 0;
                 bundleIndex < bundleAssets.Count;
                 bundleIndex++)
            {
                var bundle = context.Manager.GetBaseField(
                    context.Assets,
                    bundleAssets[bundleIndex],
                    AssetReadFlags.None);
                var container = GetArray(bundle["m_Container"]);
                if (container == null)
                {
                    continue;
                }

                for (var index = 0; index < container.Children.Count; index++)
                {
                    var entry = container.Children[index];
                    var containerPath = entry["first"].AsString;
                    if (!string.Equals(
                            Path.GetFileNameWithoutExtension(containerPath),
                            assetName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pointer = entry["second"]["asset"];
                    var external = context.Resolve(pointer);
                    if (external.info == null ||
                        external.info.TypeId != (int)AssetClassID.GameObject ||
                        !string.Equals(
                            external.baseField["m_Name"].AsString,
                            assetName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return external.info.PathId;
                }
            }

            return 0;
        }

        private static void AddArray(
            ParseContext context,
            Metadata metadata,
            long rootPathId,
            AssetTypeValueField field,
            KoikatsuClothesTextureSlot slot)
        {
            var array = GetArray(field);
            if (array == null)
            {
                return;
            }

            for (var index = 0; index < array.Children.Count; index++)
            {
                AddPointer(
                    context,
                    metadata,
                    rootPathId,
                    array.Children[index],
                    slot);
            }
        }

        private static void AddPointer(
            ParseContext context,
            Metadata metadata,
            long rootPathId,
            AssetTypeValueField pointer,
            KoikatsuClothesTextureSlot slot)
        {
            var locator = CreateSerializedLocator(
                context,
                rootPathId,
                pointer);
            if (locator != null)
            {
                metadata.Slots[locator] = slot;
            }
        }

        private static void AddObjectArray(
            ParseContext context,
            ISet<string> paths,
            long rootPathId,
            AssetTypeValueField field)
        {
            var array = GetArray(field);
            if (array == null)
            {
                return;
            }

            for (var index = 0; index < array.Children.Count; index++)
            {
                var owner = context.Resolve(array.Children[index]);
                if (owner.info == null ||
                    owner.info.TypeId != (int)AssetClassID.GameObject)
                {
                    continue;
                }

                var transform = FindTransform(context, owner.baseField);
                var path = transform.info == null
                    ? null
                    : CreateSerializedPath(context, rootPathId, transform);
                if (path != null)
                {
                    paths.Add(path);
                }
            }
        }

        private static void AddRendererArray(
            ParseContext context,
            ISet<string> locators,
            long rootPathId,
            AssetTypeValueField field)
        {
            var array = GetArray(field);
            if (array == null)
            {
                return;
            }

            for (var index = 0; index < array.Children.Count; index++)
            {
                AddRendererPointer(
                    context,
                    locators,
                    rootPathId,
                    array.Children[index]);
            }
        }

        private static void AddRendererPointer(
            ParseContext context,
            ISet<string> locators,
            long rootPathId,
            AssetTypeValueField pointer)
        {
            var locator = CreateSerializedLocator(
                context,
                rootPathId,
                pointer);
            if (locator != null)
            {
                locators.Add(locator);
            }
        }

        private static IReadOnlyList<GameObject> BindObjects(
            Transform root,
            ISet<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return Array.Empty<GameObject>();
            }

            var result = new List<GameObject>(paths.Count);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                var path = CreateRuntimePath(root, transforms[index]);
                if (path != null && paths.Contains(path))
                {
                    result.Add(transforms[index].gameObject);
                }
            }

            return result.AsReadOnly();
        }

        internal static string CreateSerializedLocator(
            ParseContext context,
            long rootPathId,
            AssetTypeValueField rendererPointer)
        {
            var rendererPathId = GetPathId(rendererPointer);
            var renderer = context.Resolve(rendererPointer);
            if (renderer.info == null || rendererPathId == 0)
            {
                return null;
            }

            var owner = context.Resolve(renderer.baseField["m_GameObject"]);
            if (owner.info == null)
            {
                return null;
            }

            var transform = FindTransform(context, owner.baseField);
            if (transform.info == null)
            {
                return null;
            }

            var path = CreateSerializedPath(
                context,
                rootPathId,
                transform);
            if (path == null)
            {
                return null;
            }

            var rendererType = GetRendererTypeName(renderer.info.TypeId);
            var rendererIndex = GetRendererIndex(
                context,
                owner.baseField,
                rendererPathId,
                renderer.info.TypeId);
            return rendererType == null || rendererIndex < 0
                ? null
                : CreateLocator(path, rendererType, rendererIndex);
        }

        internal static string CreateSerializedPath(
            ParseContext context,
            long rootPathId,
            AssetExternal transform)
        {
            var segments = new List<string>();
            var current = transform;
            while (current.info != null)
            {
                var owner = context.Resolve(
                    current.baseField["m_GameObject"]);
                if (owner.info == null)
                {
                    return null;
                }

                if (owner.info.PathId == rootPathId)
                {
                    segments.Reverse();
                    return string.Concat(segments);
                }

                var parent = context.Resolve(current.baseField["m_Father"]);
                if (parent.info == null)
                {
                    return null;
                }

                var name = owner.baseField["m_Name"].AsString;
                var occurrence = GetSerializedSiblingOccurrence(
                    context,
                    parent.baseField,
                    current.info.PathId,
                    name);
                segments.Add(CreatePathSegment(name, occurrence));
                current = parent;
            }

            return null;
        }

        private static int GetSerializedSiblingOccurrence(
            ParseContext context,
            AssetTypeValueField parent,
            long childPathId,
            string childName)
        {
            var children = GetArray(parent["m_Children"]);
            if (children == null)
            {
                return 0;
            }

            var occurrence = 0;
            for (var index = 0; index < children.Children.Count; index++)
            {
                var child = context.Resolve(children.Children[index]);
                if (child.info == null)
                {
                    continue;
                }

                if (child.info.PathId == childPathId)
                {
                    return occurrence;
                }

                var owner = context.Resolve(child.baseField["m_GameObject"]);
                if (owner.info != null && string.Equals(
                        owner.baseField["m_Name"].AsString,
                        childName,
                        StringComparison.Ordinal))
                {
                    occurrence++;
                }
            }

            return occurrence;
        }

        internal static AssetExternal FindTransform(
            ParseContext context,
            AssetTypeValueField gameObject)
        {
            var components = GetArray(gameObject["m_Component"]);
            if (components == null)
            {
                return default(AssetExternal);
            }

            for (var index = 0; index < components.Children.Count; index++)
            {
                var component = context.Resolve(
                    GetComponentPointer(components.Children[index]));
                if (component.info != null &&
                    component.info.TypeId == (int)AssetClassID.Transform)
                {
                    return component;
                }
            }

            return default(AssetExternal);
        }

        private static int GetRendererIndex(
            ParseContext context,
            AssetTypeValueField gameObject,
            long rendererPathId,
            int rendererTypeId)
        {
            var components = GetArray(gameObject["m_Component"]);
            if (components == null)
            {
                return -1;
            }

            var rendererIndex = 0;
            for (var index = 0; index < components.Children.Count; index++)
            {
                var pointer = GetComponentPointer(components.Children[index]);
                var component = context.Resolve(pointer);
                if (component.info == null ||
                    component.info.TypeId != rendererTypeId)
                {
                    continue;
                }

                if (GetPathId(pointer) == rendererPathId)
                {
                    return rendererIndex;
                }

                rendererIndex++;
            }

            return -1;
        }

        private static AssetTypeValueField GetComponentPointer(
            AssetTypeValueField component)
        {
            var pointer = component["component"];
            return pointer.IsDummy ? component : pointer;
        }

        internal static AssetTypeValueField GetArray(
            AssetTypeValueField field)
        {
            if (field == null || field.IsDummy)
            {
                return null;
            }

            var array = field["Array"];
            return array.IsDummy ? null : array;
        }

        private static long GetPathId(AssetTypeValueField pointer)
        {
            if (pointer == null || pointer.IsDummy)
            {
                return 0;
            }

            var pathId = pointer["m_PathID"];
            return pathId.IsDummy ? 0 : pathId.AsLong;
        }

        internal static string CreateRuntimeLocator(
            Transform root,
            Renderer renderer)
        {
            var path = CreateRuntimePath(root, renderer.transform);
            if (path == null)
            {
                return null;
            }

            var rendererType = renderer.GetType();
            var renderers = renderer.gameObject.GetComponents<Renderer>();
            var rendererIndex = 0;
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].GetType() != rendererType)
                {
                    continue;
                }

                if (ReferenceEquals(renderers[index], renderer))
                {
                    return CreateLocator(
                        path,
                        rendererType.Name,
                        rendererIndex);
                }

                rendererIndex++;
            }

            return null;
        }

        internal static string CreateRuntimePath(
            Transform root,
            Transform transform)
        {
            var segments = new List<string>();
            var current = transform;
            while (current != null && current != root)
            {
                var occurrence = 0;
                var siblingIndex = current.GetSiblingIndex();
                var parent = current.parent;
                if (parent == null)
                {
                    return null;
                }

                for (var index = 0; index < siblingIndex; index++)
                {
                    if (string.Equals(
                            parent.GetChild(index).name,
                            current.name,
                            StringComparison.Ordinal))
                    {
                        occurrence++;
                    }
                }

                segments.Add(CreatePathSegment(current.name, occurrence));
                current = parent;
            }

            if (current != root)
            {
                return null;
            }

            segments.Reverse();
            return string.Concat(segments);
        }

        private static string CreatePathSegment(string name, int occurrence)
        {
            name = name ?? string.Empty;
            return name.Length + ":" + name + ":" + occurrence + ";";
        }

        private static string CreateLocator(
            string path,
            string rendererType,
            int rendererIndex)
        {
            return path + "|" + rendererType + "|" + rendererIndex;
        }

        private static string GetRendererTypeName(int typeId)
        {
            switch ((AssetClassID)typeId)
            {
                case AssetClassID.MeshRenderer:
                    return nameof(MeshRenderer);
                case AssetClassID.SkinnedMeshRenderer:
                    return nameof(SkinnedMeshRenderer);
                default:
                    return null;
            }
        }

        private sealed class Metadata
        {
            public Dictionary<string, KoikatsuClothesTextureSlot> Slots { get; } =
                new Dictionary<string, KoikatsuClothesTextureSlot>(
                    StringComparer.Ordinal);
            public HashSet<string> Option01 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Option02 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Sleeves01 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Sleeves02 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Sleeves03 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Emblem01 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Emblem02 { get; } =
                new HashSet<string>(StringComparer.Ordinal);
        }

        internal sealed class ParseContext
        {
            private readonly Dictionary<long, AssetExternal> localAssets =
                new Dictionary<long, AssetExternal>();

            public ParseContext(
                AssetsManager manager,
                AssetsFileInstance assets)
            {
                Manager = manager;
                Assets = assets;
            }

            public AssetsManager Manager { get; }

            public AssetsFileInstance Assets { get; }

            public AssetExternal Resolve(AssetTypeValueField pointer)
            {
                var pathId = GetPathId(pointer);
                if (pathId == 0)
                {
                    return default(AssetExternal);
                }

                var fileId = pointer["m_FileID"];
                if (fileId.IsDummy || fileId.AsInt != 0)
                {
                    return Manager.GetExtAsset(
                        Assets,
                        pointer,
                        false,
                        AssetReadFlags.None);
                }

                if (!localAssets.TryGetValue(pathId, out var external))
                {
                    external = Manager.GetExtAsset(
                        Assets,
                        pointer,
                        false,
                        AssetReadFlags.None);
                    localAssets.Add(pathId, external);
                }

                return external;
            }
        }

        private sealed class OffsetReadStream : Stream
        {
            private readonly Stream source;
            private readonly long start;

            public OffsetReadStream(Stream source, long start)
            {
                this.source = source ??
                    throw new ArgumentNullException(nameof(source));
                if (!source.CanRead || !source.CanSeek)
                {
                    throw new ArgumentException(
                        "The source stream must be readable and seekable.",
                        nameof(source));
                }

                if (start < 0 || start > source.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(start));
                }

                this.start = start;
                source.Position = start;
            }

            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => source.Length - start;

            public override long Position
            {
                get => source.Position - start;
                set
                {
                    if (value < 0 || value > Length)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }

                    source.Position = start + value;
                }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return source.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length + offset;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(origin));
                }

                return Position;
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    source.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
