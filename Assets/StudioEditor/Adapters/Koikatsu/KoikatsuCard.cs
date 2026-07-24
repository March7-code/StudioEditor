using System;
using System.Collections.Generic;
using MessagePack;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    public sealed class KoikatsuCard
    {
        internal KoikatsuCard(
            string sourcePath,
            int productNumber,
            string version,
            KoikatsuCardFace face,
            KoikatsuCardBody body,
            KoikatsuCardHair hair,
            KoikatsuCardParameter parameter,
            IReadOnlyList<KoikatsuCardCoordinate> coordinates,
            int activeCoordinateIndex,
            KoikatsuCardStatus status,
            IReadOnlyDictionary<string, KoikatsuCardBlock> blocks,
            IReadOnlyList<KoikatsuSideloaderResolution> sideloaderResolutions)
        {
            SourcePath = sourcePath;
            ProductNumber = productNumber;
            Version = version;
            Face = face;
            Body = body;
            Hair = hair;
            Parameter = parameter;
            Coordinates = coordinates;
            ActiveCoordinateIndex = activeCoordinateIndex;
            Status = status ?? KoikatsuCardStatus.CreateDefault(
                activeCoordinateIndex);
            Blocks = blocks;
            MaterialEditorSharedTextures =
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            SideloaderResolutions = sideloaderResolutions ??
                Array.Empty<KoikatsuSideloaderResolution>();
        }

        public string SourcePath { get; }

        public int ProductNumber { get; }

        public string Version { get; }

        public KoikatsuCardFace Face { get; }

        public KoikatsuCardBody Body { get; }

        public KoikatsuCardHair Hair { get; }

        public KoikatsuCardParameter Parameter { get; }

        public IReadOnlyList<KoikatsuCardCoordinate> Coordinates { get; }

        public int ActiveCoordinateIndex { get; }

        public KoikatsuCardStatus Status { get; }

        public IReadOnlyDictionary<string, KoikatsuCardBlock> Blocks { get; }

        internal IReadOnlyDictionary<string, byte[]>
            MaterialEditorSharedTextures { get; private set; }

        internal IReadOnlyList<KoikatsuSideloaderResolution>
            SideloaderResolutions { get; }

        internal void AttachMaterialEditorSharedTextures(
            IReadOnlyDictionary<string, byte[]> textures)
        {
            MaterialEditorSharedTextures = textures ??
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }

        internal string FindSideloaderGuid(
            string property,
            int category,
            int slot)
        {
            for (var index = 0; index < SideloaderResolutions.Count; index++)
            {
                var resolution = SideloaderResolutions[index];
                if (resolution.Category == category &&
                    resolution.Slot == slot &&
                    string.Equals(
                        resolution.Property,
                        property,
                        StringComparison.Ordinal))
                {
                    return resolution.Guid;
                }
            }

            return string.Empty;
        }

        public string DisplayName
        {
            get
            {
                var name = $"{Parameter?.LastName} {Parameter?.FirstName}".Trim();
                return string.IsNullOrEmpty(name)
                    ? System.IO.Path.GetFileNameWithoutExtension(SourcePath)
                    : name;
            }
        }
    }

    internal sealed class KoikatsuSideloaderResolution
    {
        public KoikatsuSideloaderResolution(
            string guid,
            int slot,
            string property,
            int category,
            string name)
        {
            Guid = guid ?? string.Empty;
            Slot = slot;
            Property = property ?? string.Empty;
            Category = category;
            Name = name ?? string.Empty;
        }

        public string Guid { get; }

        public int Slot { get; }

        public string Property { get; }

        public int Category { get; }

        public string Name { get; }
    }

    public sealed class KoikatsuCardFace
    {
        internal KoikatsuCardFace(
            int headId,
            int skinId,
            float[] shapeValues,
            KoikatsuCardFaceAppearance appearance)
        {
            HeadId = headId;
            SkinId = skinId;
            ShapeValues = shapeValues ?? Array.Empty<float>();
            Appearance = appearance ?? KoikatsuCardFaceAppearance.CreateDefault();
        }

        public int HeadId { get; }

        public int SkinId { get; }

        public IReadOnlyList<float> ShapeValues { get; }

        public KoikatsuCardFaceAppearance Appearance { get; }
    }

    public sealed class KoikatsuCardBody
    {
        internal KoikatsuCardBody(
            int skinId,
            int typeBone,
            float[] shapeValues,
            KoikatsuCardBodyAppearance appearance)
        {
            SkinId = skinId;
            TypeBone = typeBone;
            ShapeValues = shapeValues ?? Array.Empty<float>();
            Appearance = appearance ?? KoikatsuCardBodyAppearance.CreateDefault();
        }

        public int SkinId { get; }

        public int TypeBone { get; }

        public IReadOnlyList<float> ShapeValues { get; }

        public KoikatsuCardBodyAppearance Appearance { get; }
    }

    public sealed class KoikatsuCardFaceAppearance
    {
        internal KoikatsuCardFaceAppearance(
            int detailId,
            float detailPower,
            float cheekGlossPower,
            int eyebrowId,
            Color eyebrowColor,
            int noseId,
            IReadOnlyList<KoikatsuCardPupil> pupils,
            int highlightUpId,
            Color highlightUpColor,
            int highlightDownId,
            Color highlightDownColor,
            int eyeWhiteId,
            Color eyeWhiteBaseColor,
            Color eyeWhiteSubColor,
            float pupilWidth,
            float pupilHeight,
            float pupilX,
            float pupilY,
            float highlightUpY,
            float highlightDownY,
            int eyelineUpId,
            float eyelineUpWeight,
            int eyelineDownId,
            Color eyelineColor,
            int moleId,
            Color moleColor,
            Vector4 moleLayout,
            int lipLineId,
            Color lipLineColor,
            float lipGlossPower,
            KoikatsuCardMakeup baseMakeup,
            byte foregroundEyes,
            byte foregroundEyebrow)
        {
            DetailId = detailId;
            DetailPower = detailPower;
            CheekGlossPower = cheekGlossPower;
            EyebrowId = eyebrowId;
            EyebrowColor = eyebrowColor;
            NoseId = noseId;
            Pupils = pupils ?? Array.Empty<KoikatsuCardPupil>();
            HighlightUpId = highlightUpId;
            HighlightUpColor = highlightUpColor;
            HighlightDownId = highlightDownId;
            HighlightDownColor = highlightDownColor;
            EyeWhiteId = eyeWhiteId;
            EyeWhiteBaseColor = eyeWhiteBaseColor;
            EyeWhiteSubColor = eyeWhiteSubColor;
            PupilWidth = pupilWidth;
            PupilHeight = pupilHeight;
            PupilX = pupilX;
            PupilY = pupilY;
            HighlightUpY = highlightUpY;
            HighlightDownY = highlightDownY;
            EyelineUpId = eyelineUpId;
            EyelineUpWeight = eyelineUpWeight;
            EyelineDownId = eyelineDownId;
            EyelineColor = eyelineColor;
            MoleId = moleId;
            MoleColor = moleColor;
            MoleLayout = moleLayout;
            LipLineId = lipLineId;
            LipLineColor = lipLineColor;
            LipGlossPower = lipGlossPower;
            BaseMakeup = baseMakeup ?? KoikatsuCardMakeup.CreateDefault();
            ForegroundEyes = foregroundEyes;
            ForegroundEyebrow = foregroundEyebrow;
        }

        public int DetailId { get; }
        public float DetailPower { get; }
        public float CheekGlossPower { get; }
        public int EyebrowId { get; }
        public Color EyebrowColor { get; }
        public int NoseId { get; }
        public IReadOnlyList<KoikatsuCardPupil> Pupils { get; }
        public int HighlightUpId { get; }
        public Color HighlightUpColor { get; }
        public int HighlightDownId { get; }
        public Color HighlightDownColor { get; }
        public int EyeWhiteId { get; }
        public Color EyeWhiteBaseColor { get; }
        public Color EyeWhiteSubColor { get; }
        public float PupilWidth { get; }
        public float PupilHeight { get; }
        public float PupilX { get; }
        public float PupilY { get; }
        public float HighlightUpY { get; }
        public float HighlightDownY { get; }
        public int EyelineUpId { get; }
        public float EyelineUpWeight { get; }
        public int EyelineDownId { get; }
        public Color EyelineColor { get; }
        public int MoleId { get; }
        public Color MoleColor { get; }
        public Vector4 MoleLayout { get; }
        public int LipLineId { get; }
        public Color LipLineColor { get; }
        public float LipGlossPower { get; }
        public KoikatsuCardMakeup BaseMakeup { get; }
        public byte ForegroundEyes { get; }
        public byte ForegroundEyebrow { get; }

        internal static KoikatsuCardFaceAppearance CreateDefault()
        {
            return new KoikatsuCardFaceAppearance(
                0, 0f, 0f, 0, Color.white, 0,
                Array.Empty<KoikatsuCardPupil>(),
                0, Color.white, 0, Color.white,
                0, Color.white, Color.white,
                0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f,
                0, 1f, 0, Color.white,
                0, Color.white, new Vector4(0.5f, 0.5f, 0f, 0.5f),
                 0, Color.white, 0f,
                 KoikatsuCardMakeup.CreateDefault(), 0, 0);
        }
    }

    public sealed class KoikatsuCardPupil
    {
        internal KoikatsuCardPupil(
            int id,
            Color baseColor,
            Color subColor,
            int gradientMaskId,
            float gradientBlend,
            float gradientOffsetY,
            float gradientScale)
        {
            Id = id;
            BaseColor = baseColor;
            SubColor = subColor;
            GradientMaskId = gradientMaskId;
            GradientBlend = gradientBlend;
            GradientOffsetY = gradientOffsetY;
            GradientScale = gradientScale;
        }

        public int Id { get; }
        public Color BaseColor { get; }
        public Color SubColor { get; }
        public int GradientMaskId { get; }
        public float GradientBlend { get; }
        public float GradientOffsetY { get; }
        public float GradientScale { get; }
    }

    public sealed class KoikatsuCardBodyAppearance
    {
        internal KoikatsuCardBodyAppearance(
            int detailId,
            float detailPower,
            Color skinMainColor,
            Color skinSubColor,
            float skinGlossPower,
            IReadOnlyList<int> paintIds,
            IReadOnlyList<Color> paintColors,
            IReadOnlyList<int> paintLayoutIds,
            IReadOnlyList<Vector4> paintLayouts,
            int sunburnId,
            Color sunburnColor,
            int nippleId,
            Color nippleColor,
            int underhairId,
            Color underhairColor,
            Color nailColor)
        {
            DetailId = detailId;
            DetailPower = detailPower;
            SkinMainColor = skinMainColor;
            SkinSubColor = skinSubColor;
            SkinGlossPower = skinGlossPower;
            PaintIds = paintIds ?? Array.Empty<int>();
            PaintColors = paintColors ?? Array.Empty<Color>();
            PaintLayoutIds = paintLayoutIds ?? Array.Empty<int>();
            PaintLayouts = paintLayouts ?? Array.Empty<Vector4>();
            SunburnId = sunburnId;
            SunburnColor = sunburnColor;
            NippleId = nippleId;
            NippleColor = nippleColor;
            UnderhairId = underhairId;
            UnderhairColor = underhairColor;
            NailColor = nailColor;
        }

        public int DetailId { get; }
        public float DetailPower { get; }
        public Color SkinMainColor { get; }
        public Color SkinSubColor { get; }
        public float SkinGlossPower { get; }
        public IReadOnlyList<int> PaintIds { get; }
        public IReadOnlyList<Color> PaintColors { get; }
        public IReadOnlyList<int> PaintLayoutIds { get; }
        public IReadOnlyList<Vector4> PaintLayouts { get; }
        public int SunburnId { get; }
        public Color SunburnColor { get; }
        public int NippleId { get; }
        public Color NippleColor { get; }
        public int UnderhairId { get; }
        public Color UnderhairColor { get; }
        public Color NailColor { get; }

        internal static KoikatsuCardBodyAppearance CreateDefault()
        {
            return new KoikatsuCardBodyAppearance(
                0, 0f, Color.white, Color.white, 0f,
                Array.Empty<int>(), Array.Empty<Color>(),
                Array.Empty<int>(), Array.Empty<Vector4>(),
                0, Color.white, 0, Color.white,
                0, Color.white, Color.white);
        }
    }

    public sealed class KoikatsuCardMakeup
    {
        internal KoikatsuCardMakeup(
            int eyeshadowId,
            Color eyeshadowColor,
            int cheekId,
            Color cheekColor,
            int lipId,
            Color lipColor,
            IReadOnlyList<int> paintIds,
            IReadOnlyList<Color> paintColors,
            IReadOnlyList<Vector4> paintLayouts)
        {
            EyeshadowId = eyeshadowId;
            EyeshadowColor = eyeshadowColor;
            CheekId = cheekId;
            CheekColor = cheekColor;
            LipId = lipId;
            LipColor = lipColor;
            PaintIds = paintIds ?? Array.Empty<int>();
            PaintColors = paintColors ?? Array.Empty<Color>();
            PaintLayouts = paintLayouts ?? Array.Empty<Vector4>();
        }

        public int EyeshadowId { get; }
        public Color EyeshadowColor { get; }
        public int CheekId { get; }
        public Color CheekColor { get; }
        public int LipId { get; }
        public Color LipColor { get; }
        public IReadOnlyList<int> PaintIds { get; }
        public IReadOnlyList<Color> PaintColors { get; }
        public IReadOnlyList<Vector4> PaintLayouts { get; }

        internal static KoikatsuCardMakeup CreateDefault()
        {
            return new KoikatsuCardMakeup(
                0, Color.white, 0, Color.white, 0, Color.white,
                Array.Empty<int>(), Array.Empty<Color>(), Array.Empty<Vector4>());
        }
    }

    public sealed class KoikatsuCardHair
    {
        internal const int PartCount = 4;

        internal KoikatsuCardHair(
            int kind,
            int glossId,
            IReadOnlyList<KoikatsuCardHairPart> parts)
        {
            Kind = kind;
            GlossId = glossId;

            var normalizedParts = new KoikatsuCardHairPart[PartCount];
            var partIds = new int[PartCount];
            for (var index = 0; index < normalizedParts.Length; index++)
            {
                var part = parts != null && index < parts.Count
                    ? parts[index]
                    : null;
                part = part ?? KoikatsuCardHairPart.CreateDefault();
                normalizedParts[index] = part;
                partIds[index] = part.Id;
            }

            Parts = Array.AsReadOnly(normalizedParts);
            PartIds = Array.AsReadOnly(partIds);
        }

        public int Kind { get; }

        public int GlossId { get; }

        public IReadOnlyList<KoikatsuCardHairPart> Parts { get; }

        public IReadOnlyList<int> PartIds { get; }
    }

    public sealed class KoikatsuCardHairPart
    {
        internal KoikatsuCardHairPart(
            int id,
            Color baseColor,
            Color startColor,
            Color endColor,
            float length,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            IReadOnlyList<Color> accessoryColors,
            Color outlineColor,
            bool noShake)
        {
            Id = id;
            BaseColor = baseColor;
            StartColor = startColor;
            EndColor = endColor;
            Length = length;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            AccessoryColors = accessoryColors ?? Array.Empty<Color>();
            OutlineColor = outlineColor;
            NoShake = noShake;
        }

        public int Id { get; }

        public Color BaseColor { get; }

        public Color StartColor { get; }

        public Color EndColor { get; }

        public float Length { get; }

        public Vector3 Position { get; }

        public Vector3 Rotation { get; }

        public Vector3 Scale { get; }

        public IReadOnlyList<Color> AccessoryColors { get; }

        public Color OutlineColor { get; }

        public bool NoShake { get; }

        internal static KoikatsuCardHairPart CreateDefault()
        {
            return new KoikatsuCardHairPart(
                0,
                Color.white,
                Color.white,
                Color.white,
                0f,
                Vector3.zero,
                Vector3.zero,
                Vector3.one,
                Array.Empty<Color>(),
                Color.black,
                false);
        }
    }

    public sealed class KoikatsuCardParameter
    {
        internal KoikatsuCardParameter(byte sex, string lastName, string firstName)
        {
            Sex = sex;
            LastName = lastName ?? string.Empty;
            FirstName = firstName ?? string.Empty;
        }

        public byte Sex { get; }

        public string LastName { get; }

        public string FirstName { get; }
    }

    public sealed class KoikatsuCardStatus
    {
        internal KoikatsuCardStatus(
            int activeCoordinateIndex,
            int eyebrowPattern,
            float eyebrowOpenMax,
            int eyesPattern,
            float eyesOpenMax,
            bool eyesBlink,
            int mouthPattern,
            int eyesLookPattern,
            byte[] clothesStates,
            byte shoesType,
            bool[] showAccessories,
            bool hideEyesHighlight)
        {
            ActiveCoordinateIndex = Math.Max(activeCoordinateIndex, 0);
            EyebrowPattern = Math.Max(eyebrowPattern, 0);
            EyebrowOpenMax = Mathf.Clamp01(eyebrowOpenMax);
            EyesPattern = Math.Max(eyesPattern, 0);
            EyesOpenMax = Mathf.Clamp01(eyesOpenMax);
            EyesBlink = eyesBlink;
            MouthPattern = Math.Max(mouthPattern, 0);
            EyesLookPattern = Math.Max(eyesLookPattern, 0);
            ClothesStates = clothesStates ?? new byte[9];
            ShoesType = shoesType;
            ShowAccessories = showAccessories ?? CreateVisibleAccessories();
            HideEyesHighlight = hideEyesHighlight;
        }

        public int ActiveCoordinateIndex { get; }

        public int EyebrowPattern { get; }

        public float EyebrowOpenMax { get; }

        public int EyesPattern { get; }

        public float EyesOpenMax { get; }

        public bool EyesBlink { get; }

        public int MouthPattern { get; }

        public int EyesLookPattern { get; }

        public IReadOnlyList<byte> ClothesStates { get; }

        public byte ShoesType { get; }

        public IReadOnlyList<bool> ShowAccessories { get; }

        public bool HideEyesHighlight { get; }

        internal static KoikatsuCardStatus CreateDefault(
            int activeCoordinateIndex = 0)
        {
            return new KoikatsuCardStatus(
                activeCoordinateIndex,
                0,
                1f,
                0,
                1f,
                true,
                0,
                0,
                new byte[9],
                0,
                CreateVisibleAccessories(),
                false);
        }

        private static bool[] CreateVisibleAccessories()
        {
            var values = new bool[20];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = true;
            }

            return values;
        }
    }

    public sealed class KoikatsuCardCoordinate
    {
        internal KoikatsuCardCoordinate(
            IReadOnlyList<KoikatsuCardClothesPart> clothes,
            int[] subPartsIds,
            bool[] hideBraOptions,
            bool[] hideShortsOptions,
            IReadOnlyList<KoikatsuCardAccessory> accessories,
            bool makeupEnabled,
            KoikatsuCardMakeup makeup)
        {
            Clothes = clothes ?? Array.Empty<KoikatsuCardClothesPart>();
            var clothesIds = new int[Clothes.Count];
            for (var index = 0; index < clothesIds.Length; index++)
            {
                clothesIds[index] = Clothes[index].Id;
            }

            ClothesIds = Array.AsReadOnly(clothesIds);
            SubPartsIds = subPartsIds ?? Array.Empty<int>();
            HideBraOptions = hideBraOptions ?? Array.Empty<bool>();
            HideShortsOptions = hideShortsOptions ?? Array.Empty<bool>();
            Accessories = accessories ?? Array.Empty<KoikatsuCardAccessory>();
            MakeupEnabled = makeupEnabled;
            Makeup = makeup ?? KoikatsuCardMakeup.CreateDefault();
        }

        public IReadOnlyList<int> ClothesIds { get; }

        public IReadOnlyList<KoikatsuCardClothesPart> Clothes { get; }

        public IReadOnlyList<int> SubPartsIds { get; }

        public IReadOnlyList<bool> HideBraOptions { get; }

        public IReadOnlyList<bool> HideShortsOptions { get; }

        public IReadOnlyList<KoikatsuCardAccessory> Accessories { get; }

        public bool MakeupEnabled { get; }

        public KoikatsuCardMakeup Makeup { get; }
    }

    public sealed class KoikatsuCardClothesPart
    {
        internal KoikatsuCardClothesPart(
            int id,
            IReadOnlyList<KoikatsuCardClothesColor> colors,
            int emblemId,
            int emblemId2,
            bool[] hideOptions,
            int sleevesType)
        {
            Id = id;
            Colors = colors ?? Array.Empty<KoikatsuCardClothesColor>();
            EmblemId = emblemId;
            EmblemId2 = emblemId2;
            HideOptions = hideOptions ?? Array.Empty<bool>();
            SleevesType = sleevesType;
        }

        public int Id { get; }

        public IReadOnlyList<KoikatsuCardClothesColor> Colors { get; }

        public int EmblemId { get; }

        public int EmblemId2 { get; }

        public IReadOnlyList<bool> HideOptions { get; }

        public int SleevesType { get; }
    }

    public sealed class KoikatsuCardClothesColor
    {
        internal KoikatsuCardClothesColor(
            Color baseColor,
            int pattern,
            Vector2 tiling,
            Color patternColor)
        {
            BaseColor = baseColor;
            Pattern = pattern;
            Tiling = tiling;
            PatternColor = patternColor;
        }

        public Color BaseColor { get; }

        public int Pattern { get; }

        public Vector2 Tiling { get; }

        public Color PatternColor { get; }
    }

    public sealed class KoikatsuCardAccessory
    {
        internal KoikatsuCardAccessory(
            int type,
            int id,
            string parentKey,
            Vector3[,] additionalMoves,
            IReadOnlyList<Color> colors,
            int hideCategory,
            bool noShake)
        {
            Type = type;
            Id = id;
            ParentKey = parentKey ?? string.Empty;
            AdditionalMoves = additionalMoves ?? CreateDefaultMoves();
            Colors = colors ?? Array.Empty<Color>();
            HideCategory = hideCategory;
            NoShake = noShake;
        }

        public int Type { get; }

        public int Id { get; }

        public string ParentKey { get; }

        public Vector3[,] AdditionalMoves { get; }

        public IReadOnlyList<Color> Colors { get; }

        public int HideCategory { get; }

        public bool NoShake { get; }

        private static Vector3[,] CreateDefaultMoves()
        {
            var result = new Vector3[2, 3];
            for (var index = 0; index < 2; index++)
            {
                result[index, 2] = Vector3.one;
            }

            return result;
        }
    }

    public sealed class KoikatsuCardBlock
    {
        internal KoikatsuCardBlock(string name, string version, byte[] data)
        {
            Name = name;
            Version = version;
            Data = data;
        }

        public string Name { get; }

        public string Version { get; }

        public byte[] Data { get; }
    }

    internal sealed class KoikatsuMaterialEditorData
    {
        private const string PluginId =
            "com.deathweasel.bepinex.materialeditor";
        private const int CharacterObjectType = 4;

        private readonly IReadOnlyDictionary<int, byte[]> textures;
        private readonly IReadOnlyList<KoikatsuCardReader.MaterialColorPropertyDto>
            colorProperties;
        private readonly IReadOnlyList<KoikatsuCardReader.MaterialFloatPropertyDto>
            floatProperties;
        private readonly IReadOnlyList<KoikatsuCardReader.MaterialTexturePropertyDto>
            textureProperties;

        private KoikatsuMaterialEditorData(
            IReadOnlyDictionary<int, byte[]> textures,
            IReadOnlyList<KoikatsuCardReader.MaterialColorPropertyDto>
                colorProperties,
            IReadOnlyList<KoikatsuCardReader.MaterialFloatPropertyDto>
                floatProperties,
            IReadOnlyList<KoikatsuCardReader.MaterialTexturePropertyDto>
                textureProperties)
        {
            this.textures = textures;
            this.colorProperties = colorProperties;
            this.floatProperties = floatProperties;
            this.textureProperties = textureProperties;
        }

        public static KoikatsuMaterialEditorData Read(
            IReadOnlyDictionary<string, KoikatsuCardBlock> blocks,
            IReadOnlyDictionary<string, byte[]> sharedTextures = null)
        {
            if (blocks == null ||
                !blocks.TryGetValue("KKEx", out var extendedBlock))
            {
                return null;
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    extendedBlock.Data);
                if (plugins == null ||
                    !plugins.TryGetValue(PluginId, out var plugin) ||
                    plugin?.Data == null)
                {
                    return null;
                }

                var textures = ReadValue<Dictionary<int, byte[]>>(
                                   plugin.Data,
                                   "TextureDictionary") ??
                               new Dictionary<int, byte[]>();
                var dedupedTextures = ReadValue<Dictionary<int, string>>(
                    plugin.Data,
                    "DEDUPED_TextureDictionary");
                if (dedupedTextures != null && sharedTextures != null)
                {
                    foreach (var pair in dedupedTextures)
                    {
                        if (!textures.ContainsKey(pair.Key) &&
                            !string.IsNullOrWhiteSpace(pair.Value) &&
                            sharedTextures.TryGetValue(
                                pair.Value,
                                out var textureData) &&
                            textureData != null && textureData.Length > 0)
                        {
                            textures.Add(pair.Key, textureData);
                        }
                    }
                }
                var colorProperties = ReadValue<List<
                                         KoikatsuCardReader.
                                             MaterialColorPropertyDto>>(
                                     plugin.Data,
                                     "MaterialColorPropertyList") ??
                                 new List<KoikatsuCardReader.
                                     MaterialColorPropertyDto>();
                var floatProperties = ReadValue<List<
                                         KoikatsuCardReader.
                                             MaterialFloatPropertyDto>>(
                                     plugin.Data,
                                     "MaterialFloatPropertyList") ??
                                 new List<KoikatsuCardReader.
                                     MaterialFloatPropertyDto>();
                var textureProperties = ReadValue<List<
                                         KoikatsuCardReader.
                                             MaterialTexturePropertyDto>>(
                                     plugin.Data,
                                     "MaterialTexturePropertyList") ??
                                 new List<KoikatsuCardReader.
                                     MaterialTexturePropertyDto>();
                return new KoikatsuMaterialEditorData(
                    textures,
                    colorProperties,
                    floatProperties,
                    textureProperties);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu MaterialEditor data from the " +
                    $"card: {exception.Message}");
                return null;
            }
        }

        public bool TryGetCharacterTexture(
            string materialName,
            string propertyName,
            out byte[] data)
        {
            return TryGetTexture(
                CharacterObjectType,
                -1,
                -1,
                materialName,
                propertyName,
                out data);
        }

        public bool TryGetTexture(
            int objectType,
            int coordinateIndex,
            int slot,
            string materialName,
            string propertyName,
            out byte[] data)
        {
            for (var index = textureProperties.Count - 1; index >= 0; index--)
            {
                var property = textureProperties[index];
                if (property == null ||
                    property.ObjectType != objectType ||
                    !CoordinateMatches(
                        objectType,
                        coordinateIndex,
                        property.CoordinateIndex) ||
                    slot >= 0 && property.Slot != slot ||
                    !property.TextureId.HasValue ||
                    !NamesMatch(property.MaterialName, materialName) ||
                    !NamesMatch(property.Property, propertyName))
                {
                    continue;
                }

                if (textures.TryGetValue(property.TextureId.Value, out data) &&
                    data != null && data.Length != 0)
                {
                    return true;
                }
            }

            data = null;
            return false;
        }

        public IEnumerable<
            KoikatsuCardReader.MaterialTexturePropertyDto>
            GetTextureProperties(
                int objectType,
                int coordinateIndex,
                int slot,
                string materialName)
        {
            for (var index = 0; index < textureProperties.Count; index++)
            {
                var property = textureProperties[index];
                if (property == null ||
                    property.ObjectType != objectType ||
                    !CoordinateMatches(
                        objectType,
                        coordinateIndex,
                        property.CoordinateIndex) ||
                    slot >= 0 && property.Slot != slot ||
                    !property.TextureId.HasValue ||
                    !NamesMatch(property.MaterialName, materialName) ||
                    !textures.TryGetValue(property.TextureId.Value, out var data) ||
                    data == null || data.Length == 0)
                {
                    continue;
                }

                yield return property;
            }
        }

        public bool TryGetTextureData(int textureId, out byte[] data)
        {
            return textures.TryGetValue(textureId, out data) &&
                   data != null && data.Length != 0;
        }

        public IEnumerable<KoikatsuCardReader.MaterialColorPropertyDto>
            GetColorProperties(
                int objectType,
                int coordinateIndex,
                int slot,
                string materialName)
        {
            for (var index = 0; index < colorProperties.Count; index++)
            {
                var property = colorProperties[index];
                if (property == null ||
                    property.ObjectType != objectType ||
                    !CoordinateMatches(
                        objectType,
                        coordinateIndex,
                        property.CoordinateIndex) ||
                    slot >= 0 && property.Slot != slot ||
                    property.Value == null ||
                    !NamesMatch(property.MaterialName, materialName))
                {
                    continue;
                }

                yield return property;
            }
        }

        public IEnumerable<KoikatsuCardReader.MaterialFloatPropertyDto>
            GetFloatProperties(
                int objectType,
                int coordinateIndex,
                int slot,
                string materialName)
        {
            for (var index = 0; index < floatProperties.Count; index++)
            {
                var property = floatProperties[index];
                if (property == null ||
                    property.ObjectType != objectType ||
                    !CoordinateMatches(
                        objectType,
                        coordinateIndex,
                        property.CoordinateIndex) ||
                    slot >= 0 && property.Slot != slot ||
                    !NamesMatch(property.MaterialName, materialName))
                {
                    continue;
                }

                yield return property;
            }
        }

        public bool TryGetColor(
            int objectType,
            int coordinateIndex,
            int slot,
            string materialName,
            string propertyName,
            out Color color)
        {
            for (var index = colorProperties.Count - 1; index >= 0; index--)
            {
                var property = colorProperties[index];
                if (property == null ||
                    property.ObjectType != objectType ||
                    !CoordinateMatches(
                        objectType,
                        coordinateIndex,
                        property.CoordinateIndex) ||
                    slot >= 0 && property.Slot != slot ||
                    property.Value == null ||
                    !NamesMatch(property.MaterialName, materialName) ||
                    !NamesMatch(property.Property, propertyName))
                {
                    continue;
                }

                color = property.Value.ToColor(Color.white);
                return true;
            }

            color = default;
            return false;
        }

        public bool TryGetFloat(
            int objectType,
            int coordinateIndex,
            int slot,
            string materialName,
            string propertyName,
            out float value)
        {
            for (var index = floatProperties.Count - 1; index >= 0; index--)
            {
                var property = floatProperties[index];
                if (property == null ||
                    property.ObjectType != objectType ||
                    !CoordinateMatches(
                        objectType,
                        coordinateIndex,
                        property.CoordinateIndex) ||
                    slot >= 0 && property.Slot != slot ||
                    !NamesMatch(property.MaterialName, materialName) ||
                    !NamesMatch(property.Property, propertyName) ||
                    !float.TryParse(
                        property.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value))
                {
                    continue;
                }

                return true;
            }

            value = 0f;
            return false;
        }

        private static bool CoordinateMatches(
            int objectType,
            int requestedCoordinateIndex,
            int propertyCoordinateIndex)
        {
            return objectType == CharacterObjectType ||
                   requestedCoordinateIndex < 0 ||
                   propertyCoordinateIndex == requestedCoordinateIndex;
        }

        private static T ReadValue<T>(
            IReadOnlyDictionary<string, object> data,
            string key)
            where T : class
        {
            return data.TryGetValue(key, out var raw) && raw is byte[] bytes
                ? MessagePackSerializer.Deserialize<T>(bytes)
                : null;
        }

        private static bool NamesMatch(string left, string right)
        {
            return string.Equals(
                NormalizeName(left),
                NormalizeName(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeName(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            var previewSuffix = normalized.IndexOf(
                " (Koikatsu ",
                StringComparison.Ordinal);
            if (previewSuffix >= 0)
            {
                normalized = normalized.Substring(0, previewSuffix);
            }

            var instanceSuffix = normalized.IndexOf(
                " (Instance)",
                StringComparison.OrdinalIgnoreCase);
            if (instanceSuffix >= 0)
            {
                normalized = normalized.Substring(0, instanceSuffix);
            }

            return normalized.TrimStart('_');
        }

    }

    internal enum KoikatsuSkinOverlayType
    {
        BodyOver = 1,
        FaceOver = 2,
        BodyUnder = 3,
        FaceUnder = 4,
        EyeUnder = 5,
        EyeOver = 6,
        EyeUnderLeft = 7,
        EyeOverLeft = 8,
        EyeUnderRight = 9,
        EyeOverRight = 10,
        EyebrowUnder = 20,
        EyelineUnder = 30,
    }

    internal sealed class KoikatsuSkinOverlayData
    {
        private const string PluginId = "KSOX";
        private const string TexturePrefix = "_TextureID_";

        private readonly IReadOnlyDictionary<int, byte[]> textures;
        private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>>
            lookup;

        private KoikatsuSkinOverlayData(
            IReadOnlyDictionary<int, byte[]> textures,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> lookup)
        {
            this.textures = textures;
            this.lookup = lookup;
        }

        public static KoikatsuSkinOverlayData Read(
            IReadOnlyDictionary<string, KoikatsuCardBlock> blocks)
        {
            if (blocks == null ||
                !blocks.TryGetValue("KKEx", out var block) ||
                block?.Data == null)
            {
                return null;
            }

            try
            {
                var plugins = MessagePackSerializer.Deserialize<
                    Dictionary<string, KoikatsuCardReader.PluginDataDto>>(
                    block.Data);
                if (plugins == null ||
                    !TryGetPlugin(plugins, out var plugin) ||
                    plugin?.Data == null ||
                    !plugin.Data.TryGetValue("Lookup", out var rawLookup) ||
                    !(rawLookup is byte[] lookupBytes))
                {
                    return null;
                }

                var lookup = MessagePackSerializer.Deserialize<
                    Dictionary<int, Dictionary<int, int>>>(lookupBytes);
                if (lookup == null)
                {
                    return null;
                }

                var textures = new Dictionary<int, byte[]>();
                foreach (var pair in plugin.Data)
                {
                    if (!pair.Key.StartsWith(
                            TexturePrefix,
                            StringComparison.Ordinal) ||
                        !(pair.Value is byte[] bytes) ||
                        bytes.Length == 0 ||
                        !int.TryParse(
                            pair.Key.Substring(TexturePrefix.Length),
                            out var textureId))
                    {
                        continue;
                    }

                    textures[textureId] = bytes;
                }

                var normalizedLookup = new Dictionary<
                    int,
                    IReadOnlyDictionary<int, int>>();
                foreach (var coordinate in lookup)
                {
                    normalizedLookup[coordinate.Key] = coordinate.Value;
                }

                return new KoikatsuSkinOverlayData(
                    textures,
                    normalizedLookup);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read KSOX skin overlay data from the card: " +
                    exception.Message);
                return null;
            }
        }

        public bool TryGetTexture(
            int coordinateIndex,
            KoikatsuSkinOverlayType type,
            out byte[] bytes)
        {
            bytes = null;
            if (lookup == null || textures == null)
            {
                return false;
            }

            if (!lookup.TryGetValue(coordinateIndex, out var coordinate) &&
                !lookup.TryGetValue(0, out coordinate) &&
                lookup.Count != 0)
            {
                foreach (var pair in lookup)
                {
                    coordinate = pair.Value;
                    break;
                }
            }

            if (coordinate == null ||
                !coordinate.TryGetValue((int)type, out var textureId) ||
                !textures.TryGetValue(textureId, out bytes) ||
                bytes == null ||
                bytes.Length == 0)
            {
                bytes = null;
                return false;
            }

            return true;
        }

        private static bool TryGetPlugin(
            IReadOnlyDictionary<string, KoikatsuCardReader.PluginDataDto> plugins,
            out KoikatsuCardReader.PluginDataDto plugin)
        {
            if (plugins.TryGetValue(PluginId, out plugin))
            {
                return true;
            }

            foreach (var pair in plugins)
            {
                if (string.Equals(
                        pair.Key,
                        PluginId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    plugin = pair.Value;
                    return true;
                }
            }

            plugin = null;
            return false;
        }
    }
}
