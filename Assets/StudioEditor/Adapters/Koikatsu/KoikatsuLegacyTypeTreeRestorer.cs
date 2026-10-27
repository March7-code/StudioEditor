using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuLegacyTypeTreeRestorer
    {
        // Schema metadata only. No textures, geometry, or original scripts are
        // instantiated or executed while restoring an older bundle's type trees.
        private static readonly Dictionary<string, ReferenceFile> references =
            new Dictionary<string, ReferenceFile>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] PreferredReferences =
        {
            "oo_base.unity3d", "ao_head_00.unity3d", "bo_head_00.unity3d", "co_top_00.unity3d",
        };

        internal static bool Restore(AssetsFile file, string sourcePath)
        {
            if (Complete(file.Metadata))
            {
                if (file.Metadata.TypeTreeEnabled) return false;
                file.Metadata.TypeTreeEnabled = true;
                return true;
            }
            var directory = new FileInfo(sourcePath).Directory;
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "abdata")))
                directory = directory.Parent;
            if (directory == null)
                throw new InvalidDataException("Cannot restore missing asset type trees: " +
                    $"the source game's abdata directory was not found for '{sourcePath}'.");

            var chara = Path.Combine(directory.FullName, "abdata", "chara");
            var preferred = PreferredReferences.Select(name => Path.Combine(chara, name));
            var paths = preferred.Concat(Directory.EnumerateFiles(chara, "*.unity3d")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                ReferenceModelLoadContext.Current?.CancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path)) continue;
                foreach (var reference in ReadReferences(path))
                    CopyMatchingTrees(file.Metadata, reference);
                if (!Complete(file.Metadata)) continue;
                file.Metadata.TypeTreeEnabled = true;
                return true;
            }

            var missing = file.Metadata.TypeTreeTypes.Where(type => type.Nodes == null || type.Nodes.Count == 0)
                .Select(type => $"class {type.TypeId}, layout {type.TypeHash}");
            throw new InvalidDataException("No matching original-game type trees were found for " +
                $"Unity '{file.Metadata.UnityVersion}': {string.Join("; ", missing)}.");
        }

        internal static void CopyMatchingTrees(AssetsFileMetadata target, AssetsFileMetadata reference)
        {
            if (Version(target.UnityVersion) != Version(reference.UnityVersion) ||
                target.TargetPlatform == unchecked((uint)-2) ||
                reference.TargetPlatform == unchecked((uint)-2)) return;
            foreach (var type in target.TypeTreeTypes)
            {
                if (type.Nodes != null && type.Nodes.Count > 0) continue;
                if (type.TypeHash.IsZero()) continue;
                var match = reference.TypeTreeTypes.FirstOrDefault(candidate =>
                    candidate.TypeId == type.TypeId && candidate.Nodes != null && candidate.Nodes.Count > 0 &&
                    candidate.TypeHash.data.SequenceEqual(type.TypeHash.data) &&
                    (type.TypeId != 114 || candidate.ScriptIdHash.data.SequenceEqual(type.ScriptIdHash.data)));
                if (match == null) continue;
                // Keep the destination's script index, identity hashes, object
                // IDs, and payloads. Only the omitted schema comes from the donor.
                type.Nodes = match.Nodes;
                type.StringBufferBytes = match.StringBufferBytes;
            }
            if (Complete(target)) target.TypeTreeEnabled = true;
        }

        private static bool Complete(AssetsFileMetadata metadata) =>
            metadata.TypeTreeTypes.All(type => type.Nodes != null && type.Nodes.Count > 0);

        private static string Version(string value) =>
            (value ?? string.Empty).Split('\n')[0].Trim();

        private static IReadOnlyList<AssetsFileMetadata> ReadReferences(string path)
        {
            var info = new FileInfo(path);
            var stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            if (references.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Metadata;
            var manager = new AssetsManager();
            try
            {
                var bundle = manager.LoadBundleFile(path);
                var metadata = new List<AssetsFileMetadata>();
                for (var index = 0; index < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; index++)
                {
                    if (!bundle.file.IsAssetsFile(index)) continue;
                    metadata.Add(manager.LoadAssetsFileFromBundle(bundle, index, false).file.Metadata);
                }
                references[path] = new ReferenceFile(stamp, metadata);
                return metadata;
            }
            finally { manager.UnloadAll(true); }
        }

        private sealed class ReferenceFile
        {
            internal ReferenceFile(string stamp, IReadOnlyList<AssetsFileMetadata> metadata)
            { Stamp = stamp; Metadata = metadata; }
            internal string Stamp { get; }
            internal IReadOnlyList<AssetsFileMetadata> Metadata { get; }
        }
    }
}
