using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal sealed class KoikatsuStudioPatternTextures
    {
        private readonly Texture2D[] textures = new Texture2D[3];

        public Texture2D this[int channel]
        {
            get => channel >= 0 && channel < textures.Length
                ? textures[channel]
                : null;
            set
            {
                if (channel >= 0 && channel < textures.Length)
                {
                    textures[channel] = value;
                }
            }
        }
    }

    internal static class KoikatsuStudioPatternLoader
    {
        private const int PatternCategory = 430;

        public static KoikatsuStudioPatternTextures Load(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            KoikatsuStudioListEntry entry,
            KoikatsuSceneItem appearance,
            KoikatsuScene scene,
            KoikatsuSceneObject itemObject,
            List<KoikatsuAssetBundleLease> leases,
            ICollection<Texture2D> runtimeTextures)
        {
            var result = new KoikatsuStudioPatternTextures();
            var item = itemObject?.Item ?? appearance;
            if (item?.Patterns == null)
            {
                return result;
            }

            var textureLoader = new KoikatsuTextureLoader(
                abdataRoot,
                catalog,
                leases,
                null,
                runtimeTextures);
            for (var channel = 0; channel < 3; channel++)
            {
                if (channel >= item.Patterns.Length ||
                    channel >= entry.UsePatterns.Count ||
                    !entry.UsePatterns[channel])
                {
                    continue;
                }

                var pattern = item.Patterns[channel];
                if (pattern == null)
                {
                    continue;
                }

                try
                {
                    result[channel] = LoadCustom(
                        abdataRoot,
                        pattern.FilePath,
                        runtimeTextures);
                    if (result[channel] != null)
                    {
                        continue;
                    }

                    var slot = pattern.Key;
                    string guid = null;
                    if (scene != null &&
                        scene.TryResolvePattern(
                            itemObject,
                            channel,
                            out var resolution))
                    {
                        slot = resolution.Slot;
                        guid = resolution.Guid;
                    }

                    if (slot > 0)
                    {
                        result[channel] =
                            textureLoader.LoadCatalogTextureForGuid(
                                PatternCategory,
                                slot,
                                guid,
                                "MainTexAB",
                                "MainTex");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Could not load Koikatsu Studio pattern channel " +
                        $"{channel + 1} for item {item.Group}/" +
                        $"{item.Category}/{item.No}: {exception.Message}");
                }
            }

            return result;
        }

        private static Texture2D LoadCustom(
            string abdataRoot,
            string filePath,
            ICollection<Texture2D> runtimeTextures)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var fileName = Path.GetFileName(filePath);
            var gameRoot = Directory.GetParent(
                Path.GetFullPath(abdataRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar))?.FullName;
            var path = string.IsNullOrEmpty(gameRoot) ||
                       string.IsNullOrEmpty(fileName)
                ? string.Empty
                : Path.Combine(gameRoot, "UserData", "pattern", fileName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = $"Koikatsu Studio Pattern {fileName}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };
            try
            {
                if (!ImageConversion.LoadImage(
                        texture,
                        File.ReadAllBytes(path),
                        false))
                {
                    throw new InvalidDataException(
                        $"Custom pattern image '{path}' is invalid.");
                }

                runtimeTextures.Add(texture);
                return texture;
            }
            catch
            {
                KoikatsuStudioItemLoader.Destroy(texture);
                throw;
            }
        }
    }
}
