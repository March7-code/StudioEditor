using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MessagePack;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    public static class KoikatsuCardReader
    {
        private const string CardMarker = "【KoiKatuChara】";
        private static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        };

        public static KoikatsuCard Read(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Koikatsu card was not found.", path);
            }

            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                stream.Position = ReadPngLength(reader);
                return ReadCard(reader, Path.GetFullPath(path));
            }
        }

        internal static KoikatsuCard ReadEmbedded(
            BinaryReader reader,
            string sourcePath)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            return ReadCard(reader, sourcePath ?? string.Empty);
        }

        private static KoikatsuCard ReadCard(
            BinaryReader reader,
            string sourcePath)
        {
            var productNumber = reader.ReadInt32();
            if (productNumber > 100)
            {
                throw new InvalidDataException(
                    $"Unsupported Koikatsu product number {productNumber}.");
            }

            var marker = reader.ReadString();
            if (!string.Equals(marker, CardMarker, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Expected Koikatsu card marker, found '{marker}'.");
            }

            var version = reader.ReadString();
            SkipLengthPrefixedBytes(reader, "face PNG");
            var headerBytes = ReadLengthPrefixedBytes(reader, "block header");
            var header = MessagePackSerializer.Deserialize<BlockHeaderDto>(
                headerBytes);
            if (header?.Items == null)
            {
                throw new InvalidDataException("Koikatsu block header is empty.");
            }

            var totalBlockLength = reader.ReadInt64();
            var blockDataStart = reader.BaseStream.Position;
            ValidateRange(
                blockDataStart,
                totalBlockLength,
                reader.BaseStream.Length,
                "block data");

            var blocks = ReadBlocks(
                reader,
                header.Items,
                blockDataStart,
                totalBlockLength);
            reader.BaseStream.Position = blockDataStart + totalBlockLength;
            if (!blocks.TryGetValue("Custom", out var customBlock))
            {
                throw new InvalidDataException(
                    "Koikatsu card does not contain a Custom block.");
            }

            ReadCustom(
                customBlock.Data,
                out var face,
                out var body,
                out var hair);
            var parameter = blocks.TryGetValue("Parameter", out var parameterBlock)
                ? ReadParameter(parameterBlock.Data)
                : new KoikatsuCardParameter(1, string.Empty, string.Empty);
            var coordinates = blocks.TryGetValue(
                "Coordinate",
                out var coordinateBlock)
                ? ReadCoordinates(coordinateBlock.Data)
                : Array.Empty<KoikatsuCardCoordinate>();
            var activeCoordinateIndex = ReadActiveCoordinateIndex(
                blocks,
                coordinates.Count);
            var sideloaderResolutions = ReadSideloaderResolutions(blocks);

            return new KoikatsuCard(
                sourcePath,
                productNumber,
                version,
                face,
                body,
                hair,
                parameter,
                coordinates,
                activeCoordinateIndex,
                blocks,
                sideloaderResolutions);
        }

        private static int ReadActiveCoordinateIndex(
            IReadOnlyDictionary<string, KoikatsuCardBlock> blocks,
            int coordinateCount)
        {
            if (!blocks.TryGetValue("Status", out var statusBlock))
            {
                return 0;
            }

            try
            {
                var status = MessagePackSerializer.Deserialize<StatusDto>(
                    statusBlock.Data);
                return status != null && status.CoordinateType >= 0 &&
                       status.CoordinateType < coordinateCount
                    ? status.CoordinateType
                    : 0;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read the active Koikatsu outfit slot: " +
                    exception.Message);
                return 0;
            }
        }

        private static IReadOnlyList<KoikatsuSideloaderResolution>
            ReadSideloaderResolutions(
                IReadOnlyDictionary<string, KoikatsuCardBlock> blocks)
        {
            if (!blocks.TryGetValue("KKEx", out var extendedBlock))
            {
                return Array.Empty<KoikatsuSideloaderResolution>();
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, PluginDataDto>>(extendedBlock.Data);
                if (plugins == null ||
                    (!plugins.TryGetValue(
                         "EC.Core.Sideloader.UniversalAutoResolver",
                         out var plugin) &&
                     !plugins.TryGetValue(
                         "com.bepis.sideloader.universalautoresolver",
                         out plugin)) ||
                    plugin?.Data == null ||
                    !plugin.Data.TryGetValue("info", out var rawCollection) ||
                    !(rawCollection is object[] collection))
                {
                    return Array.Empty<KoikatsuSideloaderResolution>();
                }

                var result = new List<KoikatsuSideloaderResolution>(
                    collection.Length);
                for (var index = 0; index < collection.Length; index++)
                {
                    if (!(collection[index] is byte[] rawInfo))
                    {
                        continue;
                    }

                    var info = MessagePackSerializer.Deserialize<
                        SideloaderResolutionDto>(rawInfo);
                    if (info == null || string.IsNullOrWhiteSpace(info.Guid))
                    {
                        continue;
                    }

                    result.Add(new KoikatsuSideloaderResolution(
                        info.Guid.Trim(),
                        info.Slot,
                        info.Property,
                        info.Category,
                        info.Name));
                }

                return result.AsReadOnly();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu Sideloader metadata from the " +
                    $"card: {exception.Message}");
                return Array.Empty<KoikatsuSideloaderResolution>();
            }
        }

        private static long ReadPngLength(BinaryReader reader)
        {
            var signature = reader.ReadBytes(PngSignature.Length);
            if (!signature.SequenceEqual(PngSignature))
            {
                throw new InvalidDataException("File does not start with a PNG signature.");
            }

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var chunkLength = ReadBigEndianUInt32(reader);
                var chunkType = Encoding.ASCII.GetString(reader.ReadBytes(4));
                ValidateRange(
                    reader.BaseStream.Position,
                    chunkLength + 4L,
                    reader.BaseStream.Length,
                    $"PNG chunk '{chunkType}'");
                reader.BaseStream.Seek(chunkLength + 4L, SeekOrigin.Current);
                if (string.Equals(chunkType, "IEND", StringComparison.Ordinal))
                {
                    return reader.BaseStream.Position;
                }
            }

            throw new InvalidDataException("PNG does not contain an IEND chunk.");
        }

        private static uint ReadBigEndianUInt32(BinaryReader reader)
        {
            var data = reader.ReadBytes(4);
            if (data.Length != 4)
            {
                throw new EndOfStreamException();
            }

            return ((uint)data[0] << 24) |
                   ((uint)data[1] << 16) |
                   ((uint)data[2] << 8) |
                   data[3];
        }

        private static Dictionary<string, KoikatsuCardBlock> ReadBlocks(
            BinaryReader reader,
            IEnumerable<BlockInfoDto> items,
            long dataStart,
            long totalLength)
        {
            var blocks = new Dictionary<string, KoikatsuCardBlock>(
                StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.Name) ||
                    blocks.ContainsKey(item.Name))
                {
                    continue;
                }

                ValidateRange(item.Position, item.Size, totalLength, item.Name);
                if (item.Size > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"Koikatsu block '{item.Name}' is too large.");
                }

                reader.BaseStream.Position = dataStart + item.Position;
                var data = reader.ReadBytes((int)item.Size);
                if (data.Length != item.Size)
                {
                    throw new EndOfStreamException(
                        $"Koikatsu block '{item.Name}' is truncated.");
                }

                blocks.Add(
                    item.Name,
                    new KoikatsuCardBlock(item.Name, item.Version, data));
            }

            return blocks;
        }

        private static void ReadCustom(
            byte[] data,
            out KoikatsuCardFace face,
            out KoikatsuCardBody body,
            out KoikatsuCardHair hair)
        {
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream))
            {
                var faceDto = MessagePackSerializer.Deserialize<FaceDto>(
                    ReadLengthPrefixedBytes(reader, "face"));
                var bodyDto = MessagePackSerializer.Deserialize<BodyDto>(
                    ReadLengthPrefixedBytes(reader, "body"));
                var hairDto = MessagePackSerializer.Deserialize<HairDto>(
                    ReadLengthPrefixedBytes(reader, "hair"));

                face = new KoikatsuCardFace(
                    faceDto?.HeadId ?? 0,
                    faceDto?.SkinId ?? 0,
                    faceDto?.ShapeValues,
                    ToFaceAppearance(faceDto));
                body = new KoikatsuCardBody(
                    bodyDto?.SkinId ?? 0,
                    bodyDto?.TypeBone ?? 0,
                    bodyDto?.ShapeValues,
                    ToBodyAppearance(bodyDto));
                hair = new KoikatsuCardHair(
                    hairDto?.Kind ?? 0,
                    hairDto?.GlossId ?? 0,
                    hairDto?.Parts?
                        .Select(ToHairPart)
                        .ToArray());
            }
        }

        private static KoikatsuCardFaceAppearance ToFaceAppearance(FaceDto face)
        {
            if (face == null)
            {
                return KoikatsuCardFaceAppearance.CreateDefault();
            }

            return new KoikatsuCardFaceAppearance(
                face.DetailId,
                face.DetailPower,
                face.CheekGlossPower,
                face.EyebrowId,
                ToColor(face.EyebrowColor, Color.white),
                face.NoseId,
                face.Pupils?
                    .Select(pupil => new KoikatsuCardPupil(
                        pupil?.Id ?? 0,
                        ToColor(pupil?.BaseColor, Color.black),
                        ToColor(pupil?.SubColor, Color.white),
                        pupil?.GradientMaskId ?? 0,
                        pupil?.GradientBlend ?? 0f,
                        pupil?.GradientOffsetY ?? 0f,
                        pupil?.GradientScale ?? 0f))
                    .ToArray(),
                face.HighlightUpId,
                ToColor(face.HighlightUpColor, Color.white),
                face.HighlightDownId,
                ToColor(face.HighlightDownColor, Color.white),
                face.EyeWhiteId,
                ToColor(face.EyeWhiteBaseColor, Color.white),
                ToColor(face.EyeWhiteSubColor, Color.white),
                face.PupilWidth,
                face.PupilHeight,
                face.PupilX,
                face.PupilY,
                face.HighlightUpY,
                face.HighlightDownY,
                face.EyelineUpId,
                face.EyelineUpWeight,
                face.EyelineDownId,
                ToColor(face.EyelineColor, Color.white),
                face.MoleId,
                ToColor(face.MoleColor, Color.white),
                ToVector4(face.MoleLayout, new Vector4(0.5f, 0.5f, 0f, 0.5f)),
                face.LipLineId,
                ToColor(face.LipLineColor, Color.white),
                face.LipGlossPower,
                ToMakeup(face.BaseMakeup));
        }

        private static KoikatsuCardBodyAppearance ToBodyAppearance(BodyDto body)
        {
            if (body == null)
            {
                return KoikatsuCardBodyAppearance.CreateDefault();
            }

            return new KoikatsuCardBodyAppearance(
                body.DetailId,
                body.DetailPower,
                ToColor(body.SkinMainColor, Color.white),
                ToColor(body.SkinSubColor, Color.white),
                body.SkinGlossPower,
                body.PaintIds,
                body.PaintColors?
                    .Select(color => ToColor(color, Color.white))
                    .ToArray(),
                body.PaintLayoutIds,
                body.PaintLayouts?
                    .Select(layout => ToVector4(layout, Vector4.zero))
                    .ToArray(),
                body.SunburnId,
                ToColor(body.SunburnColor, Color.white),
                body.NippleId,
                ToColor(body.NippleColor, Color.white),
                body.UnderhairId,
                ToColor(body.UnderhairColor, Color.white),
                ToColor(body.NailColor, Color.white));
        }

        private static KoikatsuCardMakeup ToMakeup(MakeupDto makeup)
        {
            if (makeup == null)
            {
                return KoikatsuCardMakeup.CreateDefault();
            }

            return new KoikatsuCardMakeup(
                makeup.EyeshadowId,
                ToColor(makeup.EyeshadowColor, Color.white),
                makeup.CheekId,
                ToColor(makeup.CheekColor, Color.white),
                makeup.LipId,
                ToColor(makeup.LipColor, Color.white),
                makeup.PaintIds,
                makeup.PaintColors?
                    .Select(color => ToColor(color, Color.white))
                    .ToArray(),
                makeup.PaintLayouts?
                    .Select(layout => ToVector4(layout, Vector4.zero))
                    .ToArray());
        }

        private static Color ToColor(ColorDto color, Color fallback)
        {
            return color?.ToColor(fallback) ?? fallback;
        }

        private static Vector4 ToVector4(Vector4Dto vector, Vector4 fallback)
        {
            return vector?.ToVector4(fallback) ?? fallback;
        }

        private static KoikatsuCardHairPart ToHairPart(HairPartDto part)
        {
            if (part == null)
            {
                return KoikatsuCardHairPart.CreateDefault();
            }

            return new KoikatsuCardHairPart(
                part.Id,
                part.BaseColor?.ToColor(Color.white) ?? Color.white,
                part.StartColor?.ToColor(Color.white) ?? Color.white,
                part.EndColor?.ToColor(Color.white) ?? Color.white,
                part.Length,
                part.Position?.ToVector3(Vector3.zero) ?? Vector3.zero,
                part.Rotation?.ToVector3(Vector3.zero) ?? Vector3.zero,
                part.Scale?.ToVector3(Vector3.one) ?? Vector3.one,
                part.AccessoryColors?
                    .Select(color => color?.ToColor(Color.white) ?? Color.white)
                    .ToArray(),
                part.OutlineColor?.ToColor(Color.black) ?? Color.black,
                part.NoShake);
        }

        private static KoikatsuCardParameter ReadParameter(byte[] data)
        {
            var parameter = MessagePackSerializer.Deserialize<ParameterDto>(data);
            return new KoikatsuCardParameter(
                parameter?.Sex ?? 1,
                parameter?.LastName,
                parameter?.FirstName);
        }

        private static IReadOnlyList<KoikatsuCardCoordinate> ReadCoordinates(
            byte[] data)
        {
            var serializedCoordinates =
                MessagePackSerializer.Deserialize<List<byte[]>>(data) ??
                new List<byte[]>();
            var coordinates = new KoikatsuCardCoordinate[
                serializedCoordinates.Count];
            for (var index = 0; index < serializedCoordinates.Count; index++)
            {
                coordinates[index] = ReadCoordinate(serializedCoordinates[index]);
            }

            return Array.AsReadOnly(coordinates);
        }

        private static KoikatsuCardCoordinate ReadCoordinate(byte[] data)
        {
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream))
            {
                var clothes = MessagePackSerializer.Deserialize<ClothesDto>(
                    ReadLengthPrefixedBytes(reader, "clothes"));
                var accessories = MessagePackSerializer.Deserialize<AccessoryDto>(
                    ReadLengthPrefixedBytes(reader, "accessories"));
                var makeupEnabled = reader.ReadBoolean();
                var makeup = MessagePackSerializer.Deserialize<MakeupDto>(
                    ReadLengthPrefixedBytes(reader, "makeup"));

                return new KoikatsuCardCoordinate(
                    clothes?.Parts?.Select(ToClothesPart).ToArray(),
                    clothes?.SubPartsIds,
                    clothes?.HideBraOptions,
                    clothes?.HideShortsOptions,
                    accessories?.Parts?
                        .Select(part => new KoikatsuCardAccessory(
                            part?.Type ?? 120,
                            part?.Id ?? 0,
                            part?.ParentKey,
                            ToAccessoryMoves(part?.AdditionalMoves),
                            part?.Colors?
                                .Select(color => ToColor(color, Color.white))
                                .ToArray(),
                            part?.HideCategory ?? 0,
                            part?.NoShake ?? false))
                        .ToArray(),
                    makeupEnabled,
                    ToMakeup(makeup));
            }
        }

        private static KoikatsuCardClothesPart ToClothesPart(ClothesPartDto part)
        {
            return new KoikatsuCardClothesPart(
                part?.Id ?? 0,
                part?.Colors?
                    .Select(color => new KoikatsuCardClothesColor(
                        ToColor(color?.BaseColor, Color.white),
                        color?.Pattern ?? 0,
                        color?.Tiling?.ToVector2(Vector2.zero) ?? Vector2.zero,
                        ToColor(color?.PatternColor, Color.white)))
                    .ToArray(),
                part?.EmblemId ?? 0,
                part?.EmblemId2 ?? 0,
                part?.HideOptions,
                part?.SleevesType ?? 0);
        }

        private static Vector3[,] ToAccessoryMoves(Vector3Dto[,] source)
        {
            var result = new Vector3[2, 3];
            for (var moveIndex = 0; moveIndex < 2; moveIndex++)
            {
                result[moveIndex, 2] = Vector3.one;
            }

            if (source == null)
            {
                return result;
            }

            var moveCount = Math.Min(result.GetLength(0), source.GetLength(0));
            var transformCount = Math.Min(result.GetLength(1), source.GetLength(1));
            for (var moveIndex = 0; moveIndex < moveCount; moveIndex++)
            {
                for (var transformIndex = 0;
                     transformIndex < transformCount;
                     transformIndex++)
                {
                    var fallback = transformIndex == 2
                        ? Vector3.one
                        : Vector3.zero;
                    result[moveIndex, transformIndex] =
                        source[moveIndex, transformIndex]?.ToVector3(fallback) ??
                        fallback;
                }
            }

            return result;
        }

        private static byte[] ReadLengthPrefixedBytes(
            BinaryReader reader,
            string description)
        {
            var length = reader.ReadInt32();
            if (length < 0)
            {
                throw new InvalidDataException(
                    $"Koikatsu {description} has a negative length.");
            }

            ValidateRange(
                reader.BaseStream.Position,
                length,
                reader.BaseStream.Length,
                description);
            var data = reader.ReadBytes(length);
            if (data.Length != length)
            {
                throw new EndOfStreamException(
                    $"Koikatsu {description} is truncated.");
            }

            return data;
        }

        private static void SkipLengthPrefixedBytes(
            BinaryReader reader,
            string description)
        {
            var length = reader.ReadInt32();
            if (length < 0)
            {
                throw new InvalidDataException(
                    $"Koikatsu {description} has a negative length.");
            }

            ValidateRange(
                reader.BaseStream.Position,
                length,
                reader.BaseStream.Length,
                description);
            reader.BaseStream.Seek(length, SeekOrigin.Current);
        }

        private static void ValidateRange(
            long position,
            long length,
            long containerLength,
            string description)
        {
            if (position < 0 || length < 0 ||
                position > containerLength ||
                length > containerLength - position)
            {
                throw new InvalidDataException(
                    $"Koikatsu {description} points outside its container.");
            }
        }

        [MessagePackObject]
        public sealed class BlockHeaderDto
        {
            [Key("lstInfo")]
            public List<BlockInfoDto> Items { get; set; }
        }

        [MessagePackObject]
        public sealed class BlockInfoDto
        {
            [Key("name")]
            public string Name { get; set; }

            [Key("version")]
            public string Version { get; set; }

            [Key("pos")]
            public long Position { get; set; }

            [Key("size")]
            public long Size { get; set; }
        }

        [MessagePackObject]
        public sealed class PluginDataDto
        {
            [Key(0)]
            public int Version { get; set; }

            [Key(1)]
            public Dictionary<string, object> Data { get; set; }
        }

        [MessagePackObject]
        public sealed class SideloaderResolutionDto
        {
            [Key("ModID")]
            public string Guid { get; set; }

            [Key("Slot")]
            public int Slot { get; set; }

            [Key("Property")]
            public string Property { get; set; }

            [Key("CategoryNo")]
            public int Category { get; set; }

            [Key("Name")]
            public string Name { get; set; }
        }

        [MessagePackObject]
        public sealed class MaterialTexturePropertyDto
        {
            [Key("ObjectType")]
            public int ObjectType { get; set; }

            [Key("MaterialName")]
            public string MaterialName { get; set; }

            [Key("Property")]
            public string Property { get; set; }

            [Key("TexID")]
            public int? TextureId { get; set; }
        }

        [MessagePackObject]
        public sealed class StatusDto
        {
            [Key("coordinateType")]
            public int CoordinateType { get; set; }
        }

        [MessagePackObject]
        public sealed class FaceDto
        {
            [Key("shapeValueFace")]
            public float[] ShapeValues { get; set; }

            [Key("headId")]
            public int HeadId { get; set; }

            [Key("skinId")]
            public int SkinId { get; set; }

            [Key("detailId")]
            public int DetailId { get; set; }

            [Key("detailPower")]
            public float DetailPower { get; set; }

            [Key("cheekGlossPower")]
            public float CheekGlossPower { get; set; }

            [Key("eyebrowId")]
            public int EyebrowId { get; set; }

            [Key("eyebrowColor")]
            public ColorDto EyebrowColor { get; set; }

            [Key("noseId")]
            public int NoseId { get; set; }

            [Key("pupil")]
            public PupilDto[] Pupils { get; set; }

            [Key("hlUpId")]
            public int HighlightUpId { get; set; }

            [Key("hlUpColor")]
            public ColorDto HighlightUpColor { get; set; }

            [Key("hlDownId")]
            public int HighlightDownId { get; set; }

            [Key("hlDownColor")]
            public ColorDto HighlightDownColor { get; set; }

            [Key("whiteId")]
            public int EyeWhiteId { get; set; }

            [Key("whiteBaseColor")]
            public ColorDto EyeWhiteBaseColor { get; set; }

            [Key("whiteSubColor")]
            public ColorDto EyeWhiteSubColor { get; set; }

            [Key("pupilWidth")]
            public float PupilWidth { get; set; } = 0.5f;

            [Key("pupilHeight")]
            public float PupilHeight { get; set; } = 0.5f;

            [Key("pupilX")]
            public float PupilX { get; set; } = 0.5f;

            [Key("pupilY")]
            public float PupilY { get; set; } = 0.5f;

            [Key("hlUpY")]
            public float HighlightUpY { get; set; } = 0.5f;

            [Key("hlDownY")]
            public float HighlightDownY { get; set; } = 0.5f;

            [Key("eyelineUpId")]
            public int EyelineUpId { get; set; }

            [Key("eyelineUpWeight")]
            public float EyelineUpWeight { get; set; }

            [Key("eyelineDownId")]
            public int EyelineDownId { get; set; }

            [Key("eyelineColor")]
            public ColorDto EyelineColor { get; set; }

            [Key("moleId")]
            public int MoleId { get; set; }

            [Key("moleColor")]
            public ColorDto MoleColor { get; set; }

            [Key("moleLayout")]
            public Vector4Dto MoleLayout { get; set; }

            [Key("lipLineId")]
            public int LipLineId { get; set; }

            [Key("lipLineColor")]
            public ColorDto LipLineColor { get; set; }

            [Key("lipGlossPower")]
            public float LipGlossPower { get; set; }

            [Key("baseMakeup")]
            public MakeupDto BaseMakeup { get; set; }
        }

        [MessagePackObject]
        public sealed class BodyDto
        {
            [Key("shapeValueBody")]
            public float[] ShapeValues { get; set; }

            [Key("skinId")]
            public int SkinId { get; set; }

            [Key("detailId")]
            public int DetailId { get; set; }

            [Key("detailPower")]
            public float DetailPower { get; set; }

            [Key("skinMainColor")]
            public ColorDto SkinMainColor { get; set; }

            [Key("skinSubColor")]
            public ColorDto SkinSubColor { get; set; }

            [Key("skinGlossPower")]
            public float SkinGlossPower { get; set; }

            [Key("paintId")]
            public int[] PaintIds { get; set; }

            [Key("paintColor")]
            public ColorDto[] PaintColors { get; set; }

            [Key("paintLayoutId")]
            public int[] PaintLayoutIds { get; set; }

            [Key("paintLayout")]
            public Vector4Dto[] PaintLayouts { get; set; }

            [Key("sunburnId")]
            public int SunburnId { get; set; }

            [Key("sunburnColor")]
            public ColorDto SunburnColor { get; set; }

            [Key("nipId")]
            public int NippleId { get; set; }

            [Key("nipColor")]
            public ColorDto NippleColor { get; set; }

            [Key("underhairId")]
            public int UnderhairId { get; set; }

            [Key("underhairColor")]
            public ColorDto UnderhairColor { get; set; }

            [Key("nailColor")]
            public ColorDto NailColor { get; set; }

            [Key("typeBone")]
            public int TypeBone { get; set; }
        }

        [MessagePackObject]
        public sealed class HairDto
        {
            [Key("parts")]
            public HairPartDto[] Parts { get; set; }

            [Key("kind")]
            public int Kind { get; set; }

            [Key("glossId")]
            public int GlossId { get; set; }
        }

        [MessagePackObject]
        public sealed class HairPartDto
        {
            [Key("id")]
            public int Id { get; set; }

            [Key("baseColor")]
            public ColorDto BaseColor { get; set; }

            [Key("startColor")]
            public ColorDto StartColor { get; set; }

            [Key("endColor")]
            public ColorDto EndColor { get; set; }

            [Key("length")]
            public float Length { get; set; }

            [Key("pos")]
            public Vector3Dto Position { get; set; }

            [Key("rot")]
            public Vector3Dto Rotation { get; set; }

            [Key("scl")]
            public Vector3Dto Scale { get; set; }

            [Key("acsColor")]
            public ColorDto[] AccessoryColors { get; set; }

            [Key("outlineColor")]
            public ColorDto OutlineColor { get; set; }

            [Key("noShake")]
            public bool NoShake { get; set; }
        }

        [MessagePackObject]
        public sealed class ColorDto
        {
            [Key(0)]
            public float Red { get; set; }

            [Key(1)]
            public float Green { get; set; }

            [Key(2)]
            public float Blue { get; set; }

            [Key(3)]
            public float Alpha { get; set; }

            public Color ToColor(Color fallback)
            {
                return new Color(Red, Green, Blue, Alpha);
            }
        }

        [MessagePackObject]
        public sealed class Vector3Dto
        {
            [Key(0)]
            public float X { get; set; }

            [Key(1)]
            public float Y { get; set; }

            [Key(2)]
            public float Z { get; set; }

            public Vector3 ToVector3(Vector3 fallback)
            {
                return new Vector3(X, Y, Z);
            }
        }

        [MessagePackObject]
        public sealed class Vector2Dto
        {
            [Key(0)]
            public float X { get; set; }

            [Key(1)]
            public float Y { get; set; }

            public Vector2 ToVector2(Vector2 fallback)
            {
                return new Vector2(X, Y);
            }
        }

        [MessagePackObject]
        public sealed class Vector4Dto
        {
            [Key(0)]
            public float X { get; set; }

            [Key(1)]
            public float Y { get; set; }

            [Key(2)]
            public float Z { get; set; }

            [Key(3)]
            public float W { get; set; }

            public Vector4 ToVector4(Vector4 fallback)
            {
                return new Vector4(X, Y, Z, W);
            }
        }

        [MessagePackObject]
        public sealed class PupilDto
        {
            [Key("id")]
            public int Id { get; set; }

            [Key("baseColor")]
            public ColorDto BaseColor { get; set; }

            [Key("subColor")]
            public ColorDto SubColor { get; set; }

            [Key("gradMaskId")]
            public int GradientMaskId { get; set; }

            [Key("gradBlend")]
            public float GradientBlend { get; set; }

            [Key("gradOffsetY")]
            public float GradientOffsetY { get; set; }

            [Key("gradScale")]
            public float GradientScale { get; set; }
        }

        [MessagePackObject]
        public sealed class MakeupDto
        {
            [Key("eyeshadowId")]
            public int EyeshadowId { get; set; }

            [Key("eyeshadowColor")]
            public ColorDto EyeshadowColor { get; set; }

            [Key("cheekId")]
            public int CheekId { get; set; }

            [Key("cheekColor")]
            public ColorDto CheekColor { get; set; }

            [Key("lipId")]
            public int LipId { get; set; }

            [Key("lipColor")]
            public ColorDto LipColor { get; set; }

            [Key("paintId")]
            public int[] PaintIds { get; set; }

            [Key("paintColor")]
            public ColorDto[] PaintColors { get; set; }

            [Key("paintLayout")]
            public Vector4Dto[] PaintLayouts { get; set; }
        }

        [MessagePackObject]
        public sealed class ParameterDto
        {
            [Key("sex")]
            public byte Sex { get; set; }

            [Key("lastname")]
            public string LastName { get; set; }

            [Key("firstname")]
            public string FirstName { get; set; }
        }

        [MessagePackObject]
        public sealed class ClothesDto
        {
            [Key("parts")]
            public ClothesPartDto[] Parts { get; set; }

            [Key("subPartsId")]
            public int[] SubPartsIds { get; set; }

            [Key("hideBraOpt")]
            public bool[] HideBraOptions { get; set; }

            [Key("hideShortsOpt")]
            public bool[] HideShortsOptions { get; set; }
        }

        [MessagePackObject]
        public sealed class ClothesPartDto
        {
            [Key("id")]
            public int Id { get; set; }

            [Key("colorInfo")]
            public ClothesColorDto[] Colors { get; set; }

            [Key("emblemeId")]
            public int EmblemId { get; set; }

            [Key("emblemeId2")]
            public int EmblemId2 { get; set; }

            [Key("hideOpt")]
            public bool[] HideOptions { get; set; }

            [Key("sleevesType")]
            public int SleevesType { get; set; }
        }

        [MessagePackObject]
        public sealed class ClothesColorDto
        {
            [Key("baseColor")]
            public ColorDto BaseColor { get; set; }

            [Key("pattern")]
            public int Pattern { get; set; }

            [Key("tiling")]
            public Vector2Dto Tiling { get; set; }

            [Key("patternColor")]
            public ColorDto PatternColor { get; set; }
        }

        [MessagePackObject]
        public sealed class AccessoryDto
        {
            [Key("parts")]
            public AccessoryPartDto[] Parts { get; set; }
        }

        [MessagePackObject]
        public sealed class AccessoryPartDto
        {
            [Key("type")]
            public int Type { get; set; }

            [Key("id")]
            public int Id { get; set; }

            [Key("parentKey")]
            public string ParentKey { get; set; }

            [Key("addMove")]
            public Vector3Dto[,] AdditionalMoves { get; set; }

            [Key("color")]
            public ColorDto[] Colors { get; set; }

            [Key("hideCategory")]
            public int HideCategory { get; set; }

            [Key("noShake")]
            public bool NoShake { get; set; }
        }
    }
}
