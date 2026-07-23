using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace StudioEditor.ReferenceModels
{
    internal enum KoikatsuAccessoryRendererRole
    {
        Unknown,
        Normal,
        Alpha,
        Hair,
    }

    internal sealed class KoikatsuAccessoryRendererInfo
    {
        public KoikatsuAccessoryRendererInfo(
            KoikatsuAccessoryRendererRole role,
            bool useColor01 = false,
            bool useColor02 = false,
            bool useColor03 = false)
        {
            Role = role;
            UseColor01 = useColor01;
            UseColor02 = useColor02;
            UseColor03 = useColor03;
        }

        public KoikatsuAccessoryRendererRole Role { get; }

        public bool UseColor01 { get; }

        public bool UseColor02 { get; }

        public bool UseColor03 { get; }
    }

    internal sealed class KoikatsuAccessoryRendererMap
    {
        private readonly IReadOnlyDictionary<Renderer, KoikatsuAccessoryRendererInfo>
            roles;

        public KoikatsuAccessoryRendererMap(
            IReadOnlyDictionary<Renderer, KoikatsuAccessoryRendererInfo> roles)
        {
            this.roles = roles ??
                throw new ArgumentNullException(nameof(roles));
        }

        public bool TryGet(
            Renderer renderer,
            out KoikatsuAccessoryRendererInfo role)
        {
            return roles.TryGetValue(renderer, out role);
        }
    }

    // ChaAccessoryComponent.Initialize builds these arrays from serialized
    // renderer references. Keep the same source of truth instead of guessing
    // from a material's display name.
    internal static class KoikatsuAccessoryRendererMapLoader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Metadata> Cache =
            new Dictionary<string, Metadata>(StringComparer.OrdinalIgnoreCase);

        public static KoikatsuAccessoryRendererMap TryCreate(
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
                KoikatsuAccessoryRendererInfo>();
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                var locator = KoikatsuClothesRendererMapLoader
                    .CreateRuntimeLocator(instance.transform, renderer);
                if (locator != null && metadata.Roles.TryGetValue(
                        locator,
                        out var role))
                {
                    assignments[renderer] = role;
                }
            }

            if (metadata.Roles.Count != 0 && assignments.Count == 0)
            {
                Debug.LogWarning(
                    "Koikatsu accessory renderer groups were parsed but " +
                    $"could not be bound for prefab '{assetName}' in " +
                    $"'{source.DisplayName}'. Card colors will be left " +
                    "unchanged for this accessory.");
                return null;
            }

            if (assignments.Count != metadata.Roles.Count)
            {
                Debug.LogWarning(
                    $"Koikatsu accessory prefab '{assetName}' bound " +
                    $"{assignments.Count} of {metadata.Roles.Count} " +
                    $"serialized renderer entries from '{source.DisplayName}'.");
            }

            return new KoikatsuAccessoryRendererMap(assignments);
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
                        "Koikatsu accessory renderer groups were not found " +
                        $"for prefab '{assetName}' in '{source.DisplayName}'. " +
                        "The prefab's source colors will be preserved.");
                }

                return metadata;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Koikatsu accessory renderer groups could not be parsed " +
                    $"for prefab '{assetName}' in '{source.DisplayName}': " +
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
                var bundle = KoikatsuClothesRendererMapLoader.LoadBundle(
                    manager,
                    source,
                    out ownedStream);
                var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                var context = new KoikatsuClothesRendererMapLoader.ParseContext(
                    manager,
                    assets);
                var rootPathId = KoikatsuClothesRendererMapLoader.FindRootGameObject(
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
                    if (GetPathId(behaviour["m_GameObject"]) != rootPathId ||
                        !HasAccessoryFields(behaviour))
                    {
                        continue;
                    }

                    var metadata = new Metadata();
                    var normalInfo = new KoikatsuAccessoryRendererInfo(
                        KoikatsuAccessoryRendererRole.Normal,
                        ReadBool(behaviour["useColor01"]),
                        ReadBool(behaviour["useColor02"]),
                        ReadBool(behaviour["useColor03"]));
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendNormal"],
                        normalInfo);
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendAlpha"],
                        new KoikatsuAccessoryRendererInfo(
                            KoikatsuAccessoryRendererRole.Alpha));
                    AddArray(
                        context,
                        metadata,
                        rootPathId,
                        behaviour["rendHair"],
                        new KoikatsuAccessoryRendererInfo(
                            KoikatsuAccessoryRendererRole.Hair));
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

        private static bool HasAccessoryFields(AssetTypeValueField behaviour)
        {
            return !behaviour["rendNormal"].IsDummy ||
                   !behaviour["rendAlpha"].IsDummy ||
                   !behaviour["rendHair"].IsDummy;
        }

        private static void AddArray(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            Metadata metadata,
            long rootPathId,
            AssetTypeValueField field,
            KoikatsuAccessoryRendererInfo role)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return;
            }

            for (var index = 0; index < array.Children.Count; index++)
            {
                var locator = KoikatsuClothesRendererMapLoader
                    .CreateSerializedLocator(
                        context,
                        rootPathId,
                        array.Children[index]);
                if (locator != null)
                {
                    metadata.Roles[locator] = role;
                }
            }
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

        private static bool ReadBool(AssetTypeValueField field)
        {
            return field != null && !field.IsDummy && field.AsBool;
        }

        private sealed class Metadata
        {
            public Dictionary<string, KoikatsuAccessoryRendererInfo> Roles {
                get;
            } = new Dictionary<string, KoikatsuAccessoryRendererInfo>(
                StringComparer.Ordinal);
        }
    }
}
