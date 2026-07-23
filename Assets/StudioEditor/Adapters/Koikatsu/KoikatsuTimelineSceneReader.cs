using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml;
using MessagePack;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    public static class KoikatsuTimelineSceneReader
    {
        private const string TimelinePluginId = "timeline";
        private const string TimelineDataKey = "sceneInfo";
        private const int ExtendedHeaderSize = 13;

        private static readonly byte[] ExtendedMarker =
        {
            4,
            (byte)'K',
            (byte)'K',
            (byte)'E',
            (byte)'x',
        };

        public static KoikatsuTimelineScene Read(string path)
        {
            if (!TryRead(path, out var scene))
            {
                throw new InvalidDataException(
                    "Koikatsu scene does not contain Timeline data.");
            }

            return scene;
        }

        public static bool TryRead(
            string path,
            out KoikatsuTimelineScene scene)
        {
            if (!TryReadPluginString(
                    path,
                    TimelinePluginId,
                    TimelineDataKey,
                    out var xml))
            {
                scene = null;
                return false;
            }

            scene = ParseXml(xml);
            return true;
        }

        internal static bool TryReadPluginString(
            string path,
            string pluginId,
            string dataKey,
            out string value)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Koikatsu scene card was not found.",
                    path);
            }

            var fileData = File.ReadAllBytes(path);
            if (!TryReadExtendedPayload(fileData, out var payload))
            {
                value = null;
                return false;
            }

            Dictionary<string, KoikatsuCardReader.PluginDataDto> plugins;
            try
            {
                plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    payload);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Koikatsu scene ExtendedSave data is invalid.",
                    exception);
            }

            if (plugins != null &&
                plugins.TryGetValue(pluginId, out var plugin) &&
                plugin?.Data != null &&
                plugin.Data.TryGetValue(dataKey, out var data) &&
                data is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }

            value = null;
            return false;
        }

        public static KoikatsuTimelineScene ParseXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new InvalidDataException("Timeline scene XML is empty.");
            }

            var document = new XmlDocument
            {
                XmlResolver = null,
            };
            try
            {
                using (var stringReader = new StringReader(xml))
                using (var reader = XmlReader.Create(
                           stringReader,
                           new XmlReaderSettings
                           {
                               DtdProcessing = DtdProcessing.Prohibit,
                               XmlResolver = null,
                           }))
                {
                    document.Load(reader);
                }
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException(
                    "Timeline scene XML is invalid.",
                    exception);
            }

            var root = document.DocumentElement;
            if (root == null ||
                !string.Equals(root.Name, "root", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Timeline scene XML has no root element.");
            }

            var tracks = new List<KoikatsuTimelineTrack>();
            var nodes = root.SelectNodes(".//interpolable");
            if (nodes != null)
            {
                for (var index = 0; index < nodes.Count; index++)
                {
                    tracks.Add(ReadTrack(nodes[index], index));
                }
            }

            var duration = ReadOptionalSingle(root, "duration", 0f);
            if (!root.HasAttribute("duration"))
            {
                for (var trackIndex = 0;
                     trackIndex < tracks.Count;
                     trackIndex++)
                {
                    var keyframes = tracks[trackIndex].Keyframes;
                    for (var keyIndex = 0;
                         keyIndex < keyframes.Count;
                         keyIndex++)
                    {
                        duration = Math.Max(duration, keyframes[keyIndex].Time);
                    }
                }

                if (Math.Abs(duration) < 0.000001f)
                {
                    duration = 10f;
                }
            }

            return new KoikatsuTimelineScene(
                duration,
                ReadOptionalSingle(root, "blockLength", 10f),
                ReadOptionalInt(root, "divisions", 10),
                ReadOptionalSingle(root, "timeScale", 1f),
                tracks.AsReadOnly(),
                xml);
        }

        private static KoikatsuTimelineTrack ReadTrack(
            XmlNode node,
            int trackIndex)
        {
            var owner = ReadRequiredAttribute(
                node,
                "owner",
                $"Timeline track {trackIndex}");
            var id = ReadRequiredAttribute(
                node,
                "id",
                $"Timeline track {trackIndex}");
            int? objectIndex = null;
            if (node.Attributes?["objectIndex"] != null)
            {
                objectIndex = ReadRequiredInt(
                    node,
                    "objectIndex",
                    $"Timeline track {trackIndex}");
            }

            var attributes = ReadAttributes(node);
            var keyframes = new List<KoikatsuTimelineKeyframe>();
            for (var childIndex = 0;
                 childIndex < node.ChildNodes.Count;
                 childIndex++)
            {
                var child = node.ChildNodes[childIndex];
                if (string.Equals(
                        child.Name,
                        "keyframe",
                        StringComparison.Ordinal))
                {
                    keyframes.Add(ReadKeyframe(
                        child,
                        trackIndex,
                        keyframes.Count));
                }
            }

            return new KoikatsuTimelineTrack(
                owner,
                id,
                objectIndex,
                ReadOptionalBoolean(node, "enabled", true),
                node.Attributes?["alias"]?.Value ?? string.Empty,
                attributes,
                keyframes.AsReadOnly());
        }

        private static KoikatsuTimelineKeyframe ReadKeyframe(
            XmlNode node,
            int trackIndex,
            int keyframeIndex)
        {
            var context = $"Timeline track {trackIndex}, keyframe " +
                          keyframeIndex;
            var curve = new List<KoikatsuTimelineCurveKey>();
            for (var childIndex = 0;
                 childIndex < node.ChildNodes.Count;
                 childIndex++)
            {
                var child = node.ChildNodes[childIndex];
                if (!string.Equals(
                        child.Name,
                        "curveKeyframe",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                curve.Add(new KoikatsuTimelineCurveKey(
                    ReadRequiredSingle(child, "time", context),
                    ReadRequiredSingle(child, "value", context),
                    ReadRequiredSingle(child, "inTangent", context),
                    ReadRequiredSingle(child, "outTangent", context)));
            }

            return new KoikatsuTimelineKeyframe(
                ReadRequiredSingle(node, "time", context),
                ReadAttributes(node),
                curve.AsReadOnly());
        }

        private static IReadOnlyDictionary<string, string> ReadAttributes(
            XmlNode node)
        {
            var attributes = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (node.Attributes != null)
            {
                for (var index = 0; index < node.Attributes.Count; index++)
                {
                    var attribute = node.Attributes[index];
                    attributes[attribute.Name] = attribute.Value;
                }
            }

            return new ReadOnlyDictionary<string, string>(attributes);
        }

        private static bool TryReadExtendedPayload(
            byte[] fileData,
            out byte[] payload)
        {
            for (var index = fileData.Length - ExtendedHeaderSize;
                 index >= 0;
                 index--)
            {
                if (!Matches(fileData, index, ExtendedMarker))
                {
                    continue;
                }

                var length = ReadInt32LittleEndian(fileData, index + 9);
                if (length < 0 ||
                    index + (long)ExtendedHeaderSize + length !=
                    fileData.Length)
                {
                    continue;
                }

                payload = new byte[length];
                Buffer.BlockCopy(
                    fileData,
                    index + ExtendedHeaderSize,
                    payload,
                    0,
                    length);
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

        private static int ReadInt32LittleEndian(byte[] source, int offset)
        {
            if (offset < 0 || offset > source.Length - sizeof(int))
            {
                return -1;
            }

            return source[offset] |
                   source[offset + 1] << 8 |
                   source[offset + 2] << 16 |
                   source[offset + 3] << 24;
        }

        private static string ReadRequiredAttribute(
            XmlNode node,
            string name,
            string context)
        {
            var value = node.Attributes?[name]?.Value;
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidDataException(
                    $"{context} has no '{name}' attribute.");
            }

            return value;
        }

        private static float ReadRequiredSingle(
            XmlNode node,
            string name,
            string context)
        {
            var value = ReadRequiredAttribute(node, name, context);
            try
            {
                return XmlConvert.ToSingle(value);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    $"{context} has an invalid '{name}' value '{value}'.",
                    exception);
            }
        }

        private static int ReadRequiredInt(
            XmlNode node,
            string name,
            string context)
        {
            var value = ReadRequiredAttribute(node, name, context);
            try
            {
                return XmlConvert.ToInt32(value);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    $"{context} has an invalid '{name}' value '{value}'.",
                    exception);
            }
        }

        private static float ReadOptionalSingle(
            XmlElement node,
            string name,
            float fallback)
        {
            return node.HasAttribute(name)
                ? ReadRequiredSingle(node, name, "Timeline root")
                : fallback;
        }

        private static int ReadOptionalInt(
            XmlElement node,
            string name,
            int fallback)
        {
            return node.HasAttribute(name)
                ? ReadRequiredInt(node, name, "Timeline root")
                : fallback;
        }

        private static bool ReadOptionalBoolean(
            XmlNode node,
            string name,
            bool fallback)
        {
            var value = node.Attributes?[name]?.Value;
            if (value == null)
            {
                return fallback;
            }

            try
            {
                return XmlConvert.ToBoolean(value);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(
                    $"Timeline has an invalid '{name}' value '{value}'.",
                    exception);
            }
        }
    }

    public sealed class KoikatsuTimelineScene
    {
        internal KoikatsuTimelineScene(
            float duration,
            float blockLength,
            int divisions,
            float timeScale,
            IReadOnlyList<KoikatsuTimelineTrack> tracks,
            string sourceXml)
        {
            Duration = duration;
            BlockLength = blockLength;
            Divisions = divisions;
            TimeScale = timeScale;
            Tracks = tracks;
            SourceXml = sourceXml ?? string.Empty;
        }

        public float Duration { get; }

        public float BlockLength { get; }

        public int Divisions { get; }

        public float TimeScale { get; }

        public IReadOnlyList<KoikatsuTimelineTrack> Tracks { get; }

        public string SourceXml { get; }
    }

    public sealed class KoikatsuTimelineTrack
    {
        internal KoikatsuTimelineTrack(
            string owner,
            string id,
            int? objectIndex,
            bool enabled,
            string alias,
            IReadOnlyDictionary<string, string> attributes,
            IReadOnlyList<KoikatsuTimelineKeyframe> keyframes)
        {
            Owner = owner ?? string.Empty;
            Id = id ?? string.Empty;
            ObjectIndex = objectIndex;
            Enabled = enabled;
            Alias = alias ?? string.Empty;
            Attributes = attributes;
            Keyframes = keyframes;
        }

        public string Owner { get; }

        public string Id { get; }

        public int? ObjectIndex { get; }

        public bool Enabled { get; }

        public string Alias { get; }

        public string GuideObjectPath => GetAttribute("guideObjectPath");

        public IReadOnlyDictionary<string, string> Attributes { get; }

        public IReadOnlyList<KoikatsuTimelineKeyframe> Keyframes { get; }

        public string GetAttribute(string name)
        {
            return name != null && Attributes.TryGetValue(name, out var value)
                ? value
                : string.Empty;
        }
    }

    public sealed class KoikatsuTimelineKeyframe
    {
        internal KoikatsuTimelineKeyframe(
            float time,
            IReadOnlyDictionary<string, string> attributes,
            IReadOnlyList<KoikatsuTimelineCurveKey> curve)
        {
            Time = time;
            Attributes = attributes;
            Curve = curve;
        }

        public float Time { get; }

        public IReadOnlyDictionary<string, string> Attributes { get; }

        public IReadOnlyList<KoikatsuTimelineCurveKey> Curve { get; }

        public bool TryGetSingle(string name, out float value)
        {
            value = default;
            if (name == null || !Attributes.TryGetValue(name, out var text))
            {
                return false;
            }

            try
            {
                value = XmlConvert.ToSingle(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool TryGetInt(string name, out int value)
        {
            value = default;
            if (name == null || !Attributes.TryGetValue(name, out var text))
            {
                return false;
            }

            try
            {
                value = XmlConvert.ToInt32(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        public bool TryGetBoolean(string name, out bool value)
        {
            value = default;
            if (name == null || !Attributes.TryGetValue(name, out var text))
            {
                return false;
            }

            try
            {
                value = XmlConvert.ToBoolean(text);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool TryGetVector3(string prefix, out Vector3 value)
        {
            value = default;
            return TryGetSingle(prefix + "X", out value.x) &&
                   TryGetSingle(prefix + "Y", out value.y) &&
                   TryGetSingle(prefix + "Z", out value.z);
        }

        public bool TryGetQuaternion(string prefix, out Quaternion value)
        {
            value = default;
            return TryGetSingle(prefix + "X", out value.x) &&
                   TryGetSingle(prefix + "Y", out value.y) &&
                   TryGetSingle(prefix + "Z", out value.z) &&
                   TryGetSingle(prefix + "W", out value.w);
        }
    }

    public readonly struct KoikatsuTimelineCurveKey
    {
        public KoikatsuTimelineCurveKey(
            float time,
            float value,
            float inTangent,
            float outTangent)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
        }

        public float Time { get; }

        public float Value { get; }

        public float InTangent { get; }

        public float OutTangent { get; }
    }
}
