using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuShapeApplicator
    {
        private const string ShapeBundlePath = "list/customshape.unity3d";
        private const string CorrectionBundlePath =
            "list/shapecorrect/shapecorrect.unity3d";

        public static void Apply(
            KoikatsuCard card,
            string abdataRoot,
            AssetBundle baseBundle,
            AssetBundle headBundle,
            KoikatsuListEntry headEntry,
            KoikatsuListCatalog catalog,
            Transform bodySkeleton,
            Transform headSkeleton)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            var shapeSources = catalog.ResolveBundleCandidates(
                abdataRoot,
                ShapeBundlePath);
            using (var bodyShapeLease =
                   KoikatsuVirtualAssetLoader.AcquireAsset<TextAsset>(
                       shapeSources,
                       "cf_custombody",
                       out var bodyCategoryAsset,
                       out _))
            using (var headShapeLease =
                   KoikatsuVirtualAssetLoader.AcquireAsset<TextAsset>(
                       shapeSources,
                       "cf_customhead",
                       out var headCategoryAsset,
                       out _))
            {
                if (bodyShapeLease == null || headShapeLease == null)
                {
                    throw new InvalidDataException(
                        "Koikatsu custom shape tables were not found in any " +
                        "Sideloader candidate bundle.");
                }

                var bodyAnimation = ShapeAnimation.Read(
                    LoadRequiredTextAsset(baseBundle, "cf_anmShapeBody").bytes);
                var bodyCategories = ShapeCategoryTable.Read(
                    bodyCategoryAsset.bytes);
                var bodyValues = Evaluate(
                    bodyAnimation,
                    bodyCategories,
                    card.Body.ShapeValues);

                var corrections = ReadCorrections(abdataRoot, catalog);
                new FemaleBodyOutput(
                    bodySkeleton,
                    bodyValues,
                    corrections,
                    card.Body.TypeBone).Apply();

                var shapeAnimationName = headEntry.Get("ShapeAnime");
                if (string.IsNullOrWhiteSpace(shapeAnimationName))
                {
                    throw new InvalidDataException(
                        "The selected Koikatsu head has no ShapeAnime entry.");
                }

                var headAnimation = ShapeAnimation.Read(
                    LoadRequiredTextAsset(
                        headBundle,
                        shapeAnimationName).bytes);
                var headCategories = ShapeCategoryTable.Read(
                    headCategoryAsset.bytes);
                var headValues = Evaluate(
                    headAnimation,
                    headCategories,
                    card.Face.ShapeValues);
                var headCorrection = card.Body.TypeBone != 0 &&
                                     corrections.Length > 2
                    ? 1f / (1f + corrections[2].vctScl.y)
                    : 1f;
                ApplyHead(
                    headSkeleton,
                    headValues,
                    card.Body.TypeBone,
                    headCorrection);
            }
        }

        private static Dictionary<string, BoneInfo> Evaluate(
            ShapeAnimation animation,
            ShapeCategoryTable categories,
            IReadOnlyList<float> parameters)
        {
            var values = new Dictionary<string, BoneInfo>(
                StringComparer.Ordinal);
            foreach (var pair in categories.Entries)
            {
                var category = pair.Key;
                var rate = category < parameters.Count
                    ? Mathf.Clamp01(parameters[category])
                    : 0.5f;
                for (var index = 0; index < pair.Value.Count; index++)
                {
                    var mapping = pair.Value[index];
                    if (!values.TryGetValue(mapping.Name, out var destination))
                    {
                        destination = new BoneInfo();
                        values.Add(mapping.Name, destination);
                    }

                    var sample = animation.Sample(mapping.Name, rate);
                    CopyChannels(
                        destination,
                        sample,
                        mapping.Position,
                        mapping.Rotation,
                        mapping.Scale);
                }
            }

            return values;
        }

        private static void CopyChannels(
            BoneInfo destination,
            BoneInfo source,
            bool[] position,
            bool[] rotation,
            bool[] scale)
        {
            destination.vctPos = CopyVector(
                destination.vctPos,
                source.vctPos,
                position);
            destination.vctRot = CopyVector(
                destination.vctRot,
                source.vctRot,
                rotation);
            destination.vctScl = CopyVector(
                destination.vctScl,
                source.vctScl,
                scale);
            MergeMask(destination.PositionChannels, position);
            MergeMask(destination.RotationChannels, rotation);
            MergeMask(destination.ScaleChannels, scale);
        }

        private static Vector3 CopyVector(
            Vector3 destination,
            Vector3 source,
            bool[] channels)
        {
            if (channels[0])
            {
                destination.x = source.x;
            }

            if (channels[1])
            {
                destination.y = source.y;
            }

            if (channels[2])
            {
                destination.z = source.z;
            }

            return destination;
        }

        private static void MergeMask(bool[] destination, bool[] source)
        {
            for (var index = 0; index < 3; index++)
            {
                destination[index] |= source[index];
            }
        }

        private static void ApplyHead(
            Transform root,
            IReadOnlyDictionary<string, BoneInfo> values,
            int typeBone,
            float headCorrection)
        {
            var transforms = BuildNameMap(root);
            foreach (var pair in values)
            {
                if (!transforms.TryGetValue(pair.Key, out var target))
                {
                    continue;
                }

                var value = pair.Value;
                if (string.Equals(
                        pair.Key,
                        "cf_J_FaceBase",
                        StringComparison.Ordinal))
                {
                    var parentScale = target.parent.lossyScale;
                    var uniformScale = parentScale.y *
                                       (typeBone != 0 ? headCorrection : 1f);
                    target.localScale = new Vector3(
                        uniformScale / parentScale.x +
                        (value.vctScl.x - 1f),
                        uniformScale / parentScale.y,
                        uniformScale / parentScale.z);
                    continue;
                }

                target.localPosition = CopyVector(
                    target.localPosition,
                    value.vctPos,
                    value.PositionChannels);
                target.localEulerAngles = CopyVector(
                    target.localEulerAngles,
                    value.vctRot,
                    value.RotationChannels);
                target.localScale = CopyVector(
                    target.localScale,
                    value.vctScl,
                    value.ScaleChannels);
            }
        }

        private static BoneInfo[] ReadCorrections(
            string abdataRoot,
            KoikatsuListCatalog catalog)
        {
            var sources = catalog.ResolveBundleCandidates(
                abdataRoot,
                CorrectionBundlePath);
            using (var lease =
                   KoikatsuVirtualAssetLoader.AcquireAsset<TextAsset>(
                       sources,
                       "shapecorrect",
                       out var asset,
                       out _))
            {
                if (lease == null || asset == null)
                {
                    throw new InvalidDataException(
                        "Koikatsu shape correction data was not found in any " +
                        "Sideloader candidate bundle.");
                }

                var data = asset.bytes;
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream))
                {
                    var count = reader.ReadInt32();
                    if (count < 0 || count > 1024)
                    {
                        throw new InvalidDataException(
                            $"Invalid Koikatsu shape correction count {count}.");
                    }

                    var result = new BoneInfo[count];
                    for (var index = 0; index < count; index++)
                    {
                        result[index] = new BoneInfo(
                            ReadVector3(reader),
                            ReadVector3(reader),
                            ReadVector3(reader));
                    }

                    return result;
                }
            }
        }

        private static TextAsset LoadRequiredTextAsset(
            AssetBundle bundle,
            string assetName)
        {
            var asset = bundle.LoadAsset<TextAsset>(assetName);
            if (asset == null)
            {
                throw new InvalidDataException(
                    $"Koikatsu AssetBundle is missing TextAsset '{assetName}'.");
            }

            return asset;
        }

        private static Dictionary<string, Transform> BuildNameMap(
            Transform root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (!result.ContainsKey(transforms[index].name))
                {
                    result.Add(transforms[index].name, transforms[index]);
                }
            }

            return result;
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        private sealed class ShapeAnimation
        {
            private readonly Dictionary<string, Keyframe[]> tracks;

            private ShapeAnimation(Dictionary<string, Keyframe[]> tracks)
            {
                this.tracks = tracks;
            }

            public static ShapeAnimation Read(byte[] data)
            {
                var result = new Dictionary<string, Keyframe[]>(
                    StringComparer.Ordinal);
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream))
                {
                    var trackCount = reader.ReadInt32();
                    if (trackCount < 0 || trackCount > 10000)
                    {
                        throw new InvalidDataException(
                            $"Invalid Koikatsu shape track count {trackCount}.");
                    }

                    for (var trackIndex = 0;
                         trackIndex < trackCount;
                         trackIndex++)
                    {
                        var name = reader.ReadString();
                        var keyCount = reader.ReadInt32();
                        if (keyCount <= 0 || keyCount > 1000)
                        {
                            throw new InvalidDataException(
                                $"Invalid key count {keyCount} for '{name}'.");
                        }

                        var keys = new Keyframe[keyCount];
                        for (var keyIndex = 0;
                             keyIndex < keyCount;
                             keyIndex++)
                        {
                            reader.ReadInt32();
                            keys[keyIndex] = new Keyframe(
                                ReadVector3(reader),
                                ReadVector3(reader),
                                ReadVector3(reader));
                        }

                        result[name] = keys;
                    }
                }

                return new ShapeAnimation(result);
            }

            public BoneInfo Sample(string name, float rate)
            {
                if (!tracks.TryGetValue(name, out var keys))
                {
                    throw new InvalidDataException(
                        $"Koikatsu shape animation has no track '{name}'.");
                }

                if (rate <= 0f)
                {
                    return keys[0].ToBoneInfo();
                }

                if (rate >= 1f)
                {
                    return keys[keys.Length - 1].ToBoneInfo();
                }

                var frame = (keys.Length - 1) * rate;
                var first = Mathf.FloorToInt(frame);
                var blend = frame - first;
                return new BoneInfo(
                    Vector3.Lerp(
                        keys[first].Position,
                        keys[first + 1].Position,
                        blend),
                    new Vector3(
                        Mathf.LerpAngle(
                            keys[first].Rotation.x,
                            keys[first + 1].Rotation.x,
                            blend),
                        Mathf.LerpAngle(
                            keys[first].Rotation.y,
                            keys[first + 1].Rotation.y,
                            blend),
                        Mathf.LerpAngle(
                            keys[first].Rotation.z,
                            keys[first + 1].Rotation.z,
                            blend)),
                    Vector3.Lerp(
                        keys[first].Scale,
                        keys[first + 1].Scale,
                        blend));
            }
        }

        private sealed class ShapeCategoryTable
        {
            private ShapeCategoryTable(
                Dictionary<int, List<CategoryEntry>> entries)
            {
                Entries = entries;
            }

            public IReadOnlyDictionary<int, List<CategoryEntry>> Entries
            {
                get;
            }

            public static ShapeCategoryTable Read(byte[] data)
            {
                var entries = new Dictionary<int, List<CategoryEntry>>();
                var text = Encoding.UTF8.GetString(data);
                var lines = text.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                for (var lineIndex = 0;
                     lineIndex < lines.Length;
                     lineIndex++)
                {
                    var columns = lines[lineIndex].Split('\t');
                    if (columns.Length < 11)
                    {
                        throw new InvalidDataException(
                            $"Malformed Koikatsu shape row {lineIndex + 1}.");
                    }

                    var category = int.Parse(
                        columns[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture);
                    if (!entries.TryGetValue(category, out var list))
                    {
                        list = new List<CategoryEntry>();
                        entries.Add(category, list);
                    }

                    list.Add(new CategoryEntry(
                        columns[1],
                        ReadMask(columns, 2),
                        ReadMask(columns, 5),
                        ReadMask(columns, 8)));
                }

                return new ShapeCategoryTable(entries);
            }

            private static bool[] ReadMask(string[] columns, int offset)
            {
                return new[]
                {
                    columns[offset] != "0",
                    columns[offset + 1] != "0",
                    columns[offset + 2] != "0",
                };
            }
        }

        private sealed class CategoryEntry
        {
            public CategoryEntry(
                string name,
                bool[] position,
                bool[] rotation,
                bool[] scale)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public string Name { get; }

            public bool[] Position { get; }

            public bool[] Rotation { get; }

            public bool[] Scale { get; }
        }

        private sealed class Keyframe
        {
            public Keyframe(
                Vector3 position,
                Vector3 rotation,
                Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public Vector3 Position { get; }

            public Vector3 Rotation { get; }

            public Vector3 Scale { get; }

            public BoneInfo ToBoneInfo()
            {
                return new BoneInfo(Position, Rotation, Scale);
            }
        }

        private sealed class BoneInfo
        {
            public BoneInfo()
                : this(Vector3.zero, Vector3.zero, Vector3.one)
            {
            }

            public BoneInfo(
                Vector3 position,
                Vector3 rotation,
                Vector3 scale)
            {
                vctPos = position;
                vctRot = rotation;
                vctScl = scale;
            }

            public Transform trfBone;
            public Vector3 vctPos;
            public Vector3 vctRot;
            public Vector3 vctScl;
            public bool[] PositionChannels { get; } = new bool[3];
            public bool[] RotationChannels { get; } = new bool[3];
            public bool[] ScaleChannels { get; } = new bool[3];
        }

        private sealed class FemaleBodyOutput
        {
            private static readonly string[] DestinationNames =
            {
                "cf_n_height",
                "cf_s_hand_L",
                "cf_s_hand_R",
                "cf_s_head",
                "cf_s_neck",
                "cf_s_spine03",
                "cf_s_shoulder02_L",
                "cf_s_shoulder02_R",
                "cf_s_arm01_L",
                "cf_s_arm01_R",
                "cf_s_arm02_L",
                "cf_s_arm02_R",
                "cf_s_arm03_L",
                "cf_s_arm03_R",
                "cf_s_forearm01_L",
                "cf_s_forearm01_R",
                "cf_s_forearm02_L",
                "cf_s_forearm02_R",
                "cf_s_wrist_L",
                "cf_s_wrist_R",
                "cf_s_spine02",
                "cf_s_spine01",
                "cf_s_waist01",
                "cf_s_waist02",
                "cf_s_siri_L",
                "cf_s_siri_R",
                "cf_s_thigh01_L",
                "cf_s_thigh01_R",
                "cf_s_thigh02_L",
                "cf_s_thigh02_R",
                "cf_s_thigh03_L",
                "cf_s_thigh03_R",
                "cf_s_leg01_L",
                "cf_s_leg01_R",
                "cf_s_leg02_L",
                "cf_s_leg02_R",
                "cf_s_leg03_L",
                "cf_s_leg03_R",
                "cf_d_kokan",
                "cf_s_bust00_L",
                "cf_d_bust01_L",
                "cf_d_bust02_L",
                "cf_d_bust03_L",
                "cf_s_bust01_L",
                "cf_s_bust02_L",
                "cf_s_bust03_L",
                "cf_hit_bust02_L",
                "cf_d_bnip01_L",
                "cf_s_bnip01_L",
                "cf_s_bnip025_L",
                "cf_s_bnip015_L",
                "cf_s_bnip02_L",
                "cf_s_bnipacc_L",
                "cf_s_bust00_R",
                "cf_d_bust01_R",
                "cf_d_bust02_R",
                "cf_d_bust03_R",
                "cf_s_bust01_R",
                "cf_s_bust02_R",
                "cf_s_bust03_R",
                "cf_hit_bust02_R",
                "cf_d_bnip01_R",
                "cf_s_bnip01_R",
                "cf_s_bnip025_R",
                "cf_s_bnip015_R",
                "cf_s_bnip02_R",
                "cf_s_bnipacc_R",
                "cf_hit_siri_L",
                "cf_hit_siri_R",
                "cf_hit_waist_L",
                "cf_hit_berry",
                "cf_hit_spine02_L",
                "cf_hit_shoulder_L",
                "cf_hit_shoulder_R",
                "cf_hit_arm_L",
                "cf_hit_arm_R",
                "cf_hit_spine01",
                "cf_d_sk_top",
                "cf_d_sk_00_00",
                "cf_d_sk_01_00",
                "cf_d_sk_02_00",
                "cf_d_sk_03_00",
                "cf_d_sk_04_00",
                "cf_d_sk_05_00",
                "cf_d_sk_06_00",
                "cf_d_sk_07_00",
            };

            private static readonly string[] SourceNames =
            {
                "cf_a_height",
                "cf_a_height_aid",
                "cf_a_head",
                "cf_a_neck",
                "cf_a_spine03",
                "cf_a_shoulder",
                "cf_a_shoulder_L_aid03",
                "cf_a_shoulder_R_aid03",
                "cf_a_arm_L_aid03",
                "cf_a_arm_R_aid03",
                "cf_a_arm02",
                "cf_a_arm03_blend01",
                "cf_a_arm03_blend02",
                "cf_a_farm01",
                "cf_a_farm02_blend01",
                "cf_a_farm02_blend03",
                "cf_a_farm03",
                "cf_a_spine02",
                "cf_a_spine02_aid_berry",
                "cf_a_spine01",
                "cf_a_berry",
                "cf_a_waist01",
                "cf_a_waist02",
                "cf_a_siri",
                "cf_a_thigh01_L",
                "cf_a_thigh01_L_aid",
                "cf_a_thigh01_R",
                "cf_a_thigh01_R_aid",
                "cf_a_thigh02_L_blend01",
                "cf_a_thigh02_L_blend03",
                "cf_a_thigh02_R_blend01",
                "cf_a_thigh02_R_blend03",
                "cf_a_thigh03_L",
                "cf_a_thigh03_R",
                "cf_a_leg01_L",
                "cf_a_leg01_R",
                "cf_a_leg02_L",
                "cf_a_leg02_R",
                "cf_a_leg03",
                "cf_a_dan",
                "cf_a_bust_ty",
                "cf_a_bust00_aid03_sz",
                "cf_a_bust00_aid02_sz",
                "cf_a_bust_L_ry",
                "cf_a_bust_rx",
                "cf_a_bust01_size",
                "cf_a_bust_L_tx",
                "cf_a_bust02_size",
                "cf_a_bust_tz",
                "cf_a_bust03_size",
                "cf_a_bust01_shape1",
                "cf_a_bust02_shape1",
                "cf_a_bust03_shape1",
                "cf_a_hit_bust_shape1",
                "cf_a_hit_bust_shape2",
                "cf_a_bnip01",
                "cf_a_bnip01_size",
                "cf_a_d_bnip01_size",
                "cf_a_bnip02_size",
                "cf_a_bnip015_size",
                "cf_a_bnip02",
                "cf_a_bnipacc_stand",
                "cf_a_bnipacc_size",
                "cf_a_bust_R_ry",
                "cf_a_bust_R_tx",
                "cf_a_hit_siri_shape1",
                "cf_a_hit_siri_shape2",
                "cf_a_hit_siri_shape3",
                "cf_a_hit_siri_shape4",
                "cf_a_hit_siri_shape5",
                "cf_a_hit_siri_shape6",
                "cf_a_hit_waist_shape1",
                "cf_a_hit_waist_shape2",
                "cf_a_hit_waist_shape3",
                "cf_a_hit_spinety_shape",
                "cf_a_hit_waist_shape4",
                "cf_a_hit_waist_shape5",
                "cf_a_hit_berry_shape",
                "cf_a_hit_berry_shape2",
                "cf_a_hit_berry_shape3",
                "cf_a_hit_spine02_shape1",
                "cf_a_hit_spine02_shape2",
                "cf_a_hit_spine02_shape3",
                "cf_a_hit_shoulder_shape1",
                "cf_a_hit_shoulder_shape2",
                "cf_a_hit_shoulder_shape3",
                "cf_a_hit_arm_shape2",
                "cf_a_hit_arm_shape3",
                "cf_a_hit_arm_shape4",
                "cf_a_hit_spine01_shape1",
                "cf_a_hit_spine01_shape2",
                "cf_a_sk_00_00",
                "cf_a_sk_00_01",
                "cf_a_sk_berry",
                "cf_a_sk_thigh01_sz",
                "cf_a_sk_01_00",
                "cf_a_sk_01_01",
                "cf_a_sk_thigh01_sx",
                "cf_a_sk_02_00",
                "cf_a_sk_02_01",
                "cf_a_sk_siri",
                "cf_a_sk_03_00",
                "cf_a_sk_03_01",
                "cf_a_sk_04_00",
                "cf_a_sk_04_01",
                "cf_a_sk_05_00",
                "cf_a_sk_05_01",
                "cf_a_sk_06_00",
                "cf_a_sk_06_01",
                "cf_a_sk_07_00",
                "cf_a_sk_07_01",
            };

            private readonly Dictionary<int, BoneInfo> dictDst =
                new Dictionary<int, BoneInfo>();
            private readonly Dictionary<int, BoneInfo> dictSrc =
                new Dictionary<int, BoneInfo>();
            private readonly BoneInfo[] correctValue;
            private readonly Transform[] fixCorrectBone = new Transform[2];
            private readonly int typeBone;
            private readonly bool InitEnd = true;
            private readonly int updateMask = 7;
            private readonly float correctHeadSize = 1f;
            private readonly float correctNeckSize = 1f;

            public FemaleBodyOutput(
                Transform root,
                IReadOnlyDictionary<string, BoneInfo> sources,
                BoneInfo[] corrections,
                int typeBone)
            {
                this.typeBone = typeBone;
                correctValue = new BoneInfo[Math.Max(32, corrections.Length)];
                for (var index = 0; index < correctValue.Length; index++)
                {
                    correctValue[index] = index < corrections.Length
                        ? corrections[index]
                        : new BoneInfo(
                            Vector3.zero,
                            Vector3.zero,
                            Vector3.zero);
                }

                var transforms = BuildNameMap(root);
                for (var index = 0;
                     index < DestinationNames.Length;
                     index++)
                {
                    if (transforms.TryGetValue(
                            DestinationNames[index],
                            out var transform))
                    {
                        dictDst.Add(
                            index,
                            new BoneInfo { trfBone = transform });
                    }
                }

                for (var index = 0; index < SourceNames.Length; index++)
                {
                    if (!sources.TryGetValue(
                            SourceNames[index],
                            out var source))
                    {
                        source = new BoneInfo();
                    }

                    dictSrc.Add(index, source);
                }

                transforms.TryGetValue(
                    "cf_d_shoulder_L",
                    out fixCorrectBone[0]);
                transforms.TryGetValue(
                    "cf_d_shoulder_R",
                    out fixCorrectBone[1]);
            }

            public void Apply()
            {
                Update();
                UpdateAlways();
            }

            private void Update()
            {
                if (!this.InitEnd)
                {
                    return;
                }
                if (this.dictSrc.Count == 0)
                {
                    return;
                }
                BoneInfo boneInfo = null;
                if ((this.updateMask & 4) != 0)
                {
                    if (this.dictDst.TryGetValue(0, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[0].vctScl.x, this.dictSrc[0].vctScl.y, this.dictSrc[0].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(1, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[1].vctScl.x, this.dictSrc[1].vctScl.y, this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(2, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[1].vctScl.x, this.dictSrc[1].vctScl.y, this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(3, out boneInfo))
                    {
                        float num = (this.typeBone != 0) ? this.correctValue[2].vctPos.y : 0f;
                        float num2 = (this.typeBone != 0) ? this.correctValue[2].vctScl.x : 0f;
                        float num3 = (this.typeBone != 0) ? this.correctValue[2].vctScl.y : 0f;
                        float num4 = (this.typeBone != 0) ? this.correctValue[2].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[2].vctPos.y + num);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[2].vctScl.x * this.dictSrc[1].vctScl.x * this.correctHeadSize + num2, this.dictSrc[2].vctScl.y * this.dictSrc[1].vctScl.y * this.correctHeadSize + num3, this.dictSrc[2].vctScl.z * this.dictSrc[1].vctScl.z * this.correctHeadSize + num4);
                    }
                    if (this.dictDst.TryGetValue(4, out boneInfo))
                    {
                        float y = (this.typeBone != 0) ? this.correctValue[1].vctPos.y : 0f;
                        float x = (this.typeBone != 0) ? this.correctValue[1].vctRot.x : 0f;
                        float num5 = (this.typeBone != 0) ? this.correctValue[1].vctScl.x : 0f;
                        float num6 = (this.typeBone != 0) ? this.correctValue[1].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[3].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[3].vctScl.x * this.dictSrc[1].vctScl.x * this.correctNeckSize + num5, 1f, this.dictSrc[3].vctScl.z * this.dictSrc[1].vctScl.z * this.correctNeckSize + num6);
                    }
                    if (this.dictDst.TryGetValue(5, out boneInfo))
                    {
                        float num7 = (this.typeBone != 0) ? this.correctValue[5].vctPos.z : 0f;
                        float x2 = (this.typeBone != 0) ? this.correctValue[5].vctRot.x : 0f;
                        float num8 = (this.typeBone != 0) ? this.correctValue[5].vctScl.x : 0f;
                        float num9 = (this.typeBone != 0) ? this.correctValue[5].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[4].vctPos.z + num7);
                        boneInfo.trfBone.SetLocalRotation(x2, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[4].vctScl.x * this.dictSrc[1].vctScl.x + num8, 1f, this.dictSrc[4].vctScl.z * this.dictSrc[1].vctScl.z + num9);
                    }
                    if (this.dictDst.TryGetValue(6, out boneInfo))
                    {
                        float num10 = (this.typeBone != 0) ? this.correctValue[14].vctPos.x : 0f;
                        float num11 = (this.typeBone != 0) ? this.correctValue[14].vctPos.y : 0f;
                        float z = (this.typeBone != 0) ? this.correctValue[14].vctPos.z : 0f;
                        float num12 = (this.typeBone != 0) ? this.correctValue[14].vctScl.y : 0f;
                        float num13 = (this.typeBone != 0) ? this.correctValue[14].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[6].vctPos.x + this.dictSrc[5].vctPos.x + num10);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[5].vctPos.y + num11);
                        boneInfo.trfBone.SetLocalPositionZ(z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[5].vctScl.x, this.dictSrc[5].vctScl.y * this.dictSrc[1].vctScl.y + num12, this.dictSrc[5].vctScl.z * this.dictSrc[6].vctScl.z * this.dictSrc[1].vctScl.z + num13);
                    }
                    if (this.dictDst.TryGetValue(7, out boneInfo))
                    {
                        float num14 = (this.typeBone != 0) ? this.correctValue[15].vctPos.x : 0f;
                        float num15 = (this.typeBone != 0) ? this.correctValue[15].vctPos.y : 0f;
                        float z2 = (this.typeBone != 0) ? this.correctValue[15].vctPos.z : 0f;
                        float num16 = (this.typeBone != 0) ? this.correctValue[15].vctScl.y : 0f;
                        float num17 = (this.typeBone != 0) ? this.correctValue[15].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[7].vctPos.x - this.dictSrc[5].vctPos.x + num14);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[5].vctPos.y + num15);
                        boneInfo.trfBone.SetLocalPositionZ(z2);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[5].vctScl.x, this.dictSrc[5].vctScl.y * this.dictSrc[1].vctScl.y + num16, this.dictSrc[5].vctScl.z * this.dictSrc[7].vctScl.z * this.dictSrc[1].vctScl.z + num17);
                    }
                    if (this.dictDst.TryGetValue(8, out boneInfo))
                    {
                        float num18 = (this.typeBone != 0) ? this.correctValue[16].vctPos.y : 0f;
                        float num19 = (this.typeBone != 0) ? this.correctValue[16].vctScl.x : 0f;
                        float num20 = (this.typeBone != 0) ? this.correctValue[16].vctScl.y : 0f;
                        float num21 = (this.typeBone != 0) ? this.correctValue[16].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[8].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[5].vctPos.y + num18);
                        boneInfo.trfBone.SetLocalScale(1f + num19, this.dictSrc[5].vctScl.y * this.dictSrc[1].vctScl.y + num20, this.dictSrc[5].vctScl.z * this.dictSrc[8].vctScl.z * this.dictSrc[1].vctScl.z + num21);
                    }
                    if (this.dictDst.TryGetValue(9, out boneInfo))
                    {
                        float num22 = (this.typeBone != 0) ? this.correctValue[17].vctPos.y : 0f;
                        float num23 = (this.typeBone != 0) ? this.correctValue[17].vctScl.x : 0f;
                        float num24 = (this.typeBone != 0) ? this.correctValue[17].vctScl.y : 0f;
                        float num25 = (this.typeBone != 0) ? this.correctValue[17].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[9].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[5].vctPos.y + num22);
                        boneInfo.trfBone.SetLocalScale(1f + num23, this.dictSrc[5].vctScl.y * this.dictSrc[1].vctScl.y + num24, this.dictSrc[5].vctScl.z * this.dictSrc[9].vctScl.z * this.dictSrc[1].vctScl.z + num25);
                    }
                    if (this.dictDst.TryGetValue(10, out boneInfo))
                    {
                        float num26 = (this.typeBone != 0) ? this.correctValue[18].vctPos.y : 0f;
                        float num27 = (this.typeBone != 0) ? this.correctValue[18].vctPos.z : 0f;
                        float num28 = (this.typeBone != 0) ? this.correctValue[18].vctScl.y : 0f;
                        float num29 = (this.typeBone != 0) ? this.correctValue[18].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[10].vctPos.y + num26);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[10].vctPos.z + num27);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[10].vctScl.y * this.dictSrc[1].vctScl.y + num28, this.dictSrc[10].vctScl.z * this.dictSrc[1].vctScl.z + num29);
                    }
                    if (this.dictDst.TryGetValue(11, out boneInfo))
                    {
                        float num30 = (this.typeBone != 0) ? this.correctValue[19].vctPos.y : 0f;
                        float num31 = (this.typeBone != 0) ? this.correctValue[19].vctPos.z : 0f;
                        float num32 = (this.typeBone != 0) ? this.correctValue[19].vctScl.y : 0f;
                        float num33 = (this.typeBone != 0) ? this.correctValue[19].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[10].vctPos.y + num30);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[10].vctPos.z + num31);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[10].vctScl.y * this.dictSrc[1].vctScl.y + num32, this.dictSrc[10].vctScl.z * this.dictSrc[1].vctScl.z + num33);
                    }
                    if (this.dictDst.TryGetValue(12, out boneInfo))
                    {
                        float y2 = (this.typeBone != 0) ? this.correctValue[20].vctPos.y : 0f;
                        float num34 = (this.typeBone != 0) ? this.correctValue[20].vctScl.y : 0f;
                        float num35 = (this.typeBone != 0) ? this.correctValue[20].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(y2);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[11].vctScl.y * this.dictSrc[12].vctScl.y * this.dictSrc[1].vctScl.y + num34, this.dictSrc[11].vctScl.z * this.dictSrc[12].vctScl.z * this.dictSrc[1].vctScl.z + num35);
                    }
                    if (this.dictDst.TryGetValue(13, out boneInfo))
                    {
                        float y3 = (this.typeBone != 0) ? this.correctValue[21].vctPos.y : 0f;
                        float num36 = (this.typeBone != 0) ? this.correctValue[21].vctScl.y : 0f;
                        float num37 = (this.typeBone != 0) ? this.correctValue[21].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(y3);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[11].vctScl.y * this.dictSrc[12].vctScl.y * this.dictSrc[1].vctScl.y + num36, this.dictSrc[11].vctScl.z * this.dictSrc[12].vctScl.z * this.dictSrc[1].vctScl.z + num37);
                    }
                    if (this.dictDst.TryGetValue(14, out boneInfo))
                    {
                        float y4 = (this.typeBone != 0) ? this.correctValue[22].vctPos.y : 0f;
                        boneInfo.trfBone.SetLocalPositionY(y4);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[13].vctScl.y * this.dictSrc[1].vctScl.y, this.dictSrc[13].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(15, out boneInfo))
                    {
                        float y5 = (this.typeBone != 0) ? this.correctValue[23].vctPos.y : 0f;
                        boneInfo.trfBone.SetLocalPositionY(y5);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[13].vctScl.y * this.dictSrc[1].vctScl.y, this.dictSrc[13].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(16, out boneInfo))
                    {
                        float y6 = (this.typeBone != 0) ? this.correctValue[24].vctRot.y : 0f;
                        float z3 = (this.typeBone != 0) ? this.correctValue[24].vctRot.z : 0f;
                        float num38 = (this.typeBone != 0) ? this.correctValue[24].vctScl.y : 0f;
                        float num39 = (this.typeBone != 0) ? this.correctValue[24].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalRotation(0f, y6, z3);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[14].vctScl.y * this.dictSrc[15].vctScl.y * this.dictSrc[1].vctScl.y + num38, this.dictSrc[14].vctScl.z * this.dictSrc[15].vctScl.z * this.dictSrc[1].vctScl.z + num39);
                    }
                    if (this.dictDst.TryGetValue(17, out boneInfo))
                    {
                        float y7 = (this.typeBone != 0) ? this.correctValue[25].vctRot.y : 0f;
                        float z4 = (this.typeBone != 0) ? this.correctValue[25].vctRot.z : 0f;
                        float num40 = (this.typeBone != 0) ? this.correctValue[25].vctScl.y : 0f;
                        float num41 = (this.typeBone != 0) ? this.correctValue[25].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalRotation(0f, y7, z4);
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[14].vctScl.y * this.dictSrc[15].vctScl.y * this.dictSrc[1].vctScl.y + num40, this.dictSrc[14].vctScl.z * this.dictSrc[15].vctScl.z * this.dictSrc[1].vctScl.z + num41);
                    }
                    if (this.dictDst.TryGetValue(18, out boneInfo))
                    {
                        float num42 = (this.typeBone != 0) ? this.correctValue[26].vctScl.x : 0f;
                        float num43 = (this.typeBone != 0) ? this.correctValue[26].vctScl.y : 0f;
                        boneInfo.trfBone.SetLocalScale(1f + num42, this.dictSrc[16].vctScl.y * this.dictSrc[1].vctScl.y + num43, this.dictSrc[16].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(19, out boneInfo))
                    {
                        float num44 = (this.typeBone != 0) ? this.correctValue[27].vctScl.x : 0f;
                        float num45 = (this.typeBone != 0) ? this.correctValue[27].vctScl.y : 0f;
                        boneInfo.trfBone.SetLocalScale(1f + num44, this.dictSrc[16].vctScl.y * this.dictSrc[1].vctScl.y + num45, this.dictSrc[16].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(20, out boneInfo))
                    {
                        float num46 = (this.typeBone != 0) ? this.correctValue[4].vctPos.z : 0f;
                        float num47 = (this.typeBone != 0) ? this.correctValue[4].vctScl.x : 0f;
                        float num48 = (this.typeBone != 0) ? this.correctValue[4].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[17].vctPos.z + this.dictSrc[18].vctPos.z + num46);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[17].vctScl.x * this.dictSrc[1].vctScl.x + num47, 1f, this.dictSrc[17].vctScl.z * this.dictSrc[1].vctScl.z * this.dictSrc[18].vctScl.z + num48);
                    }
                    if (this.dictDst.TryGetValue(21, out boneInfo))
                    {
                        float num49 = (this.typeBone != 0) ? this.correctValue[3].vctPos.z : 0f;
                        float num50 = (this.typeBone != 0) ? this.correctValue[3].vctScl.x : 0f;
                        float num51 = (this.typeBone != 0) ? this.correctValue[3].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[19].vctPos.y + this.dictSrc[20].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[19].vctPos.z + this.dictSrc[20].vctPos.z + num49);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[20].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[19].vctScl.x * this.dictSrc[1].vctScl.x * this.dictSrc[20].vctScl.x + num50, this.dictSrc[20].vctScl.y, this.dictSrc[19].vctScl.z * this.dictSrc[1].vctScl.z * this.dictSrc[20].vctScl.z + num51);
                    }
                    if (this.dictDst.TryGetValue(22, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[20].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[21].vctPos.z + this.dictSrc[20].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[20].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[21].vctScl.x * this.dictSrc[1].vctScl.x * this.dictSrc[20].vctScl.x, this.dictSrc[20].vctScl.y, this.dictSrc[21].vctScl.z * this.dictSrc[1].vctScl.z * this.dictSrc[20].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(23, out boneInfo))
                    {
                        float num52 = (this.typeBone != 0) ? this.correctValue[0].vctScl.x : 0f;
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[22].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[22].vctScl.x * this.dictSrc[1].vctScl.x + num52, 1f, this.dictSrc[22].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(24, out boneInfo))
                    {
                        float x3 = (this.typeBone != 0) ? this.correctValue[28].vctPos.x : 0f;
                        float num53 = (this.typeBone != 0) ? this.correctValue[28].vctRot.x : 0f;
                        float z5 = (this.typeBone != 0) ? this.correctValue[28].vctRot.z : 0f;
                        float num54 = (this.typeBone != 0) ? this.correctValue[28].vctScl.x : 0f;
                        boneInfo.trfBone.SetLocalPositionX(x3);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[23].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[23].vctPos.z + this.dictSrc[22].vctPos.z * 0.3f);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[23].vctRot.x + num53, 0f, z5);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[23].vctScl.x + (-1f + this.dictSrc[22].vctScl.x) * 0.5f + num54, this.dictSrc[23].vctScl.y, this.dictSrc[23].vctScl.z + (-1f + this.dictSrc[22].vctScl.z) * 0.5f + (-1f + this.dictSrc[21].vctScl.z) * 0.5f);
                    }
                    if (this.dictDst.TryGetValue(25, out boneInfo))
                    {
                        float x4 = (this.typeBone != 0) ? this.correctValue[29].vctPos.x : 0f;
                        float num55 = (this.typeBone != 0) ? this.correctValue[29].vctRot.x : 0f;
                        float z6 = (this.typeBone != 0) ? this.correctValue[29].vctRot.z : 0f;
                        float num56 = (this.typeBone != 0) ? this.correctValue[29].vctScl.x : 0f;
                        boneInfo.trfBone.SetLocalPositionX(x4);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[23].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[23].vctPos.z + this.dictSrc[22].vctPos.z * 0.3f);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[23].vctRot.x + num55, 0f, z6);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[23].vctScl.x + (-1f + this.dictSrc[22].vctScl.x) * 0.5f + num56, this.dictSrc[23].vctScl.y, this.dictSrc[23].vctScl.z + (-1f + this.dictSrc[22].vctScl.z) * 0.5f + (-1f + this.dictSrc[21].vctScl.z) * 0.5f);
                    }
                    if (this.dictDst.TryGetValue(26, out boneInfo))
                    {
                        float num57 = (this.typeBone != 0) ? this.correctValue[6].vctPos.x : 0f;
                        float num58 = (this.typeBone != 0) ? this.correctValue[6].vctPos.z : 0f;
                        float num59 = (this.typeBone != 0) ? this.correctValue[6].vctRot.x : 0f;
                        float y8 = (this.typeBone != 0) ? this.correctValue[6].vctRot.y : 0f;
                        float num60 = (this.typeBone != 0) ? this.correctValue[6].vctRot.z : 0f;
                        float num61 = (this.typeBone != 0) ? this.correctValue[6].vctScl.x : 0f;
                        float num62 = (this.typeBone != 0) ? this.correctValue[6].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[24].vctPos.x + this.dictSrc[25].vctPos.x + num57);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[24].vctPos.z + num58);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[24].vctRot.x + num59, y8, this.dictSrc[24].vctRot.z + num60);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[24].vctScl.x * this.dictSrc[25].vctScl.x * this.dictSrc[1].vctScl.x + num61, 1f, this.dictSrc[24].vctScl.z * this.dictSrc[25].vctScl.z * this.dictSrc[1].vctScl.z + num62);
                    }
                    if (this.dictDst.TryGetValue(27, out boneInfo))
                    {
                        float num63 = (this.typeBone != 0) ? this.correctValue[7].vctPos.x : 0f;
                        float num64 = (this.typeBone != 0) ? this.correctValue[7].vctPos.z : 0f;
                        float num65 = (this.typeBone != 0) ? this.correctValue[7].vctRot.x : 0f;
                        float y9 = (this.typeBone != 0) ? this.correctValue[7].vctRot.y : 0f;
                        float num66 = (this.typeBone != 0) ? this.correctValue[7].vctRot.z : 0f;
                        float num67 = (this.typeBone != 0) ? this.correctValue[7].vctScl.x : 0f;
                        float num68 = (this.typeBone != 0) ? this.correctValue[7].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[26].vctPos.x + this.dictSrc[27].vctPos.x + num63);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[26].vctPos.z + num64);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[26].vctRot.x + num65, y9, this.dictSrc[26].vctRot.z + num66);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[26].vctScl.x * this.dictSrc[27].vctScl.x * this.dictSrc[1].vctScl.x + num67, 1f, this.dictSrc[26].vctScl.z * this.dictSrc[27].vctScl.z * this.dictSrc[1].vctScl.z + num68);
                    }
                    if (this.dictDst.TryGetValue(28, out boneInfo))
                    {
                        float num69 = (this.typeBone != 0) ? this.correctValue[8].vctPos.x : 0f;
                        float num70 = (this.typeBone != 0) ? this.correctValue[8].vctScl.x : 0f;
                        float num71 = (this.typeBone != 0) ? this.correctValue[8].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[28].vctPos.x + this.dictSrc[29].vctPos.x + num69);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[28].vctPos.z + this.dictSrc[29].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[28].vctScl.x * this.dictSrc[29].vctScl.x * this.dictSrc[1].vctScl.x + num70, 1f, this.dictSrc[28].vctScl.z * this.dictSrc[29].vctScl.z * this.dictSrc[1].vctScl.z + num71);
                    }
                    if (this.dictDst.TryGetValue(29, out boneInfo))
                    {
                        float num72 = (this.typeBone != 0) ? this.correctValue[9].vctPos.x : 0f;
                        float num73 = (this.typeBone != 0) ? this.correctValue[9].vctScl.x : 0f;
                        float num74 = (this.typeBone != 0) ? this.correctValue[9].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[30].vctPos.x + this.dictSrc[31].vctPos.x + num72);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[30].vctPos.z + this.dictSrc[31].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[30].vctScl.x * this.dictSrc[31].vctScl.x * this.dictSrc[1].vctScl.x + num73, 1f, this.dictSrc[30].vctScl.z * this.dictSrc[31].vctScl.z * this.dictSrc[1].vctScl.z + num74);
                    }
                    if (this.dictDst.TryGetValue(30, out boneInfo))
                    {
                        float num75 = (this.typeBone != 0) ? this.correctValue[10].vctPos.z : 0f;
                        float num76 = (this.typeBone != 0) ? this.correctValue[10].vctScl.x : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[32].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[32].vctPos.z + num75);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[32].vctRot.x, 0f, this.dictSrc[32].vctRot.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[32].vctScl.x * this.dictSrc[1].vctScl.x + num76, 1f, this.dictSrc[32].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(31, out boneInfo))
                    {
                        float num77 = (this.typeBone != 0) ? this.correctValue[11].vctPos.z : 0f;
                        float num78 = (this.typeBone != 0) ? this.correctValue[11].vctScl.x : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[33].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[33].vctPos.z + num77);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[33].vctRot.x, 0f, this.dictSrc[33].vctRot.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[33].vctScl.x * this.dictSrc[1].vctScl.x + num78, 1f, this.dictSrc[33].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(32, out boneInfo))
                    {
                        float num79 = (this.typeBone != 0) ? this.correctValue[12].vctScl.x : 0f;
                        float num80 = (this.typeBone != 0) ? this.correctValue[12].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[34].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[34].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[34].vctScl.x * this.dictSrc[1].vctScl.x + num79, 1f, this.dictSrc[34].vctScl.z * this.dictSrc[1].vctScl.z + num80);
                    }
                    if (this.dictDst.TryGetValue(33, out boneInfo))
                    {
                        float num81 = (this.typeBone != 0) ? this.correctValue[13].vctScl.x : 0f;
                        float num82 = (this.typeBone != 0) ? this.correctValue[13].vctScl.z : 0f;
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[35].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[35].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[35].vctScl.x * this.dictSrc[1].vctScl.x + num81, 1f, this.dictSrc[35].vctScl.z * this.dictSrc[1].vctScl.z + num82);
                    }
                    if (this.dictDst.TryGetValue(34, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[36].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[36].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[36].vctScl.x * this.dictSrc[1].vctScl.x, 1f, this.dictSrc[36].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(35, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[37].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[37].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[37].vctScl.x * this.dictSrc[1].vctScl.x, 1f, this.dictSrc[37].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(36, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[38].vctScl.x, 1f, this.dictSrc[38].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(37, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[38].vctScl.x, 1f, this.dictSrc[38].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(67, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[65].vctPos.x + this.dictSrc[66].vctPos.x + this.dictSrc[67].vctPos.x + this.dictSrc[68].vctPos.x + this.dictSrc[69].vctPos.x + this.dictSrc[70].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[67].vctPos.y + this.dictSrc[68].vctPos.y + this.dictSrc[70].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[65].vctPos.z + this.dictSrc[67].vctPos.z + this.dictSrc[70].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[65].vctScl.x * this.dictSrc[67].vctScl.x * this.dictSrc[68].vctScl.x * this.dictSrc[1].vctScl.x * this.dictSrc[70].vctScl.x, this.dictSrc[65].vctScl.y * this.dictSrc[67].vctScl.y * this.dictSrc[68].vctScl.y * this.dictSrc[1].vctScl.y * this.dictSrc[70].vctScl.x, this.dictSrc[65].vctScl.z * this.dictSrc[67].vctScl.z * this.dictSrc[68].vctScl.z * this.dictSrc[1].vctScl.z * this.dictSrc[70].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(68, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(-this.dictSrc[65].vctPos.x - this.dictSrc[66].vctPos.x - this.dictSrc[67].vctPos.x - this.dictSrc[68].vctPos.x - this.dictSrc[69].vctPos.x - this.dictSrc[70].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[67].vctPos.y + this.dictSrc[68].vctPos.y + this.dictSrc[70].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[65].vctPos.z + this.dictSrc[67].vctPos.z + this.dictSrc[70].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[65].vctScl.x * this.dictSrc[67].vctScl.x * this.dictSrc[68].vctScl.x * this.dictSrc[1].vctScl.x * this.dictSrc[70].vctScl.x, this.dictSrc[65].vctScl.y * this.dictSrc[67].vctScl.y * this.dictSrc[68].vctScl.y * this.dictSrc[1].vctScl.y * this.dictSrc[70].vctScl.x, this.dictSrc[65].vctScl.z * this.dictSrc[67].vctScl.z * this.dictSrc[68].vctScl.z * this.dictSrc[1].vctScl.z * this.dictSrc[70].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(69, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[71].vctPos.y + this.dictSrc[72].vctPos.y + this.dictSrc[73].vctPos.y + this.dictSrc[74].vctPos.y + this.dictSrc[75].vctPos.y + this.dictSrc[76].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[72].vctPos.z + this.dictSrc[73].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[71].vctScl.x * this.dictSrc[72].vctScl.x * this.dictSrc[1].vctScl.x * this.dictSrc[73].vctScl.x * this.dictSrc[76].vctScl.x * this.dictSrc[75].vctScl.x, this.dictSrc[71].vctScl.x * this.dictSrc[72].vctScl.x * this.dictSrc[1].vctScl.y * this.dictSrc[73].vctScl.y * this.dictSrc[76].vctScl.y * this.dictSrc[75].vctScl.y, this.dictSrc[71].vctScl.x * this.dictSrc[72].vctScl.x * this.dictSrc[1].vctScl.z * this.dictSrc[73].vctScl.z * this.dictSrc[76].vctScl.z * this.dictSrc[75].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(70, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[77].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[77].vctPos.y + this.dictSrc[74].vctPos.y * 2f + this.dictSrc[78].vctPos.y + this.dictSrc[79].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[77].vctPos.z + this.dictSrc[78].vctPos.z + this.dictSrc[79].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[77].vctScl.x * this.dictSrc[78].vctScl.x * this.dictSrc[1].vctScl.x * this.dictSrc[79].vctScl.x, this.dictSrc[77].vctScl.y * this.dictSrc[78].vctScl.y * this.dictSrc[1].vctScl.y * this.dictSrc[79].vctScl.y, this.dictSrc[77].vctScl.z * this.dictSrc[78].vctScl.z * this.dictSrc[1].vctScl.z * this.dictSrc[79].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(71, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[80].vctPos.y + this.dictSrc[81].vctPos.y + this.dictSrc[82].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[82].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[81].vctScl.x * this.dictSrc[82].vctScl.x * this.dictSrc[1].vctScl.x, this.dictSrc[81].vctScl.y * this.dictSrc[82].vctScl.y * this.dictSrc[1].vctScl.y, this.dictSrc[81].vctScl.z * this.dictSrc[82].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(72, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[83].vctPos.x + this.dictSrc[84].vctPos.x + this.dictSrc[85].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[83].vctPos.y + this.dictSrc[84].vctPos.y + this.dictSrc[85].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[83].vctPos.z + this.dictSrc[84].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[83].vctScl.x * this.dictSrc[84].vctScl.x * this.dictSrc[85].vctScl.x * this.dictSrc[1].vctScl.x, this.dictSrc[83].vctScl.y * this.dictSrc[84].vctScl.y * this.dictSrc[85].vctScl.y * this.dictSrc[1].vctScl.y, this.dictSrc[83].vctScl.z * this.dictSrc[84].vctScl.z * this.dictSrc[85].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(73, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(-this.dictSrc[83].vctPos.x - this.dictSrc[84].vctPos.x - this.dictSrc[85].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[83].vctPos.y + this.dictSrc[84].vctPos.y + this.dictSrc[85].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[83].vctPos.z + this.dictSrc[84].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[83].vctScl.x * this.dictSrc[84].vctScl.x * this.dictSrc[85].vctScl.x * this.dictSrc[1].vctScl.x, this.dictSrc[83].vctScl.y * this.dictSrc[84].vctScl.y * this.dictSrc[85].vctScl.y * this.dictSrc[1].vctScl.y, this.dictSrc[83].vctScl.z * this.dictSrc[84].vctScl.z * this.dictSrc[85].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(74, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[86].vctScl.y * this.dictSrc[87].vctScl.y * this.dictSrc[88].vctScl.y * this.dictSrc[1].vctScl.x, this.dictSrc[86].vctScl.z * this.dictSrc[87].vctScl.z * this.dictSrc[88].vctScl.z * this.dictSrc[1].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(75, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(1f, this.dictSrc[86].vctScl.y * this.dictSrc[87].vctScl.y * this.dictSrc[88].vctScl.y * this.dictSrc[1].vctScl.x, this.dictSrc[86].vctScl.z * this.dictSrc[87].vctScl.z * this.dictSrc[88].vctScl.z * this.dictSrc[1].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(76, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[89].vctPos.z + this.dictSrc[90].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[89].vctScl.x * this.dictSrc[90].vctScl.x * this.dictSrc[1].vctScl.x, this.dictSrc[89].vctScl.y * this.dictSrc[90].vctScl.y * this.dictSrc[1].vctScl.y, this.dictSrc[89].vctScl.z * this.dictSrc[90].vctScl.z * this.dictSrc[1].vctScl.z);
                    }
                    float num83 = (180f >= this.dictSrc[94].vctRot.x) ? this.dictSrc[94].vctRot.x : (this.dictSrc[94].vctRot.x - 360f);
                    float num84 = (180f >= this.dictSrc[97].vctRot.x) ? this.dictSrc[97].vctRot.x : (this.dictSrc[97].vctRot.x - 360f);
                    if (this.dictDst.TryGetValue(77, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[1].vctScl.x, this.dictSrc[1].vctScl.y, this.dictSrc[1].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(78, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[91].vctPos.x + this.dictSrc[92].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[91].vctPos.z + this.dictSrc[92].vctPos.z + this.dictSrc[93].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[91].vctRot.x + this.dictSrc[92].vctRot.x + this.dictSrc[92].vctRot.z + num83, this.dictSrc[91].vctRot.y, this.dictSrc[91].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(79, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[95].vctPos.x + this.dictSrc[96].vctPos.x - this.dictSrc[93].vctPos.x * 0.5f);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[95].vctPos.z + this.dictSrc[96].vctPos.z + this.dictSrc[93].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[95].vctRot.x + this.dictSrc[96].vctRot.x + this.dictSrc[96].vctRot.z + num83 * 0.6f + num84 * 0.6f, this.dictSrc[95].vctRot.y, this.dictSrc[95].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(80, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[98].vctPos.x + this.dictSrc[99].vctPos.x + this.dictSrc[100].vctPos.x * 0.5f - this.dictSrc[93].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[98].vctPos.z + this.dictSrc[99].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[98].vctRot.x + this.dictSrc[99].vctRot.x + this.dictSrc[99].vctRot.z + num84, this.dictSrc[98].vctRot.y, this.dictSrc[98].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(81, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[101].vctPos.x + this.dictSrc[102].vctPos.x + this.dictSrc[100].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[101].vctPos.z + this.dictSrc[102].vctPos.z + this.dictSrc[100].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[101].vctRot.x + this.dictSrc[102].vctRot.x + this.dictSrc[102].vctRot.z + num83 * 0.6f + num84 * 0.6f + this.dictSrc[100].vctRot.x, this.dictSrc[101].vctRot.y, this.dictSrc[101].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(82, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[103].vctPos.x + this.dictSrc[104].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[103].vctPos.z + this.dictSrc[104].vctPos.z + this.dictSrc[100].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[103].vctRot.x + this.dictSrc[104].vctRot.x + this.dictSrc[104].vctRot.z + num83 + this.dictSrc[100].vctRot.x, this.dictSrc[103].vctRot.y, this.dictSrc[103].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(83, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[105].vctPos.x + this.dictSrc[106].vctPos.x - this.dictSrc[100].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[105].vctPos.z + this.dictSrc[106].vctPos.z + this.dictSrc[100].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[105].vctRot.x + this.dictSrc[106].vctRot.x + this.dictSrc[106].vctRot.z + num83 * 0.6f + num84 * 0.6f + this.dictSrc[100].vctRot.x, this.dictSrc[105].vctRot.y, this.dictSrc[105].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(84, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[107].vctPos.x + this.dictSrc[108].vctPos.x - this.dictSrc[100].vctPos.x * 0.5f + this.dictSrc[93].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[107].vctPos.z + this.dictSrc[108].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[107].vctRot.x + this.dictSrc[108].vctRot.x + this.dictSrc[108].vctRot.z + num84, this.dictSrc[107].vctRot.y, this.dictSrc[107].vctRot.z);
                    }
                    if (this.dictDst.TryGetValue(85, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[109].vctPos.x + this.dictSrc[110].vctPos.x + this.dictSrc[93].vctPos.x * 0.5f);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[109].vctPos.z + this.dictSrc[110].vctPos.z + this.dictSrc[93].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[109].vctRot.x + this.dictSrc[110].vctRot.x + this.dictSrc[110].vctRot.z + num83 * 0.6f + num84 * 0.6f, this.dictSrc[109].vctRot.y, this.dictSrc[109].vctRot.z);
                    }
                }
                if ((this.updateMask & 1) != 0)
                {
                    if (this.dictDst.TryGetValue(39, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[40].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[41].vctPos.z + this.dictSrc[42].vctPos.z + this.dictSrc[43].vctPos.z + this.dictSrc[40].vctPos.z + this.dictSrc[44].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[44].vctRot.x, 0f, 0f);
                    }
                    if (this.dictDst.TryGetValue(40, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[46].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[45].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[45].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[45].vctRot.x, this.dictSrc[43].vctRot.y + this.dictSrc[45].vctRot.y, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[45].vctScl.x, this.dictSrc[45].vctScl.y, this.dictSrc[45].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(41, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[47].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[47].vctPos.z + this.dictSrc[48].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[47].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[47].vctScl.x, this.dictSrc[47].vctScl.y, this.dictSrc[47].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(42, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[49].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[49].vctPos.z + this.dictSrc[48].vctPos.z / 2f);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[49].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[49].vctScl.x, this.dictSrc[49].vctScl.y, this.dictSrc[49].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(43, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[50].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[50].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[50].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[50].vctRot.x, this.dictSrc[50].vctRot.y, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[50].vctScl.x, this.dictSrc[50].vctScl.y, this.dictSrc[48].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(44, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[51].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[51].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[51].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[51].vctRot.x, this.dictSrc[51].vctRot.y, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[51].vctScl.x * this.dictSrc[48].vctScl.x, this.dictSrc[51].vctScl.y * this.dictSrc[48].vctScl.y, this.dictSrc[51].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(45, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[52].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[52].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[52].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[52].vctScl.x * this.dictSrc[48].vctScl.x, this.dictSrc[52].vctScl.y * this.dictSrc[48].vctScl.y, 1f);
                    }
                    if (this.dictDst.TryGetValue(46, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[53].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[53].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[53].vctPos.z + this.dictSrc[54].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[53].vctScl.x * this.dictSrc[54].vctScl.x, this.dictSrc[53].vctScl.y * this.dictSrc[54].vctScl.x, this.dictSrc[53].vctScl.z * this.dictSrc[54].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(47, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[55].vctPos.z + this.dictSrc[56].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[57].vctScl.x, this.dictSrc[57].vctScl.x, this.dictSrc[57].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(48, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(-(this.dictSrc[55].vctPos.z - 0.01f) * 1.2f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[55].vctScl.x * this.dictSrc[56].vctScl.x, this.dictSrc[55].vctScl.y * this.dictSrc[56].vctScl.y, this.dictSrc[55].vctScl.z * this.dictSrc[56].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(49, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(1f / this.dictSrc[55].vctScl.x * (this.dictSrc[58].vctScl.x * 1.2f), 1f / this.dictSrc[55].vctScl.y * (this.dictSrc[58].vctScl.y * 1.2f), 1f / this.dictSrc[55].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(50, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(0.0025f + this.dictSrc[59].vctPos.z - (this.dictSrc[55].vctPos.z - 0.01f) * 1f);
                        boneInfo.trfBone.SetLocalScale(0.1f + this.dictSrc[59].vctScl.y * this.dictSrc[59].vctScl.x, 0.1f + this.dictSrc[59].vctScl.y * this.dictSrc[59].vctScl.x, 0.1f + this.dictSrc[59].vctScl.z * this.dictSrc[59].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(51, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(0.004f + this.dictSrc[60].vctPos.z + this.dictSrc[55].vctPos.x + this.dictSrc[58].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[60].vctScl.x * this.dictSrc[58].vctScl.x, this.dictSrc[60].vctScl.y * this.dictSrc[58].vctScl.y, this.dictSrc[60].vctScl.z * this.dictSrc[58].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(52, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[61].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(1f * this.dictSrc[61].vctScl.x / this.dictSrc[60].vctScl.x / this.dictSrc[58].vctScl.x * this.dictSrc[62].vctScl.x, 1f * this.dictSrc[61].vctScl.y / this.dictSrc[60].vctScl.y / this.dictSrc[58].vctScl.y * this.dictSrc[62].vctScl.y, 1f * this.dictSrc[61].vctScl.z / this.dictSrc[60].vctScl.z / this.dictSrc[58].vctScl.z * this.dictSrc[62].vctScl.z);
                    }
                }
                if ((this.updateMask & 2) != 0)
                {
                    if (this.dictDst.TryGetValue(53, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[40].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[41].vctPos.z + this.dictSrc[42].vctPos.z + this.dictSrc[43].vctPos.z + this.dictSrc[40].vctPos.z + this.dictSrc[44].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[44].vctRot.x, 0f, 0f);
                    }
                    if (this.dictDst.TryGetValue(54, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(this.dictSrc[64].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[45].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[45].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[45].vctRot.x, this.dictSrc[63].vctRot.y - this.dictSrc[45].vctRot.y, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[45].vctScl.x, this.dictSrc[45].vctScl.y, this.dictSrc[45].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(55, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[47].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[47].vctPos.z + this.dictSrc[48].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[47].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[47].vctScl.x, this.dictSrc[47].vctScl.y, this.dictSrc[47].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(56, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[49].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[49].vctPos.z + this.dictSrc[48].vctPos.z / 2f);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[49].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[49].vctScl.x, this.dictSrc[49].vctScl.y, this.dictSrc[49].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(57, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(-this.dictSrc[50].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[50].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[50].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[50].vctRot.x, -this.dictSrc[50].vctRot.y, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[50].vctScl.x, this.dictSrc[50].vctScl.y, this.dictSrc[48].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(58, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(-this.dictSrc[51].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[51].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[51].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[51].vctRot.x, -this.dictSrc[51].vctRot.y, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[51].vctScl.x * this.dictSrc[48].vctScl.x, this.dictSrc[51].vctScl.y * this.dictSrc[48].vctScl.y, this.dictSrc[51].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(59, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[52].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[52].vctPos.z);
                        boneInfo.trfBone.SetLocalRotation(this.dictSrc[52].vctRot.x, 0f, 0f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[52].vctScl.x * this.dictSrc[48].vctScl.x, this.dictSrc[52].vctScl.y * this.dictSrc[48].vctScl.y, 1f);
                    }
                    if (this.dictDst.TryGetValue(60, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionX(-this.dictSrc[53].vctPos.x);
                        boneInfo.trfBone.SetLocalPositionY(this.dictSrc[53].vctPos.y);
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[53].vctPos.z + this.dictSrc[54].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[53].vctScl.x * this.dictSrc[54].vctScl.x, this.dictSrc[53].vctScl.y * this.dictSrc[54].vctScl.x, this.dictSrc[53].vctScl.z * this.dictSrc[54].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(61, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[55].vctPos.z + this.dictSrc[56].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[57].vctScl.x, this.dictSrc[57].vctScl.x, this.dictSrc[57].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(62, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(-(this.dictSrc[55].vctPos.z - 0.01f) * 1.2f);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[55].vctScl.x * this.dictSrc[56].vctScl.x, this.dictSrc[55].vctScl.y * this.dictSrc[56].vctScl.y, this.dictSrc[55].vctScl.z * this.dictSrc[56].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(63, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalScale(1f / this.dictSrc[55].vctScl.x * (this.dictSrc[58].vctScl.x * 1.2f), 1f / this.dictSrc[55].vctScl.y * (this.dictSrc[58].vctScl.y * 1.2f), 1f / this.dictSrc[55].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(64, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(0.0025f + this.dictSrc[59].vctPos.z - (this.dictSrc[55].vctPos.z - 0.01f) * 1f);
                        boneInfo.trfBone.SetLocalScale(0.1f + this.dictSrc[59].vctScl.y * this.dictSrc[59].vctScl.x, 0.1f + this.dictSrc[59].vctScl.y * this.dictSrc[59].vctScl.x, 0.1f + this.dictSrc[59].vctScl.z * this.dictSrc[59].vctScl.x);
                    }
                    if (this.dictDst.TryGetValue(65, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(0.004f + this.dictSrc[60].vctPos.z + this.dictSrc[55].vctPos.x + this.dictSrc[58].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(this.dictSrc[60].vctScl.x * this.dictSrc[58].vctScl.x, this.dictSrc[60].vctScl.y * this.dictSrc[58].vctScl.y, this.dictSrc[60].vctScl.z * this.dictSrc[58].vctScl.z);
                    }
                    if (this.dictDst.TryGetValue(66, out boneInfo))
                    {
                        boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[61].vctPos.z);
                        boneInfo.trfBone.SetLocalScale(1f * this.dictSrc[61].vctScl.x / this.dictSrc[60].vctScl.x / this.dictSrc[58].vctScl.x * this.dictSrc[62].vctScl.x, 1f * this.dictSrc[61].vctScl.y / this.dictSrc[60].vctScl.y / this.dictSrc[58].vctScl.y * this.dictSrc[62].vctScl.y, 1f * this.dictSrc[61].vctScl.z / this.dictSrc[60].vctScl.z / this.dictSrc[58].vctScl.z * this.dictSrc[62].vctScl.z);
                    }
                }
            }


            private void UpdateAlways()
            {
                if (!this.InitEnd)
                {
                    return;
                }
                if (this.dictSrc.Count == 0)
                {
                    return;
                }
                BoneInfo boneInfo = null;
                if (this.dictDst.TryGetValue(38, out boneInfo))
                {
                    boneInfo.trfBone.SetLocalPositionZ(this.dictSrc[39].vctPos.z);
                }
                if (this.fixCorrectBone != null && this.fixCorrectBone.Length == 2)
                {
                    if (this.fixCorrectBone[0])
                    {
                        float x = (this.typeBone != 0) ? this.correctValue[30].vctPos.x : -0.01563369f;
                        this.fixCorrectBone[0].SetLocalPositionX(x);
                    }
                    if (this.fixCorrectBone[1])
                    {
                        float x2 = (this.typeBone != 0) ? this.correctValue[31].vctPos.x : 0.01560147f;
                        this.fixCorrectBone[1].SetLocalPositionX(x2);
                    }
                }
            }

        }
    }

    internal static class KoikatsuTransformShapeExtensions
    {
        public static void SetLocalPositionX(
            this Transform transform,
            float value)
        {
            var position = transform.localPosition;
            position.x = value;
            transform.localPosition = position;
        }

        public static void SetLocalPositionY(
            this Transform transform,
            float value)
        {
            var position = transform.localPosition;
            position.y = value;
            transform.localPosition = position;
        }

        public static void SetLocalPositionZ(
            this Transform transform,
            float value)
        {
            var position = transform.localPosition;
            position.z = value;
            transform.localPosition = position;
        }

        public static void SetLocalRotation(
            this Transform transform,
            float x,
            float y,
            float z)
        {
            transform.localEulerAngles = new Vector3(x, y, z);
        }

        public static void SetLocalScale(
            this Transform transform,
            float x,
            float y,
            float z)
        {
            transform.localScale = new Vector3(x, y, z);
        }
    }
}
