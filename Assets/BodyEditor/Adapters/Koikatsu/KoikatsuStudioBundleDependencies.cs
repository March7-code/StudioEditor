using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuStudioBundleDependencies
    {
        private static readonly Dictionary<string, string[]> Cache =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public static void Acquire(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            KoikatsuStudioListEntry entry,
            ICollection<KoikatsuAssetBundleLease> leases)
        {
            if (entry == null || leases == null)
            {
                return;
            }

            if (entry.Archive != null)
            {
                AcquireVirtualConvention(
                    abdataRoot,
                    catalog,
                    entry.BundlePath,
                    entry.Archive,
                    leases);
                return;
            }

            var dependencies = GetDependencies(abdataRoot, entry);
            for (var index = 0; index < dependencies.Length; index++)
            {
                AcquireVirtualPath(
                    abdataRoot,
                    catalog,
                    dependencies[index],
                    null,
                    leases);
            }
        }

        public static void Acquire(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            KoikatsuMapListEntry entry,
            ICollection<KoikatsuAssetBundleLease> leases)
        {
            if (entry == null || leases == null)
            {
                return;
            }

            if (entry.Archive != null)
            {
                AcquireVirtualConvention(
                    abdataRoot,
                    catalog,
                    entry.BundlePath,
                    entry.Archive,
                    leases);
                return;
            }

            var dependencies = GetDependencies(
                abdataRoot,
                entry.Manifest,
                entry.BundlePath);
            for (var index = 0; index < dependencies.Length; index++)
            {
                AcquireVirtualPath(
                    abdataRoot,
                    catalog,
                    dependencies[index],
                    null,
                    leases);
            }
        }

        private static string[] GetDependencies(
            string abdataRoot,
            KoikatsuStudioListEntry entry)
        {
            return GetDependencies(
                abdataRoot,
                entry.Manifest,
                entry.BundlePath);
        }

        private static string[] GetDependencies(
            string abdataRoot,
            string manifest,
            string bundlePath)
        {
            var cacheKey = Path.GetFullPath(abdataRoot) + "|" +
                           manifest + "|" + bundlePath;
            if (Cache.TryGetValue(cacheKey, out var dependencies))
            {
                return dependencies;
            }

            dependencies = ReadManifest(
                abdataRoot,
                manifest,
                bundlePath);
            if (dependencies.Length == 0)
            {
                var conventional = GetMaterialBundlePath(bundlePath);
                if (!string.IsNullOrEmpty(conventional))
                {
                    dependencies = new[] { conventional };
                }
            }

            Cache[cacheKey] = dependencies;
            return dependencies;
        }

        private static string[] ReadManifest(
            string abdataRoot,
            string manifestName,
            string bundlePath)
        {
            if (string.IsNullOrWhiteSpace(manifestName))
            {
                return Array.Empty<string>();
            }

            var path = KoikatsuAssetPath.ResolveAbdataPath(
                abdataRoot,
                manifestName);
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            try
            {
                using (var lease = KoikatsuAssetBundleCache.Acquire(path))
                {
                    var manifest = lease.Bundle.LoadAsset<AssetBundleManifest>(
                                       "AssetBundleManifest") ??
                                   lease.Bundle.LoadAsset<AssetBundleManifest>(
                                       Path.GetFileName(manifestName));
                    return manifest != null
                        ? manifest.GetAllDependencies(
                            Normalize(bundlePath))
                        : Array.Empty<string>();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not read Koikatsu AssetBundle manifest " +
                    $"'{path}': {exception.Message}");
                return Array.Empty<string>();
            }
        }

        private static void AcquireVirtualConvention(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            string bundlePath,
            KoikatsuZipmodArchive preferredArchive,
            ICollection<KoikatsuAssetBundleLease> leases)
        {
            var materialPath = GetMaterialBundlePath(bundlePath);
            if (!string.IsNullOrEmpty(materialPath))
            {
                AcquireVirtualPath(
                    abdataRoot,
                    catalog,
                    materialPath,
                    preferredArchive,
                    leases);
            }
        }

        private static void AcquireVirtualPath(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            string bundlePath,
            KoikatsuZipmodArchive preferredArchive,
            ICollection<KoikatsuAssetBundleLease> leases)
        {
            var sources = catalog.ResolveBundleCandidates(
                abdataRoot,
                bundlePath,
                preferredArchive);
            for (var index = 0; index < sources.Count; index++)
            {
                if (File.Exists(sources[index].FilePath))
                {
                    leases.Add(KoikatsuAssetBundleCache.Acquire(
                        sources[index]));
                }
            }
        }

        private static string GetMaterialBundlePath(string bundlePath)
        {
            var normalized = Normalize(bundlePath);
            var slash = normalized.LastIndexOf('/');
            if (slash < 0 || normalized.Contains("/mat/"))
            {
                return string.Empty;
            }

            return normalized.Substring(0, slash + 1) + "mat/" +
                   normalized.Substring(slash + 1);
        }

        private static string Normalize(string path)
        {
            path = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            const string prefix = "abdata/";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefix.Length)
                : path;
        }
    }
}
