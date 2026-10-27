using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuAssetBundleCache
    {
        private static readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static KoikatsuAssetBundleLease Acquire(string path)
        {
            return Acquire(new KoikatsuBundleSource(path));
        }

        public static KoikatsuAssetBundleLease Acquire(
            KoikatsuBundleSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var key = source.CacheKey;
            if (!entries.TryGetValue(key, out var entry))
            {
                var bundle = Load(source);
                if (bundle == null)
                {
                    throw new InvalidDataException(
                        "Unity could not load Koikatsu AssetBundle " +
                        $"'{source.DisplayName}'.");
                }

                entry = new Entry(bundle);
                entries.Add(key, entry);
            }

            entry.ReferenceCount++;
            return new KoikatsuAssetBundleLease(key, entry.Bundle);
        }

        public static async Task<KoikatsuAssetBundleLease> AcquireAsync(
            KoikatsuBundleSource source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            StudioEditorDiskCache.Initialize();
            if (entries.ContainsKey(source.CacheKey)) return Acquire(source);
            var preparedSource = source;
            KoikatsuBundleSource prepared;
            try { prepared = await Task.Run(() => KoikatsuLegacyBundleSanitizer.Prepare(preparedSource), token); }
            catch (InvalidDataException) when (source.StreamOffset > 0 && !string.IsNullOrEmpty(source.FallbackArchiveEntryName))
            {
                preparedSource = new KoikatsuBundleSource(source.FilePath, 0, source.FallbackArchiveEntryName);
                prepared = await Task.Run(() => KoikatsuLegacyBundleSanitizer.Prepare(preparedSource), token);
            }
            var bundle = await LoadPreparedAsync(prepared, token);
            if (bundle == null && source.StreamOffset > 0 && !string.IsNullOrEmpty(source.FallbackArchiveEntryName) &&
                string.IsNullOrEmpty(preparedSource.ArchiveEntryName))
            {
                preparedSource = new KoikatsuBundleSource(source.FilePath, 0, source.FallbackArchiveEntryName);
                prepared = await Task.Run(() => KoikatsuLegacyBundleSanitizer.Prepare(preparedSource), token);
                bundle = await LoadPreparedAsync(prepared, token);
            }
            if (bundle == null)
            {
                // Rebuild corrupt cached bundles once, without deleting game data.
                await Task.Run(() => KoikatsuLegacyBundleSanitizer.Invalidate(preparedSource), token);
                prepared = await Task.Run(() => KoikatsuLegacyBundleSanitizer.Prepare(preparedSource), token);
                bundle = await LoadPreparedAsync(prepared, token);
            }
            if (bundle == null)
                throw new InvalidDataException($"Unity could not load '{source.DisplayName}'.");
            if (entries.TryGetValue(source.CacheKey, out var existing))
            {
                if (bundle != existing.Bundle) bundle.Unload(true);
                return Acquire(source);
            }
            entries.Add(source.CacheKey, new Entry(bundle) { ReferenceCount = 1 });
            return new KoikatsuAssetBundleLease(source.CacheKey, bundle);
        }

        private static async Task<AssetBundle> LoadPreparedAsync(KoikatsuBundleSource prepared, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            AssetBundleCreateRequest request;
            if (string.IsNullOrEmpty(prepared.ArchiveEntryName))
                request = AssetBundle.LoadFromFileAsync(prepared.FilePath, 0, (ulong)prepared.StreamOffset);
            else
            {
                var bytes = await Task.Run(() => ReadArchiveEntry(prepared), token);
                token.ThrowIfCancellationRequested();
                request = AssetBundle.LoadFromMemoryAsync(bytes);
            }
            // Unity cannot cancel bundle creation. Drain it before releasing the
            // loaded bundle, so cancellation cannot leak resources or race a retry.
            while (!request.isDone) await Task.Yield();
            var bundle = request.assetBundle;
            if (token.IsCancellationRequested)
            {
                bundle?.Unload(true);
                token.ThrowIfCancellationRequested();
            }
            return bundle;
        }

        private static byte[] ReadArchiveEntry(KoikatsuBundleSource source)
        {
            using (var file = File.OpenRead(source.FilePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry(source.ArchiveEntryName);
                if (entry == null) throw new InvalidDataException("Zipmod bundle entry is missing.");
                using (var input = entry.Open())
                using (var memory = new MemoryStream())
                { input.CopyTo(memory); return memory.ToArray(); }
            }
        }

        private static AssetBundle Load(KoikatsuBundleSource source)
        {
            if (!File.Exists(source.FilePath))
            {
                throw new FileNotFoundException(
                    "Koikatsu AssetBundle source was not found.",
                    source.FilePath);
            }

            KoikatsuBundleSource fallback = null;
            if (source.StreamOffset > 0 &&
                !string.IsNullOrEmpty(source.FallbackArchiveEntryName))
            {
                fallback = new KoikatsuBundleSource(
                    source.FilePath,
                    0,
                    source.FallbackArchiveEntryName);
            }

            AssetBundle bundle;
            try
            {
                bundle = LoadPrepared(
                    KoikatsuLegacyBundleSanitizer.Prepare(source));
            }
            catch (InvalidDataException) when (fallback != null)
            {
                bundle = null;
            }

            if (bundle != null || fallback == null)
            {
                return bundle;
            }

            return LoadPrepared(
                KoikatsuLegacyBundleSanitizer.Prepare(fallback));
        }

        private static AssetBundle LoadPrepared(KoikatsuBundleSource source)
        {

            if (source.StreamOffset > 0)
            {
                return AssetBundle.LoadFromFile(
                    source.FilePath,
                    0,
                    (ulong)source.StreamOffset);
            }

            if (string.IsNullOrEmpty(source.ArchiveEntryName))
            {
                return AssetBundle.LoadFromFile(source.FilePath);
            }

            using (var file = File.OpenRead(source.FilePath))
            using (var archive = new ZipArchive(
                       file,
                       ZipArchiveMode.Read,
                       false))
            {
                var archiveEntry = archive.GetEntry(source.ArchiveEntryName);
                if (archiveEntry == null)
                {
                    throw new InvalidDataException(
                        $"Zipmod '{source.FilePath}' does not contain " +
                        $"'{source.ArchiveEntryName}'.");
                }

                using (var input = archiveEntry.Open())
                using (var memory = new MemoryStream())
                {
                    input.CopyTo(memory);
                    return AssetBundle.LoadFromMemory(memory.ToArray());
                }
            }
        }

        public static void Release(string key)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0)
            {
                return;
            }

            entries.Remove(key);
            entry.Bundle.Unload(true);
        }

        private sealed class Entry
        {
            public Entry(AssetBundle bundle)
            {
                Bundle = bundle;
            }

            public AssetBundle Bundle { get; }

            public int ReferenceCount { get; set; }
        }
    }

    internal sealed class KoikatsuBundleSource
    {
        public KoikatsuBundleSource(
            string filePath,
            long streamOffset = 0,
            string archiveEntryName = null,
            string fallbackArchiveEntryName = null)
        {
            FilePath = Path.GetFullPath(
                filePath ?? throw new ArgumentNullException(nameof(filePath)));
            StreamOffset = streamOffset;
            ArchiveEntryName = archiveEntryName ?? string.Empty;
            FallbackArchiveEntryName = fallbackArchiveEntryName ?? string.Empty;
        }

        public string FilePath { get; }

        public long StreamOffset { get; }

        public string ArchiveEntryName { get; }

        public string FallbackArchiveEntryName { get; }

        public string CacheKey =>
            $"{FilePath}|{StreamOffset}|{ArchiveEntryName}|" +
            FallbackArchiveEntryName;

        public string DisplayName => string.IsNullOrEmpty(ArchiveEntryName)
            ? FilePath
            : $"{FilePath}::{ArchiveEntryName}";
    }

    internal sealed class KoikatsuAssetBundleLease : IDisposable
    {
        private string key;

        public KoikatsuAssetBundleLease(string key, AssetBundle bundle)
        {
            this.key = key;
            Bundle = bundle;
        }

        public AssetBundle Bundle { get; private set; }

        public void Dispose()
        {
            if (key == null)
            {
                return;
            }

            KoikatsuAssetBundleCache.Release(key);
            key = null;
            Bundle = null;
        }
    }
}
