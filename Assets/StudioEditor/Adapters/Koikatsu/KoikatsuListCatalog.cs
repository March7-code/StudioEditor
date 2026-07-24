using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using MessagePack;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal sealed class KoikatsuListCatalog
    {
        private static readonly Dictionary<string, CachedCatalog> cache =
            new Dictionary<string, CachedCatalog>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<CategoryId, KoikatsuListEntry> entries =
            new Dictionary<CategoryId, KoikatsuListEntry>();
        private readonly Dictionary<string, KoikatsuListEntry> modEntries =
            new Dictionary<string, KoikatsuListEntry>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<CategoryId, List<KoikatsuListEntry>>
            compatibleModEntries =
                new Dictionary<CategoryId, List<KoikatsuListEntry>>();
        private readonly Dictionary<StudioItemId, KoikatsuStudioListEntry>
            studioEntries =
                new Dictionary<StudioItemId, KoikatsuStudioListEntry>();
        private readonly Dictionary<string, KoikatsuStudioListEntry>
            studioModEntries =
                new Dictionary<string, KoikatsuStudioListEntry>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, KoikatsuMapListEntry> mapEntries =
            new Dictionary<int, KoikatsuMapListEntry>();
        private readonly Dictionary<string, KoikatsuMapListEntry>
            mapModEntries =
                new Dictionary<string, KoikatsuMapListEntry>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, KoikatsuStudioBoneEntry> boneEntries =
            new Dictionary<int, KoikatsuStudioBoneEntry>();
        private readonly Dictionary<int, KoikatsuStudioLightEntry> lightEntries =
            new Dictionary<int, KoikatsuStudioLightEntry>();
        private readonly Dictionary<int, string> accessoryPointKeys =
            new Dictionary<int, string>();
        private readonly Dictionary<StudioItemId, KoikatsuStudioAnimationEntry>
            animationEntries =
                new Dictionary<StudioItemId, KoikatsuStudioAnimationEntry>();
        private readonly Dictionary<string, KoikatsuStudioAnimationEntry>
            animationModEntries =
                new Dictionary<string, KoikatsuStudioAnimationEntry>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly SortedDictionary<int, KoikatsuHandPoseEntry>[]
            handPoseEntries =
            {
                new SortedDictionary<int, KoikatsuHandPoseEntry>(),
                new SortedDictionary<int, KoikatsuHandPoseEntry>(),
            };
        private readonly HashSet<string> activeManifestGuids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<KoikatsuMigrationInfoDto>>
            migrationsByOldGuid =
                new Dictionary<string, List<KoikatsuMigrationInfoDto>>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly KoikatsuSideloaderVirtualFileSystem virtualFiles =
            new KoikatsuSideloaderVirtualFileSystem();

        public static KoikatsuListCatalog Load(
            string abdataRoot,
            string modsRoot = null)
        {
            abdataRoot = Path.GetFullPath(abdataRoot);
            modsRoot = string.IsNullOrWhiteSpace(modsRoot)
                ? string.Empty
                : Path.GetFullPath(modsRoot);
            var cacheKey = abdataRoot + "\n" + modsRoot;
            var fingerprint = GetCacheFingerprint(abdataRoot);
            if (cache.TryGetValue(cacheKey, out var cached) &&
                string.Equals(
                    cached.Fingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                return cached.Catalog;
            }

            var listDirectory = Path.Combine(abdataRoot, "list", "characustom");
            if (!Directory.Exists(listDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Koikatsu character list directory was not found: {listDirectory}");
            }

            var catalog = new KoikatsuListCatalog();
            var bundlePaths = Directory.GetFiles(
                listDirectory,
                "*.unity3d",
                SearchOption.TopDirectoryOnly);
            Array.Sort(bundlePaths, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < bundlePaths.Length; index++)
            {
                using (var lease = KoikatsuAssetBundleCache.Acquire(bundlePaths[index]))
                {
                    var assets = lease.Bundle.LoadAllAssets<TextAsset>();
                    for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                    {
                        catalog.Add(MessagePackSerializer.Deserialize<KoikatsuChaListDataDto>(
                            assets[assetIndex].bytes));
                    }
                }
            }

            catalog.LoadVanillaStudioLists(abdataRoot);

            catalog.LoadSideloaderCache(abdataRoot, modsRoot);
            cache[cacheKey] = new CachedCatalog(catalog, fingerprint);

            return catalog;
        }

        private static string GetCacheFingerprint(string abdataRoot)
        {
            var gameRoot = Directory.GetParent(
                abdataRoot.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            var cacheDirectory = string.IsNullOrEmpty(gameRoot)
                ? string.Empty
                : Path.Combine(gameRoot, "BepInEx", "cache");
            if (!Directory.Exists(cacheDirectory))
            {
                return string.Empty;
            }

            var parts = Directory.GetFiles(
                    cacheDirectory,
                    "sideloader_zipmod_cache.bin.*",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            return string.Join(
                "|",
                parts.Select(path =>
                {
                    var file = new FileInfo(path);
                    return $"{file.Name}:{file.Length}:" +
                           file.LastWriteTimeUtc.Ticks;
                }));
        }

        public bool TryGet(int category, int id, out KoikatsuListEntry entry)
        {
            var key = new CategoryId(category, id);
            if (entries.TryGetValue(key, out entry))
            {
                return true;
            }

            if (compatibleModEntries.TryGetValue(key, out var candidates) &&
                candidates.Count != 0)
            {
                entry = candidates[0];
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryGet(
            int category,
            int id,
            string modGuid,
            out KoikatsuListEntry entry)
        {
            ApplyManifestMigration(
                migrationsByOldGuid,
                activeManifestGuids,
                category,
                ref id,
                ref modGuid);
            if (!string.IsNullOrWhiteSpace(modGuid) &&
                modEntries.TryGetValue(
                    BuildModKey(modGuid, category, id),
                    out entry))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(modGuid))
            {
                entry = null;
                return false;
            }

            return TryGet(category, id, out entry);
        }

        public bool TryGetStudio(
            int group,
            int category,
            int id,
            string modGuid,
            out KoikatsuStudioListEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(modGuid) &&
                studioModEntries.TryGetValue(
                    BuildStudioModKey(modGuid, group, category, id),
                    out entry))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(modGuid) &&
                studioEntries.TryGetValue(
                    new StudioItemId(group, category, id),
                    out entry))
            {
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryGetMap(
            int id,
            string modGuid,
            out KoikatsuMapListEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(modGuid) &&
                mapModEntries.TryGetValue(
                    BuildMapModKey(modGuid, id),
                    out entry))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(modGuid) &&
                mapEntries.TryGetValue(id, out entry))
            {
                return true;
            }

            entry = null;
            return false;
        }

        public bool TryGetStudioBone(
            int id,
            out KoikatsuStudioBoneEntry entry)
        {
            return boneEntries.TryGetValue(id, out entry);
        }

        public bool TryGetStudioLight(
            int id,
            out KoikatsuStudioLightEntry entry)
        {
            return lightEntries.TryGetValue(id, out entry);
        }

        public bool TryGetStudioAccessoryPoint(
            int id,
            out string referenceKey)
        {
            return accessoryPointKeys.TryGetValue(id, out referenceKey);
        }

        public bool TryGetStudioAnimation(
            int group,
            int category,
            int id,
            out KoikatsuStudioAnimationEntry entry)
        {
            return animationEntries.TryGetValue(
                new StudioItemId(group, category, id),
                out entry);
        }

        public bool TryGetStudioAnimation(
            int group,
            int category,
            int id,
            string modGuid,
            out KoikatsuStudioAnimationEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(modGuid) &&
                animationModEntries.TryGetValue(
                    BuildStudioModKey(modGuid, group, category, id),
                    out entry))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(modGuid))
            {
                entry = null;
                return false;
            }

            return TryGetStudioAnimation(group, category, id, out entry);
        }

        public IReadOnlyList<KoikatsuHandPoseEntry> GetHandPoses(int hand)
        {
            if (hand < 0 || hand >= handPoseEntries.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(hand));
            }

            return handPoseEntries[hand].Values.ToArray();
        }

        public IReadOnlyList<KoikatsuBundleSource> ResolveBundleCandidates(
            string abdataRoot,
            string relativePath,
            KoikatsuZipmodArchive preferredArchive = null)
        {
            var result = new List<KoikatsuBundleSource>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            virtualFiles.AddBundleSources(relativePath, result, keys);

            if (preferredArchive != null &&
                preferredArchive.TryResolveBundle(relativePath, out var preferred) &&
                keys.Add(preferred.CacheKey))
            {
                result.Add(preferred);
            }

            var vanillaPath = KoikatsuAssetPath.ResolveAbdataPath(
                abdataRoot,
                relativePath);
            if (File.Exists(vanillaPath))
            {
                var vanilla = new KoikatsuBundleSource(vanillaPath);
                if (keys.Add(vanilla.CacheKey))
                {
                    result.Add(vanilla);
                }
            }

            return result.AsReadOnly();
        }

        public bool TryReadVirtualLooseTexture(
            string relativeBundlePath,
            string textureName,
            out byte[] data,
            out string archiveEntryName,
            out string archivePath)
        {
            return virtualFiles.TryReadLooseTexture(
                relativeBundlePath,
                textureName,
                out data,
                out archiveEntryName,
                out archivePath);
        }

        private void LoadVanillaStudioLists(string abdataRoot)
        {
            var directory = Path.Combine(abdataRoot, "studio", "info");
            if (!Directory.Exists(directory))
            {
                return;
            }

            var bundles = Directory.GetFiles(
                directory,
                "*.unity3d",
                SearchOption.TopDirectoryOnly);
            Array.Sort(bundles, StringComparer.OrdinalIgnoreCase);
            for (var bundleIndex = 0; bundleIndex < bundles.Length; bundleIndex++)
            {
                var manager = new AssetsManager();
                try
                {
                    var bundle = manager.LoadBundleFile(bundles[bundleIndex]);
                    var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                    var behaviours = assets.file.GetAssetsOfType(
                        AssetClassID.MonoBehaviour);
                    for (var index = 0; index < behaviours.Count; index++)
                    {
                        var field = manager.GetBaseField(
                            assets,
                            behaviours[index],
                            AssetReadFlags.None);
                        var name = field["m_Name"].AsString;
                        var isItemList = !string.IsNullOrEmpty(name) &&
                                         name.StartsWith(
                                             "ItemList_",
                                             StringComparison.OrdinalIgnoreCase);
                        var isMapList = !string.IsNullOrEmpty(name) &&
                                        name.StartsWith(
                                            "Map_",
                                            StringComparison.OrdinalIgnoreCase);
                        var isBoneList = !string.IsNullOrEmpty(name) &&
                                         name.StartsWith(
                                             "Bone_",
                                             StringComparison.OrdinalIgnoreCase);
                        var isLightList = !string.IsNullOrEmpty(name) &&
                                          name.StartsWith(
                                              "Light_",
                                              StringComparison.OrdinalIgnoreCase);
                        var isAccessoryPointList = !string.IsNullOrEmpty(name) &&
                                                   name.StartsWith(
                                                       "AccessoryPoint_",
                                                       StringComparison.OrdinalIgnoreCase);
                        var isAnimationList = IsAnimationListName(name);
                        var isHandList = TryParseHandList(name, out var hand);
                        if (!isItemList && !isMapList && !isBoneList &&
                            !isLightList && !isAccessoryPointList &&
                            !isAnimationList && !isHandList)
                        {
                            continue;
                        }

                        var rows = GetArray(field["list"]);
                        if (rows == null)
                        {
                            continue;
                        }

                        for (var rowIndex = 0; rowIndex < rows.Children.Count; rowIndex++)
                        {
                            var values = GetStringRow(rows.Children[rowIndex]);
                            if (values == null)
                            {
                                continue;
                            }

                            if (isItemList)
                            {
                                AddStudioEntry(
                                    values,
                                    null,
                                    string.Empty);
                            }
                            else if (isMapList)
                            {
                                AddMapEntry(
                                    values,
                                    null,
                                    string.Empty);
                            }
                            else if (isBoneList)
                            {
                                AddBoneEntry(values);
                            }
                            else if (isLightList)
                            {
                                AddLightEntry(values);
                            }
                            else if (isAccessoryPointList)
                            {
                                AddAccessoryPointEntry(values);
                            }
                            else if (isHandList)
                            {
                                AddHandPoseEntry(hand, values);
                            }
                            else
                            {
                                AddAnimationEntry(values);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not read Koikatsu Studio item list " +
                        $"'{bundles[bundleIndex]}': {exception.Message}");
                }
                finally
                {
                    manager.UnloadAll(true);
                }
            }
        }

        private static AssetTypeValueField GetArray(AssetTypeValueField field)
        {
            if (field == null || field.IsDummy)
            {
                return null;
            }

            var array = field["Array"];
            return array == null || array.IsDummy ? null : array;
        }

        private static List<string> GetStringRow(AssetTypeValueField row)
        {
            var values = GetArray(row?["list"]);
            if (values == null)
            {
                return null;
            }

            var result = new List<string>(values.Children.Count);
            for (var index = 0; index < values.Children.Count; index++)
            {
                result.Add(values.Children[index].AsString ?? string.Empty);
            }

            return result;
        }

        private void AddAccessoryPointEntry(IReadOnlyList<string> values)
        {
            if (values == null || values.Count < 3 ||
                !int.TryParse(values[0], out var id) ||
                string.IsNullOrWhiteSpace(values[2]))
            {
                return;
            }

            accessoryPointKeys[id] = values[2];
        }

        private void Add(KoikatsuChaListDataDto data)
        {
            if (data?.Keys == null || data.Rows == null)
            {
                return;
            }

            foreach (var row in data.Rows)
            {
                if (row.Value == null || row.Value.Count != data.Keys.Count)
                {
                    continue;
                }

                var values = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < data.Keys.Count; index++)
                {
                    values[data.Keys[index]] = row.Value[index];
                }

                var key = new CategoryId(data.Category, row.Key);
                if (!entries.ContainsKey(key))
                {
                    entries.Add(
                        key,
                        new KoikatsuListEntry(
                            data.Category,
                            row.Key,
                            values,
                            null,
                            string.Empty));
                }
            }
        }

        private void LoadSideloaderCache(
            string abdataRoot,
            string modsRoot)
        {
            var gameRoot = Directory.GetParent(
                Path.GetFullPath(abdataRoot).TrimEnd(
                    Path.DirectorySeparatorChar))?.FullName;
            if (string.IsNullOrEmpty(gameRoot))
            {
                return;
            }

            var cacheDirectory = Path.Combine(gameRoot, "BepInEx", "cache");
            if (!Directory.Exists(cacheDirectory))
            {
                return;
            }

            var cacheParts = Directory.GetFiles(
                    cacheDirectory,
                    "sideloader_zipmod_cache.bin.*",
                    SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(
                    ".ver",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var allZipmods = new List<KoikatsuZipmodInfoDto>();
            for (var partIndex = 0;
                 partIndex < cacheParts.Length;
                 partIndex++)
            {
                try
                {
                    using (var stream = File.OpenRead(cacheParts[partIndex]))
                    {
                        var zipmods = LZ4MessagePackSerializer.Deserialize<
                            List<KoikatsuZipmodInfoDto>>(stream);
                        if (zipmods != null)
                        {
                            allZipmods.AddRange(zipmods);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not read the Koikatsu Sideloader cache " +
                        $"'{cacheParts[partIndex]}': {exception.Message}");
                }
            }

            AddZipmods(allZipmods, gameRoot, modsRoot);
        }

        private void AddZipmods(
            IReadOnlyList<KoikatsuZipmodInfoDto> zipmods,
            string gameRoot,
            string modsRoot)
        {
            if (zipmods == null)
            {
                return;
            }

            var activeZipmods = SelectActiveZipmods(zipmods);
            var archivePaths =
                new Dictionary<KoikatsuZipmodInfoDto, string>();
            for (var zipIndex = 0;
                 zipIndex < activeZipmods.Count;
                 zipIndex++)
            {
                var zipmod = activeZipmods[zipIndex];
                var archivePath = ResolveArchivePath(
                    zipmod,
                    gameRoot,
                    modsRoot);
                var guid = zipmod?.Manifest?.Guid?.Trim();
                if (!string.IsNullOrEmpty(archivePath) &&
                    !string.IsNullOrEmpty(guid))
                {
                    archivePaths.Add(zipmod, archivePath);
                    activeManifestGuids.Add(guid);
                }
            }

            for (var zipIndex = 0;
                 zipIndex < activeZipmods.Count;
                 zipIndex++)
            {
                if (archivePaths.ContainsKey(activeZipmods[zipIndex]))
                {
                    AddManifestMigrations(activeZipmods[zipIndex].Manifest);
                }
            }

            for (var zipIndex = 0;
                 zipIndex < activeZipmods.Count;
                 zipIndex++)
            {
                var zipmod = activeZipmods[zipIndex];
                if (zipmod == null || !string.IsNullOrEmpty(zipmod.Error) ||
                    zipmod.Manifest == null ||
                    string.IsNullOrWhiteSpace(zipmod.Manifest.Guid))
                {
                    continue;
                }

                if (!archivePaths.TryGetValue(zipmod, out var archivePath))
                {
                    continue;
                }

                var archive = new KoikatsuZipmodArchive(
                    archivePath,
                    zipmod.Bundles);
                virtualFiles.AddArchive(
                    archive,
                    zipmod.Bundles,
                    zipmod.PngNames);
                for (var listIndex = 0;
                     zipmod.CharaLists != null &&
                     listIndex < zipmod.CharaLists.Count;
                     listIndex++)
                {
                    AddMod(
                        zipmod.CharaLists[listIndex],
                        zipmod.Manifest.Guid,
                        archive);
                }

                for (var listIndex = 0;
                     zipmod.StudioLists != null &&
                     listIndex < zipmod.StudioLists.Count;
                     listIndex++)
                {
                    AddStudioMod(
                        zipmod.StudioLists[listIndex],
                        zipmod.Manifest.Guid,
                        archive);
                }

                for (var listIndex = 0;
                     zipmod.MapLists != null &&
                     listIndex < zipmod.MapLists.Count;
                     listIndex++)
                {
                    AddMapMod(
                        zipmod.MapLists[listIndex],
                        zipmod.Manifest.Guid,
                        archive);
                }
            }
        }

        internal static IReadOnlyList<KoikatsuZipmodInfoDto> SelectActiveZipmods(
            IReadOnlyList<KoikatsuZipmodInfoDto> zipmods)
        {
            var groups = new Dictionary<string, List<KoikatsuZipmodInfoDto>>(
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < zipmods.Count; index++)
            {
                var zipmod = zipmods[index];
                var guid = zipmod?.Manifest?.Guid?.Trim();
                if (string.IsNullOrEmpty(guid) ||
                    !string.IsNullOrEmpty(zipmod.Error))
                {
                    continue;
                }

                if (!groups.TryGetValue(guid, out var candidates))
                {
                    candidates = new List<KoikatsuZipmodInfoDto>();
                    groups.Add(guid, candidates);
                }

                candidates.Add(zipmod);
            }

            var result = new List<KoikatsuZipmodInfoDto>(groups.Count);
            foreach (var pair in groups.OrderBy(
                         value => value.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                var selected = pair.Value[0];
                if (pair.Value.All(
                        candidate => !string.IsNullOrEmpty(
                            candidate.Manifest.Version)))
                {
                    for (var index = 1; index < pair.Value.Count; index++)
                    {
                        var candidate = pair.Value[index];
                        var comparison = CompareManifestVersions(
                            candidate.Manifest.Version,
                            selected.Manifest.Version);
                        if (comparison > 0 ||
                            (comparison == 0 &&
                             (candidate.FileName?.Length ?? 0) >
                             (selected.FileName?.Length ?? 0)))
                        {
                            selected = candidate;
                        }
                    }
                }
                else
                {
                    for (var index = 1; index < pair.Value.Count; index++)
                    {
                        if (pair.Value[index].LastWriteTime >
                            selected.LastWriteTime)
                        {
                            selected = pair.Value[index];
                        }
                    }
                }

                result.Add(selected);
            }

            return result.AsReadOnly();
        }

        private void AddManifestMigrations(KoikatsuZipmodManifestDto manifest)
        {
            if (manifest?.Migrations == null)
            {
                return;
            }

            for (var index = 0; index < manifest.Migrations.Count; index++)
            {
                var migration = manifest.Migrations[index];
                var oldGuid = migration?.GuidOld?.Trim();
                if (string.IsNullOrEmpty(oldGuid))
                {
                    continue;
                }

                if (!migrationsByOldGuid.TryGetValue(
                        oldGuid,
                        out var migrations))
                {
                    migrations = new List<KoikatsuMigrationInfoDto>();
                    migrationsByOldGuid.Add(oldGuid, migrations);
                }

                migrations.Add(migration);
            }
        }

        internal static void ApplyManifestMigration(
            IReadOnlyDictionary<string, List<KoikatsuMigrationInfoDto>>
                migrationsByGuid,
            ISet<string> activeGuids,
            int category,
            ref int id,
            ref string guid)
        {
            guid = guid?.Trim() ?? string.Empty;
            if (guid.Length == 0 ||
                migrationsByGuid == null ||
                !migrationsByGuid.TryGetValue(guid, out var migrations))
            {
                return;
            }

            if (migrations.Any(
                    migration => migration.MigrationType ==
                                 KoikatsuMigrationType.StripAll))
            {
                guid = string.Empty;
                return;
            }

            for (var index = 0; index < migrations.Count; index++)
            {
                var migration = migrations[index];
                if (migration.IdOld != id ||
                    migration.Category != category ||
                    activeGuids == null ||
                    !activeGuids.Contains(
                        migration.GuidNew?.Trim() ?? string.Empty))
                {
                    continue;
                }

                guid = migration.GuidNew.Trim();
                id = migration.IdNew;
                return;
            }

            for (var index = 0; index < migrations.Count; index++)
            {
                var migration = migrations[index];
                if (migration.MigrationType !=
                    KoikatsuMigrationType.MigrateAll ||
                    activeGuids == null ||
                    !activeGuids.Contains(
                        migration.GuidNew?.Trim() ?? string.Empty))
                {
                    continue;
                }

                guid = migration.GuidNew.Trim();
                return;
            }
        }

        internal static int CompareManifestVersions(
            string firstVersion,
            string secondVersion)
        {
            firstVersion = NormalizeManifestVersion(firstVersion);
            secondVersion = NormalizeManifestVersion(secondVersion);
            if (string.Equals(
                    firstVersion,
                    secondVersion,
                    StringComparison.Ordinal))
            {
                return 0;
            }

            var firstTokens = TokenizeManifestVersion(firstVersion);
            var secondTokens = TokenizeManifestVersion(secondVersion);
            var count = Math.Max(firstTokens.Count, secondTokens.Count);
            for (var index = 0; index < count; index++)
            {
                var first = index < firstTokens.Count
                    ? firstTokens[index]
                    : (IComparable)0;
                var second = index < secondTokens.Count
                    ? secondTokens[index]
                    : (IComparable)0;
                int comparison;
                try
                {
                    comparison = first.CompareTo(second);
                }
                catch (ArgumentException)
                {
                    var firstText = first.ToString();
                    var secondText = second.ToString();
                    if (firstText == "0" && secondText != "0")
                    {
                        return -1;
                    }

                    if (secondText == "0" && firstText != "0")
                    {
                        return 1;
                    }

                    comparison = string.CompareOrdinal(
                        firstText,
                        secondText);
                }

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static string NormalizeManifestVersion(string version)
        {
            version = version?.Trim().TrimStart('v', 'V', 'r', 'R', ' ');
            return string.IsNullOrEmpty(version) ? "0" : version;
        }

        private static IReadOnlyList<IComparable> TokenizeManifestVersion(
            string version)
        {
            var tokens = new List<IComparable>(2);
            var parts = version.Trim().Split('.', ' ', '-', ',', '_');
            for (var partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                var part = parts[partIndex];
                if (part.Length == 0)
                {
                    tokens.Add(0);
                    continue;
                }

                var digits = char.IsDigit(part[0]);
                var tokenStart = 0;
                for (var index = 1; index < part.Length; index++)
                {
                    if (digits == char.IsDigit(part[index]))
                    {
                        continue;
                    }

                    tokens.Add(ParseManifestVersionToken(
                        part.Substring(tokenStart, index - tokenStart)));
                    tokenStart = index;
                    digits = !digits;
                }

                tokens.Add(ParseManifestVersionToken(
                    part.Substring(tokenStart)));
            }

            return tokens.AsReadOnly();
        }

        private static IComparable ParseManifestVersionToken(string token)
        {
            return int.TryParse(token, out var value)
                ? (IComparable)value
                : token;
        }

        private void AddStudioMod(
            KoikatsuStudioListDataDto data,
            string guid,
            KoikatsuZipmodArchive archive)
        {
            if (data?.Entries == null || data.Entries.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(data.FileNameWithoutExtension))
            {
                return;
            }

            var isItemList = data.FileNameWithoutExtension.StartsWith(
                "ItemList_",
                StringComparison.OrdinalIgnoreCase);
            var isAnimationList = IsAnimationListName(
                data.FileNameWithoutExtension);
            var isMapList = data.FileNameWithoutExtension.StartsWith(
                "Map_",
                StringComparison.OrdinalIgnoreCase);
            if (!isItemList && !isAnimationList && !isMapList)
            {
                return;
            }

            for (var index = 0; index < data.Entries.Count; index++)
            {
                if (isItemList)
                {
                    AddStudioEntry(
                        data.Entries[index],
                        archive,
                        guid,
                        data.AssetBundleName);
                }
                else if (isAnimationList)
                {
                    AddAnimationEntry(
                        data.Entries[index],
                        archive,
                        guid,
                        data.AssetBundleName);
                }
                else
                {
                    AddMapEntry(
                        data.Entries[index],
                        archive,
                        guid,
                        data.AssetBundleName);
                }
            }
        }

        private void AddMapMod(
            KoikatsuStudioListDataDto data,
            string guid,
            KoikatsuZipmodArchive archive)
        {
            if (data?.Entries == null || data.Entries.Count == 0 ||
                string.IsNullOrEmpty(data.FileNameWithoutExtension) ||
                !data.FileNameWithoutExtension.StartsWith(
                    "Map",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (var index = 0; index < data.Entries.Count; index++)
            {
                AddMapEntry(
                    data.Entries[index],
                    archive,
                    guid,
                    data.AssetBundleName);
            }
        }

        private void AddMapEntry(
            IReadOnlyList<string> values,
            KoikatsuZipmodArchive archive,
            string modGuid,
            string fallbackBundle = null)
        {
            if (values == null || values.Count < 4 ||
                !int.TryParse(values[0], out var id))
            {
                return;
            }

            var bundlePath = string.IsNullOrWhiteSpace(values[2]) ||
                             values[2] == "0"
                ? fallbackBundle
                : values[2];
            if (string.IsNullOrWhiteSpace(bundlePath) ||
                string.IsNullOrWhiteSpace(values[3]))
            {
                return;
            }

            var entry = new KoikatsuMapListEntry(
                id,
                GetValue(values, 1),
                GetValue(values, 4),
                bundlePath,
                values[3],
                archive,
                modGuid);
            if (archive == null)
            {
                if (!mapEntries.ContainsKey(id))
                {
                    mapEntries.Add(id, entry);
                }
            }
            else
            {
                var key = BuildMapModKey(modGuid, id);
                if (!mapModEntries.ContainsKey(key))
                {
                    mapModEntries.Add(key, entry);
                }
            }
        }

        private void AddBoneEntry(IReadOnlyList<string> values)
        {
            if (values == null || values.Count < 5 ||
                !int.TryParse(values[0], out var id) ||
                string.IsNullOrWhiteSpace(values[1]) ||
                !int.TryParse(values[3], out var group) ||
                !int.TryParse(values[4], out var level) ||
                boneEntries.ContainsKey(id))
            {
                return;
            }

            boneEntries.Add(
                id,
                new KoikatsuStudioBoneEntry(
                    id,
                    values[1],
                    GetValue(values, 2),
                    group,
                    level));
        }

        private void AddLightEntry(IReadOnlyList<string> values)
        {
            if (values == null || values.Count < 6 ||
                !int.TryParse(values[0], out var id) ||
                string.IsNullOrWhiteSpace(values[4]) ||
                !int.TryParse(values[5], out var target) ||
                lightEntries.ContainsKey(id))
            {
                return;
            }

            lightEntries.Add(
                id,
                new KoikatsuStudioLightEntry(
                    id,
                    GetValue(values, 1),
                    GetValue(values, 2),
                    GetValue(values, 3),
                    values[4],
                    target));
        }

        private void AddHandPoseEntry(
            int hand,
            IReadOnlyList<string> values)
        {
            if (hand < 0 || hand >= handPoseEntries.Length ||
                values == null || values.Count < 5 ||
                !int.TryParse(values[0], out var id) ||
                string.IsNullOrWhiteSpace(values[2]) ||
                string.IsNullOrWhiteSpace(values[3]) ||
                string.IsNullOrWhiteSpace(values[4]))
            {
                return;
            }

            handPoseEntries[hand][id] = new KoikatsuHandPoseEntry(
                id,
                GetValue(values, 1),
                values[2],
                values[3],
                values[4]);
        }

        private static bool TryParseHandList(string name, out int hand)
        {
            hand = -1;
            if (string.IsNullOrEmpty(name) ||
                !name.StartsWith("HandAnime_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parts = name.Split('_');
            return parts.Length >= 2 &&
                   int.TryParse(parts[1], out hand) &&
                   hand >= 0 && hand <= 1;
        }

        private void AddAnimationEntry(
            IReadOnlyList<string> values,
            KoikatsuZipmodArchive archive = null,
            string modGuid = null,
            string fallbackBundle = null)
        {
            if (values == null || values.Count < 7 ||
                !int.TryParse(values[0], out var id) ||
                !int.TryParse(values[1], out var group) ||
                !int.TryParse(values[2], out var category) ||
                (string.IsNullOrWhiteSpace(values[4]) &&
                 string.IsNullOrWhiteSpace(fallbackBundle)) ||
                string.IsNullOrWhiteSpace(values[5]))
            {
                return;
            }

            var isHAnimation = IsHAnimationRow(values);
            var stateName = isHAnimation ? GetValue(values, 8) : values[6];
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return;
            }

            var isMotion = isHAnimation &&
                           bool.TryParse(GetValue(values, 12), out var motion) &&
                           motion;

            var key = new StudioItemId(group, category, id);
            var entry = new KoikatsuStudioAnimationEntry(
                group,
                category,
                id,
                values[3],
                string.IsNullOrWhiteSpace(values[4]) || values[4] == "0"
                    ? fallbackBundle
                    : values[4],
                values[5],
                stateName,
                isHAnimation,
                isMotion,
                isHAnimation ? GetValue(values, 6) : string.Empty,
                isHAnimation ? GetValue(values, 7) : string.Empty,
                archive,
                modGuid);
            if (archive == null)
            {
                if (!animationEntries.ContainsKey(key))
                {
                    animationEntries.Add(key, entry);
                }
            }
            else
            {
                var modKey = BuildStudioModKey(
                    modGuid,
                    group,
                    category,
                    id);
                if (!animationModEntries.ContainsKey(modKey))
                {
                    animationModEntries.Add(modKey, entry);
                }
            }
        }

        private static bool IsHAnimationRow(IReadOnlyList<string> values)
        {
            return values != null && values.Count >= 17 &&
                   bool.TryParse(GetValue(values, 10), out _) &&
                   bool.TryParse(GetValue(values, 11), out _) &&
                   bool.TryParse(GetValue(values, 12), out _);
        }

        private static bool IsAnimationListName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   (name.StartsWith(
                        "Anime_",
                        StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(
                        "HAnime_",
                        StringComparison.OrdinalIgnoreCase));
        }

        private void AddStudioEntry(
            IReadOnlyList<string> values,
            KoikatsuZipmodArchive archive,
            string modGuid,
            string fallbackBundle = null)
        {
            if (values == null || values.Count < 7 ||
                !int.TryParse(values[0], out var id) ||
                !int.TryParse(values[1], out var group) ||
                !int.TryParse(values[2], out var category))
            {
                return;
            }

            var entry = new KoikatsuStudioListEntry(
                group,
                category,
                id,
                values[3],
                values[4],
                string.IsNullOrWhiteSpace(values[5]) || values[5] == "0"
                    ? fallbackBundle
                    : values[5],
                values[6],
                GetValue(values, 7),
                ParseBoolean(values, 8),
                new[]
                {
                    ParseBoolean(values, 9),
                    ParseBoolean(values, 11),
                    ParseBoolean(values, 13),
                },
                new[]
                {
                    ParseBoolean(values, 10),
                    ParseBoolean(values, 12),
                    ParseBoolean(values, 14),
                },
                ParseBoolean(values, 16),
                ParseBoolean(values, 17),
                archive,
                modGuid);
            var key = new StudioItemId(group, category, id);
            if (archive == null)
            {
                if (!studioEntries.ContainsKey(key))
                {
                    studioEntries.Add(key, entry);
                }
            }
            else
            {
                var modKey = BuildStudioModKey(
                    modGuid,
                    group,
                    category,
                    id);
                if (!studioModEntries.ContainsKey(modKey))
                {
                    studioModEntries.Add(modKey, entry);
                }
            }
        }

        private static string GetValue(
            IReadOnlyList<string> values,
            int index)
        {
            return values != null && index >= 0 && index < values.Count
                ? values[index] ?? string.Empty
                : string.Empty;
        }

        private static bool ParseBoolean(
            IReadOnlyList<string> values,
            int index)
        {
            return bool.TryParse(GetValue(values, index), out var value) && value;
        }

        private void AddMod(
            KoikatsuChaListDataDto data,
            string guid,
            KoikatsuZipmodArchive archive)
        {
            if (data?.Keys == null || data.Rows == null)
            {
                return;
            }

            foreach (var row in data.Rows)
            {
                if (row.Value == null || row.Value.Count != data.Keys.Count ||
                    row.Value.Count == 0 ||
                    !int.TryParse(row.Value[0], out var id))
                {
                    continue;
                }

                var values = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < data.Keys.Count; index++)
                {
                    values[data.Keys[index]] = row.Value[index];
                }

                var entry = new KoikatsuListEntry(
                    data.Category,
                    id,
                    values,
                    archive,
                    guid);
                var modKey = BuildModKey(guid, data.Category, id);
                if (!modEntries.ContainsKey(modKey))
                {
                    modEntries.Add(modKey, entry);
                }

                var categoryKey = new CategoryId(data.Category, id);
                if (!compatibleModEntries.TryGetValue(
                        categoryKey,
                        out var candidates))
                {
                    candidates = new List<KoikatsuListEntry>();
                    compatibleModEntries.Add(categoryKey, candidates);
                }

                candidates.Add(entry);
            }
        }

        private static string ResolveArchivePath(
            KoikatsuZipmodInfoDto zipmod,
            string gameRoot,
            string modsRoot)
        {
            if (!string.IsNullOrWhiteSpace(zipmod.FileName) &&
                File.Exists(zipmod.FileName))
            {
                return Path.GetFullPath(zipmod.FileName);
            }

            if (!string.IsNullOrWhiteSpace(zipmod.RelativeFileName))
            {
                var relative = zipmod.RelativeFileName.Replace(
                    '/',
                    Path.DirectorySeparatorChar);
                var candidates = new[]
                {
                    Path.Combine(gameRoot, relative),
                    string.IsNullOrWhiteSpace(modsRoot)
                        ? string.Empty
                        : Path.Combine(modsRoot, relative),
                    string.IsNullOrWhiteSpace(modsRoot)
                        ? string.Empty
                        : Path.Combine(modsRoot, Path.GetFileName(relative)),
                };
                for (var index = 0; index < candidates.Length; index++)
                {
                    if (!string.IsNullOrEmpty(candidates[index]) &&
                        File.Exists(candidates[index]))
                    {
                        return Path.GetFullPath(candidates[index]);
                    }
                }
            }

            return string.Empty;
        }

        private static string BuildModKey(string guid, int category, int id)
        {
            return $"{guid.Trim()}\n{category}\n{id}";
        }

        private static string BuildStudioModKey(
            string guid,
            int group,
            int category,
            int id)
        {
            return $"{guid.Trim()}\n{group}\n{category}\n{id}";
        }

        private static string BuildMapModKey(string guid, int id)
        {
            return $"{guid.Trim()}\n{id}";
        }

        private readonly struct StudioItemId : IEquatable<StudioItemId>
        {
            public StudioItemId(int group, int category, int id)
            {
                Group = group;
                Category = category;
                Id = id;
            }

            public int Group { get; }

            public int Category { get; }

            public int Id { get; }

            public bool Equals(StudioItemId other)
            {
                return Group == other.Group &&
                       Category == other.Category &&
                       Id == other.Id;
            }

            public override bool Equals(object obj)
            {
                return obj is StudioItemId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Group * 397) ^ Category) * 397 ^ Id;
                }
            }
        }

        private readonly struct CategoryId : IEquatable<CategoryId>
        {
            public CategoryId(int category, int id)
            {
                Category = category;
                Id = id;
            }

            public int Category { get; }

            public int Id { get; }

            public bool Equals(CategoryId other)
            {
                return Category == other.Category && Id == other.Id;
            }

            public override bool Equals(object obj)
            {
                return obj is CategoryId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Category * 397) ^ Id;
                }
            }
        }

        private sealed class CachedCatalog
        {
            public CachedCatalog(
                KoikatsuListCatalog catalog,
                string fingerprint)
            {
                Catalog = catalog;
                Fingerprint = fingerprint;
            }

            public KoikatsuListCatalog Catalog { get; }

            public string Fingerprint { get; }
        }
    }

    [MessagePackObject]
    public sealed class KoikatsuChaListDataDto
    {
        [Key("categoryNo")]
        public int Category { get; set; }

        [Key("lstKey")]
        public List<string> Keys { get; set; }

        [Key("dictList")]
        public Dictionary<int, List<string>> Rows { get; set; }
    }

    internal sealed class KoikatsuListEntry
    {
        private readonly IReadOnlyDictionary<string, string> values;

        public KoikatsuListEntry(
            int category,
            int id,
            IReadOnlyDictionary<string, string> values,
            KoikatsuZipmodArchive archive,
            string modGuid)
        {
            Category = category;
            Id = id;
            this.values = values;
            Archive = archive;
            ModGuid = modGuid ?? string.Empty;
        }

        public int Category { get; }

        public int Id { get; }

        public KoikatsuZipmodArchive Archive { get; }

        public string ModGuid { get; }

        public string Get(string key)
        {
            return values.TryGetValue(key, out var value)
                ? value
                : string.Empty;
        }

        public KoikatsuBundleSource ResolveBundle(
            string abdataRoot,
            string relativePath)
        {
            if (Archive != null &&
                Archive.TryResolveBundle(relativePath, out var archiveSource))
            {
                return archiveSource;
            }

            var vanillaPath = KoikatsuAssetPath.ResolveAbdataPath(
                abdataRoot,
                relativePath);
            if (File.Exists(vanillaPath) || Archive == null)
            {
                return new KoikatsuBundleSource(vanillaPath);
            }

            return Archive.ResolveBundle(relativePath);
        }
    }

    internal sealed class KoikatsuStudioBoneEntry
    {
        public KoikatsuStudioBoneEntry(
            int id,
            string boneName,
            string displayName,
            int group,
            int level)
        {
            Id = id;
            BoneName = boneName ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Group = group;
            Level = level;
        }

        public int Id { get; }

        public string BoneName { get; }

        public string DisplayName { get; }

        public int Group { get; }

        public int Level { get; }
    }

    internal sealed class KoikatsuStudioLightEntry
    {
        public KoikatsuStudioLightEntry(
            int id,
            string name,
            string manifest,
            string bundlePath,
            string assetName,
            int target)
        {
            Id = id;
            Name = name ?? string.Empty;
            Manifest = manifest ?? string.Empty;
            BundlePath = bundlePath ?? string.Empty;
            AssetName = assetName ?? string.Empty;
            Target = target;
        }

        public int Id { get; }

        public string Name { get; }

        public string Manifest { get; }

        public string BundlePath { get; }

        public string AssetName { get; }

        public int Target { get; }
    }

    internal sealed class KoikatsuStudioAnimationEntry
    {
        public KoikatsuStudioAnimationEntry(
            int group,
            int category,
            int id,
            string name,
            string bundlePath,
            string controllerName,
            string stateName,
            bool isHAnimation,
            bool isMotion,
            string overrideBundlePath,
            string overrideControllerName,
            KoikatsuZipmodArchive archive,
            string modGuid)
        {
            Group = group;
            Category = category;
            Id = id;
            Name = name ?? string.Empty;
            BundlePath = bundlePath ?? string.Empty;
            ControllerName = controllerName ?? string.Empty;
            StateName = stateName ?? string.Empty;
            IsHAnimation = isHAnimation;
            IsMotion = isMotion;
            OverrideBundlePath = overrideBundlePath ?? string.Empty;
            OverrideControllerName = overrideControllerName ?? string.Empty;
            Archive = archive;
            ModGuid = modGuid ?? string.Empty;
        }

        public int Group { get; }

        public int Category { get; }

        public int Id { get; }

        public string Name { get; }

        public string BundlePath { get; }

        public string ControllerName { get; }

        public string StateName { get; }

        public bool IsHAnimation { get; }

        public bool IsMotion { get; }

        public string OverrideBundlePath { get; }

        public string OverrideControllerName { get; }

        public KoikatsuZipmodArchive Archive { get; }

        public string ModGuid { get; }

        public KoikatsuBundleSource ResolveBundle(string abdataRoot)
        {
            return new KoikatsuBundleSource(
                KoikatsuAssetPath.ResolveAbdataPath(abdataRoot, BundlePath));
        }
    }

    internal sealed class KoikatsuHandPoseEntry
    {
        public KoikatsuHandPoseEntry(
            int id,
            string name,
            string bundlePath,
            string controllerName,
            string clipName)
        {
            Id = id;
            Name = name ?? string.Empty;
            BundlePath = bundlePath ?? string.Empty;
            ControllerName = controllerName ?? string.Empty;
            ClipName = clipName ?? string.Empty;
        }

        public int Id { get; }

        public string Name { get; }

        public string BundlePath { get; }

        public string ControllerName { get; }

        public string ClipName { get; }
    }

    internal sealed class KoikatsuMapListEntry
    {
        public KoikatsuMapListEntry(
            int id,
            string name,
            string manifest,
            string bundlePath,
            string sceneName,
            KoikatsuZipmodArchive archive,
            string modGuid)
        {
            Id = id;
            Name = name ?? string.Empty;
            Manifest = manifest ?? string.Empty;
            BundlePath = bundlePath ?? string.Empty;
            SceneName = sceneName ?? string.Empty;
            Archive = archive;
            ModGuid = modGuid ?? string.Empty;
        }

        public int Id { get; }

        public string Name { get; }

        public string Manifest { get; }

        public string BundlePath { get; }

        public string SceneName { get; }

        public KoikatsuZipmodArchive Archive { get; }

        public string ModGuid { get; }

        public KoikatsuBundleSource ResolveBundle(string abdataRoot)
        {
            if (Archive != null)
            {
                return Archive.ResolveBundle(BundlePath);
            }

            return new KoikatsuBundleSource(
                KoikatsuAssetPath.ResolveAbdataPath(abdataRoot, BundlePath));
        }
    }

    internal sealed class KoikatsuStudioListEntry
    {
        public KoikatsuStudioListEntry(
            int group,
            int category,
            int id,
            string name,
            string manifest,
            string bundlePath,
            string assetName,
            string childRoot,
            bool isAnime,
            IReadOnlyList<bool> useColors,
            IReadOnlyList<bool> usePatterns,
            bool isEmission,
            bool isGlass,
            KoikatsuZipmodArchive archive,
            string modGuid)
        {
            Group = group;
            Category = category;
            Id = id;
            Name = name ?? string.Empty;
            Manifest = manifest ?? string.Empty;
            BundlePath = bundlePath ?? string.Empty;
            AssetName = assetName ?? string.Empty;
            ChildRoot = childRoot ?? string.Empty;
            IsAnime = isAnime;
            UseColors = useColors ?? Array.Empty<bool>();
            UsePatterns = usePatterns ?? Array.Empty<bool>();
            IsEmission = isEmission;
            IsGlass = isGlass;
            Archive = archive;
            ModGuid = modGuid ?? string.Empty;
        }

        public int Group { get; }

        public int Category { get; }

        public int Id { get; }

        public string Name { get; }

        public string Manifest { get; }

        public string BundlePath { get; }

        public string AssetName { get; }

        public string ChildRoot { get; }

        public bool IsAnime { get; }

        public IReadOnlyList<bool> UseColors { get; }

        public IReadOnlyList<bool> UsePatterns { get; }

        public bool IsEmission { get; }

        public bool IsGlass { get; }

        public KoikatsuZipmodArchive Archive { get; }

        public string ModGuid { get; }

        public KoikatsuBundleSource ResolveBundle(string abdataRoot)
        {
            if (Archive != null)
            {
                return Archive.ResolveBundle(BundlePath);
            }

            return new KoikatsuBundleSource(
                KoikatsuAssetPath.ResolveAbdataPath(abdataRoot, BundlePath));
        }
    }

    internal sealed class KoikatsuSideloaderVirtualFileSystem
    {
        private readonly Dictionary<string, List<KoikatsuZipmodArchive>>
            bundleOverlays =
                new Dictionary<string, List<KoikatsuZipmodArchive>>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KoikatsuZipmodArchive>
            looseTextureOverlays =
                new Dictionary<string, KoikatsuZipmodArchive>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly List<KoikatsuZipmodArchive> unindexedArchives =
            new List<KoikatsuZipmodArchive>();

        public void AddArchive(
            KoikatsuZipmodArchive archive,
            IReadOnlyList<KoikatsuZipmodBundleDto> bundles,
            IReadOnlyList<string> pngNames = null)
        {
            if (archive == null)
            {
                return;
            }

            if (pngNames == null)
            {
                unindexedArchives.Add(archive);
            }
            else
            {
                for (var pngIndex = 0;
                     pngIndex < pngNames.Count;
                     pngIndex++)
                {
                    var pngPath = NormalizeArchiveEntryPath(
                        pngNames[pngIndex]);
                    if (!string.IsNullOrEmpty(pngPath) &&
                        !looseTextureOverlays.ContainsKey(pngPath))
                    {
                        looseTextureOverlays.Add(pngPath, archive);
                    }
                }
            }

            if (bundles == null)
            {
                return;
            }

            for (var index = 0; index < bundles.Count; index++)
            {
                var bundle = bundles[index];
                if (bundle == null)
                {
                    continue;
                }

                AddBundlePath(bundle.TrimmedPath, archive);
                AddBundlePath(bundle.FullPath, archive);
            }
        }

        public void AddBundleSources(
            string relativePath,
            ICollection<KoikatsuBundleSource> result,
            ISet<string> keys)
        {
            var key = NormalizeBundlePath(relativePath);
            if (!bundleOverlays.TryGetValue(key, out var candidates))
            {
                return;
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                if (candidates[index].TryResolveBundle(
                        relativePath,
                        out var source) &&
                    keys.Add(source.CacheKey))
                {
                    result.Add(source);
                }
            }
        }

        public bool TryReadLooseTexture(
            string relativeBundlePath,
            string textureName,
            out byte[] data,
            out string archiveEntryName,
            out string archivePath)
        {
            var candidates = BuildLooseTexturePaths(
                relativeBundlePath,
                textureName);
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                if (!looseTextureOverlays.TryGetValue(
                        candidates[candidateIndex],
                        out var archive))
                {
                    continue;
                }

                if (archive.TryReadLooseTexture(
                        relativeBundlePath,
                        textureName,
                        out data,
                        out archiveEntryName))
                {
                    archivePath = archive.ArchivePath;
                    return true;
                }
            }

            for (var index = 0; index < unindexedArchives.Count; index++)
            {
                if (!unindexedArchives[index].TryReadLooseTexture(
                        relativeBundlePath,
                        textureName,
                        out data,
                        out archiveEntryName))
                {
                    continue;
                }

                archivePath = unindexedArchives[index].ArchivePath;
                return true;
            }

            data = null;
            archiveEntryName = string.Empty;
            archivePath = string.Empty;
            return false;
        }

        private void AddBundlePath(
            string path,
            KoikatsuZipmodArchive archive)
        {
            var key = NormalizeBundlePath(path);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!bundleOverlays.TryGetValue(key, out var candidates))
            {
                candidates = new List<KoikatsuZipmodArchive>();
                bundleOverlays.Add(key, candidates);
            }

            if (!candidates.Contains(archive))
            {
                candidates.Add(archive);
            }
        }

        private static string NormalizeBundlePath(string path)
        {
            path = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            const string prefix = "abdata/";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefix.Length)
                : path;
        }

        private static IReadOnlyList<string> BuildLooseTexturePaths(
            string relativeBundlePath,
            string textureName)
        {
            var bundlePath = NormalizeBundlePath(relativeBundlePath);
            const string bundleExtension = ".unity3d";
            if (bundlePath.EndsWith(
                    bundleExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                bundlePath = bundlePath.Substring(
                    0,
                    bundlePath.Length - bundleExtension.Length);
            }

            var assetName = (textureName ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
            var extensions = Path.HasExtension(assetName)
                ? new[] { string.Empty }
                : new[] { ".png", ".jpg", ".jpeg" };
            var result = new List<string>(extensions.Length);
            for (var index = 0; index < extensions.Length; index++)
            {
                result.Add(NormalizeArchiveEntryPath(
                    $"abdata/{bundlePath}/{assetName}{extensions[index]}"));
            }

            return result.AsReadOnly();
        }

        private static string NormalizeArchiveEntryPath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
        }
    }

    internal sealed class KoikatsuZipmodArchive
    {
        private readonly string archivePath;
        private readonly Dictionary<string, KoikatsuZipmodBundleDto> bundles =
            new Dictionary<string, KoikatsuZipmodBundleDto>(
                StringComparer.OrdinalIgnoreCase);

        public KoikatsuZipmodArchive(
            string archivePath,
            IReadOnlyList<KoikatsuZipmodBundleDto> bundleInfos)
        {
            this.archivePath = Path.GetFullPath(archivePath);
            if (bundleInfos == null)
            {
                return;
            }

            for (var index = 0; index < bundleInfos.Count; index++)
            {
                var bundle = bundleInfos[index];
                if (bundle == null)
                {
                    continue;
                }

                AddBundleKey(bundle.TrimmedPath, bundle);
                AddBundleKey(bundle.FullPath, bundle);
            }
        }

        public string ArchivePath => archivePath;

        public KoikatsuBundleSource ResolveBundle(string relativePath)
        {
            var key = NormalizeBundlePath(relativePath);
            if (!bundles.TryGetValue(key, out var bundle))
            {
                throw new InvalidDataException(
                    $"Zipmod '{archivePath}' has no AssetBundle matching " +
                    $"'{relativePath}'.");
            }

            return new KoikatsuBundleSource(
                archivePath,
                bundle.StreamOffset,
                bundle.StreamOffset > 0 ? null : bundle.FullPath,
                bundle.StreamOffset > 0 ? bundle.FullPath : null);
        }

        public bool TryResolveBundle(
            string relativePath,
            out KoikatsuBundleSource source)
        {
            var key = NormalizeBundlePath(relativePath);
            if (bundles.TryGetValue(key, out var bundle))
            {
                source = new KoikatsuBundleSource(
                    archivePath,
                    bundle.StreamOffset,
                    bundle.StreamOffset > 0 ? null : bundle.FullPath,
                    bundle.StreamOffset > 0 ? bundle.FullPath : null);
                return true;
            }

            source = null;
            return false;
        }

        public bool TryReadLooseTexture(
            string relativeBundlePath,
            string textureName,
            out byte[] data,
            out string archiveEntryName)
        {
            data = null;
            archiveEntryName = string.Empty;
            if (string.IsNullOrWhiteSpace(relativeBundlePath) ||
                string.IsNullOrWhiteSpace(textureName) ||
                !File.Exists(archivePath))
            {
                return false;
            }

            var bundlePath = NormalizeBundlePath(relativeBundlePath);
            const string bundleExtension = ".unity3d";
            if (bundlePath.EndsWith(
                    bundleExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                bundlePath = bundlePath.Substring(
                    0,
                    bundlePath.Length - bundleExtension.Length);
            }

            var assetName = textureName.Replace('\\', '/').TrimStart('/');
            var extensions = Path.HasExtension(assetName)
                ? new[] { string.Empty }
                : new[] { ".png", ".jpg", ".jpeg" };
            var candidates = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < extensions.Length; index++)
            {
                candidates.Add(NormalizeArchiveEntryPath(
                    $"abdata/{bundlePath}/{assetName}{extensions[index]}"));
            }

            using (var file = new FileStream(
                       archivePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite))
            using (var archive = new ZipArchive(
                       file,
                       ZipArchiveMode.Read,
                       false))
            {
                ZipArchiveEntry match = null;
                foreach (var entry in archive.Entries)
                {
                    if (candidates.Contains(
                            NormalizeArchiveEntryPath(entry.FullName)))
                    {
                        match = entry;
                        break;
                    }
                }

                if (match == null || match.Length > int.MaxValue)
                {
                    return false;
                }

                using (var source = match.Open())
                using (var target = new MemoryStream(
                           match.Length > 0 ? (int)match.Length : 0))
                {
                    source.CopyTo(target);
                    data = target.ToArray();
                }

                archiveEntryName = match.FullName;
                return data.Length != 0;
            }
        }

        private void AddBundleKey(
            string path,
            KoikatsuZipmodBundleDto bundle)
        {
            var key = NormalizeBundlePath(path);
            if (!string.IsNullOrEmpty(key) && !bundles.ContainsKey(key))
            {
                bundles.Add(key, bundle);
            }
        }

        private static string NormalizeBundlePath(string path)
        {
            path = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            const string prefix = "abdata/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(prefix.Length);
            }

            return path;
        }

        private static string NormalizeArchiveEntryPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }
    }

    [MessagePackObject]
    public sealed class KoikatsuZipmodInfoDto
    {
        [Key(0)] public KoikatsuZipmodManifestDto Manifest { get; set; }
        [Key(1)] public string FileName { get; set; }
        [Key(2)] public string RelativeFileName { get; set; }
        [Key(3)] public DateTime LastWriteTime { get; set; }
        [Key(4)] public long FileSize { get; set; }
        [Key(5)] public string Error { get; set; }
        [Key(6)] public List<string> PngNames { get; set; }
        [Key(7)] public List<KoikatsuChaListDataDto> CharaLists { get; set; }
        [Key(8)] public List<KoikatsuZipmodBundleDto> Bundles { get; set; }
        [Key(9)] public List<KoikatsuStudioListDataDto> BoneLists { get; set; }
        [Key(10)] public List<KoikatsuStudioListDataDto> StudioLists { get; set; }
        [Key(11)] public List<KoikatsuStudioListDataDto> MapLists { get; set; }
    }

    [MessagePackObject]
    public sealed class KoikatsuStudioListDataDto
    {
        [Key("fileName")] public string FileName { get; set; }
        [Key("fileNameWithoutExtension")] public string FileNameWithoutExtension { get; set; }
        [Key("assetBundleName")] public string AssetBundleName { get; set; }
        [Key("headers")] public List<List<string>> Headers { get; set; }
        [Key("entries")] public List<List<string>> Entries { get; set; }
    }

    [MessagePackObject]
    public sealed class KoikatsuZipmodManifestDto
    {
        [Key(0)] public int SchemaVersion { get; set; }
        [Key(1)] public string Guid { get; set; }
        [Key(2)] public string Name { get; set; }
        [Key(3)] public string Version { get; set; }
        [Key(4)] public string Author { get; set; }
        [Key(5)] public string Website { get; set; }
        [Key(6)] public string Description { get; set; }
        [Key(7)] public string ManifestString { get; set; }
        [Key(8)] public List<string> Games { get; set; }
        [Key(9)] public List<KoikatsuMigrationInfoDto> Migrations { get; set; }
    }

    public enum KoikatsuMigrationType
    {
        Migrate,
        MigrateAll,
        StripAll,
    }

    [MessagePackObject]
    public sealed class KoikatsuMigrationInfoDto
    {
        [Key(0)] public KoikatsuMigrationType MigrationType { get; set; }

        [Key(1)] public int Category { get; set; }
        [Key(2)] public string GuidOld { get; set; }
        [Key(3)] public string GuidNew { get; set; }
        [Key(4)] public int IdOld { get; set; }
        [Key(5)] public int IdNew { get; set; }
    }

    [MessagePackObject]
    public sealed class KoikatsuZipmodBundleDto
    {
        [Key(0)] public string ArchiveFileName { get; set; }
        [Key(1)] public long StreamOffset { get; set; }
        [Key(2)] public string FullPath { get; set; }
        [Key(3)] public string TrimmedPath { get; set; }
    }
}
