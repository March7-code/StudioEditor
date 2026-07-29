using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.Fbx;
using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.Editor
{
    internal static class CascadeurQuickRigGenerator
    {
        private const string TemplatePath =
            "StudioEditor/Adapters/Koikatsu/Resources/Koikatsu.qrigcasc";
        private const string CharacterMarkerPrefix =
            "__CascadeurRigCharacter_";

        private static string templateJson;
        private static HashSet<string> canonicalJointNames;

        public static bool IsCanonicalJointName(string name)
        {
            EnsureTemplateLoaded();
            if (canonicalJointNames.Contains(name))
            {
                return true;
            }

            foreach (var canonicalName in canonicalJointNames)
            {
                if (IsExportedName(name, canonicalName))
                {
                    return true;
                }
            }

            return false;
        }

        public static string GetCharacterMarkerName(int index)
        {
            return $"{CharacterMarkerPrefix}{index + 1:0000}__";
        }

        public static QuickRigGenerationResult[] Generate(
            string fbxPath,
            IReadOnlyList<ICharacterModel> characters)
        {
            if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath))
            {
                throw new FileNotFoundException(
                    "The exported FBX could not be opened.",
                    fbxPath);
            }

            EnsureTemplateLoaded();
            var results = new QuickRigGenerationResult[characters.Count];
            using (var manager = FbxManager.Create())
            using (var settings = FbxIOSettings.Create(manager, Globals.IOSROOT))
            using (var importer = FbxImporter.Create(manager, "CascadeurBridge"))
            using (var scene = FbxScene.Create(manager, "CascadeurBridge"))
            {
                manager.SetIOSettings(settings);
                if (!importer.Initialize(fbxPath, -1, settings))
                {
                    throw new InvalidDataException(
                        "Autodesk FBX SDK could not initialize the exported FBX.");
                }

                if (!importer.Import(scene))
                {
                    throw new InvalidDataException(
                        "Autodesk FBX SDK could not read the exported FBX.");
                }

                var root = scene.GetRootNode();
                for (var index = 0; index < characters.Count; index++)
                {
                    try
                    {
                        results[index] = GenerateCharacterConfig(
                            fbxPath,
                            root,
                            index,
                            characters[index]?.DisplayName);
                    }
                    catch (Exception exception)
                    {
                        results[index] = new QuickRigGenerationResult(
                            index,
                            string.Empty,
                            0,
                            exception.Message);
                    }
                }
            }

            return results;
        }

        private static QuickRigGenerationResult GenerateCharacterConfig(
            string fbxPath,
            FbxNode sceneRoot,
            int index,
            string displayName)
        {
            var markerName = GetCharacterMarkerName(index);
            var marker = FindNode(sceneRoot, markerName);
            var hips = marker?.GetParent();
            if (hips == null ||
                !IsExportedName(GetNodeName(hips), "cf_j_hips"))
            {
                throw new InvalidDataException(
                    $"Could not locate the exported skeleton for character " +
                    $"{index + 1} ('{displayName}').");
            }

            var exportedNames = BuildExportedNameMap(hips);
            var missing = new List<string>();
            foreach (var canonicalName in canonicalJointNames)
            {
                if (!exportedNames.ContainsKey(canonicalName))
                {
                    missing.Add(canonicalName);
                }
            }

            if (missing.Count > 0)
            {
                missing.Sort(StringComparer.Ordinal);
                throw new InvalidDataException(
                    $"Character {index + 1} ('{displayName}') is missing " +
                    $"{missing.Count} Quick Rig joint(s): " +
                    string.Join(", ", missing));
            }

            var outputPath = BuildOutputPath(fbxPath, index, displayName);
            WriteConfig(outputPath, exportedNames);
            return new QuickRigGenerationResult(
                index,
                outputPath,
                exportedNames.Count,
                string.Empty);
        }

        private static Dictionary<string, string> BuildExportedNameMap(
            FbxNode hips)
        {
            var candidates = new Dictionary<string, ExportedNameCandidate>(
                StringComparer.Ordinal);
            CollectExportedNames(hips, 0, candidates);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in candidates)
            {
                result.Add(pair.Key, pair.Value.Name);
            }

            return result;
        }

        private static void CollectExportedNames(
            FbxNode node,
            int depth,
            IDictionary<string, ExportedNameCandidate> result)
        {
            if (node == null)
            {
                return;
            }

            var exportedName = GetNodeName(node);
            foreach (var canonicalName in canonicalJointNames)
            {
                if (!IsExportedName(exportedName, canonicalName))
                {
                    continue;
                }

                var exact = string.Equals(
                    exportedName,
                    canonicalName,
                    StringComparison.Ordinal);
                if (!result.TryGetValue(canonicalName, out var current) ||
                    exact && !current.IsExact ||
                    exact == current.IsExact && depth < current.Depth)
                {
                    result[canonicalName] = new ExportedNameCandidate(
                        exportedName,
                        depth,
                        exact);
                }
            }

            for (var index = 0; index < node.GetChildCount(); index++)
            {
                CollectExportedNames(node.GetChild(index), depth + 1, result);
            }
        }

        private static bool IsExportedName(
            string exportedName,
            string canonicalName)
        {
            if (string.Equals(
                    exportedName,
                    canonicalName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var suffixStart = canonicalName.Length + 1;
            if (exportedName == null ||
                exportedName.Length <= suffixStart ||
                !exportedName.StartsWith(
                    canonicalName + "_",
                    StringComparison.Ordinal))
            {
                return false;
            }

            for (var index = suffixStart; index < exportedName.Length; index++)
            {
                if (!char.IsDigit(exportedName[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static FbxNode FindNode(FbxNode node, string name)
        {
            if (node == null)
            {
                return null;
            }

            if (string.Equals(GetNodeName(node), name, StringComparison.Ordinal))
            {
                return node;
            }

            for (var index = 0; index < node.GetChildCount(); index++)
            {
                var match = FindNode(node.GetChild(index), name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static string GetNodeName(FbxNode node)
        {
            return node?.GetNameWithoutNameSpacePrefix() ?? string.Empty;
        }

        private static string BuildOutputPath(
            string fbxPath,
            int index,
            string displayName)
        {
            var directory = Path.GetDirectoryName(fbxPath) ?? string.Empty;
            var sceneName = Path.GetFileNameWithoutExtension(fbxPath);
            var characterName = SanitizeFileName(displayName);
            if (string.IsNullOrWhiteSpace(characterName))
            {
                characterName = "Character";
            }

            return Path.Combine(
                directory,
                $"{sceneName}.{index + 1:00}-{characterName}.qrigcasc");
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

            return value.Trim();
        }

        private static void WriteConfig(
            string outputPath,
            IReadOnlyDictionary<string, string> exportedNames)
        {
            using (var document = JsonDocument.Parse(templateJson))
            using (var stream = File.Create(outputPath))
            using (var writer = new Utf8JsonWriter(
                       stream,
                       new JsonWriterOptions { Indented = true }))
            {
                WriteAdjustedElement(writer, document.RootElement, exportedNames);
            }
        }

        private static void WriteAdjustedElement(
            Utf8JsonWriter writer,
            JsonElement element,
            IReadOnlyDictionary<string, string> exportedNames)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                element.WriteTo(writer);
                return;
            }

            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.NameEquals("Joint name"))
                {
                    writer.WriteStringValue(
                        exportedNames[property.Value.GetString()]);
                }
                else if (property.NameEquals("Joint path"))
                {
                    writer.WriteStartArray();
                    foreach (var value in property.Value.EnumerateArray())
                    {
                        writer.WriteStringValue(
                            exportedNames[value.GetString()]);
                    }

                    writer.WriteEndArray();
                }
                else if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    WriteAdjustedElement(writer, property.Value, exportedNames);
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    WriteAdjustedArray(writer, property.Value, exportedNames);
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        private static void WriteAdjustedArray(
            Utf8JsonWriter writer,
            JsonElement array,
            IReadOnlyDictionary<string, string> exportedNames)
        {
            writer.WriteStartArray();
            foreach (var element in array.EnumerateArray())
            {
                WriteAdjustedElement(writer, element, exportedNames);
            }

            writer.WriteEndArray();
        }

        private static void EnsureTemplateLoaded()
        {
            if (templateJson != null)
            {
                return;
            }

            var path = Path.Combine(
                Application.dataPath,
                TemplatePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The Koikatsu Quick Rig template is missing.",
                    path);
            }

            templateJson = File.ReadAllText(path);
            canonicalJointNames = new HashSet<string>(StringComparer.Ordinal);
            using (var document = JsonDocument.Parse(templateJson))
            {
                CollectCanonicalNames(
                    document.RootElement,
                    canonicalJointNames);
            }

            if (!canonicalJointNames.Contains("cf_j_hips"))
            {
                throw new InvalidDataException(
                    "The Koikatsu Quick Rig template contains no hips joint.");
            }
        }

        private static void CollectCanonicalNames(
            JsonElement element,
            ISet<string> result)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("Joint name"))
                    {
                        result.Add(property.Value.GetString());
                    }
                    else if (property.NameEquals("Joint path"))
                    {
                        foreach (var value in property.Value.EnumerateArray())
                        {
                            result.Add(value.GetString());
                        }
                    }
                    else
                    {
                        CollectCanonicalNames(property.Value, result);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in element.EnumerateArray())
                {
                    CollectCanonicalNames(value, result);
                }
            }
        }

        private readonly struct ExportedNameCandidate
        {
            public ExportedNameCandidate(string name, int depth, bool isExact)
            {
                Name = name;
                Depth = depth;
                IsExact = isExact;
            }

            public string Name { get; }

            public int Depth { get; }

            public bool IsExact { get; }
        }
    }

    internal readonly struct QuickRigGenerationResult
    {
        public QuickRigGenerationResult(
            int characterIndex,
            string filePath,
            int mappedJointCount,
            string error)
        {
            CharacterIndex = characterIndex;
            FilePath = filePath;
            MappedJointCount = mappedJointCount;
            Error = error;
        }

        public int CharacterIndex { get; }

        public string FilePath { get; }

        public int MappedJointCount { get; }

        public string Error { get; }

        public bool Succeeded => !string.IsNullOrEmpty(FilePath);
    }
}
