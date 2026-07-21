using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BodyEditor.Characters;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public sealed class KoikatsuReferenceModelAdapter :
        IReferenceModelVariantFormatAdapter
    {
        private static readonly IReadOnlyList<string> extensions =
            Array.AsReadOnly(new[] { ".png", ".unity3d" });

        public static string GameRootOverride { get; set; }

        public string FormatName => "Koikatsu character AssetBundle";

        public IReadOnlyList<string> FileExtensions => extensions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAdapter()
        {
            ReferenceModelAdapterRegistry.Register<KoikatsuReferenceModelAdapter>();
        }

        public Task<IReferenceModelInstance> ImportAsync(
            string filePath,
            Transform parent,
            CancellationToken cancellationToken)
        {
            return ImportVariantAsync(
                filePath,
                parent,
                0,
                cancellationToken);
        }

        public Task<IReferenceModelInstance> ImportVariantAsync(
            string filePath,
            Transform parent,
            int variantIndex,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Koikatsu AssetBundle was not found.",
                    filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            KoikatsuReferenceModelInstance instance;
            if (string.Equals(
                    Path.GetExtension(filePath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                var card = KoikatsuCardReader.Read(filePath);
                var installation = KoikatsuInstallation.Find(filePath);
                instance = KoikatsuCharacterAssembler.BuildFromCard(
                    card,
                    installation.AbdataRoot,
                    installation.ModsRoot,
                    parent,
                    cancellationToken,
                    variantIndex);
            }
            else
            {
                if (variantIndex != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(variantIndex),
                        "Direct AssetBundle imports have no outfit slots.");
                }

                if (!string.Equals(
                        Path.GetFileName(filePath),
                        KoikatsuCharacterAssembler.BaseBundleFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Direct Koikatsu bundle import expects " +
                        $"'{KoikatsuCharacterAssembler.BaseBundleFileName}'.");
                }

                instance = KoikatsuCharacterAssembler.BuildFemaleBase(
                    Path.GetFullPath(filePath),
                    parent,
                    cancellationToken);
            }

            return Task.FromResult<IReferenceModelInstance>(instance);
        }
    }

    public sealed class KoikatsuSceneReferenceModelAdapter :
        IReferenceSceneFormatAdapter
    {
        private static readonly IReadOnlyList<string> extensions =
            Array.AsReadOnly(new[] { ".png" });

        public string FormatName => "Koikatsu Studio scene";

        public IReadOnlyList<string> FileExtensions => extensions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAdapter()
        {
            ReferenceModelAdapterRegistry.Register<
                KoikatsuSceneReferenceModelAdapter>();
        }

        public async Task<IReferenceModelInstance> ImportAsync(
            string filePath,
            Transform parent,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "Koikatsu Studio scene card was not found.",
                    filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var scene = KoikatsuSceneReader.Read(filePath);
            KoikatsuTimelineScene timeline = null;
            try
            {
                KoikatsuTimelineSceneReader.TryRead(filePath, out timeline);
            }
            catch (InvalidDataException exception)
            {
                Debug.LogWarning(
                    "Could not import Koikatsu Timeline data: " +
                    exception.Message);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var installation = KoikatsuInstallation.Find(filePath);
            var loaded = await KoikatsuStudioSceneLoader.LoadAsync(
                scene,
                installation.AbdataRoot,
                installation.ModsRoot,
                parent,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                loaded.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new KoikatsuSceneReferenceModelInstance(
                filePath,
                loaded,
                timeline);
        }

    }

    internal sealed class KoikatsuSceneReferenceModelInstance :
        IReferenceModelInstance,
        IReferenceModelCameraProvider,
        IReferenceModelPhysicsController,
        IReferenceModelTimelineProvider,
        ICharacterModelCollection
    {
        private KoikatsuStudioSceneInstance scene;
        private KoikatsuTimelinePlayer timeline;
        private bool physicsEnabled;

        public KoikatsuSceneReferenceModelInstance(
            string sourcePath,
            KoikatsuStudioSceneInstance scene,
            KoikatsuTimelineScene timelineData = null)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            var missing = scene.MissingItems.Count;
            DisplayName = $"{Path.GetFileNameWithoutExtension(sourcePath)} " +
                          $"({scene.ImportedItemCount} items, " +
                          $"{scene.ImportedCharacterCount} characters" +
                          (missing > 0 ? $", {missing} missing" : string.Empty) +
                          ")";
            if (timelineData != null)
            {
                timeline = KoikatsuTimelinePlayer.Attach(
                    scene.Root,
                    timelineData,
                    scene.ObjectsByTimelineIndex);
            }

            SetPhysicsEnabled(false);
        }

        public string DisplayName { get; }

        public GameObject Root => scene?.Root;

        public bool SupportsPhysics => KoikatsuPhysicsRuntime.Supports(Root);

        public bool PhysicsEnabled => SupportsPhysics && physicsEnabled;

        public IReferenceModelTimelineController Timeline => timeline;

        public IReadOnlyList<ICharacterModel> CharacterModels =>
            scene?.CharacterModels ?? Array.Empty<ICharacterModel>();

        public void SetPhysicsEnabled(bool enabled)
        {
            physicsEnabled = enabled && SupportsPhysics;
            KoikatsuPhysicsRuntime.SetEnabled(Root, physicsEnabled);
        }

        public bool TryGetCamera(out ReferenceModelCameraPose pose)
        {
            var camera = scene?.Scene?.Camera;
            if (camera == null)
            {
                pose = default;
                return false;
            }

            pose = new ReferenceModelCameraPose(
                camera.Target,
                camera.EulerAngles,
                camera.Distance,
                camera.FieldOfView);
            return true;
        }

        public void Dispose()
        {
            timeline = null;
            scene?.Dispose();
            scene = null;
            physicsEnabled = false;
        }
    }

    internal sealed class KoikatsuInstallation
    {
        private KoikatsuInstallation(string gameRoot)
        {
            GameRoot = gameRoot;
            AbdataRoot = Path.Combine(gameRoot, "abdata");
            ModsRoot = Path.Combine(gameRoot, "mods");
        }

        public string GameRoot { get; }

        public string AbdataRoot { get; }

        public string ModsRoot { get; }

        public static KoikatsuInstallation Find(string cardPath)
        {
            var configuredRoot = KoikatsuReferenceModelAdapter.GameRootOverride;
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                return Validate(configuredRoot);
            }

            var configuredRoots = KoikatsuAdapterConfiguration.GameRoots;
            for (var index = 0; index < configuredRoots.Count; index++)
            {
                configuredRoot = configuredRoots[index];
                if (string.IsNullOrWhiteSpace(configuredRoot))
                {
                    continue;
                }

                configuredRoot = Path.GetFullPath(configuredRoot.Trim());
                if (Directory.Exists(Path.Combine(configuredRoot, "abdata")))
                {
                    return new KoikatsuInstallation(configuredRoot);
                }
            }

            var directory = new DirectoryInfo(
                Path.GetDirectoryName(Path.GetFullPath(cardPath)) ??
                string.Empty);
            while (directory != null)
            {
                if (string.Equals(
                        directory.Name,
                        "UserData",
                        StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent != null)
                {
                    var candidate = directory.Parent.FullName;
                    if (Directory.Exists(Path.Combine(candidate, "abdata")))
                    {
                        return new KoikatsuInstallation(candidate);
                    }
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Koikatsu installation for this card. " +
                "Add the directory containing abdata, mods, and UserData to " +
                "KoikatsuAdapterConfig.json.");
        }

        private static KoikatsuInstallation Validate(string gameRoot)
        {
            gameRoot = Path.GetFullPath(gameRoot.Trim());
            var abdataRoot = Path.Combine(gameRoot, "abdata");
            if (!Directory.Exists(abdataRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Koikatsu abdata directory was not found: {abdataRoot}");
            }

            return new KoikatsuInstallation(gameRoot);
        }
    }

    internal static class KoikatsuAdapterConfiguration
    {
        private const string ResourceName = "KoikatsuAdapterConfig";
        private static IReadOnlyList<string> gameRoots;

        public static IReadOnlyList<string> GameRoots
        {
            get
            {
                if (gameRoots != null)
                {
                    return gameRoots;
                }

                var asset = Resources.Load<TextAsset>(ResourceName);
                if (asset == null)
                {
                    gameRoots = Array.Empty<string>();
                    return gameRoots;
                }

                var table = JsonUtility.FromJson<ConfigurationTable>(asset.text);
                gameRoots = Array.AsReadOnly(
                    table?.gameRoots ?? Array.Empty<string>());
                return gameRoots;
            }
        }

        [Serializable]
        private sealed class ConfigurationTable
        {
            public string[] gameRoots = Array.Empty<string>();
        }
    }
}
