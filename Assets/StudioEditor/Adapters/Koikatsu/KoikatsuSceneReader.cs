using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MessagePack;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    public static class KoikatsuSceneReader
    {
        private const string NewResolverId =
            "com.bepis.sideloader.universalautoresolver";
        private const string OldResolverId =
            "EC.Core.Sideloader.UniversalAutoResolver";
        private const string MaterialEditorId =
            "com.deathweasel.bepinex.materialeditor";
        private const int MaxObjectCount = 100000;
        private const int MaxCollectionCount = 1000000;
        private const int StoredCameraSlotCount = 10;

        private static readonly byte[] ExtendedMarker =
        {
            4,
            (byte)'K',
            (byte)'K',
            (byte)'E',
            (byte)'x',
        };

        public static KoikatsuScene Read(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Koikatsu Studio scene was not found.",
                    path);
            }

            var bytes = File.ReadAllBytes(path);
            var mapModGuid = ReadSideloaderMapGuid(bytes);
            var materialEditorTextures =
                ReadMaterialEditorSharedTextures(bytes);
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    SkipPng(reader);
                    var versionText = reader.ReadString();
                    if (!Version.TryParse(versionText, out var version))
                    {
                        throw new InvalidDataException(
                            $"Scene version '{versionText}' is invalid.");
                    }

                    var count = ReadCount(reader, MaxObjectCount, "scene objects");
                    var objects = new List<KoikatsuSceneObject>(count);
                    for (var index = 0; index < count; index++)
                    {
                        var key = reader.ReadInt32();
                        var kind = reader.ReadInt32();
                        objects.Add(ReadObject(reader, kind, version, 0, key));
                    }

                    AttachMaterialEditorSharedTextures(
                        objects,
                        materialEditorTextures);

                    var camera = ReadSceneCamera(
                        reader,
                        version,
                        mapModGuid,
                        out var map);
                    var storedCameraSlots = new List<KoikatsuSceneCamera>(
                        StoredCameraSlotCount);
                    for (var index = 0;
                         index < StoredCameraSlotCount;
                         index++)
                    {
                        storedCameraSlots.Add(ReadCameraData(reader, index + 1));
                    }

                    return new KoikatsuScene(
                        path,
                        version,
                        objects.AsReadOnly(),
                        map,
                        camera,
                        GetSavedCameraSlots(storedCameraSlots),
                        ReadSideloaderResolutions(bytes),
                        ReadSideloaderPatternResolutions(bytes));
                }
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException(
                    "Koikatsu Studio scene ended before all objects were read.",
                    exception);
            }
        }

        private static KoikatsuSceneObject ReadObject(
            BinaryReader reader,
            int kind,
            Version version,
            int depth,
            int dictionaryKey)
        {
            if (depth > 128)
            {
                throw new InvalidDataException(
                    "Koikatsu scene object nesting is too deep.");
            }

            var result = new KoikatsuSceneObject(
                dictionaryKey,
                (KoikatsuSceneObjectKind)kind,
                ReadObjectBase(reader));
            switch (kind)
            {
                case 0:
                    ReadCharacter(reader, version, depth, result);
                    break;
                case 1:
                    ReadItem(reader, version, depth, result);
                    break;
                case 2:
                    result.Light = ReadLight(reader);
                    break;
                case 3:
                    result.Name = reader.ReadString();
                    result.Children = ReadChildren(reader, version, depth);
                    break;
                case 4:
                    result.Name = reader.ReadString();
                    result.Children = ReadChildren(reader, version, depth);
                    ReadRoute(reader, version, depth, result);
                    break;
                case 5:
                    result.Name = reader.ReadString();
                    result.Active = reader.ReadBoolean();
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported Koikatsu scene object kind {kind}.");
            }

            return result;
        }

        private static KoikatsuSceneObjectBase ReadObjectBase(BinaryReader reader)
        {
            return new KoikatsuSceneObjectBase(
                reader.ReadInt32(),
                ReadVector3(reader),
                ReadVector3(reader),
                ReadVector3(reader),
                reader.ReadInt32(),
                reader.ReadBoolean());
        }

        private static void ReadItem(
            BinaryReader reader,
            Version version,
            int depth,
            KoikatsuSceneObject result)
        {
            var item = new KoikatsuSceneItem(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());
            item.AnimeSpeed = reader.ReadSingle();

            var colorCount = version >= new Version(0, 0, 3) ? 8 : 7;
            item.Colors = new Color[8];
            for (var index = 0; index < colorCount; index++)
            {
                item.Colors[index] = ReadColor(reader.ReadString());
            }

            for (var index = colorCount; index < item.Colors.Length; index++)
            {
                item.Colors[index] = Color.white;
            }

            item.Patterns = new KoikatsuScenePattern[3];
            for (var index = 0; index < item.Patterns.Length; index++)
            {
                item.Patterns[index] = ReadPattern(reader);
            }

            item.Alpha = reader.ReadSingle();
            if (version >= new Version(0, 0, 4))
            {
                item.LineColor = ReadColor(reader.ReadString());
                item.LineWidth = reader.ReadSingle();
            }

            if (version >= new Version(0, 0, 7))
            {
                item.EmissionColor = ReadColor(reader.ReadString());
                item.EmissionPower = reader.ReadSingle();
                item.LightCancel = reader.ReadSingle();
            }

            if (version >= new Version(0, 0, 6))
            {
                item.Panel = ReadPattern(reader);
            }

            item.EnableFK = reader.ReadBoolean();
            var boneCount = ReadCount(reader, MaxCollectionCount, "item bones");
            var itemBones = new Dictionary<string, KoikatsuSceneBone>(
                StringComparer.Ordinal);
            for (var index = 0; index < boneCount; index++)
            {
                itemBones[reader.ReadString()] = ReadBone(reader);
            }

            item.Bones = itemBones;
            item.EnableDynamicBone = version < new Version(1, 0, 1) ||
                                     reader.ReadBoolean();
            item.AnimeNormalizedTime = reader.ReadSingle();
            result.Children = ReadChildren(reader, version, depth);
            result.Item = item;
        }

        private static void ReadCharacter(
            BinaryReader reader,
            Version version,
            int depth,
            KoikatsuSceneObject result)
        {
            var character = new KoikatsuSceneCharacter(
                reader.ReadInt32(),
                KoikatsuCardReader.ReadEmbedded(reader, string.Empty));

            var bones = ReadCount(reader, MaxCollectionCount, "character bones");
            var characterBones = new Dictionary<int, KoikatsuSceneBone>(bones);
            for (var index = 0; index < bones; index++)
            {
                characterBones[reader.ReadInt32()] = ReadBone(reader);
            }
            character.Bones = characterBones;

            var ikTargets = ReadCount(
                reader,
                MaxCollectionCount,
                "character IK targets");
            var characterIkTargets =
                new Dictionary<int, KoikatsuSceneBone>(ikTargets);
            for (var index = 0; index < ikTargets; index++)
            {
                characterIkTargets[reader.ReadInt32()] = ReadBone(reader);
            }
            character.IkTargets = characterIkTargets;

            var childGroups = ReadCount(
                reader,
                MaxCollectionCount,
                "character child groups");
            var children = new List<KoikatsuSceneObject>();
            for (var index = 0; index < childGroups; index++)
            {
                reader.ReadInt32();
                children.AddRange(ReadChildren(reader, version, depth));
            }

            character.KinematicMode = reader.ReadInt32();
            character.AnimationGroup = reader.ReadInt32();
            character.AnimationCategory = reader.ReadInt32();
            character.AnimationNo = reader.ReadInt32();
            character.LeftHandPattern = reader.ReadInt32();
            character.RightHandPattern = reader.ReadInt32();
            reader.ReadSingle();
            ReadBytesExact(reader, 5, "character moisture");
            character.MouthOpen = reader.ReadSingle();
            character.LipSync = reader.ReadBoolean();
            SkipLookAtTarget(reader);
            character.EnableIK = reader.ReadBoolean();
            character.ActiveIK = ReadBooleans(reader, 5);
            character.EnableFK = reader.ReadBoolean();
            character.ActiveFK = ReadBooleans(reader, 7);
            character.ExpressionEnabled = ReadBooleans(
                reader,
                version < new Version(0, 0, 9) ? 4 : 8);
            character.AnimationSpeed = reader.ReadSingle();
            character.AnimationPattern = reader.ReadSingle();
            character.AnimationOptionVisible = reader.ReadBoolean();
            character.AnimationForceLoop = reader.ReadBoolean();

            var voices = ReadCount(reader, MaxCollectionCount, "character voices");
            for (var index = 0; index < voices; index++)
            {
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
            }

            reader.ReadInt32();
            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadBoolean();
            ReadColor(reader.ReadString());
            character.AnimationOptionParam1 = reader.ReadSingle();
            character.AnimationOptionParam2 = reader.ReadSingle();
            SkipLengthPrefixedBytes(reader, "character neck data");
            character.EyeLookData = ReadLengthPrefixedBytes(
                reader,
                "character eye data");
            character.AnimationNormalizedTime = reader.ReadSingle();
            SkipIntDictionary(reader, "character access groups");
            SkipIntDictionary(reader, "character access numbers");
            result.Children = children.AsReadOnly();
            result.Character = character;
        }

        private static void ReadRoute(
            BinaryReader reader,
            Version version,
            int depth,
            KoikatsuSceneObject result)
        {
            var points = ReadCount(reader, MaxCollectionCount, "route points");
            for (var index = 0; index < points; index++)
            {
                reader.ReadInt32();
                SkipChangeAmount(reader);
                reader.ReadSingle();
                reader.ReadInt32();
                if (version == new Version(1, 0, 3))
                {
                    reader.ReadBoolean();
                }

                if (version >= new Version(1, 0, 4, 1))
                {
                    reader.ReadInt32();
                    SkipChangeAmountWithKey(reader);
                }

                if (version >= new Version(1, 0, 4, 2))
                {
                    reader.ReadBoolean();
                }
            }

            if (version >= new Version(1, 0, 3))
            {
                result.Active = reader.ReadBoolean();
                reader.ReadBoolean();
                reader.ReadBoolean();
            }

            if (version >= new Version(1, 0, 4))
            {
                reader.ReadInt32();
            }

            if (version >= new Version(1, 0, 4, 1))
            {
                ReadColor(reader.ReadString());
            }
        }

        private static IReadOnlyList<KoikatsuSceneObject> ReadChildren(
            BinaryReader reader,
            Version version,
            int depth)
        {
            var count = ReadCount(reader, MaxCollectionCount, "child objects");
            var children = new List<KoikatsuSceneObject>(count);
            for (var index = 0; index < count; index++)
            {
                var kind = reader.ReadInt32();
                children.Add(ReadObject(reader, kind, version, depth + 1, -1));
            }

            return children.AsReadOnly();
        }

        private static KoikatsuSceneLight ReadLight(BinaryReader reader)
        {
            return new KoikatsuSceneLight(
                reader.ReadInt32(),
                ReadColor(reader),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean());
        }

        private static KoikatsuScenePattern ReadPattern(BinaryReader reader)
        {
            return new KoikatsuScenePattern(
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadBoolean(),
                ReadVector4(reader.ReadString()),
                reader.ReadSingle());
        }

        private static KoikatsuSceneBone ReadBone(BinaryReader reader)
        {
            return new KoikatsuSceneBone(
                reader.ReadInt32(),
                ReadVector3(reader),
                ReadVector3(reader),
                ReadVector3(reader));
        }

        private static void SkipBone(BinaryReader reader)
        {
            reader.ReadInt32();
            SkipChangeAmount(reader);
        }

        private static void SkipLookAtTarget(BinaryReader reader)
        {
            reader.ReadInt32();
            SkipChangeAmount(reader);
        }

        private static void SkipChangeAmount(BinaryReader reader)
        {
            ReadVector3(reader);
            ReadVector3(reader);
            ReadVector3(reader);
        }

        private static void SkipChangeAmountWithKey(BinaryReader reader)
        {
            reader.ReadInt32();
            SkipChangeAmount(reader);
            reader.ReadBoolean();
        }

        private static KoikatsuSceneCamera ReadSceneCamera(
            BinaryReader reader,
            Version version,
            string mapModGuid,
            out KoikatsuSceneMap map)
        {
            map = new KoikatsuSceneMap(
                reader.ReadInt32(),
                ReadVector3(reader),
                ReadVector3(reader),
                ReadVector3(reader),
                mapModGuid);
            reader.ReadInt32();
            reader.ReadBoolean();
            reader.ReadInt32();

            if (version >= new Version(0, 0, 2))
            {
                reader.ReadSingle();
            }
            else
            {
                reader.ReadBoolean();
                reader.ReadSingle();
                reader.ReadString();
            }

            if (version >= new Version(0, 0, 2))
            {
                reader.ReadBoolean();
                reader.ReadString();
                reader.ReadSingle();
            }

            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadSingle();
            if (version >= new Version(0, 0, 2))
            {
                reader.ReadSingle();
            }
            else
            {
                reader.ReadBoolean();
            }

            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadSingle();
            reader.ReadBoolean();
            if (version <= new Version(0, 0, 1))
            {
                reader.ReadSingle();
            }

            reader.ReadBoolean();
            if (version >= new Version(0, 0, 2))
            {
                reader.ReadString();
                reader.ReadSingle();
                reader.ReadSingle();
            }

            reader.ReadBoolean();
            if (version >= new Version(0, 0, 2))
            {
                reader.ReadString();
                reader.ReadString();
            }

            if (version >= new Version(0, 0, 4))
            {
                reader.ReadInt32();
            }

            if (version >= new Version(0, 0, 2))
            {
                reader.ReadBoolean();
            }

            if (version >= new Version(0, 0, 4))
            {
                reader.ReadBoolean();
                reader.ReadBoolean();
                reader.ReadSingle();
                reader.ReadString();
            }

            if (version >= new Version(0, 0, 5))
            {
                reader.ReadSingle();
                reader.ReadInt32();
                reader.ReadSingle();
            }

            return ReadCameraData(reader);
        }

        private static KoikatsuSceneCamera ReadCameraData(
            BinaryReader reader,
            int slotNumber = 0)
        {
            var cameraVersion = reader.ReadInt32();
            var target = ReadVector3(reader);
            var rotation = ReadVector3(reader);
            var distance = cameraVersion == 1
                ? new Vector3(0f, 0f, reader.ReadSingle())
                : ReadVector3(reader);
            return new KoikatsuSceneCamera(
                target,
                rotation,
                distance,
                reader.ReadSingle(),
                slotNumber);
        }

        private static IReadOnlyList<KoikatsuSceneCamera> GetSavedCameraSlots(
            IReadOnlyList<KoikatsuSceneCamera> storedSlots)
        {
            if (storedSlots == null || storedSlots.Count == 0)
            {
                return Array.Empty<KoikatsuSceneCamera>();
            }

            var repeatedPose = storedSlots[0];
            var repeatedCount = 1;
            for (var candidateIndex = 0;
                 candidateIndex < storedSlots.Count;
                 candidateIndex++)
            {
                var count = 0;
                for (var index = 0; index < storedSlots.Count; index++)
                {
                    if (CameraPosesEqual(
                            storedSlots[candidateIndex],
                            storedSlots[index]))
                    {
                        count++;
                    }
                }

                if (count > repeatedCount)
                {
                    repeatedPose = storedSlots[candidateIndex];
                    repeatedCount = count;
                }
            }

            var saved = new List<KoikatsuSceneCamera>(storedSlots.Count);
            for (var index = 0; index < storedSlots.Count; index++)
            {
                if (repeatedCount < 2 ||
                    !CameraPosesEqual(storedSlots[index], repeatedPose))
                {
                    saved.Add(storedSlots[index]);
                }
            }

            return saved.AsReadOnly();
        }

        internal static bool CameraPosesEqual(
            KoikatsuSceneCamera left,
            KoikatsuSceneCamera right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            var rotationDelta = new Vector3(
                Mathf.DeltaAngle(
                    left.EulerAngles.x,
                    right.EulerAngles.x),
                Mathf.DeltaAngle(
                    left.EulerAngles.y,
                    right.EulerAngles.y),
                Mathf.DeltaAngle(
                    left.EulerAngles.z,
                    right.EulerAngles.z));
            return Vector3.SqrMagnitude(left.Target - right.Target) < 0.000001f &&
                   Vector3.SqrMagnitude(rotationDelta) < 0.000001f &&
                   Vector3.SqrMagnitude(
                       left.Distance - right.Distance) < 0.000001f &&
                   Mathf.Abs(left.FieldOfView - right.FieldOfView) < 0.0001f;
        }

        private static void SkipIntDictionary(BinaryReader reader, string context)
        {
            var count = ReadCount(reader, MaxCollectionCount, context);
            SkipBytes(reader, checked(count * sizeof(int) * 2L), context);
        }

        private static void SkipBooleans(BinaryReader reader, int count)
        {
            SkipBytes(reader, count, "boolean array");
        }

        private static IReadOnlyList<bool> ReadBooleans(
            BinaryReader reader,
            int count)
        {
            var values = new bool[count];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = reader.ReadBoolean();
            }

            return Array.AsReadOnly(values);
        }

        private static void SkipLengthPrefixedBytes(
            BinaryReader reader,
            string context)
        {
            var length = reader.ReadInt32();
            SkipBytes(reader, length, context);
        }

        private static byte[] ReadLengthPrefixedBytes(
            BinaryReader reader,
            string context)
        {
            var length = reader.ReadInt32();
            return ReadBytesExact(reader, length, context);
        }

        private static void SkipBytes(
            BinaryReader reader,
            long length,
            string context)
        {
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException($"{context} length is invalid: {length}.");
            }

            reader.BaseStream.Seek(length, SeekOrigin.Current);
        }

        private static byte[] ReadBytesExact(
            BinaryReader reader,
            long length,
            string context)
        {
            if (length < 0 || length > int.MaxValue)
            {
                throw new InvalidDataException($"{context} length is invalid: {length}.");
            }

            var bytes = reader.ReadBytes((int)length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException(
                    $"Could not read {context}; expected {length} bytes.");
            }

            return bytes;
        }

        private static int ReadCount(
            BinaryReader reader,
            int maximum,
            string context)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > maximum)
            {
                throw new InvalidDataException($"{context} count is invalid: {count}.");
            }

            return count;
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        private static Vector4 ReadVector4(string json)
        {
            try
            {
                return JsonUtility.FromJson<Vector4>(json);
            }
            catch
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }
        }

        private static Color ReadColor(string json)
        {
            try
            {
                return JsonUtility.FromJson<Color>(json);
            }
            catch
            {
                return Color.white;
            }
        }

        private static Color ReadColor(BinaryReader reader)
        {
            return new Color(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        private static void SkipPng(BinaryReader reader)
        {
            var signature = reader.ReadBytes(8);
            var expected = new byte[]
            {
                0x89, 0x50, 0x4e, 0x47,
                0x0d, 0x0a, 0x1a, 0x0a,
            };
            if (!signature.SequenceEqual(expected))
            {
                throw new InvalidDataException("Scene does not start with a PNG.");
            }

            while (true)
            {
                var lengthBytes = reader.ReadBytes(4);
                if (lengthBytes.Length != 4)
                {
                    throw new EndOfStreamException();
                }

                var length = ((uint)lengthBytes[0] << 24) |
                             ((uint)lengthBytes[1] << 16) |
                             ((uint)lengthBytes[2] << 8) |
                             lengthBytes[3];
                var type = Encoding.ASCII.GetString(reader.ReadBytes(4));
                SkipBytes(reader, length + 4L, $"PNG chunk '{type}'");
                if (type == "IEND")
                {
                    return;
                }
            }
        }

        private static IReadOnlyList<KoikatsuStudioResolution>
            ReadSideloaderResolutions(byte[] fileData)
        {
            if (!TryReadExtendedPayload(fileData, out var payload))
            {
                return Array.Empty<KoikatsuStudioResolution>();
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    payload);
                if (plugins == null ||
                    (!plugins.TryGetValue(NewResolverId, out var plugin) &&
                     !plugins.TryGetValue(OldResolverId, out plugin)) ||
                    plugin?.Data == null ||
                    !plugin.Data.TryGetValue("itemInfo", out var raw))
                {
                    return Array.Empty<KoikatsuStudioResolution>();
                }

                var result = new List<KoikatsuStudioResolution>();
                if (!(raw is IEnumerable collection) || raw is byte[])
                {
                    return result.AsReadOnly();
                }

                foreach (var item in collection)
                {
                    if (!(item is byte[] bytes))
                    {
                        continue;
                    }

                    var dto = MessagePackSerializer.Deserialize<
                        KoikatsuStudioResolutionDto>(bytes);
                    if (dto == null || string.IsNullOrWhiteSpace(dto.Guid))
                    {
                        continue;
                    }

                    result.Add(new KoikatsuStudioResolution(
                        dto.Guid,
                        dto.Slot,
                        dto.LocalSlot,
                        dto.DicKey,
                        dto.ObjectOrder,
                        dto.ResolveItem,
                        dto.Group,
                        dto.Category,
                        dto.Name));
                }

                return result.AsReadOnly();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu Studio Sideloader metadata: " +
                    exception.Message);
                return Array.Empty<KoikatsuStudioResolution>();
            }
        }

        private static string ReadSideloaderMapGuid(byte[] fileData)
        {
            if (!TryReadExtendedPayload(fileData, out var payload))
            {
                return string.Empty;
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    payload);
                if (plugins == null ||
                    (!plugins.TryGetValue(NewResolverId, out var plugin) &&
                     !plugins.TryGetValue(OldResolverId, out plugin)) ||
                    plugin?.Data == null ||
                    !plugin.Data.TryGetValue("mapInfoGUID", out var raw) ||
                    !(raw is string guid))
                {
                    return string.Empty;
                }

                return guid.Trim();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu Studio map metadata: " +
                    exception.Message);
                return string.Empty;
            }
        }

        private static IReadOnlyDictionary<string, byte[]>
            ReadMaterialEditorSharedTextures(byte[] fileData)
        {
            var result = new Dictionary<string, byte[]>(
                StringComparer.OrdinalIgnoreCase);
            if (!TryReadExtendedPayload(fileData, out var payload))
            {
                return result;
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    payload);
                if (plugins == null ||
                    !plugins.TryGetValue(MaterialEditorId, out var plugin) ||
                    plugin?.Data == null ||
                    !plugin.Data.TryGetValue(
                        "DEDUPED_TextureDictionary_DATA",
                        out var raw) ||
                    !(raw is byte[] bytes))
                {
                    return result;
                }

                var textures = MessagePackSerializer.Deserialize<
                    Dictionary<string, byte[]>>(bytes);
                if (textures == null)
                {
                    return result;
                }

                foreach (var pair in textures)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) &&
                        pair.Value != null && pair.Value.Length > 0)
                    {
                        result[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu Studio MaterialEditor textures: " +
                    exception.Message);
            }

            return result;
        }

        private static void AttachMaterialEditorSharedTextures(
            IReadOnlyList<KoikatsuSceneObject> objects,
            IReadOnlyDictionary<string, byte[]> textures)
        {
            if (objects == null || textures == null || textures.Count == 0)
            {
                return;
            }

            for (var index = 0; index < objects.Count; index++)
            {
                var item = objects[index];
                item?.Character?.Card.AttachMaterialEditorSharedTextures(
                    textures);
                AttachMaterialEditorSharedTextures(item?.Children, textures);
            }
        }

        private static IReadOnlyList<KoikatsuStudioPatternResolution>
            ReadSideloaderPatternResolutions(byte[] fileData)
        {
            if (!TryReadExtendedPayload(fileData, out var payload))
            {
                return Array.Empty<KoikatsuStudioPatternResolution>();
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    payload);
                if (plugins == null ||
                    (!plugins.TryGetValue(NewResolverId, out var plugin) &&
                     !plugins.TryGetValue(OldResolverId, out plugin)) ||
                    plugin?.Data == null ||
                    !plugin.Data.TryGetValue("patternInfo", out var raw))
                {
                    return Array.Empty<KoikatsuStudioPatternResolution>();
                }

                var result = new List<KoikatsuStudioPatternResolution>();
                if (!(raw is IEnumerable collection) || raw is byte[])
                {
                    return result.AsReadOnly();
                }

                foreach (var item in collection)
                {
                    if (!(item is byte[] bytes))
                    {
                        continue;
                    }

                    var dto = MessagePackSerializer.Deserialize<
                        KoikatsuStudioPatternResolutionDto>(bytes);
                    if (dto?.ObjectPatternInfo == null)
                    {
                        continue;
                    }

                    foreach (var pair in dto.ObjectPatternInfo)
                    {
                        var pattern = pair.Value;
                        if (pattern == null ||
                            string.IsNullOrWhiteSpace(pattern.Guid))
                        {
                            continue;
                        }

                        result.Add(new KoikatsuStudioPatternResolution(
                            pattern.Guid,
                            pattern.Slot,
                            pattern.LocalSlot,
                            dto.DicKey,
                            dto.ObjectOrder,
                            pair.Key,
                            pattern.Name));
                    }
                }

                return result.AsReadOnly();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu Studio pattern metadata: " +
                    exception.Message);
                return Array.Empty<KoikatsuStudioPatternResolution>();
            }
        }

        private static bool TryReadExtendedPayload(
            byte[] fileData,
            out byte[] payload)
        {
            const int headerSize = 13;
            for (var index = fileData.Length - headerSize; index >= 0; index--)
            {
                if (!Matches(fileData, index, ExtendedMarker))
                {
                    continue;
                }

                var length = fileData[index + 9] |
                             fileData[index + 10] << 8 |
                             fileData[index + 11] << 16 |
                             fileData[index + 12] << 24;
                if (length < 0 || index + headerSize + (long)length != fileData.Length)
                {
                    continue;
                }

                payload = new byte[length];
                Buffer.BlockCopy(fileData, index + headerSize, payload, 0, length);
                return true;
            }

            payload = null;
            return false;
        }

        private static bool Matches(byte[] source, int offset, byte[] value)
        {
            if (offset < 0 || offset > source.Length - value.Length)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (source[offset + index] != value[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public enum KoikatsuSceneObjectKind
    {
        Character = 0,
        Item = 1,
        Light = 2,
        Folder = 3,
        Route = 4,
        Camera = 5,
    }

    public sealed class KoikatsuScene
    {
        internal KoikatsuScene(
            string sourcePath,
            Version version,
            IReadOnlyList<KoikatsuSceneObject> objects,
            KoikatsuSceneMap map,
            KoikatsuSceneCamera camera,
            IReadOnlyList<KoikatsuSceneCamera> cameraSlots,
            IReadOnlyList<KoikatsuStudioResolution> resolutions,
            IReadOnlyList<KoikatsuStudioPatternResolution> patternResolutions)
        {
            SourcePath = sourcePath;
            Version = version;
            Objects = objects;
            Map = map;
            Camera = camera;
            CameraSlots = cameraSlots ?? Array.Empty<KoikatsuSceneCamera>();
            SideloaderResolutions = resolutions;
            SideloaderPatternResolutions = patternResolutions;
        }

        public string SourcePath { get; }

        public Version Version { get; }

        public IReadOnlyList<KoikatsuSceneObject> Objects { get; }

        public KoikatsuSceneMap Map { get; }

        public KoikatsuSceneCamera Camera { get; }

        public IReadOnlyList<KoikatsuSceneCamera> CameraSlots { get; }

        public IReadOnlyList<KoikatsuStudioResolution> SideloaderResolutions { get; }

        public IReadOnlyList<KoikatsuStudioPatternResolution>
            SideloaderPatternResolutions { get; }

        public IEnumerable<KoikatsuSceneObject> Walk()
        {
            foreach (var item in Objects)
            {
                foreach (var nested in Walk(item))
                {
                    yield return nested;
                }
            }
        }

        public bool TryResolveItem(
            KoikatsuSceneObject itemObject,
            out KoikatsuStudioResolution resolution)
        {
            resolution = null;
            if (itemObject?.Item == null)
            {
                return false;
            }

            for (var index = 0; index < SideloaderResolutions.Count; index++)
            {
                var candidate = SideloaderResolutions[index];
                if (candidate.DicKey == itemObject.Base.DicKey ||
                    (candidate.Slot == itemObject.Item.No &&
                     candidate.Group == itemObject.Item.Group &&
                     candidate.Category == itemObject.Item.Category))
                {
                    resolution = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryResolveCharacterAnimation(
            KoikatsuSceneObject characterObject,
            out KoikatsuStudioResolution resolution)
        {
            resolution = null;
            var character = characterObject?.Character;
            if (character == null)
            {
                return false;
            }

            for (var index = 0; index < SideloaderResolutions.Count; index++)
            {
                var candidate = SideloaderResolutions[index];
                if ((candidate.DicKey == characterObject.Base.DicKey ||
                     candidate.DicKey == characterObject.DictionaryKey) &&
                    candidate.Group == character.AnimationGroup &&
                    candidate.Category == character.AnimationCategory)
                {
                    resolution = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryResolvePattern(
            KoikatsuSceneObject itemObject,
            int channel,
            out KoikatsuStudioPatternResolution resolution)
        {
            resolution = null;
            if (itemObject?.Item == null || channel < 0 || channel >= 3)
            {
                return false;
            }

            for (var index = 0;
                 index < SideloaderPatternResolutions.Count;
                 index++)
            {
                var candidate = SideloaderPatternResolutions[index];
                if (candidate.Channel == channel &&
                    (candidate.DicKey == itemObject.Base.DicKey ||
                     candidate.DicKey == itemObject.DictionaryKey))
                {
                    resolution = candidate;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<KoikatsuSceneObject> Walk(
            KoikatsuSceneObject source)
        {
            yield return source;
            if (source.Children == null)
            {
                yield break;
            }

            for (var index = 0; index < source.Children.Count; index++)
            {
                foreach (var nested in Walk(source.Children[index]))
                {
                    yield return nested;
                }
            }
        }
    }

    public sealed class KoikatsuSceneMap
    {
        internal KoikatsuSceneMap(
            int id,
            Vector3 position,
            Vector3 eulerAngles,
            Vector3 scale,
            string modGuid)
        {
            Id = id;
            Position = position;
            EulerAngles = eulerAngles;
            Scale = scale;
            ModGuid = modGuid ?? string.Empty;
        }

        public int Id { get; }

        public Vector3 Position { get; }

        public Vector3 EulerAngles { get; }

        public Vector3 Scale { get; }

        public string ModGuid { get; }
    }

    public sealed class KoikatsuSceneCamera
    {
        internal KoikatsuSceneCamera(
            Vector3 target,
            Vector3 eulerAngles,
            Vector3 distance,
            float fieldOfView,
            int slotNumber = 0)
        {
            Target = target;
            EulerAngles = eulerAngles;
            Distance = distance;
            FieldOfView = fieldOfView;
            SlotNumber = slotNumber;
        }

        public Vector3 Target { get; }

        public Vector3 EulerAngles { get; }

        public Vector3 Distance { get; }

        public float FieldOfView { get; }

        public int SlotNumber { get; }
    }

    public sealed class KoikatsuSceneObject
    {
        internal KoikatsuSceneObject(
            int dictionaryKey,
            KoikatsuSceneObjectKind kind,
            KoikatsuSceneObjectBase @base)
        {
            DictionaryKey = dictionaryKey;
            Kind = kind;
            Base = @base;
            Children = Array.Empty<KoikatsuSceneObject>();
        }

        public int DictionaryKey { get; }

        public KoikatsuSceneObjectKind Kind { get; }

        public KoikatsuSceneObjectBase Base { get; }

        public string Name { get; internal set; }

        public bool Active { get; internal set; }

        public KoikatsuSceneItem Item { get; internal set; }

        public KoikatsuSceneCharacter Character { get; internal set; }

        public KoikatsuSceneLight Light { get; internal set; }

        public IReadOnlyList<KoikatsuSceneObject> Children { get; internal set; }
    }

    public sealed class KoikatsuSceneCharacter
    {
        internal KoikatsuSceneCharacter(int sex, KoikatsuCard card)
        {
            Sex = sex;
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Bones = new Dictionary<int, KoikatsuSceneBone>();
            IkTargets = new Dictionary<int, KoikatsuSceneBone>();
            ActiveIK = Array.Empty<bool>();
            ActiveFK = Array.Empty<bool>();
            ExpressionEnabled = Array.Empty<bool>();
            EyeLookData = Array.Empty<byte>();
        }

        public int Sex { get; }

        public KoikatsuCard Card { get; internal set; }

        public int ActiveCoordinateIndex => Card.ActiveCoordinateIndex;

        public IReadOnlyDictionary<int, KoikatsuSceneBone> Bones
        {
            get;
            internal set;
        }

        public IReadOnlyDictionary<int, KoikatsuSceneBone> IkTargets
        {
            get;
            internal set;
        }

        public int KinematicMode { get; internal set; }

        public bool EnableIK { get; internal set; }

        public IReadOnlyList<bool> ActiveIK { get; internal set; }

        public bool EnableFK { get; internal set; }

        public IReadOnlyList<bool> ActiveFK { get; internal set; }

        public int AnimationGroup { get; internal set; }

        public int AnimationCategory { get; internal set; }

        public int AnimationNo { get; internal set; }

        public int LeftHandPattern { get; internal set; }

        public int RightHandPattern { get; internal set; }

        public float MouthOpen { get; internal set; }

        public bool LipSync { get; internal set; }

        public IReadOnlyList<bool> ExpressionEnabled { get; internal set; }

        public byte[] EyeLookData { get; internal set; }

        public float AnimationSpeed { get; internal set; }

        public float AnimationPattern { get; internal set; }

        public bool AnimationOptionVisible { get; internal set; }

        public bool AnimationForceLoop { get; internal set; }

        public float AnimationOptionParam1 { get; internal set; }

        public float AnimationOptionParam2 { get; internal set; }

        public float AnimationNormalizedTime { get; internal set; }

        public string AnimationModGuid { get; internal set; } = string.Empty;
    }

    public sealed class KoikatsuSceneObjectBase
    {
        internal KoikatsuSceneObjectBase(
            int dicKey,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            int treeState,
            bool visible)
        {
            DicKey = dicKey;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            TreeState = treeState;
            Visible = visible;
        }

        public int DicKey { get; }

        public Vector3 Position { get; }

        public Vector3 Rotation { get; }

        public Vector3 Scale { get; }

        public int TreeState { get; }

        public bool Visible { get; }
    }

    public sealed class KoikatsuSceneItem
    {
        internal KoikatsuSceneItem(int group, int category, int no)
        {
            Group = group;
            Category = category;
            No = no;
            LineColor = Color.gray;
            EmissionColor = Color.white;
            LineWidth = 1f;
            EnableDynamicBone = true;
        }

        public int Group { get; }

        public int Category { get; }

        public int No { get; }

        public float AnimeSpeed { get; internal set; }

        public Color[] Colors { get; internal set; }

        public KoikatsuScenePattern[] Patterns { get; internal set; }

        public float Alpha { get; internal set; }

        public Color LineColor { get; internal set; }

        public float LineWidth { get; internal set; }

        public Color EmissionColor { get; internal set; }

        public float EmissionPower { get; internal set; }

        public float LightCancel { get; internal set; }

        public KoikatsuScenePattern Panel { get; internal set; }

        public bool EnableFK { get; internal set; }

        public IReadOnlyDictionary<string, KoikatsuSceneBone> Bones { get; internal set; }

        public bool EnableDynamicBone { get; internal set; }

        public float AnimeNormalizedTime { get; internal set; }
    }

    public sealed class KoikatsuScenePattern
    {
        internal KoikatsuScenePattern(
            int key,
            string filePath,
            bool clamp,
            Vector4 uv,
            float rotation)
        {
            Key = key;
            FilePath = filePath ?? string.Empty;
            Clamp = clamp;
            UV = uv;
            Rotation = rotation;
        }

        public int Key { get; }

        public string FilePath { get; }

        public bool Clamp { get; }

        public Vector4 UV { get; }

        public float Rotation { get; }
    }

    public sealed class KoikatsuSceneBone
    {
        internal KoikatsuSceneBone(
            int dictionaryKey,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale)
        {
            DictionaryKey = dictionaryKey;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public int DictionaryKey { get; }

        public Vector3 Position { get; }

        public Vector3 Rotation { get; }

        public Vector3 Scale { get; }
    }

    public sealed class KoikatsuSceneLight
    {
        internal KoikatsuSceneLight(
            int no,
            Color color,
            float intensity,
            float range,
            float spotAngle,
            bool shadow,
            bool enable,
            bool drawTarget)
        {
            No = no;
            Color = color;
            Intensity = intensity;
            Range = range;
            SpotAngle = spotAngle;
            Shadow = shadow;
            Enable = enable;
            DrawTarget = drawTarget;
        }

        public int No { get; }

        public Color Color { get; }

        public float Intensity { get; }

        public float Range { get; }

        public float SpotAngle { get; }

        public bool Shadow { get; }

        public bool Enable { get; }

        public bool DrawTarget { get; }
    }

    public sealed class KoikatsuStudioResolution
    {
        internal KoikatsuStudioResolution(
            string guid,
            int slot,
            int localSlot,
            int dicKey,
            int objectOrder,
            bool resolveItem,
            int group,
            int category,
            string name)
        {
            Guid = guid?.Trim() ?? string.Empty;
            Slot = slot;
            LocalSlot = localSlot;
            DicKey = dicKey;
            ObjectOrder = objectOrder;
            ResolveItem = resolveItem;
            Group = group;
            Category = category;
            Name = name ?? string.Empty;
        }

        public string Guid { get; }

        public int Slot { get; }

        public int LocalSlot { get; }

        public int DicKey { get; }

        public int ObjectOrder { get; }

        public bool ResolveItem { get; }

        public int Group { get; }

        public int Category { get; }

        public string Name { get; }
    }

    public sealed class KoikatsuStudioPatternResolution
    {
        internal KoikatsuStudioPatternResolution(
            string guid,
            int slot,
            int localSlot,
            int dicKey,
            int objectOrder,
            int channel,
            string name)
        {
            Guid = guid?.Trim() ?? string.Empty;
            Slot = slot;
            LocalSlot = localSlot;
            DicKey = dicKey;
            ObjectOrder = objectOrder;
            Channel = channel;
            Name = name ?? string.Empty;
        }

        public string Guid { get; }

        public int Slot { get; }

        public int LocalSlot { get; }

        public int DicKey { get; }

        public int ObjectOrder { get; }

        public int Channel { get; }

        public string Name { get; }
    }

    [MessagePackObject]
    public sealed class KoikatsuStudioResolutionDto
    {
        [Key("ModID")] public string Guid { get; set; }
        [Key("Slot")] public int Slot { get; set; }
        [Key("LocalSlot")] public int LocalSlot { get; set; }
        [Key("SceneDicKey")] public int DicKey { get; set; }
        [Key("SceneObjectOrder")] public int ObjectOrder { get; set; }
        [Key("ResolveItem")] public bool ResolveItem { get; set; }
        [Key("Group")] public int Group { get; set; }
        [Key("Category")] public int Category { get; set; }
        [Key("Name")] public string Name { get; set; }
    }

    [MessagePackObject]
    public sealed class KoikatsuStudioPatternResolutionDto
    {
        [Key("SceneDicKey")] public int DicKey { get; set; }
        [Key("SceneObjectOrder")] public int ObjectOrder { get; set; }
        [Key("ObjectPatternInfo")]
        public Dictionary<int, KoikatsuStudioPatternInfoDto>
            ObjectPatternInfo { get; set; }
    }

    [MessagePackObject]
    public sealed class KoikatsuStudioPatternInfoDto
    {
        [Key("GUID")] public string Guid { get; set; }
        [Key("Slot")] public int Slot { get; set; }
        [Key("LocalSlot")] public int LocalSlot { get; set; }
        [Key("Name")] public string Name { get; set; }
    }
}
