using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using StudioEditor.Characters;
using StudioEditor.ReferenceModels;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

namespace StudioEditor.Editor
{
    internal sealed class CascadeurBridgeWindow : EditorWindow
    {
        private const string LastManifestKey =
            "StudioEditor.CascadeurBridge.LastManifest";
        private const string ImportDirectory =
            "Assets/StudioEditor/CascadeurImports";

        private bool bakeTimeline = true;
        private int exportFrameRate = 30;
        private string manifestPath = string.Empty;
        private CascadeurExportManifest manifest;
        private GameObject importedModel;
        private AnimationClip[] importedClips = Array.Empty<AnimationClip>();
        private int targetCharacterIndex;
        private int sourceCharacterIndex;
        private int clipIndex;
        private string status =
            "Enter Play Mode and import a scene card to begin.";
        private Vector2 scroll;

        [MenuItem("Tools/Studio Editor/Cascadeur Bridge")]
        private static void Open()
        {
            GetWindow<CascadeurBridgeWindow>("Cascadeur Bridge");
        }

        private void OnEnable()
        {
            manifestPath = EditorPrefs.GetString(LastManifestKey, string.Empty);
            TryLoadManifest(manifestPath, false);
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawExport();
            EditorGUILayout.Space(12f);
            DrawImport();
            EditorGUILayout.Space(12f);
            DrawPlayback();
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawExport()
        {
            EditorGUILayout.LabelField("Unity to Cascadeur", EditorStyles.boldLabel);
            bakeTimeline = EditorGUILayout.Toggle(
                new GUIContent(
                    "Bake Timeline",
                    "Bake the current imported timeline into an FBX animation take."),
                bakeTimeline);
            using (new EditorGUI.DisabledScope(!bakeTimeline))
            {
                exportFrameRate = EditorGUILayout.IntSlider(
                    "Frame Rate",
                    exportFrameRate,
                    1,
                    120);
            }

            using (new EditorGUI.DisabledScope(!CanUseScene(out _)))
            {
                if (GUILayout.Button("Export Scene FBX...", GUILayout.Height(28f)))
                {
                    ExportScene();
                }
            }
        }

        private void DrawImport()
        {
            EditorGUILayout.LabelField("Cascadeur to Unity", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(manifestPath)
                        ? "No binding manifest selected"
                        : manifestPath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Manifest...", GUILayout.Width(90f)))
                {
                    SelectManifest();
                }
            }

            if (GUILayout.Button("Import Returned FBX...", GUILayout.Height(28f)))
            {
                ImportReturnedFbx();
            }

            var controller = FindSceneController();
            var characters = controller?.CharacterModels;
            var targetNames = BuildTargetNames(characters);
            targetCharacterIndex = PopupClamped(
                "Target Character",
                targetCharacterIndex,
                targetNames);

            var sourceNames = BuildSourceNames(manifest);
            sourceCharacterIndex = PopupClamped(
                "Source Character",
                sourceCharacterIndex,
                sourceNames);

            var clipNames = BuildClipNames(importedClips);
            clipIndex = PopupClamped(
                "Animation Clip",
                clipIndex,
                clipNames);

            var canBind = Application.isPlaying &&
                          characters != null && characters.Count > 0 &&
                          importedModel != null && importedClips.Length > 0;
            using (new EditorGUI.DisabledScope(!canBind))
            {
                if (GUILayout.Button("Apply Animation Layer", GUILayout.Height(28f)))
                {
                    ApplyAnimation(characters);
                }
            }
        }

        private void DrawPlayback()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            var player = GetSelectedPlayer();
            using (new EditorGUI.DisabledScope(player == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Play"))
                    {
                        player.Play();
                    }

                    if (GUILayout.Button("Pause"))
                    {
                        player.Pause();
                    }

                    if (GUILayout.Button("Stop"))
                    {
                        player.Stop();
                    }

                    if (GUILayout.Button("Remove"))
                    {
                        DestroyImmediate(player);
                        status = "Removed the Cascadeur animation layer.";
                        return;
                    }
                }

                if (player != null)
                {
                    var nextTime = EditorGUILayout.Slider(
                        "Time",
                        player.CurrentTime,
                        0f,
                        Mathf.Max(0.0001f, player.Duration));
                    if (!Mathf.Approximately(nextTime, player.CurrentTime))
                    {
                        player.Seek(nextTime);
                    }

                    player.Loop = EditorGUILayout.Toggle("Loop", player.Loop);
                    player.PlaybackSpeed = EditorGUILayout.Slider(
                        "Speed",
                        player.PlaybackSpeed,
                        0.05f,
                        4f);
                    EditorGUILayout.LabelField(
                        "Binding",
                        $"{player.ClipName}, {player.BoundBoneCount} bones");
                }
            }
        }

        private void ExportScene()
        {
            if (!CanUseScene(out var controller))
            {
                status = "No imported scene is available in Play Mode.";
                return;
            }

            var defaultName = SanitizeFileName(
                Path.GetFileNameWithoutExtension(controller.CurrentPath));
            if (string.IsNullOrEmpty(defaultName))
            {
                defaultName = "CascadeurScene";
            }

            var path = EditorUtility.SaveFilePanel(
                "Export Scene for Cascadeur",
                GetInitialDirectory(),
                defaultName,
                "fbx");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            IReferenceModelTimelineController timeline = null;
            if (controller.Current is IReferenceModelTimelineProvider provider)
            {
                timeline = provider.Timeline;
            }

            AnimationClip bakedClip = null;
            Animation temporaryAnimation = null;
            var createdAnimation = false;
            var previousTime = timeline?.CurrentTime ?? 0f;
            var wasPlaying = timeline?.IsPlaying ?? false;
            try
            {
                if (bakeTimeline && timeline != null && timeline.Duration > 0f)
                {
                    status = "Baking the imported timeline...";
                    timeline.Pause();
                    bakedClip = BakeTimeline(
                        controller,
                        timeline,
                        exportFrameRate);
                    temporaryAnimation = controller.Current.Root.GetComponent<
                        Animation>();
                    if (temporaryAnimation == null)
                    {
                        temporaryAnimation = controller.Current.Root.AddComponent<
                            Animation>();
                        createdAnimation = true;
                    }

                    temporaryAnimation.AddClip(bakedClip, bakedClip.name);
                }

                var options = new ExportModelOptions
                {
                    ExportFormat = ExportFormat.Binary,
                    ModelAnimIncludeOption = Include.ModelAndAnim,
                    AnimateSkinnedMesh = true,
                    UseMayaCompatibleNames = false,
                    ExportUnrendered = true,
                    KeepInstances = false,
                    EmbedTextures = false,
                    ObjectPosition = ObjectPosition.LocalCentered,
                };
                status = "Exporting the loaded scene to FBX...";
                var result = ModelExporter.ExportObject(
                    path,
                    controller.Current.Root,
                    options);
                if (string.IsNullOrEmpty(result))
                {
                    throw new InvalidOperationException("FBX export returned no file.");
                }

                manifest = BuildManifest(
                    controller,
                    exportFrameRate,
                    bakedClip != null ? timeline.Duration : 0f);
                manifestPath = Path.ChangeExtension(path, ".cascadeur.json");
                File.WriteAllText(
                    manifestPath,
                    JsonUtility.ToJson(manifest, true));
                EditorPrefs.SetString(LastManifestKey, manifestPath);
                status = $"Exported {controller.CharacterModels.Count} character(s) " +
                         $"and scene references to {path}.";
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception exception)
            {
                status = $"Export failed: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                if (temporaryAnimation != null && bakedClip != null)
                {
                    temporaryAnimation.RemoveClip(bakedClip);
                }

                if (createdAnimation && temporaryAnimation != null)
                {
                    DestroyImmediate(temporaryAnimation);
                }

                if (bakedClip != null)
                {
                    DestroyImmediate(bakedClip);
                }

                if (timeline != null)
                {
                    timeline.Seek(previousTime);
                    if (wasPlaying)
                    {
                        timeline.Play();
                    }
                }
            }
        }

        private void SelectManifest()
        {
            var path = EditorUtility.OpenFilePanel(
                "Select Cascadeur Binding Manifest",
                GetInitialDirectory(),
                "json");
            if (!string.IsNullOrEmpty(path))
            {
                TryLoadManifest(path, true);
            }
        }

        private void ImportReturnedFbx()
        {
            var sourcePath = EditorUtility.OpenFilePanel(
                "Import FBX Returned by Cascadeur",
                GetInitialDirectory(),
                "fbx");
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            try
            {
                EnsureImportDirectory();
                var fileName = SanitizeFileName(
                    Path.GetFileNameWithoutExtension(sourcePath));
                var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{ImportDirectory}/{fileName}.fbx");
                File.Copy(sourcePath, Path.GetFullPath(assetPath), true);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                if (AssetImporter.GetAtPath(assetPath) is ModelImporter importer)
                {
                    importer.importAnimation = true;
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.importBlendShapes = true;
                    importer.SaveAndReimport();
                }

                importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                var clips = new List<AnimationClip>();
                for (var index = 0; index < assets.Length; index++)
                {
                    if (assets[index] is AnimationClip value &&
                        !value.name.StartsWith("__preview__", StringComparison.Ordinal))
                    {
                        clips.Add(value);
                    }
                }

                importedClips = clips.ToArray();
                clipIndex = 0;
                status = importedModel == null
                    ? "Unity could not import the returned FBX model."
                    : importedClips.Length == 0
                        ? "The returned FBX contains no animation clips."
                        : $"Imported {importedClips.Length} animation clip(s) " +
                          $"from {sourcePath}.";
            }
            catch (Exception exception)
            {
                status = $"Import failed: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        private void ApplyAnimation(IReadOnlyList<ICharacterModel> characters)
        {
            var target = characters[Mathf.Clamp(
                targetCharacterIndex,
                0,
                characters.Count - 1)];
            var selectedClip = importedClips[Mathf.Clamp(
                clipIndex,
                0,
                importedClips.Length - 1)];
            var sourcePath = string.Empty;
            if (manifest?.characters != null && manifest.characters.Length > 0)
            {
                sourcePath = manifest.characters[Mathf.Clamp(
                    sourceCharacterIndex,
                    0,
                    manifest.characters.Length - 1)].rootPath;
            }

            try
            {
                var existing = target.Root.GetComponent<CascadeurAnimationPlayer>();
                if (existing != null)
                {
                    DestroyImmediate(existing);
                }

                var player = target.Root.AddComponent<CascadeurAnimationPlayer>();
                player.Initialize(
                    target,
                    selectedClip,
                    importedModel,
                    sourcePath,
                    GetAnimatedPaths(selectedClip));
                player.Play();
                status = $"Applied '{selectedClip.name}' to {target.DisplayName}: " +
                         $"{player.BoundBoneCount} animated bones matched.";
            }
            catch (Exception exception)
            {
                status = $"Animation binding failed: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        private CascadeurAnimationPlayer GetSelectedPlayer()
        {
            if (!Application.isPlaying)
            {
                return null;
            }

            var characters = FindSceneController()?.CharacterModels;
            if (characters == null || characters.Count == 0)
            {
                return null;
            }

            var target = characters[Mathf.Clamp(
                targetCharacterIndex,
                0,
                characters.Count - 1)];
            return target?.Root != null
                ? target.Root.GetComponent<CascadeurAnimationPlayer>()
                : null;
        }

        private static AnimationClip BakeTimeline(
            SceneContentController controller,
            IReferenceModelTimelineController timeline,
            int frameRate)
        {
            var transforms = CollectAnimatedCharacterTransforms(controller);
            if (transforms.Count == 0)
            {
                throw new InvalidOperationException(
                    "No character skeleton transforms are available to bake.");
            }

            var curves = new List<TransformCurves>(transforms.Count);
            foreach (var transform in transforms)
            {
                curves.Add(new TransformCurves(transform));
            }

            var duration = timeline.Duration;
            var frameCount = Mathf.Max(1, Mathf.CeilToInt(duration * frameRate));
            for (var frame = 0; frame <= frameCount; frame++)
            {
                var time = frame == frameCount
                    ? duration
                    : Mathf.Min(duration, frame / (float)frameRate);
                timeline.Seek(time);
                for (var index = 0; index < curves.Count; index++)
                {
                    curves[index].AddKey(time);
                }
            }

            var clip = new AnimationClip
            {
                name = "Koikatsu Timeline",
                frameRate = frameRate,
                legacy = true,
            };
            var sceneRoot = controller.Current.Root.transform;
            for (var index = 0; index < curves.Count; index++)
            {
                curves[index].WriteTo(clip, sceneRoot);
            }

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static HashSet<Transform> CollectAnimatedCharacterTransforms(
            SceneContentController controller)
        {
            var result = new HashSet<Transform>();
            var characters = controller.CharacterModels;
            for (var characterIndex = 0;
                 characterIndex < characters.Count;
                 characterIndex++)
            {
                var character = characters[characterIndex];
                if (character?.Root == null || character.Skeleton == null)
                {
                    continue;
                }

                result.Add(character.Root.transform);
                var renderers = character.Geometry?.AnatomyRenderers;
                if (renderers != null)
                {
                    for (var rendererIndex = 0;
                         rendererIndex < renderers.Count;
                         rendererIndex++)
                    {
                        var renderer = renderers[rendererIndex];
                        if (renderer == null)
                        {
                            continue;
                        }

                        AddAncestors(result, renderer.rootBone, character.Root.transform);
                        var bones = renderer.bones;
                        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                        {
                            AddAncestors(result, bones[boneIndex], character.Root.transform);
                        }
                    }
                }

                for (var boneIndex = 0;
                     boneIndex < character.Skeleton.BoneCount;
                     boneIndex++)
                {
                    var bone = character.Skeleton.Bones[boneIndex];
                    if (bone.SemanticBone.HasValue)
                    {
                        AddAncestors(
                            result,
                            bone.Transform,
                            character.Root.transform);
                    }
                }
            }

            return result;
        }

        private static void AddAncestors(
            ISet<Transform> result,
            Transform value,
            Transform stop)
        {
            var current = value;
            while (current != null)
            {
                result.Add(current);
                if (ReferenceEquals(current, stop))
                {
                    return;
                }

                current = current.parent;
            }
        }

        private static CascadeurExportManifest BuildManifest(
            SceneContentController controller,
            int frameRate,
            float duration)
        {
            var characters = controller.CharacterModels;
            var values = new CascadeurCharacterManifest[characters.Count];
            var root = controller.Current.Root.transform;
            for (var index = 0; index < characters.Count; index++)
            {
                var character = characters[index];
                values[index] = new CascadeurCharacterManifest
                {
                    index = index,
                    displayName = character.DisplayName,
                    rootPath = AnimationUtility.CalculateTransformPath(
                        character.Root.transform,
                        root),
                };
            }

            return new CascadeurExportManifest
            {
                formatVersion = 1,
                sourceScene = controller.CurrentPath,
                exportedUtc = DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                frameRate = frameRate,
                timelineDuration = duration,
                characters = values,
            };
        }

        private static Dictionary<string, CharacterPoseChannels>
            GetAnimatedPaths(AnimationClip clip)
        {
            var result = new Dictionary<string, CharacterPoseChannels>(
                StringComparer.Ordinal);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            for (var index = 0; index < bindings.Length; index++)
            {
                var binding = bindings[index];
                if (binding.type != typeof(Transform))
                {
                    continue;
                }

                var channels = CharacterPoseChannels.None;
                var property = binding.propertyName;
                if (property.IndexOf("Position", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    property.StartsWith("RootT", StringComparison.Ordinal))
                {
                    channels = CharacterPoseChannels.Position;
                }
                else if (property.IndexOf("Rotation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         property.IndexOf("Euler", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         property.StartsWith("RootQ", StringComparison.Ordinal))
                {
                    channels = CharacterPoseChannels.Rotation;
                }
                else if (property.IndexOf("Scale", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    channels = CharacterPoseChannels.Scale;
                }

                if (channels == CharacterPoseChannels.None)
                {
                    continue;
                }

                result.TryGetValue(binding.path, out var current);
                result[binding.path] = current | channels;
            }

            return result;
        }

        private bool TryLoadManifest(string path, bool report)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                var value = JsonUtility.FromJson<CascadeurExportManifest>(
                    File.ReadAllText(path));
                if (value == null || value.formatVersion != 1)
                {
                    throw new InvalidDataException(
                        "Unsupported Cascadeur manifest version.");
                }

                manifest = value;
                manifestPath = path;
                sourceCharacterIndex = 0;
                EditorPrefs.SetString(LastManifestKey, path);
                if (report)
                {
                    status = $"Loaded bindings for " +
                             $"{value.characters?.Length ?? 0} character(s).";
                }

                return true;
            }
            catch (Exception exception)
            {
                if (report)
                {
                    status = $"Manifest load failed: {exception.Message}";
                }

                return false;
            }
        }

        private static bool CanUseScene(out SceneContentController controller)
        {
            controller = FindSceneController();
            return Application.isPlaying &&
                   controller?.Current?.Root != null &&
                   controller.CharacterModels.Count > 0;
        }

        private static SceneContentController FindSceneController()
        {
            return FindFirstObjectByType<SceneContentController>();
        }

        private static string[] BuildTargetNames(
            IReadOnlyList<ICharacterModel> characters)
        {
            if (characters == null || characters.Count == 0)
            {
                return new[] { "No characters loaded" };
            }

            var result = new string[characters.Count];
            for (var index = 0; index < characters.Count; index++)
            {
                result[index] = $"{index + 1}. {characters[index].DisplayName}";
            }

            return result;
        }

        private static string[] BuildSourceNames(CascadeurExportManifest value)
        {
            if (value?.characters == null || value.characters.Length == 0)
            {
                return new[] { "Auto-detect skeleton" };
            }

            var result = new string[value.characters.Length];
            for (var index = 0; index < value.characters.Length; index++)
            {
                result[index] = $"{index + 1}. " +
                                value.characters[index].displayName;
            }

            return result;
        }

        private static string[] BuildClipNames(AnimationClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return new[] { "No animation clips imported" };
            }

            var result = new string[clips.Length];
            for (var index = 0; index < clips.Length; index++)
            {
                result[index] = $"{clips[index].name} ({clips[index].length:0.###}s)";
            }

            return result;
        }

        private static int PopupClamped(
            string label,
            int value,
            string[] options)
        {
            var count = options?.Length ?? 0;
            if (count == 0)
            {
                return 0;
            }

            return EditorGUILayout.Popup(
                label,
                Mathf.Clamp(value, 0, count - 1),
                options);
        }

        private static string GetInitialDirectory()
        {
            var previous = EditorPrefs.GetString(LastManifestKey, string.Empty);
            if (!string.IsNullOrEmpty(previous))
            {
                var directory = Path.GetDirectoryName(previous);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    return directory;
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        private static void EnsureImportDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/StudioEditor/CascadeurImports"))
            {
                AssetDatabase.CreateFolder("Assets/StudioEditor", "CascadeurImports");
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            foreach (var character in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(character, '_');
            }

            return value;
        }

        [Serializable]
        private sealed class CascadeurExportManifest
        {
            public int formatVersion;
            public string sourceScene;
            public string exportedUtc;
            public int frameRate;
            public float timelineDuration;
            public CascadeurCharacterManifest[] characters;
        }

        [Serializable]
        private sealed class CascadeurCharacterManifest
        {
            public int index;
            public string displayName;
            public string rootPath;
        }

        private sealed class TransformCurves
        {
            private readonly AnimationCurve positionX = new AnimationCurve();
            private readonly AnimationCurve positionY = new AnimationCurve();
            private readonly AnimationCurve positionZ = new AnimationCurve();
            private readonly AnimationCurve rotationX = new AnimationCurve();
            private readonly AnimationCurve rotationY = new AnimationCurve();
            private readonly AnimationCurve rotationZ = new AnimationCurve();
            private readonly AnimationCurve rotationW = new AnimationCurve();
            private Quaternion previousRotation;
            private bool hasRotation;

            public TransformCurves(Transform transform)
            {
                Transform = transform;
            }

            public Transform Transform { get; }

            public void AddKey(float time)
            {
                var position = Transform.localPosition;
                var rotation = Transform.localRotation;
                if (hasRotation && Quaternion.Dot(previousRotation, rotation) < 0f)
                {
                    rotation = new Quaternion(
                        -rotation.x,
                        -rotation.y,
                        -rotation.z,
                        -rotation.w);
                }

                previousRotation = rotation;
                hasRotation = true;
                positionX.AddKey(time, position.x);
                positionY.AddKey(time, position.y);
                positionZ.AddKey(time, position.z);
                rotationX.AddKey(time, rotation.x);
                rotationY.AddKey(time, rotation.y);
                rotationZ.AddKey(time, rotation.z);
                rotationW.AddKey(time, rotation.w);
            }

            public void WriteTo(AnimationClip clip, Transform root)
            {
                var path = AnimationUtility.CalculateTransformPath(Transform, root);
                WriteCurve(clip, path, "m_LocalPosition.x", positionX);
                WriteCurve(clip, path, "m_LocalPosition.y", positionY);
                WriteCurve(clip, path, "m_LocalPosition.z", positionZ);
                WriteCurve(clip, path, "m_LocalRotation.x", rotationX);
                WriteCurve(clip, path, "m_LocalRotation.y", rotationY);
                WriteCurve(clip, path, "m_LocalRotation.z", rotationZ);
                WriteCurve(clip, path, "m_LocalRotation.w", rotationW);
            }

            private static void WriteCurve(
                AnimationClip clip,
                string path,
                string property,
                AnimationCurve curve)
            {
                for (var index = 0; index < curve.length; index++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(
                        curve,
                        index,
                        AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(
                        curve,
                        index,
                        AnimationUtility.TangentMode.Linear);
                }

                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(
                        path,
                        typeof(Transform),
                        property),
                    curve);
            }
        }
    }
}
