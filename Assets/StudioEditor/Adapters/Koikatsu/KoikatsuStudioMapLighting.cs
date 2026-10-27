using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using StudioEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioEditor.ReferenceModels
{
    // The old SceneInfo.mapLight fields are only saved/loaded by the game.
    // Studio.Map.sunType instead calls SunLightInfo.Set on the map's own rig.
    internal static class KoikatsuStudioMapLighting
    {
        [DataContract]
        internal sealed class Definition
        {
            [DataMember] public float[] Ambient { get; set; }
            [DataMember] public string TargetPath { get; set; }
            [DataMember] public Preset[] Presets { get; set; } = Array.Empty<Preset>();

            public ImportedSceneEnvironment CreateEnvironment()
            {
                if (Ambient == null || Ambient.Length != 41) return null;
                var probe = new SphericalHarmonicsL2();
                for (int channel = 0; channel < 3; channel++)
                    for (int coefficient = 0; coefficient < 9; coefficient++)
                        probe[channel, coefficient] = Ambient[14 + channel * 9 + coefficient];
                return new ImportedSceneEnvironment((AmbientMode)(int)Ambient[0], Ambient[1],
                    ColorAt(Ambient, 2), ColorAt(Ambient, 6), ColorAt(Ambient, 10), probe);
            }

            public Light Apply(GameObject root, int type)
            {
                var preset = Presets.FirstOrDefault(value => value.Type == type);
                if (preset == null) return null;
                var transforms = root.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(transform => KoikatsuClothesRendererMapLoader.CreateRuntimePath(root.transform, transform));
                // Apply visibility in the same array order as SunLightInfo.Set.
                foreach (var value in Presets)
                    foreach (var path in value.VisiblePaths)
                        if (transforms.TryGetValue(path, out var transform)) transform.gameObject.SetActive(value == preset);
                if (TargetPath == null || !transforms.TryGetValue(TargetPath, out var target) ||
                    !target.TryGetComponent<Light>(out var light)) return null;
                light.color = ColorAt(preset.Values, 0);
                light.transform.eulerAngles = new Vector3(preset.Values[4], preset.Values[5], preset.Values[6]);
                light.intensity = preset.Values[7];
                return light;
            }
        }

        [DataContract]
        internal sealed class Preset
        {
            [DataMember] public int Type { get; set; }
            [DataMember] public float[] Values { get; set; }
            [DataMember] public string[] VisiblePaths { get; set; } = Array.Empty<string>();
        }

        public static Definition Read(KoikatsuBundleSource source, string sceneName) =>
            KoikatsuParsedMetadataCache.Read(source, "studio-map-lighting-v1|" + sceneName,
                () => ReadCore(source, sceneName));

        private static Definition ReadCore(KoikatsuBundleSource source, string sceneName)
        {
            var manager = new AssetsManager();
            Stream ownedStream = null;
            try
            {
                var bundle = KoikatsuClothesRendererMapLoader.LoadBundle(manager, source, out ownedStream);
                var directories = bundle.file.BlockAndDirInfo.DirectoryInfos;
                for (int index = 0; index < directories.Count; index++)
                {
                    if (!bundle.file.IsAssetsFile(index) ||
                        !string.Equals(directories[index].Name, "BuildPlayer-" + sceneName, StringComparison.OrdinalIgnoreCase)) continue;
                    var assets = manager.LoadAssetsFileFromBundle(bundle, index, false);
                    return ReadScene(manager, assets);
                }
                return new Definition();
            }
            finally { manager.UnloadAll(true); ownedStream?.Dispose(); }
        }

        private static Definition ReadScene(AssetsManager manager, AssetsFileInstance assets)
        {
            var result = new Definition();
            var context = new KoikatsuClothesRendererMapLoader.ParseContext(manager, assets);
            var rootId = assets.file.GetAssetsOfType(AssetClassID.GameObject)
                .FirstOrDefault(info => manager.GetBaseField(assets, info)["m_Name"].AsString == "Map")?.PathId ?? 0;
            var settingsInfo = assets.file.GetAssetsOfType(AssetClassID.RenderSettings).FirstOrDefault();
            if (settingsInfo != null)
            {
                var settings = manager.GetBaseField(assets, settingsInfo);
                var values = new List<float> { settings["m_AmbientMode"].AsInt, settings["m_AmbientIntensity"].AsFloat };
                values.AddRange(ReadColor(settings["m_AmbientSkyColor"]));
                values.AddRange(ReadColor(settings["m_AmbientEquatorColor"]));
                values.AddRange(ReadColor(settings["m_AmbientGroundColor"]));
                values.AddRange(settings["m_AmbientProbe"].Children.Select(field => field.AsFloat));
                result.Ambient = values.ToArray();
            }
            if (rootId == 0) return result;
            foreach (var info in assets.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                var behaviour = manager.GetBaseField(assets, info);
                var presets = KoikatsuClothesRendererMapLoader.GetArray(behaviour["_infos"]);
                if (presets == null || behaviour["_targetLight"].IsDummy) continue;
                var target = context.Resolve(behaviour["_targetLight"]);
                if (target.info == null) continue;
                var targetOwner = context.Resolve(target.baseField["m_GameObject"]);
                result.TargetPath = ObjectPath(context, rootId, targetOwner);
                var values = new List<Preset>();
                foreach (var preset in presets.Children)
                {
                    var data = new List<float>(ReadColor(preset["color"]));
                    data.Add(preset["angle"]["x"].AsFloat);
                    data.Add(preset["angle"]["y"].AsFloat);
                    data.Add(preset["angle"]["z"].AsFloat);
                    data.Add(preset["intensity"].AsFloat);
                    var visible = KoikatsuClothesRendererMapLoader.GetArray(preset["visibleList"]);
                    values.Add(new Preset { Type = preset["type"].AsInt, Values = data.ToArray(),
                        VisiblePaths = visible?.Children.Select(pointer => ObjectPath(context, rootId, context.Resolve(pointer)))
                            .Where(path => path != null).ToArray() ?? Array.Empty<string>() });
                }
                result.Presets = values.ToArray();
                break;
            }
            return result;
        }

        private static string ObjectPath(KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootId, AssetExternal owner) => owner.info == null ? null :
            KoikatsuClothesRendererMapLoader.CreateSerializedPath(context, rootId,
                KoikatsuClothesRendererMapLoader.FindTransform(context, owner.baseField));

        private static float[] ReadColor(AssetTypeValueField field) => new[] {
            field["r"].AsFloat, field["g"].AsFloat, field["b"].AsFloat, field["a"].AsFloat };

        private static Color ColorAt(float[] values, int index) =>
            new Color(values[index], values[index + 1], values[index + 2], values[index + 3]);
    }
}
