using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // Preload leases keep resources alive while the existing assemblers bind them.
    // All Unity API calls stay on the captured main-thread synchronization context.
    internal sealed class KoikatsuLoadSession : IDisposable
    {
        private readonly Dictionary<string, KoikatsuAssetBundleLease> leases =
            new Dictionary<string, KoikatsuAssetBundleLease>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationToken token;

        public KoikatsuLoadSession(CancellationToken token) { this.token = token; }

        public async Task WarmAsync(KoikatsuBundleSource source, bool optional = false)
        {
            token.ThrowIfCancellationRequested();
            if (source == null || leases.ContainsKey(source.CacheKey)) return;
            if (optional && !File.Exists(source.FilePath)) return;
            try
            {
                var lease = await KoikatsuAssetBundleCache.AcquireAsync(source, token);
                leases.Add(source.CacheKey, lease);
            }
            catch (Exception exception) when (optional &&
                (exception is IOException || exception is UnityException))
            {
                // Preserve the assemblers' candidate fallback and missing-part policy.
            }
        }

        public async Task WarmEntryAsync(KoikatsuListCatalog catalog, string abdataRoot,
            KoikatsuListEntry entry)
        {
            if (entry == null) return;
            foreach (var path in entry.RuntimeBundlePaths)
                foreach (var source in catalog.ResolveBundleCandidates(abdataRoot, path, entry.Archive))
                    await WarmAsync(source, true);
            var assetName = entry.Get("MainData");
            if (!string.IsNullOrEmpty(assetName))
                foreach (var source in catalog.ResolveBundleCandidates(abdataRoot, entry.Get("MainAB"), entry.Archive))
                    if (await WarmPrefabAsync(source, assetName, entry.Category)) break;
        }

        private async Task<bool> WarmPrefabAsync(KoikatsuBundleSource source, string assetName, int category = -1)
        {
            if (!leases.TryGetValue(source.CacheKey, out var lease) ||
                !lease.Bundle.Contains(assetName)) return false;
            var request = lease.Bundle.LoadAssetAsync<GameObject>(assetName);
            while (!request.isDone) await Task.Yield();
            token.ThrowIfCancellationRequested();
            if (request.asset == null) return false;
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if ((category >= 105 && category <= 112) || (category >= 200 && category <= 212))
                    KoikatsuClothesRendererMapLoader.Prepare(source, assetName);
                else if (category >= 121 && category <= 130)
                    KoikatsuAccessoryRendererMapLoader.Prepare(source, assetName);
                KoikatsuSpringBoneMetadataLoader.Prepare(source, assetName);
                KoikatsuVer02MetadataLoader.Prepare(source, assetName);
            }, token);
            return true;
        }

        public async Task WarmItemAsync(KoikatsuScene scene, KoikatsuSceneObject source,
            string abdataRoot, string modsRoot)
        {
            var catalog = await KoikatsuListCatalog.LoadAsync(abdataRoot, modsRoot, token);
            var guid = scene.TryResolveItem(source, out var resolution) ? resolution.Guid : string.Empty;
            if (!catalog.TryGetStudio(source.Item.Group, source.Item.Category, source.Item.No,
                guid, out var entry)) return;
            await WarmDependenciesAsync(catalog, abdataRoot, entry.BundlePath, entry.Manifest, entry.Archive);
            foreach (var bundle in catalog.ResolveBundleCandidates(abdataRoot, entry.BundlePath, entry.Archive))
            {
                await WarmAsync(bundle, true);
                if (await WarmPrefabAsync(bundle, entry.AssetName)) break;
            }
        }

        public async Task WarmDependenciesAsync(KoikatsuListCatalog catalog, string abdataRoot,
            string bundlePath, string manifestName, KoikatsuZipmodArchive archive)
        {
            var dependencies = Array.Empty<string>();
            if (archive == null && !string.IsNullOrWhiteSpace(manifestName))
            {
                var source = new KoikatsuBundleSource(KoikatsuAssetPath.ResolveAbdataPath(abdataRoot, manifestName));
                await WarmAsync(source, true);
                if (leases.TryGetValue(source.CacheKey, out var lease))
                {
                    var name = lease.Bundle.Contains("AssetBundleManifest")
                        ? "AssetBundleManifest" : Path.GetFileName(manifestName);
                    var request = lease.Bundle.LoadAssetAsync<AssetBundleManifest>(name);
                    while (!request.isDone) await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    if (request.asset is AssetBundleManifest manifest)
                        dependencies = manifest.GetAllDependencies(bundlePath.Replace('\\', '/').TrimStart('/'));
                }
            }
            if (dependencies.Length == 0)
            {
                var normalized = bundlePath.Replace('\\', '/');
                var slash = normalized.LastIndexOf('/');
                if (slash >= 0 && !normalized.Contains("/mat/"))
                    dependencies = new[] { normalized.Substring(0, slash + 1) + "mat/" + normalized.Substring(slash + 1) };
            }
            foreach (var dependency in dependencies)
                foreach (var source in catalog.ResolveBundleCandidates(abdataRoot, dependency, archive))
                    await WarmAsync(source, true);
        }

        public void Dispose()
        {
            foreach (var lease in leases.Values) lease.Dispose();
            leases.Clear();
        }
    }
}
