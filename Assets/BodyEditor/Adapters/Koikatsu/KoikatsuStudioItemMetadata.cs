using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal enum KoikatsuStudioRendererRole
    {
        Normal,
        Alpha,
        Glass,
        AccessoryNormal,
        AccessoryAlpha,
        Panel,
    }

    internal sealed class KoikatsuStudioItemRendererMap
    {
        private readonly IReadOnlyDictionary<Renderer, KoikatsuStudioRendererRole>
            roles;

        public KoikatsuStudioItemRendererMap(
            IReadOnlyDictionary<Renderer, KoikatsuStudioRendererRole> roles)
        {
            this.roles = roles ?? throw new ArgumentNullException(nameof(roles));
        }

        public bool TryGetRole(
            Renderer renderer,
            out KoikatsuStudioRendererRole role)
        {
            return roles.TryGetValue(renderer, out role);
        }
    }

    internal static class KoikatsuStudioItemMetadataLoader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Metadata> Cache =
            new Dictionary<string, Metadata>(StringComparer.OrdinalIgnoreCase);

        public static KoikatsuStudioItemRendererMap TryCreate(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrEmpty(assetName) ||
                instance == null)
            {
                return null;
            }

            var file = new FileInfo(source.FilePath);
            var key = source.CacheKey + "|" + assetName + "|" +
                      file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            Metadata metadata;
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(key, out metadata))
                {
                    metadata = ParseSafely(source, assetName);
                    Cache.Add(key, metadata);
                }
            }

            if (metadata == null || metadata.Roles.Count == 0)
            {
                return null;
            }

            var roles = new Dictionary<Renderer, KoikatsuStudioRendererRole>();
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var locator = KoikatsuClothesRendererMapLoader.CreateRuntimeLocator(
                    instance.transform,
                    renderers[index]);
                if (locator != null && metadata.Roles.TryGetValue(
                        locator,
                        out var role))
                {
                    roles[renderers[index]] = role;
                }
            }

            if (roles.Count == 0)
            {
                Debug.LogWarning(
                    $"Koikatsu Studio metadata for prefab '{assetName}' in " +
                    $"'{source.DisplayName}' did not match its runtime renderers.");
                return null;
            }

            return new KoikatsuStudioItemRendererMap(roles);
        }

        private static Metadata ParseSafely(
            KoikatsuBundleSource source,
            string assetName)
        {
            try
            {
                return Parse(source, assetName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not read Koikatsu Studio metadata for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message);
                return null;
            }
        }

        private static Metadata Parse(
            KoikatsuBundleSource source,
            string assetName)
        {
            var manager = new AssetsManager();
            Stream ownedStream = null;
            try
            {
                var bundle = KoikatsuClothesRendererMapLoader.LoadBundle(
                    manager,
                    source,
                    out ownedStream);
                var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                var context = new KoikatsuClothesRendererMapLoader.ParseContext(
                    manager,
                    assets);
                var rootPathId = KoikatsuClothesRendererMapLoader.FindRootGameObject(
                    context,
                    assetName);
                if (rootPathId == 0)
                {
                    return null;
                }

                var metadata = new Metadata();
                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var index = 0; index < behaviours.Count; index++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[index],
                        AssetReadFlags.None);
                    if (GetPathId(behaviour["m_GameObject"]) != rootPathId)
                    {
                        continue;
                    }

                    if (!behaviour["info"].IsDummy &&
                        !behaviour["rendGlass"].IsDummy)
                    {
                        AddArray(
                            context,
                            metadata,
                            rootPathId,
                            behaviour["rendNormal"],
                            KoikatsuStudioRendererRole.Normal);
                        AddArray(
                            context,
                            metadata,
                            rootPathId,
                            behaviour["rendAlpha"],
                            KoikatsuStudioRendererRole.Alpha);
                        AddArray(
                            context,
                            metadata,
                            rootPathId,
                            behaviour["rendGlass"],
                            KoikatsuStudioRendererRole.Glass);
                    }
                    else if (!behaviour["rendHair"].IsDummy)
                    {
                        AddArray(
                            context,
                            metadata,
                            rootPathId,
                            behaviour["rendNormal"],
                            KoikatsuStudioRendererRole.AccessoryNormal);
                        AddArray(
                            context,
                            metadata,
                            rootPathId,
                            behaviour["rendAlpha"],
                            KoikatsuStudioRendererRole.AccessoryAlpha);
                    }
                    else if (!behaviour["renderer"].IsDummy)
                    {
                        AddArray(
                            context,
                            metadata,
                            rootPathId,
                            behaviour["renderer"],
                            KoikatsuStudioRendererRole.Panel);
                    }
                }

                return metadata;
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        private static void AddArray(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            Metadata metadata,
            long rootPathId,
            AssetTypeValueField field,
            KoikatsuStudioRendererRole role)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return;
            }

            for (var index = 0; index < array.Children.Count; index++)
            {
                var locator =
                    KoikatsuClothesRendererMapLoader.CreateSerializedLocator(
                        context,
                        rootPathId,
                        array.Children[index]);
                if (locator != null)
                {
                    metadata.Roles[locator] = role;
                }
            }
        }

        private static long GetPathId(AssetTypeValueField pointer)
        {
            if (pointer == null || pointer.IsDummy)
            {
                return 0;
            }

            var pathId = pointer["m_PathID"];
            return pathId.IsDummy ? 0 : pathId.AsLong;
        }

        private sealed class Metadata
        {
            public Dictionary<string, KoikatsuStudioRendererRole> Roles { get; } =
                new Dictionary<string, KoikatsuStudioRendererRole>(
                    StringComparer.Ordinal);
        }
    }

    internal static class KoikatsuSpringBoneMetadataLoader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, SpringDefinition[]> Cache =
            new Dictionary<string, SpringDefinition[]>(
                StringComparer.OrdinalIgnoreCase);

        public static int Attach(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance,
            bool allowed = true)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                instance == null)
            {
                return 0;
            }

            var definitions = GetDefinitions(source, assetName);
            if (definitions.Length == 0)
            {
                return 0;
            }

            var transforms = BuildRuntimeTransformMap(instance.transform);
            var attached = 0;
            for (var index = 0; index < definitions.Length; index++)
            {
                var definition = definitions[index];
                if (!transforms.TryGetValue(definition.RootPath, out var root))
                {
                    continue;
                }

                var exclusions = new List<Transform>();
                for (var exclusionIndex = 0;
                     exclusionIndex < definition.ExclusionPaths.Length;
                     exclusionIndex++)
                {
                    if (transforms.TryGetValue(
                            definition.ExclusionPaths[exclusionIndex],
                            out var exclusion))
                    {
                        exclusions.Add(exclusion);
                    }
                }

                var spring = instance.AddComponent<KoikatsuSpringBone>();
                spring.enabled = false;
                spring.Configure(
                    root,
                    definition.UpdateRate,
                    definition.Damping,
                    definition.Elasticity,
                    definition.Stiffness,
                    definition.Inert,
                    definition.EndLength,
                    definition.EndOffset,
                    definition.Gravity,
                    definition.Force,
                    definition.FreezeAxis,
                    exclusions,
                    allowed && definition.Enabled);
                attached++;
            }

            return attached;
        }

        private static SpringDefinition[] GetDefinitions(
            KoikatsuBundleSource source,
            string assetName)
        {
            var file = new FileInfo(source.FilePath);
            var key = source.CacheKey + "|" + assetName + "|spring|" +
                      file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(key, out var definitions))
                {
                    definitions = ParseSafely(source, assetName);
                    Cache.Add(key, definitions);
                }

                return definitions;
            }
        }

        private static SpringDefinition[] ParseSafely(
            KoikatsuBundleSource source,
            string assetName)
        {
            try
            {
                return Parse(source, assetName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu spring-bone metadata for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message);
                return Array.Empty<SpringDefinition>();
            }
        }

        private static SpringDefinition[] Parse(
            KoikatsuBundleSource source,
            string assetName)
        {
            var manager = new AssetsManager();
            Stream ownedStream = null;
            try
            {
                var bundle = KoikatsuClothesRendererMapLoader.LoadBundle(
                    manager,
                    source,
                    out ownedStream);
                var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                var context = new KoikatsuClothesRendererMapLoader.ParseContext(
                    manager,
                    assets);
                var rootPathId = KoikatsuClothesRendererMapLoader.FindRootGameObject(
                    context,
                    assetName);
                if (rootPathId == 0)
                {
                    return Array.Empty<SpringDefinition>();
                }

                var result = new List<SpringDefinition>();
                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var index = 0; index < behaviours.Count; index++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[index],
                        AssetReadFlags.None);
                    if (behaviour["m_Root"].IsDummy ||
                        behaviour["m_UpdateRate"].IsDummy ||
                        behaviour["m_Damping"].IsDummy ||
                        behaviour["m_Elasticity"].IsDummy ||
                        behaviour["m_Stiffness"].IsDummy ||
                        behaviour["m_Inert"].IsDummy)
                    {
                        continue;
                    }

                    var rootPath = GetTransformPath(
                        context,
                        rootPathId,
                        behaviour["m_Root"]);
                    if (rootPath == null)
                    {
                        continue;
                    }

                    result.Add(new SpringDefinition(
                        rootPath,
                        ReadFloat(behaviour["m_UpdateRate"], 60f),
                        ReadFloat(behaviour["m_Damping"], 0.2f),
                        ReadFloat(behaviour["m_Elasticity"], 0.2f),
                        ReadFloat(behaviour["m_Stiffness"], 0.1f),
                        ReadFloat(behaviour["m_Inert"], 0f),
                        ReadFloat(behaviour["m_EndLength"], 0f),
                        ReadVector3(behaviour["m_EndOffset"]),
                        ReadVector3(behaviour["m_Gravity"]),
                        ReadVector3(behaviour["m_Force"]),
                        ReadInt(behaviour["m_FreezeAxis"], 0),
                        ReadTransformPaths(
                            context,
                            rootPathId,
                            behaviour["m_Exclusions"]),
                        ReadBool(behaviour["m_Enabled"], true)));
                }

                return result.ToArray();
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        internal static Dictionary<string, Transform> BuildRuntimeTransformMap(
            Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                var path = KoikatsuClothesRendererMapLoader.CreateRuntimePath(
                    root,
                    transforms[index]);
                if (path != null && !result.ContainsKey(path))
                {
                    result.Add(path, transforms[index]);
                }
            }

            return result;
        }

        private static string[] ReadTransformPaths(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField field)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            for (var index = 0; index < array.Children.Count; index++)
            {
                var path = GetTransformPath(
                    context,
                    rootPathId,
                    array.Children[index]);
                if (path != null)
                {
                    result.Add(path);
                }
            }

            return result.ToArray();
        }

        private static string GetTransformPath(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField pointer)
        {
            var transform = context.Resolve(pointer);
            if (transform.info == null ||
                transform.info.TypeId != (int)AssetClassID.Transform)
            {
                return null;
            }

            return KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                context,
                rootPathId,
                transform);
        }

        private static float ReadFloat(AssetTypeValueField field, float fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsFloat;
        }

        private static int ReadInt(AssetTypeValueField field, int fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsInt;
        }

        private static bool ReadBool(AssetTypeValueField field, bool fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsBool;
        }

        private static Vector3 ReadVector3(AssetTypeValueField field)
        {
            if (field == null || field.IsDummy)
            {
                return Vector3.zero;
            }

            return new Vector3(
                ReadFloat(field["x"], 0f),
                ReadFloat(field["y"], 0f),
                ReadFloat(field["z"], 0f));
        }

        private sealed class SpringDefinition
        {
            public SpringDefinition(
                string rootPath,
                float updateRate,
                float damping,
                float elasticity,
                float stiffness,
                float inert,
                float endLength,
                Vector3 endOffset,
                Vector3 gravity,
                Vector3 force,
                int freezeAxis,
                string[] exclusionPaths,
                bool enabled)
            {
                RootPath = rootPath;
                UpdateRate = updateRate;
                Damping = damping;
                Elasticity = elasticity;
                Stiffness = stiffness;
                Inert = inert;
                EndLength = endLength;
                EndOffset = endOffset;
                Gravity = gravity;
                Force = force;
                FreezeAxis = freezeAxis;
                ExclusionPaths = exclusionPaths ?? Array.Empty<string>();
                Enabled = enabled;
            }

            public string RootPath { get; }
            public float UpdateRate { get; }
            public float Damping { get; }
            public float Elasticity { get; }
            public float Stiffness { get; }
            public float Inert { get; }
            public float EndLength { get; }
            public Vector3 EndOffset { get; }
            public Vector3 Gravity { get; }
            public Vector3 Force { get; }
            public int FreezeAxis { get; }
            public string[] ExclusionPaths { get; }
            public bool Enabled { get; }
        }
    }

    internal static class KoikatsuVer02MetadataLoader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Ver02Definition[]> Cache =
            new Dictionary<string, Ver02Definition[]>(
                StringComparer.OrdinalIgnoreCase);

        public static int Attach(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                instance == null)
            {
                return 0;
            }

            var definitions = GetDefinitions(source, assetName);
            if (definitions.Length == 0)
            {
                return 0;
            }

            var transforms =
                KoikatsuSpringBoneMetadataLoader.BuildRuntimeTransformMap(
                    instance.transform);
            var attached = 0;
            for (var index = 0; index < definitions.Length; index++)
            {
                var definition = definitions[index];
                if (!transforms.TryGetValue(
                        definition.MotionRootPath,
                        out var motionRoot))
                {
                    continue;
                }

                var bones = new Transform[definition.BonePaths.Length];
                var references = new Transform[definition.BonePaths.Length];
                var valid = true;
                for (var boneIndex = 0;
                     boneIndex < definition.BonePaths.Length;
                     boneIndex++)
                {
                    if (!transforms.TryGetValue(
                            definition.BonePaths[boneIndex],
                            out bones[boneIndex]))
                    {
                        valid = false;
                        break;
                    }

                    var referencePath =
                        definition.Particles[boneIndex].ReferencePath;
                    if (string.IsNullOrEmpty(referencePath) ||
                        !transforms.TryGetValue(
                            referencePath,
                            out references[boneIndex]))
                    {
                        references[boneIndex] = bones[boneIndex];
                    }
                }

                if (!valid)
                {
                    continue;
                }

                var spring = instance.AddComponent<KoikatsuVer02SpringBone>();
                spring.enabled = false;
                spring.Configure(
                    motionRoot,
                    bones,
                    references,
                    definition.Particles,
                    definition.EndParticle,
                    definition.UpdateRate,
                    definition.ReflectSpeed,
                    definition.MaximumSteps,
                    definition.Gravity,
                    definition.Force,
                    definition.Enabled);
                attached++;
            }

            return attached;
        }

        private static Ver02Definition[] GetDefinitions(
            KoikatsuBundleSource source,
            string assetName)
        {
            var file = new FileInfo(source.FilePath);
            var key = source.CacheKey + "|" + assetName + "|ver02|" +
                      file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(key, out var definitions))
                {
                    definitions = ParseSafely(source, assetName);
                    Cache.Add(key, definitions);
                }

                return definitions;
            }
        }

        private static Ver02Definition[] ParseSafely(
            KoikatsuBundleSource source,
            string assetName)
        {
            try
            {
                return Parse(source, assetName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read Koikatsu DynamicBone_Ver02 metadata for " +
                    $"prefab '{assetName}' in '{source.DisplayName}': " +
                    exception.Message);
                return Array.Empty<Ver02Definition>();
            }
        }

        private static Ver02Definition[] Parse(
            KoikatsuBundleSource source,
            string assetName)
        {
            var manager = new AssetsManager();
            Stream ownedStream = null;
            try
            {
                var bundle = KoikatsuClothesRendererMapLoader.LoadBundle(
                    manager,
                    source,
                    out ownedStream);
                var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                var context = new KoikatsuClothesRendererMapLoader.ParseContext(
                    manager,
                    assets);
                var rootPathId = KoikatsuClothesRendererMapLoader.FindRootGameObject(
                    context,
                    assetName);
                if (rootPathId == 0)
                {
                    return Array.Empty<Ver02Definition>();
                }

                var result = new List<Ver02Definition>();
                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var index = 0; index < behaviours.Count; index++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[index],
                        AssetReadFlags.None);
                    if (behaviour["Root"].IsDummy ||
                        behaviour["Bones"].IsDummy ||
                        behaviour["Patterns"].IsDummy ||
                        behaviour["ReflectSpeed"].IsDummy)
                    {
                        continue;
                    }

                    var motionRootPath = GetTransformPath(
                        context,
                        rootPathId,
                        behaviour["Root"]);
                    var bonePaths = ReadTransformPaths(
                        context,
                        rootPathId,
                        behaviour["Bones"]);
                    var patterns =
                        KoikatsuClothesRendererMapLoader.GetArray(
                            behaviour["Patterns"]);
                    if (motionRootPath == null || bonePaths.Length == 0 ||
                        patterns == null || patterns.Children.Count == 0)
                    {
                        continue;
                    }

                    var patternIndex = Mathf.Clamp(
                        ReadInt(behaviour["PtnNo"], 0),
                        0,
                        patterns.Children.Count - 1);
                    var pattern = patterns.Children[patternIndex];
                    var parameters =
                        KoikatsuClothesRendererMapLoader.GetArray(
                            pattern["Params"]);
                    if (parameters == null ||
                        parameters.Children.Count != bonePaths.Length)
                    {
                        continue;
                    }

                    var particleDefinitions =
                        new Ver02ParticleDefinition[bonePaths.Length];
                    for (var particleIndex = 0;
                         particleIndex < particleDefinitions.Length;
                         particleIndex++)
                    {
                        particleDefinitions[particleIndex] = ReadParticle(
                            context,
                            rootPathId,
                            parameters.Children[particleIndex]);
                    }

                    var endParticle = new Ver02ParticleDefinition(
                        null,
                        ReadFloat(pattern["EndOffsetDamping"], 0f),
                        ReadFloat(pattern["EndOffsetElasticity"], 0f),
                        ReadFloat(pattern["EndOffsetStiffness"], 0f),
                        ReadFloat(pattern["EndOffsetInert"], 0f),
                        false,
                        1f,
                        false,
                        Vector3.zero,
                        Vector3.zero,
                        0f,
                        0f,
                        false,
                        0f,
                        0f,
                        0f,
                        0f,
                        ReadVector3(pattern["EndOffset"]));

                    result.Add(new Ver02Definition(
                        motionRootPath,
                        bonePaths,
                        particleDefinitions,
                        endParticle,
                        ReadFloat(behaviour["UpdateRate"], 60f),
                        ReadFloat(behaviour["ReflectSpeed"], 1f),
                        Mathf.Max(1, ReadInt(
                            behaviour["HeavyLoopMaxCount"],
                            3)),
                        ReadVector3(pattern["Gravity"]),
                        ReadVector3(behaviour["Force"]),
                        ReadBool(behaviour["m_Enabled"], true)));
                }

                return result.ToArray();
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        private static Ver02ParticleDefinition ReadParticle(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField field)
        {
            return new Ver02ParticleDefinition(
                GetTransformPath(context, rootPathId, field["RefTransform"]),
                ReadFloat(field["Damping"], 0f),
                ReadFloat(field["Elasticity"], 0f),
                ReadFloat(field["Stiffness"], 0f),
                ReadFloat(field["Inert"], 0f),
                ReadBool(field["IsRotationCalc"], false),
                Mathf.Max(0f, ReadFloat(field["NextBoneLength"], 1f)),
                ReadBool(field["IsMoveLimit"], false),
                ReadVector3(field["MoveLimitMin"]),
                ReadVector3(field["MoveLimitMax"]),
                ReadFloat(field["KeepLengthLimitMin"], 0f),
                ReadFloat(field["KeepLengthLimitMax"], 0f),
                ReadBool(field["IsCrush"], false),
                ReadFloat(field["CrushMoveAreaMin"], 0f),
                ReadFloat(field["CrushMoveAreaMax"], 0f),
                ReadFloat(field["CrushAddXYMin"], 0f),
                ReadFloat(field["CrushAddXYMax"], 0f),
                Vector3.zero);
        }

        private static string[] ReadTransformPaths(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField field)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(array.Children.Count);
            for (var index = 0; index < array.Children.Count; index++)
            {
                var path = GetTransformPath(
                    context,
                    rootPathId,
                    array.Children[index]);
                if (path == null)
                {
                    return Array.Empty<string>();
                }

                result.Add(path);
            }

            return result.ToArray();
        }

        private static string GetTransformPath(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField pointer)
        {
            if (pointer == null || pointer.IsDummy)
            {
                return null;
            }

            var transform = context.Resolve(pointer);
            if (transform.info == null ||
                transform.info.TypeId != (int)AssetClassID.Transform)
            {
                return null;
            }

            return KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                context,
                rootPathId,
                transform);
        }

        private static float ReadFloat(AssetTypeValueField field, float fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsFloat;
        }

        private static int ReadInt(AssetTypeValueField field, int fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsInt;
        }

        private static bool ReadBool(AssetTypeValueField field, bool fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsBool;
        }

        private static Vector3 ReadVector3(AssetTypeValueField field)
        {
            if (field == null || field.IsDummy)
            {
                return Vector3.zero;
            }

            return new Vector3(
                ReadFloat(field["x"], 0f),
                ReadFloat(field["y"], 0f),
                ReadFloat(field["z"], 0f));
        }

        private sealed class Ver02Definition
        {
            public Ver02Definition(
                string motionRootPath,
                string[] bonePaths,
                Ver02ParticleDefinition[] particles,
                Ver02ParticleDefinition endParticle,
                float updateRate,
                float reflectSpeed,
                int maximumSteps,
                Vector3 gravity,
                Vector3 force,
                bool enabled)
            {
                MotionRootPath = motionRootPath;
                BonePaths = bonePaths;
                Particles = particles;
                EndParticle = endParticle;
                UpdateRate = updateRate;
                ReflectSpeed = reflectSpeed;
                MaximumSteps = maximumSteps;
                Gravity = gravity;
                Force = force;
                Enabled = enabled;
            }

            public string MotionRootPath { get; }
            public string[] BonePaths { get; }
            public Ver02ParticleDefinition[] Particles { get; }
            public Ver02ParticleDefinition EndParticle { get; }
            public float UpdateRate { get; }
            public float ReflectSpeed { get; }
            public int MaximumSteps { get; }
            public Vector3 Gravity { get; }
            public Vector3 Force { get; }
            public bool Enabled { get; }
        }
    }

    internal sealed class Ver02ParticleDefinition
    {
        public Ver02ParticleDefinition(
            string referencePath,
            float damping,
            float elasticity,
            float stiffness,
            float inert,
            bool calculateRotation,
            float nextBoneLengthScale,
            bool limitMovement,
            Vector3 movementMinimum,
            Vector3 movementMaximum,
            float lengthLimitMinimum,
            float lengthLimitMaximum,
            bool crush,
            float crushMovementMinimum,
            float crushMovementMaximum,
            float crushScaleMinimum,
            float crushScaleMaximum,
            Vector3 endOffset)
        {
            ReferencePath = referencePath;
            Damping = Mathf.Clamp01(damping);
            Elasticity = Mathf.Clamp01(elasticity);
            Stiffness = Mathf.Clamp01(stiffness);
            Inert = Mathf.Clamp01(inert);
            CalculateRotation = calculateRotation;
            NextBoneLengthScale = Mathf.Max(0f, nextBoneLengthScale);
            LimitMovement = limitMovement;
            MovementMinimum = movementMinimum;
            MovementMaximum = movementMaximum;
            LengthLimitMinimum = lengthLimitMinimum;
            LengthLimitMaximum = lengthLimitMaximum;
            Crush = crush;
            CrushMovementMinimum = crushMovementMinimum;
            CrushMovementMaximum = crushMovementMaximum;
            CrushScaleMinimum = crushScaleMinimum;
            CrushScaleMaximum = crushScaleMaximum;
            EndOffset = endOffset;
        }

        public string ReferencePath { get; }
        public float Damping { get; }
        public float Elasticity { get; }
        public float Stiffness { get; }
        public float Inert { get; }
        public bool CalculateRotation { get; }
        public float NextBoneLengthScale { get; }
        public bool LimitMovement { get; }
        public Vector3 MovementMinimum { get; }
        public Vector3 MovementMaximum { get; }
        public float LengthLimitMinimum { get; }
        public float LengthLimitMaximum { get; }
        public bool Crush { get; }
        public float CrushMovementMinimum { get; }
        public float CrushMovementMaximum { get; }
        public float CrushScaleMinimum { get; }
        public float CrushScaleMaximum { get; }
        public Vector3 EndOffset { get; }
    }

    [DefaultExecutionOrder(220)]
    internal sealed class KoikatsuVer02SpringBone : MonoBehaviour
    {
        private const float Epsilon = 0.000001f;

        private Transform motionRoot;
        private Particle[] particles = Array.Empty<Particle>();
        private float updateRate;
        private float reflectSpeed;
        private int maximumSteps;
        private Vector3 gravity;
        private Vector3 force;
        private float accumulator;
        private Vector3 previousRootPosition;
        private bool configured;

        public bool Allowed { get; private set; }

        public bool IsBust => particles.Length != 0 &&
                              particles[0].Transform != null &&
                              particles[0].Transform.name.IndexOf(
                                  "bust",
                                  StringComparison.OrdinalIgnoreCase) >= 0;

        public void Configure(
            Transform root,
            IReadOnlyList<Transform> bones,
            IReadOnlyList<Transform> references,
            IReadOnlyList<Ver02ParticleDefinition> definitions,
            Ver02ParticleDefinition endDefinition,
            float requestedUpdateRate,
            float requestedReflectSpeed,
            int requestedMaximumSteps,
            Vector3 requestedGravity,
            Vector3 requestedForce,
            bool allowed)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (bones == null || references == null || definitions == null ||
                endDefinition == null || bones.Count == 0 ||
                bones.Count != references.Count ||
                bones.Count != definitions.Count)
            {
                throw new ArgumentException(
                    "DynamicBone_Ver02 requires matching bones, references, and parameters.");
            }

            motionRoot = root;
            updateRate = Mathf.Max(0f, requestedUpdateRate);
            reflectSpeed = Mathf.Max(0f, requestedReflectSpeed);
            maximumSteps = Mathf.Max(1, requestedMaximumSteps);
            gravity = requestedGravity;
            force = requestedForce;
            Allowed = allowed;

            var values = new List<Particle>(bones.Count + 1);
            for (var index = 0; index < bones.Count; index++)
            {
                var parent = index > 0 ? values[index - 1] : null;
                var localOffset = parent == null
                    ? Vector3.zero
                    : references[index - 1].InverseTransformPoint(
                        references[index].position);
                values.Add(new Particle(
                    bones[index],
                    references[index],
                    definitions[index],
                    parent,
                    localOffset));
            }

            values.Add(new Particle(
                null,
                null,
                endDefinition,
                values[values.Count - 1],
                endDefinition.EndOffset));
            particles = values.ToArray();
            configured = particles.Length > 1;
            ResetSimulation();
        }

        public void SetAllowed(bool value)
        {
            Allowed = value;
            if (!Allowed)
            {
                enabled = false;
            }
        }

        public void SetSimulationEnabled(bool value)
        {
            var shouldEnable = value && Allowed && configured;
            if (enabled == shouldEnable)
            {
                if (shouldEnable)
                {
                    ResetSimulation();
                }

                return;
            }

            enabled = shouldEnable;
        }

        private void OnEnable()
        {
            if (configured)
            {
                ResetSimulation();
            }
        }

        private void OnDisable()
        {
            if (configured)
            {
                RestoreTransforms();
                ResetSimulation();
            }
        }

        private void LateUpdate()
        {
            if (!configured || motionRoot == null)
            {
                return;
            }

            RestoreTransforms();
            var rootMovement = motionRoot.position - previousRootPosition;
            previousRootPosition = motionRoot.position;
            var steps = CalculateStepCount(Time.deltaTime);
            if (steps == 0)
            {
                ShiftParticles(rootMovement);
                AnchorRoot();
                ConstrainParticles();
                ApplyParticles();
                return;
            }

            for (var step = 0; step < steps; step++)
            {
                SimulateParticles(step == 0 ? rootMovement : Vector3.zero);
                ConstrainParticles();
            }

            ApplyParticles();
        }

        private int CalculateStepCount(float deltaTime)
        {
            if (updateRate <= 0f)
            {
                return 1;
            }

            var interval = 1f / updateRate;
            accumulator += Mathf.Max(0f, deltaTime);
            var steps = 0;
            while (accumulator >= interval && steps < maximumSteps)
            {
                accumulator -= interval;
                steps++;
            }

            if (steps == maximumSteps && accumulator >= interval)
            {
                accumulator = 0f;
            }

            return steps;
        }

        private void SimulateParticles(Vector3 rootMovement)
        {
            AnchorRoot();
            var objectScale = Mathf.Abs(motionRoot.lossyScale.x);
            var acceleration = (gravity + force) * objectScale;
            for (var index = 1; index < particles.Length; index++)
            {
                var particle = particles[index];
                var velocity = (particle.Position - particle.PreviousPosition) *
                               reflectSpeed;
                var inheritedMovement = rootMovement * particle.Definition.Inert;
                particle.PreviousPosition =
                    particle.Position + inheritedMovement;
                particle.Position +=
                    velocity * (1f - particle.Definition.Damping) +
                    acceleration + inheritedMovement;
            }
        }

        private void ConstrainParticles()
        {
            for (var index = 1; index < particles.Length; index++)
            {
                var particle = particles[index];
                var parent = particle.Parent;
                var desired = parent.Position +
                              parent.Transform.TransformVector(
                                  particle.LocalOffset);
                particle.Position += (desired - particle.Position) *
                                     particle.Definition.Elasticity;

                var stiffness = particle.Definition.Stiffness;
                var restLength = parent.Transform.TransformVector(
                    particle.LocalOffset).magnitude;
                var stiffnessLimit = restLength * (1f - stiffness) * 2f;
                var displacement = desired - particle.Position;
                if (stiffnessLimit <= 0f)
                {
                    particle.Position = desired;
                }
                else if (displacement.sqrMagnitude >
                         stiffnessLimit * stiffnessLimit)
                {
                    particle.Position += displacement.normalized *
                                         (displacement.magnitude - stiffnessLimit);
                }

                ApplyLengthLimit(particle, parent, restLength);
                ApplyMovementLimit(particle, desired);
            }
        }

        private static void ApplyLengthLimit(
            Particle particle,
            Particle parent,
            float restLength)
        {
            var towardParent = parent.Position - particle.Position;
            var currentLength = towardParent.magnitude;
            if (currentLength <= Epsilon)
            {
                return;
            }

            var ratio = (currentLength - restLength) / currentLength;
            if (particle.Definition.LengthLimitMinimum >= ratio)
            {
                particle.Position += towardParent *
                                     (ratio -
                                      particle.Definition.LengthLimitMinimum);
            }
            else if (ratio >= particle.Definition.LengthLimitMaximum)
            {
                particle.Position += towardParent *
                                     (ratio -
                                      particle.Definition.LengthLimitMaximum);
            }
        }

        private static void ApplyMovementLimit(
            Particle particle,
            Vector3 desiredPosition)
        {
            if (particle.Transform == null ||
                !particle.Definition.LimitMovement)
            {
                return;
            }

            var matrix = particle.Transform.localToWorldMatrix;
            matrix.SetColumn(
                3,
                new Vector4(
                    desiredPosition.x,
                    desiredPosition.y,
                    desiredPosition.z,
                    1f));
            var local = matrix.inverse.MultiplyPoint3x4(particle.Position);
            local.x = Mathf.Clamp(
                local.x,
                particle.Definition.MovementMinimum.x,
                particle.Definition.MovementMaximum.x);
            local.y = Mathf.Clamp(
                local.y,
                particle.Definition.MovementMinimum.y,
                particle.Definition.MovementMaximum.y);
            local.z = Mathf.Clamp(
                local.z,
                particle.Definition.MovementMinimum.z,
                particle.Definition.MovementMaximum.z);
            particle.Position = matrix.MultiplyPoint3x4(local);
        }

        private void ApplyParticles()
        {
            for (var index = 1; index < particles.Length; index++)
            {
                var particle = particles[index];
                var parent = particle.Parent;
                if (parent.Definition.CalculateRotation)
                {
                    var sourceDirection =
                        parent.Transform.TransformDirection(
                            particle.LocalOffset);
                    var targetDirection = particle.Position - parent.Position;
                    if (particle.LocalOffset.sqrMagnitude > Epsilon)
                    {
                        targetDirection = particle.Position -
                                          sourceDirection *
                                          (1f - parent.Definition
                                              .NextBoneLengthScale) -
                                          parent.Position;
                    }

                    if (sourceDirection.sqrMagnitude > Epsilon &&
                        targetDirection.sqrMagnitude > Epsilon)
                    {
                        parent.Transform.rotation = Quaternion.FromToRotation(
                                                        sourceDirection,
                                                        targetDirection) *
                                                    parent.Transform.rotation;
                    }
                }

                if (particle.Transform == null)
                {
                    continue;
                }

                ApplyCrush(particle);
                particle.Transform.position = particle.Position;
            }
        }

        private static void ApplyCrush(Particle particle)
        {
            if (!particle.Definition.Crush)
            {
                return;
            }

            var local = particle.Transform.localToWorldMatrix.inverse
                .MultiplyPoint3x4(particle.Position);
            float addition;
            if (local.z <= 0f)
            {
                var rate = Mathf.Clamp01(Mathf.InverseLerp(
                    particle.Definition.CrushMovementMinimum,
                    0f,
                    local.z));
                addition = particle.Definition.CrushScaleMinimum *
                           (1f - rate);
            }
            else
            {
                var rate = Mathf.Clamp01(Mathf.InverseLerp(
                    0f,
                    particle.Definition.CrushMovementMaximum,
                    local.z));
                addition = particle.Definition.CrushScaleMaximum * rate;
            }

            particle.Transform.localScale = particle.Reference.localScale +
                                            new Vector3(
                                                addition,
                                                addition,
                                                0f);
        }

        private void RestoreTransforms()
        {
            for (var index = 0; index < particles.Length - 1; index++)
            {
                var particle = particles[index];
                particle.Transform.localPosition =
                    particle.Reference.localPosition;
                particle.Transform.localRotation =
                    particle.Reference.localRotation;
                particle.Transform.localScale = particle.Reference.localScale;
            }
        }

        private void AnchorRoot()
        {
            particles[0].PreviousPosition = particles[0].Position;
            particles[0].Position = particles[0].Transform.position;
        }

        private void ShiftParticles(Vector3 movement)
        {
            for (var index = 1; index < particles.Length; index++)
            {
                particles[index].Position += movement;
                particles[index].PreviousPosition += movement;
            }
        }

        private void ResetSimulation()
        {
            accumulator = 0f;
            previousRootPosition = motionRoot != null
                ? motionRoot.position
                : transform.position;
            for (var index = 0; index < particles.Length; index++)
            {
                var particle = particles[index];
                particle.Position = particle.Transform != null
                    ? particle.Transform.position
                    : particle.Parent.Transform.TransformPoint(
                        particle.LocalOffset);
                particle.PreviousPosition = particle.Position;
            }
        }

        private sealed class Particle
        {
            public Particle(
                Transform transform,
                Transform reference,
                Ver02ParticleDefinition definition,
                Particle parent,
                Vector3 localOffset)
            {
                Transform = transform;
                Reference = reference;
                Definition = definition;
                Parent = parent;
                LocalOffset = localOffset;
                Position = transform != null
                    ? transform.position
                    : parent.Transform.TransformPoint(localOffset);
                PreviousPosition = Position;
            }

            public Transform Transform { get; }
            public Transform Reference { get; }
            public Ver02ParticleDefinition Definition { get; }
            public Particle Parent { get; }
            public Vector3 LocalOffset { get; }
            public Vector3 Position { get; set; }
            public Vector3 PreviousPosition { get; set; }
        }
    }

    [DefaultExecutionOrder(200)]
    internal sealed class KoikatsuSpringBone : MonoBehaviour
    {
        private const float Epsilon = 0.000001f;
        private const int MaximumSteps = 3;

        private Transform springRoot;
        private Particle[] particles = Array.Empty<Particle>();
        private float updateRate;
        private float damping;
        private float elasticity;
        private float stiffness;
        private float inert;
        private Vector3 gravity;
        private Vector3 force;
        private int freezeAxis;
        private float accumulator;
        private Vector3 previousOwnerPosition;
        private bool configured;

        public bool Allowed { get; private set; }

        public void Configure(
            Transform root,
            float requestedUpdateRate,
            float requestedDamping,
            float requestedElasticity,
            float requestedStiffness,
            float requestedInert,
            float endLength,
            Vector3 endOffset,
            Vector3 requestedGravity,
            Vector3 requestedForce,
            int requestedFreezeAxis,
            IReadOnlyCollection<Transform> exclusions,
            bool allowed)
        {
            springRoot = root ?? throw new ArgumentNullException(nameof(root));
            updateRate = Mathf.Max(0f, requestedUpdateRate);
            damping = Mathf.Clamp01(requestedDamping);
            elasticity = Mathf.Clamp01(requestedElasticity);
            stiffness = Mathf.Clamp01(requestedStiffness);
            inert = Mathf.Clamp01(requestedInert);
            gravity = requestedGravity;
            force = requestedForce;
            freezeAxis = Mathf.Clamp(requestedFreezeAxis, 0, 3);
            Allowed = allowed;

            var excluded = exclusions == null
                ? new HashSet<Transform>()
                : new HashSet<Transform>(exclusions);
            var values = new List<Particle>();
            AppendParticles(
                springRoot,
                -1,
                excluded,
                values,
                endLength,
                endOffset);
            particles = values.ToArray();
            configured = particles.Length > 1;
            ResetSimulation();
        }

        public void SetSimulationEnabled(bool value)
        {
            var shouldEnable = value && Allowed && configured;
            if (enabled == shouldEnable)
            {
                if (shouldEnable)
                {
                    ResetSimulation();
                }

                return;
            }

            enabled = shouldEnable;
        }

        private void OnEnable()
        {
            if (configured)
            {
                ResetSimulation();
            }
        }

        private void OnDisable()
        {
            if (configured)
            {
                RestoreTransforms();
                ResetSimulation();
            }
        }

        private void Update()
        {
            if (configured)
            {
                RestoreTransforms();
            }
        }

        private void LateUpdate()
        {
            if (!configured || springRoot == null)
            {
                return;
            }

            var ownerMove = transform.position - previousOwnerPosition;
            previousOwnerPosition = transform.position;
            var steps = CalculateStepCount(Time.deltaTime);
            if (steps == 0)
            {
                ShiftParticles(ownerMove);
                AnchorRoot();
                ConstrainParticles();
                ApplyParticles();
                return;
            }

            for (var step = 0; step < steps; step++)
            {
                SimulateParticles(step == 0 ? ownerMove : Vector3.zero);
                ConstrainParticles();
            }

            ApplyParticles();
        }

        private int CalculateStepCount(float deltaTime)
        {
            if (updateRate <= 0f)
            {
                return 1;
            }

            var interval = 1f / updateRate;
            accumulator += Mathf.Max(0f, deltaTime);
            var steps = 0;
            while (accumulator >= interval && steps < MaximumSteps)
            {
                accumulator -= interval;
                steps++;
            }

            if (steps == MaximumSteps && accumulator >= interval)
            {
                accumulator = 0f;
            }

            return steps;
        }

        private void SimulateParticles(Vector3 ownerMove)
        {
            AnchorRoot();
            var objectScale = Mathf.Abs(transform.lossyScale.x);
            var acceleration = (gravity + force) * objectScale;
            for (var index = 1; index < particles.Length; index++)
            {
                var particle = particles[index];
                var velocity = particle.Position - particle.PreviousPosition;
                var inheritedMove = ownerMove * inert;
                particle.PreviousPosition = particle.Position + inheritedMove;
                particle.Position += velocity * (1f - damping) +
                                     acceleration + inheritedMove;
            }
        }

        private void ConstrainParticles()
        {
            for (var index = 1; index < particles.Length; index++)
            {
                var particle = particles[index];
                var parent = particles[particle.ParentIndex];
                var localOffset = particle.LocalOffset;
                var desired = parent.Position +
                              parent.Transform.TransformVector(localOffset);
                particle.Position += (desired - particle.Position) * elasticity;

                var stiffnessLimit = particle.Length *
                                     (1f - stiffness) * 2f;
                var displacement = desired - particle.Position;
                if (stiffnessLimit <= 0f)
                {
                    particle.Position = desired;
                }
                else if (displacement.sqrMagnitude >
                         stiffnessLimit * stiffnessLimit)
                {
                    particle.Position += displacement.normalized *
                                         (displacement.magnitude - stiffnessLimit);
                }

                ApplyFreezeAxis(parent, particle);
                var direction = particle.Position - parent.Position;
                if (direction.sqrMagnitude > Epsilon)
                {
                    particle.Position = parent.Position +
                                        direction.normalized * particle.Length;
                }
            }
        }

        private void ApplyFreezeAxis(Particle parent, Particle particle)
        {
            Vector3 normal;
            switch (freezeAxis)
            {
                case 1:
                    normal = parent.Transform.right;
                    break;
                case 2:
                    normal = parent.Transform.up;
                    break;
                case 3:
                    normal = parent.Transform.forward;
                    break;
                default:
                    return;
            }

            particle.Position -= normal *
                                 Vector3.Dot(
                                     particle.Position - parent.Position,
                                     normal);
        }

        private void ApplyParticles()
        {
            for (var index = 1; index < particles.Length; index++)
            {
                var particle = particles[index];
                var parent = particles[particle.ParentIndex];
                if (parent.Transform.childCount <= 1)
                {
                    var currentDirection =
                        parent.Transform.TransformDirection(particle.LocalOffset);
                    var desiredDirection = particle.Position - parent.Position;
                    if (currentDirection.sqrMagnitude > Epsilon &&
                        desiredDirection.sqrMagnitude > Epsilon)
                    {
                        parent.Transform.rotation = Quaternion.FromToRotation(
                                                        currentDirection,
                                                        desiredDirection) *
                                                    parent.Transform.rotation;
                    }
                }

                if (particle.Transform != null)
                {
                    particle.Transform.position = particle.Position;
                }
            }
        }

        private void AnchorRoot()
        {
            var rootParticle = particles[0];
            rootParticle.PreviousPosition = rootParticle.Position;
            rootParticle.Position = springRoot.position;
        }

        private void ShiftParticles(Vector3 movement)
        {
            for (var index = 1; index < particles.Length; index++)
            {
                particles[index].Position += movement;
                particles[index].PreviousPosition += movement;
            }
        }

        private void RestoreTransforms()
        {
            for (var index = 0; index < particles.Length; index++)
            {
                var particle = particles[index];
                if (particle.Transform == null)
                {
                    continue;
                }

                particle.Transform.localPosition = particle.InitialLocalPosition;
                particle.Transform.localRotation = particle.InitialLocalRotation;
            }
        }

        private void ResetSimulation()
        {
            accumulator = 0f;
            previousOwnerPosition = transform.position;
            for (var index = 0; index < particles.Length; index++)
            {
                var particle = particles[index];
                particle.Position = GetCurrentPosition(particle);
                particle.PreviousPosition = particle.Position;
            }
        }

        private static Vector3 GetCurrentPosition(Particle particle)
        {
            if (particle.Transform != null)
            {
                return particle.Transform.position;
            }

            return particle.Parent.Transform.TransformPoint(
                particle.LocalOffset);
        }

        private static void AppendParticles(
            Transform current,
            int parentIndex,
            ISet<Transform> exclusions,
            List<Particle> values,
            float endLength,
            Vector3 endOffset)
        {
            if (current == null || exclusions.Contains(current))
            {
                return;
            }

            var index = values.Count;
            var particle = new Particle(
                current,
                parentIndex,
                current.localPosition,
                current.localRotation,
                Vector3.zero,
                parentIndex >= 0 ? values[parentIndex] : null);
            values.Add(particle);

            var includedChildren = 0;
            for (var childIndex = 0;
                 childIndex < current.childCount;
                 childIndex++)
            {
                var child = current.GetChild(childIndex);
                if (exclusions.Contains(child))
                {
                    continue;
                }

                AppendParticles(
                    child,
                    index,
                    exclusions,
                    values,
                    endLength,
                    endOffset);
                includedChildren++;
            }

            if (includedChildren != 0 ||
                (endLength <= 0f && endOffset == Vector3.zero))
            {
                return;
            }

            var localEnd = endOffset;
            if (localEnd == Vector3.zero)
            {
                var worldDirection = current.parent != null
                    ? current.position - current.parent.position
                    : current.right;
                localEnd = worldDirection.sqrMagnitude > Epsilon
                    ? current.InverseTransformDirection(worldDirection.normalized) *
                      endLength
                    : Vector3.right * endLength;
            }

            var virtualParticle = new Particle(
                null,
                index,
                Vector3.zero,
                Quaternion.identity,
                localEnd,
                particle);
            values.Add(virtualParticle);
        }

        private sealed class Particle
        {
            public Particle(
                Transform transform,
                int parentIndex,
                Vector3 initialLocalPosition,
                Quaternion initialLocalRotation,
                Vector3 virtualOffset,
                Particle parent)
            {
                Transform = transform;
                ParentIndex = parentIndex;
                InitialLocalPosition = initialLocalPosition;
                InitialLocalRotation = initialLocalRotation;
                VirtualOffset = virtualOffset;
                Parent = parent;
                Position = transform != null
                    ? transform.position
                    : parent.Transform.TransformPoint(virtualOffset);
                PreviousPosition = Position;
                Length = parentIndex < 0 || parent == null
                    ? 0f
                    : Vector3.Distance(parent.Position, Position);
            }

            public Transform Transform { get; }
            public int ParentIndex { get; }
            public Vector3 InitialLocalPosition { get; }
            public Quaternion InitialLocalRotation { get; }
            public Vector3 VirtualOffset { get; }
            public Particle Parent { get; }
            public Vector3 LocalOffset => Transform != null
                ? InitialLocalPosition
                : VirtualOffset;
            public float Length { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 PreviousPosition { get; set; }
        }
    }

    internal static class KoikatsuPhysicsRuntime
    {
        public static bool Supports(GameObject root)
        {
            if (root == null)
            {
                return false;
            }

            var springs = root.GetComponentsInChildren<KoikatsuSpringBone>(true);
            for (var index = 0; index < springs.Length; index++)
            {
                if (springs[index].Allowed)
                {
                    return true;
                }
            }

            var ver02 = root.GetComponentsInChildren<KoikatsuVer02SpringBone>(true);
            for (var index = 0; index < ver02.Length; index++)
            {
                if (ver02[index].Allowed)
                {
                    return true;
                }
            }

            return root.GetComponentInChildren<Cloth>(true) != null;
        }

        public static void SetEnabled(GameObject root, bool enabled)
        {
            if (root == null)
            {
                return;
            }

            var springs = root.GetComponentsInChildren<KoikatsuSpringBone>(true);
            for (var index = 0; index < springs.Length; index++)
            {
                springs[index].SetSimulationEnabled(enabled);
            }

            var ver02 = root.GetComponentsInChildren<KoikatsuVer02SpringBone>(true);
            for (var index = 0; index < ver02.Length; index++)
            {
                ver02[index].SetSimulationEnabled(enabled);
            }

            var cloth = root.GetComponentsInChildren<Cloth>(true);
            for (var index = 0; index < cloth.Length; index++)
            {
                cloth[index].enabled = enabled;
            }
        }

        public static void SetBustAllowed(GameObject root, bool allowed)
        {
            if (root == null)
            {
                return;
            }

            var springs = root.GetComponentsInChildren<KoikatsuVer02SpringBone>(
                true);
            for (var index = 0; index < springs.Length; index++)
            {
                if (springs[index].IsBust)
                {
                    springs[index].SetAllowed(allowed);
                }
            }
        }
    }
}
