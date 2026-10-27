using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuStudioLightRig
    {
        // The saved angles rotate Light Chara, not its Directional Chara child.
        // Read the source rig instead of assuming that child's rotation is identity.
        public static Quaternion ReadCharacterLightOffset(string abdataRoot)
        {
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(abdataRoot)), "CharaStudio_Data", "level1");
            if (!File.Exists(path)) return Quaternion.identity;
            var values = KoikatsuParsedMetadataCache.Read(new KoikatsuBundleSource(path),
                "studio-character-light-rotation-v2", () =>
                {
                    var rotation = ReadOffset(path);
                    return new[] { rotation.x, rotation.y, rotation.z, rotation.w };
                });
            return new Quaternion(values[0], values[1], values[2], values[3]);
        }

        private static BinaryReader ReadAsset(AssetsFileInstance assets, AssetFileInfo info)
        {
            if (info == null || info.ByteSize > 1024 * 1024) throw new InvalidDataException("Invalid Studio light asset.");
            assets.file.Reader.Position = info.GetAbsoluteByteOffset(assets.file);
            return new BinaryReader(new MemoryStream(assets.file.Reader.ReadBytes((int)info.ByteSize), false));
        }

        private static Quaternion ReadOffset(string path)
        {
            var manager = new AssetsManager();
            try
            {
                var assets = manager.LoadAssetsFile(path, false);
                // Player scene files strip type trees. Koikatsu's Unity 5.6 / 2017
                // GameObject, PPtr and Transform layouts are read directly.
                var version = assets.file.Metadata.UnityVersion;
                if (!version.StartsWith("5.6.", StringComparison.Ordinal) &&
                    !version.StartsWith("2017.", StringComparison.Ordinal))
                    return Quaternion.identity;
                foreach (var info in assets.file.GetAssetsOfType(AssetClassID.Light))
                {
                    using var light = ReadAsset(assets, info);
                    if (light.ReadInt32() != 0) continue;
                    var ownerId = light.ReadInt64();
                    var ownerInfo = assets.file.GetAssetInfo(ownerId);
                    if (ownerInfo == null) continue;
                    using var owner = ReadAsset(assets, ownerInfo);
                    var count = owner.ReadInt32();
                    if (count < 0 || count > 1024) throw new InvalidDataException("Invalid light component count.");
                    long transformId = 0;
                    for (var index = 0; index < count; index++)
                    {
                        var fileId = owner.ReadInt32();
                        var componentId = owner.ReadInt64();
                        var component = fileId == 0 ? assets.file.GetAssetInfo(componentId) : null;
                        if (component?.TypeId == (int)AssetClassID.Transform) transformId = componentId;
                    }
                    owner.ReadUInt32(); // layer
                    var nameLength = owner.ReadInt32();
                    if (nameLength < 0 || nameLength > 1024) throw new InvalidDataException("Invalid light name.");
                    var name = System.Text.Encoding.UTF8.GetString(owner.ReadBytes(nameLength));
                    if (name != "Directional Chara" || transformId == 0) continue;
                    using var transform = ReadAsset(assets, assets.file.GetAssetInfo(transformId));
                    transform.ReadInt32(); transform.ReadInt64(); // owner PPtr
                    return new Quaternion(transform.ReadSingle(), transform.ReadSingle(),
                        transform.ReadSingle(), transform.ReadSingle()).normalized;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException)
            {
                Debug.LogWarning("Could not read the source Studio character light rig: " + exception.Message);
            }
            finally { manager.UnloadAll(true); }
            return Quaternion.identity;
        }
    }
}
