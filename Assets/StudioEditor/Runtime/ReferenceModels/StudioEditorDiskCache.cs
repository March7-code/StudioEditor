using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    // File I/O and validation are independent of Unity and can run on a worker.
    public sealed class DiskCacheStore
    {
        private const int Magic = 0x53454331;
        private const int MaxPayloadBytes = 256 * 1024 * 1024;

        public DiskCacheStore(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory);
        }

        public string DirectoryPath { get; }

        public string GetPath(string category, string key, string extension = ".bin")
        {
            if (string.IsNullOrWhiteSpace(category) ||
                category.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                category == "." || category == "..")
            {
                throw new ArgumentException("Invalid cache category.", nameof(category));
            }
            if (extension != ".bin" && extension != ".unity3d" && extension != ".clean")
            {
                throw new ArgumentException("Invalid cache extension.", nameof(extension));
            }
            return Path.Combine(DirectoryPath, category, Hash(key) + extension);
        }

        public bool TryRead(string category, string key, out byte[] payload)
        {
            payload = null;
            var path = GetPath(category, key);
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var length = reader.ReadInt32();
                    if (length < 0 || length > MaxPayloadBytes || stream.Length != length + 40L)
                        return false;
                    var checksum = reader.ReadBytes(32);
                    var bytes = reader.ReadBytes(length);
                    using (var sha = SHA256.Create())
                    {
                        var actual = sha.ComputeHash(bytes);
                        for (var index = 0; index < actual.Length; index++)
                            if (actual[index] != checksum[index]) return false;
                    }
                    payload = bytes;
                    return true;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public bool Write(string category, string key, byte[] payload,
            CancellationToken cancellationToken = default)
        {
            if (payload == null || payload.Length > MaxPayloadBytes) return false;
            var path = GetPath(category, key);
            var temporary = path + ".building-" + Guid.NewGuid().ToString("N");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = File.Create(temporary))
                using (var writer = new BinaryWriter(stream))
                using (var sha = SHA256.Create())
                {
                    writer.Write(Magic);
                    writer.Write(payload.Length);
                    writer.Write(sha.ComputeHash(payload));
                    writer.Write(payload);
                    stream.Flush();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string SourceFingerprint(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("Source file was not found.", path);
            var identity = file.FullName.Replace('\\', '/');
            // Keep installation-relative identities stable when a game moves drives.
            foreach (var folder in new[] { "/abdata/", "/mods/" })
            {
                var offset = identity.LastIndexOf(folder, StringComparison.OrdinalIgnoreCase);
                if (offset >= 0) { identity = identity.Substring(offset + 1); break; }
            }
            return identity.ToLowerInvariant() + "|" + file.Length + "|" +
                   file.LastWriteTimeUtc.Ticks;
        }
    }

    public static class StudioEditorDiskCache
    {
        private static DiskCacheStore store;
        public static DiskCacheStore Store => store ?? Initialize();

        // Call on the main thread before scheduling any worker tasks.
        public static DiskCacheStore Initialize()
        {
            if (store != null) return store;
#if UNITY_EDITOR
            var root = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library", "StudioEditor", "Cache");
#else
            var root = Path.Combine(Application.persistentDataPath, "StudioEditor", "Cache");
#endif
            store = new DiskCacheStore(root);
            return store;
        }
    }
}
