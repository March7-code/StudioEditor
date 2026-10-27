using System.IO;
using System.Runtime.Serialization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using StudioEditor.Rendering;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuStudioLightMetadata
    {
        [DataContract]
        internal sealed class Definition
        {
            [DataMember] public int CullingMask { get; set; }
            [DataMember] public int RenderMode { get; set; }
            [DataMember] public float ShadowStrength { get; set; }
            [DataMember] public float ShadowBias { get; set; }
            [DataMember] public float ShadowNormalBias { get; set; }
            [DataMember] public float ShadowNearPlane { get; set; }

            public ImportedLightAudience GetAudience(int target)
            {
                // AddObjectLight.Load toggles these source layers on the prefab
                // mask. The list's "All" label does not imply an all-layer mask.
                const int character = 1 << 10;
                const int map = 1 << 11;
                const int item = 1 << 4;
                int mask = CullingMask;
                if (target == 1) mask ^= map;
                else if (target == 2) mask ^= character;
                bool characters = (mask & character) != 0;
                bool world = (mask & (map | item | 1)) != 0;
                return characters ? world ? ImportedLightAudience.All : ImportedLightAudience.Characters
                    : world ? ImportedLightAudience.World : ImportedLightAudience.None;
            }

            public void Apply(Light light)
            {
                light.renderMode = (LightRenderMode)RenderMode;
                light.shadowStrength = ShadowStrength;
                light.shadowBias = ShadowBias;
                light.shadowNormalBias = ShadowNormalBias;
                light.shadowNearPlane = ShadowNearPlane;
            }
        }

        public static Definition Read(KoikatsuBundleSource source, string assetName) =>
            KoikatsuParsedMetadataCache.Read(source, "studio-light-v2|" + assetName, () => ReadCore(source, assetName));

        private static Definition ReadCore(KoikatsuBundleSource source, string assetName)
        {
            var manager = new AssetsManager();
            Stream ownedStream = null;
            try
            {
                var bundle = KoikatsuClothesRendererMapLoader.LoadBundle(manager, source, out ownedStream);
                var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                var context = new KoikatsuClothesRendererMapLoader.ParseContext(manager, assets);
                long root = KoikatsuClothesRendererMapLoader.FindRootGameObject(context, assetName);
                if (root == 0) return null;
                foreach (var info in assets.file.GetAssetsOfType(AssetClassID.Light))
                {
                    var field = manager.GetBaseField(assets, info);
                    var owner = context.Resolve(field["m_GameObject"]);
                    if (owner.info == null) continue;
                    if (owner.info.PathId != root && KoikatsuClothesRendererMapLoader.CreateSerializedPath(context, root,
                        KoikatsuClothesRendererMapLoader.FindTransform(context, owner.baseField)) == null) continue;
                    var shadows = field["m_Shadows"];
                    return new Definition
                    {
                        CullingMask = unchecked((int)field["m_CullingMask"]["m_Bits"].AsUInt),
                        RenderMode = field["m_RenderMode"].AsInt,
                        ShadowStrength = shadows["m_Strength"].AsFloat,
                        ShadowBias = shadows["m_Bias"].AsFloat,
                        ShadowNormalBias = shadows["m_NormalBias"].AsFloat,
                        ShadowNearPlane = shadows["m_NearPlane"].AsFloat,
                    };
                }
                return null;
            }
            finally { manager.UnloadAll(true); ownedStream?.Dispose(); }
        }
    }
}
