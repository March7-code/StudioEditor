using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuLegacyBundleSanitizer
    {
        private const string CacheVersion = "texture-zero-mips-lz4-v4";

        private static readonly Dictionary<string, KoikatsuBundleSource>
            preparedSources =
                new Dictionary<string, KoikatsuBundleSource>(
                    StringComparer.OrdinalIgnoreCase);

        public static KoikatsuBundleSource Prepare(
            KoikatsuBundleSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var sourceInfo = new FileInfo(source.FilePath);
            if (!sourceInfo.Exists)
            {
                throw new FileNotFoundException(
                    "Koikatsu AssetBundle source was not found.",
                    source.FilePath);
            }

            var fingerprint = string.Join(
                "|",
                CacheVersion,
                source.CacheKey,
                sourceInfo.Length,
                sourceInfo.LastWriteTimeUtc.Ticks);
            if (preparedSources.TryGetValue(fingerprint, out var prepared))
            {
                return prepared;
            }

            var cacheDirectory = GetCacheDirectory();
            Directory.CreateDirectory(cacheDirectory);
            var cachePath = Path.Combine(
                cacheDirectory,
                ComputeHash(fingerprint) + ".unity3d");
            var cleanMarkerPath = cachePath + ".clean";
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                prepared = new KoikatsuBundleSource(cachePath);
                preparedSources[fingerprint] = prepared;
                return prepared;
            }

            if (File.Exists(cleanMarkerPath))
            {
                preparedSources[fingerprint] = source;
                return source;
            }

            var materializedPath = string.Empty;
            var outputPath = cachePath + ".building-" +
                             Guid.NewGuid().ToString("N");
            try
            {
                materializedPath = MaterializeSource(
                    source,
                    cacheDirectory);
                if (!TryWriteSanitizedBundle(materializedPath, outputPath))
                {
                    File.WriteAllText(cleanMarkerPath, CacheVersion);
                    preparedSources[fingerprint] = source;
                    return source;
                }

                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }

                File.Move(outputPath, cachePath);
                Debug.Log(
                    "Prepared Unity 6 compatible Koikatsu AssetBundle: " +
                    $"'{source.DisplayName}' -> '{cachePath}'.");
                prepared = new KoikatsuBundleSource(cachePath);
                preparedSources[fingerprint] = prepared;
                return prepared;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Could not create a Unity 6 compatible copy of " +
                    $"Koikatsu AssetBundle '{source.DisplayName}'.",
                    exception);
            }
            finally
            {
                DeleteIfExists(outputPath);
                if (!string.IsNullOrEmpty(materializedPath) &&
                    !string.Equals(
                        materializedPath,
                        source.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    DeleteIfExists(materializedPath);
                }
            }
        }

        internal static bool TryGetBaseLevelSize(
            int width,
            int height,
            int format,
            out int size)
        {
            size = 0;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            try
            {
                switch (format)
                {
                    case 1: // Alpha8
                        size = checked(width * height);
                        break;
                    case 2: // ARGB4444
                    case 7: // RGB565
                    case 9: // R16
                    case 13: // RGBA4444
                        size = checked(width * height * 2);
                        break;
                    case 3: // RGB24
                        size = checked(width * height * 3);
                        break;
                    case 4: // RGBA32
                    case 5: // ARGB32
                    case 14: // BGRA32
                    case 18: // RFloat
                        size = checked(width * height * 4);
                        break;
                    case 15: // RHalf
                        size = checked(width * height * 2);
                        break;
                    case 16: // RGHalf
                        size = checked(width * height * 4);
                        break;
                    case 17: // RGBAHalf
                    case 19: // RGFloat
                        size = checked(width * height * 8);
                        break;
                    case 20: // RGBAFloat
                        size = checked(width * height * 16);
                        break;
                    case 10: // DXT1 / BC1
                        size = GetBlockCompressedSize(width, height, 8);
                        break;
                    case 12: // DXT5 / BC3
                    case 24: // BC6H
                    case 25: // BC7
                        size = GetBlockCompressedSize(width, height, 16);
                        break;
                    default:
                        return false;
                }
            }
            catch (OverflowException)
            {
                size = 0;
                return false;
            }

            return size > 0;
        }

        private static bool TryWriteSanitizedBundle(
            string inputPath,
            string outputPath)
        {
            var manager = new AssetsManager();
            try
            {
                var bundle = manager.LoadBundleFile(inputPath);
                if (!IsLegacyEngineVersion(bundle.file.Header.EngineVersion))
                {
                    return false;
                }

                var changed = false;
                var directories = bundle.file.BlockAndDirInfo.DirectoryInfos;
                for (var index = 0; index < directories.Count; index++)
                {
                    if (!bundle.file.IsAssetsFile(index))
                    {
                        continue;
                    }

                    var assets = manager.LoadAssetsFileFromBundle(
                        bundle,
                        index,
                        false);
                    var fileChanged = false;
                    var textures = assets.file.GetAssetsOfType(
                        AssetClassID.Texture2D);
                    for (var textureIndex = 0;
                         textureIndex < textures.Count;
                         textureIndex++)
                    {
                        var info = textures[textureIndex];
                        var texture = manager.GetBaseField(
                            assets,
                            info,
                            AssetReadFlags.None);
                        if (!TrySanitizeTexture(texture))
                        {
                            if (RequiresMipSanitization(texture))
                            {
                                throw new InvalidDataException(
                                    "Legacy Texture2D cannot be converted " +
                                    $"safely: '{texture["m_Name"].AsString}'.");
                            }

                            continue;
                        }

                        info.SetNewData(texture);
                        fileChanged = true;
                    }

                    if (!fileChanged)
                    {
                        continue;
                    }

                    directories[index].SetNewData(assets.file);
                    changed = true;
                }

                if (!changed)
                {
                    return false;
                }

                var uncompressedPath = outputPath + ".uncompressed";
                try
                {
                    using (var writer = new AssetsFileWriter(uncompressedPath))
                    {
                        bundle.file.Write(writer);
                    }

                    var packManager = new AssetsManager();
                    try
                    {
                        var rewritten = packManager.LoadBundleFile(
                            uncompressedPath);
                        using (var writer = new AssetsFileWriter(outputPath))
                        {
                            rewritten.file.Pack(
                                writer,
                                AssetBundleCompressionType.LZ4,
                                false,
                                null);
                        }
                    }
                    finally
                    {
                        packManager.UnloadAll(true);
                    }
                }
                finally
                {
                    DeleteIfExists(uncompressedPath);
                }

                return true;
            }
            finally
            {
                manager.UnloadAll(true);
            }
        }

        private static bool TrySanitizeTexture(AssetTypeValueField texture)
        {
            var mipCount = texture["m_MipCount"];
            var imageCount = texture["m_ImageCount"];
            var dimension = texture["m_TextureDimension"];
            var imageData = texture["image data"];
            if (mipCount.IsDummy ||
                !RequiresMipCountRepair(mipCount.AsInt) ||
                imageCount.IsDummy || imageCount.AsInt != 1 ||
                dimension.IsDummy || dimension.AsInt != 2 ||
                imageData.IsDummy)
            {
                return false;
            }

            var bytes = imageData.AsByteArray;
            if (bytes == null || bytes.Length == 0 ||
                !TryGetBaseLevelSize(
                    texture["m_Width"].AsInt,
                    texture["m_Height"].AsInt,
                    texture["m_TextureFormat"].AsInt,
                    out var baseSize) ||
                bytes.Length < baseSize)
            {
                return false;
            }

            var baseLevel = new byte[baseSize];
            Buffer.BlockCopy(bytes, 0, baseLevel, 0, baseSize);
            // Unity 5 bundles may encode a non-mipmapped Texture2D as zero.
            // Unity 6 requires level 0 to be represented by a count of one.
            mipCount.AsInt = 1;
            texture["m_CompleteImageSize"].AsInt = baseSize;
            imageData.AsByteArray = baseLevel;
            return true;
        }

        private static bool RequiresMipSanitization(
            AssetTypeValueField texture)
        {
            var mipCount = texture["m_MipCount"];
            var imageCount = texture["m_ImageCount"];
            var dimension = texture["m_TextureDimension"];
            return !mipCount.IsDummy &&
                   RequiresMipCountRepair(mipCount.AsInt) &&
                   !imageCount.IsDummy && imageCount.AsInt == 1 &&
                   !dimension.IsDummy && dimension.AsInt == 2;
        }

        internal static bool RequiresMipCountRepair(int mipCount)
        {
            return mipCount == 0;
        }

        internal static bool IsLegacyEngineVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return true;
            }

            var separator = version.IndexOf('.');
            var majorText = separator < 0
                ? version
                : version.Substring(0, separator);
            return !int.TryParse(majorText, out var major) || major < 2019;
        }

        private static int GetBlockCompressedSize(
            int width,
            int height,
            int bytesPerBlock)
        {
            return checked(
                Math.Max(1, (width + 3) / 4) *
                Math.Max(1, (height + 3) / 4) *
                bytesPerBlock);
        }

        private static string MaterializeSource(
            KoikatsuBundleSource source,
            string cacheDirectory)
        {
            if (source.StreamOffset == 0 &&
                string.IsNullOrEmpty(source.ArchiveEntryName))
            {
                return source.FilePath;
            }

            var path = Path.Combine(
                cacheDirectory,
                Guid.NewGuid().ToString("N") + ".source");
            using (var output = File.Create(path))
            {
                if (source.StreamOffset > 0)
                {
                    using (var input = File.OpenRead(source.FilePath))
                    {
                        var length = ReadUnityFsLength(
                            input,
                            source.StreamOffset);
                        input.Position = source.StreamOffset;
                        CopyExactly(input, output, length);
                    }
                }
                else
                {
                    using (var file = File.OpenRead(source.FilePath))
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
                            input.CopyTo(output);
                        }
                    }
                }
            }

            return path;
        }

        private static long ReadUnityFsLength(Stream stream, long offset)
        {
            stream.Position = offset;
            var signature = ReadNullTerminated(stream);
            if (!string.Equals(signature, "UnityFS", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Expected UnityFS at stream offset {offset}.");
            }

            ReadBigEndianUInt32(stream);
            ReadNullTerminated(stream);
            ReadNullTerminated(stream);
            var length = ReadBigEndianInt64(stream);
            if (length <= 0 || offset + length > stream.Length)
            {
                throw new InvalidDataException(
                    $"UnityFS at stream offset {offset} has invalid length " +
                    $"{length}.");
            }

            return length;
        }

        private static string ReadNullTerminated(Stream stream)
        {
            var bytes = new List<byte>(32);
            while (bytes.Count < 1024)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    throw new EndOfStreamException();
                }

                if (value == 0)
                {
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }

                bytes.Add((byte)value);
            }

            throw new InvalidDataException(
                "UnityFS header contains an unterminated string.");
        }

        private static uint ReadBigEndianUInt32(Stream stream)
        {
            var bytes = ReadExactly(stream, 4);
            return ((uint)bytes[0] << 24) |
                   ((uint)bytes[1] << 16) |
                   ((uint)bytes[2] << 8) |
                   bytes[3];
        }

        private static long ReadBigEndianInt64(Stream stream)
        {
            var bytes = ReadExactly(stream, 8);
            ulong value = 0;
            for (var index = 0; index < bytes.Length; index++)
            {
                value = (value << 8) | bytes[index];
            }

            return unchecked((long)value);
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var result = new byte[count];
            var offset = 0;
            while (offset < result.Length)
            {
                var read = stream.Read(result, offset, result.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }

            return result;
        }

        private static void CopyExactly(
            Stream input,
            Stream output,
            long count)
        {
            var buffer = new byte[81920];
            while (count > 0)
            {
                var read = input.Read(
                    buffer,
                    0,
                    (int)Math.Min(buffer.Length, count));
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                output.Write(buffer, 0, read);
                count -= read;
            }
        }

        private static string ComputeHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var result = new StringBuilder(hash.Length * 2);
                for (var index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2"));
                }

                return result.ToString();
            }
        }

        private static string GetCacheDirectory()
        {
#if UNITY_EDITOR
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                return Path.Combine(
                    projectRoot,
                    "Library",
                    "BodyEditor",
                    "KoikatsuBundles");
            }
#endif
            return Path.Combine(
                Application.temporaryCachePath,
                "BodyEditor",
                "KoikatsuBundles");
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
