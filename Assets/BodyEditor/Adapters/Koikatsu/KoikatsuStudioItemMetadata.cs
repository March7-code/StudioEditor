using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEngine;

namespace BodyEditor.ReferenceModels
{
    internal static class KoikatsuStudioFinalIkMetadataLoader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Definition[]> Cache =
            new Dictionary<string, Definition[]>(
                StringComparer.OrdinalIgnoreCase);

        public static int Attach(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                instance == null || !KoikatsuFinalIkRuntime.IsAvailable)
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
                if (TryAttach(definitions[index], transforms))
                {
                    attached++;
                }
            }

            return attached;
        }

        private static Definition[] GetDefinitions(
            KoikatsuBundleSource source,
            string assetName)
        {
            var file = new FileInfo(source.FilePath);
            var key = source.CacheKey + "|" + assetName + "|final-ik|" +
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

        private static Definition[] ParseSafely(
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
                    "Could not read Koikatsu Final IK metadata for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message);
                return Array.Empty<Definition>();
            }
        }

        private static Definition[] Parse(
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
                    return Array.Empty<Definition>();
                }

                var result = new List<Definition>();
                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var index = 0; index < behaviours.Count; index++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[index],
                        AssetReadFlags.None);
                    var references = behaviour["references"];
                    var solver = behaviour["solver"];
                    if (references.IsDummy || solver.IsDummy)
                    {
                        continue;
                    }

                    var host = GetOwnerPath(context, rootPathId, behaviour);
                    var definition = ReadDefinition(
                        context,
                        rootPathId,
                        host,
                        behaviour,
                        references,
                        solver);
                    if (definition != null)
                    {
                        result.Add(definition);
                    }
                }

                return result.ToArray();
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        private static Definition ReadDefinition(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            string hostPath,
            AssetTypeValueField behaviour,
            AssetTypeValueField references,
            AssetTypeValueField solver)
        {
            if (hostPath == null)
            {
                return null;
            }

            var root = GetTransformPath(
                context,
                rootPathId,
                references["root"]);
            var pelvis = GetTransformPath(
                context,
                rootPathId,
                references["pelvis"]);
            var leftThigh = GetTransformPath(
                context,
                rootPathId,
                references["leftThigh"]);
            var leftCalf = GetTransformPath(
                context,
                rootPathId,
                references["leftCalf"]);
            var leftFoot = GetTransformPath(
                context,
                rootPathId,
                references["leftFoot"]);
            var rightThigh = GetTransformPath(
                context,
                rootPathId,
                references["rightThigh"]);
            var rightCalf = GetTransformPath(
                context,
                rootPathId,
                references["rightCalf"]);
            var rightFoot = GetTransformPath(
                context,
                rootPathId,
                references["rightFoot"]);
            var leftUpperArm = GetTransformPath(
                context,
                rootPathId,
                references["leftUpperArm"]);
            var leftForearm = GetTransformPath(
                context,
                rootPathId,
                references["leftForearm"]);
            var leftHand = GetTransformPath(
                context,
                rootPathId,
                references["leftHand"]);
            var rightUpperArm = GetTransformPath(
                context,
                rootPathId,
                references["rightUpperArm"]);
            var rightForearm = GetTransformPath(
                context,
                rootPathId,
                references["rightForearm"]);
            var rightHand = GetTransformPath(
                context,
                rootPathId,
                references["rightHand"]);
            var spine = ReadTransformPaths(
                context,
                rootPathId,
                references["spine"]);
            var rootNode = GetTransformPath(
                context,
                rootPathId,
                solver["rootNode"]);
            if (root == null || pelvis == null || leftThigh == null ||
                leftCalf == null || leftFoot == null || rightThigh == null ||
                rightCalf == null || rightFoot == null ||
                leftUpperArm == null || leftForearm == null ||
                leftHand == null || rightUpperArm == null ||
                rightForearm == null || rightHand == null ||
                spine.Length == 0 || rootNode == null)
            {
                return null;
            }

            return new Definition(
                hostPath,
                ReadBool(behaviour["m_Enabled"], true),
                ReadBool(behaviour["fixTransforms"], true),
                root,
                pelvis,
                leftThigh,
                leftCalf,
                leftFoot,
                rightThigh,
                rightCalf,
                rightFoot,
                leftUpperArm,
                leftForearm,
                leftHand,
                rightUpperArm,
                rightForearm,
                rightHand,
                GetTransformPath(
                    context,
                    rootPathId,
                    references["head"]),
                spine,
                ReadTransformPaths(
                    context,
                    rootPathId,
                    references["eyes"]),
                rootNode,
                ReadFloat(solver["IKPositionWeight"], 1f),
                ReadInt(solver["iterations"], 4),
                ReadFloat(solver["spineStiffness"], 0.5f),
                ReadFloat(solver["pullBodyVertical"], 0.5f),
                ReadFloat(solver["pullBodyHorizontal"], 0f),
                ReadEffectors(context, rootPathId, solver["effectors"]),
                ReadChains(context, rootPathId, solver["chain"]),
                ReadFloat(solver["spineMapping"]["twistWeight"], 1f),
                ReadLimbMappings(solver["limbMappings"]));
        }

        private static bool TryAttach(
            Definition definition,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            if (!TryResolve(transforms, definition.HostPath, out var host) ||
                !TryResolve(transforms, definition.Root, out var root) ||
                !TryResolve(transforms, definition.Pelvis, out var pelvis) ||
                !TryResolve(
                    transforms,
                    definition.LeftThigh,
                    out var leftThigh) ||
                !TryResolve(transforms, definition.LeftCalf, out var leftCalf) ||
                !TryResolve(transforms, definition.LeftFoot, out var leftFoot) ||
                !TryResolve(
                    transforms,
                    definition.RightThigh,
                    out var rightThigh) ||
                !TryResolve(
                    transforms,
                    definition.RightCalf,
                    out var rightCalf) ||
                !TryResolve(
                    transforms,
                    definition.RightFoot,
                    out var rightFoot) ||
                !TryResolve(
                    transforms,
                    definition.LeftUpperArm,
                    out var leftUpperArm) ||
                !TryResolve(
                    transforms,
                    definition.LeftForearm,
                    out var leftForearm) ||
                !TryResolve(transforms, definition.LeftHand, out var leftHand) ||
                !TryResolve(
                    transforms,
                    definition.RightUpperArm,
                    out var rightUpperArm) ||
                !TryResolve(
                    transforms,
                    definition.RightForearm,
                    out var rightForearm) ||
                !TryResolve(
                    transforms,
                    definition.RightHand,
                    out var rightHand) ||
                !TryResolve(
                    transforms,
                    definition.RootNode,
                    out var rootNode) ||
                !TryResolveArray(
                    transforms,
                    definition.Spine,
                    out var spine))
            {
                return false;
            }

            TryResolve(transforms, definition.Head, out var head);
            TryResolveArray(transforms, definition.Eyes, out var eyes);
            var references = KoikatsuFinalIkRuntime.CreateReferences();
            SetMember(references, "root", root);
            SetMember(references, "pelvis", pelvis);
            SetMember(references, "leftThigh", leftThigh);
            SetMember(references, "leftCalf", leftCalf);
            SetMember(references, "leftFoot", leftFoot);
            SetMember(references, "rightThigh", rightThigh);
            SetMember(references, "rightCalf", rightCalf);
            SetMember(references, "rightFoot", rightFoot);
            SetMember(references, "leftUpperArm", leftUpperArm);
            SetMember(references, "leftForearm", leftForearm);
            SetMember(references, "leftHand", leftHand);
            SetMember(references, "rightUpperArm", rightUpperArm);
            SetMember(references, "rightForearm", rightForearm);
            SetMember(references, "rightHand", rightHand);
            SetMember(references, "head", head);
            SetMember(references, "spine", spine);
            SetMember(references, "eyes", eyes ?? Array.Empty<Transform>());

            if (!KoikatsuFinalIkRuntime.TryAdd(
                    host.gameObject,
                    out var component,
                    out _))
            {
                return false;
            }

            component.FixTransforms = definition.FixTransforms;
            component.SetReferences(references, rootNode);
            ApplySolver(definition, transforms, component.Solver);
            component.Enabled = definition.Enabled;
            return true;
        }

        private static void ApplySolver(
            Definition definition,
            IReadOnlyDictionary<string, Transform> transforms,
            object solver)
        {
            SetMember(solver, "IKPositionWeight", definition.IkPositionWeight);
            SetMember(solver, "iterations", definition.Iterations);
            SetMember(solver, "spineStiffness", definition.SpineStiffness);
            SetMember(
                solver,
                "pullBodyVertical",
                definition.PullBodyVertical);
            SetMember(
                solver,
                "pullBodyHorizontal",
                definition.PullBodyHorizontal);
            SetMember(
                KoikatsuFinalIkRuntime.GetMember(solver, "spineMapping"),
                "twistWeight",
                definition.SpineTwistWeight);

            var effectors = KoikatsuFinalIkRuntime.GetArray(
                solver,
                "effectors");

            for (var index = 0;
                 index < definition.Effectors.Length &&
                 index < effectors.Length;
                 index++)
            {
                var source = definition.Effectors[index];
                TryResolve(transforms, source.Target, out var target);
                var effector = effectors.GetValue(index);
                SetMember(effector, "target", target);
                SetMember(effector, "positionWeight", source.PositionWeight);
                SetMember(effector, "rotationWeight", source.RotationWeight);
                SetMember(
                    effector,
                    "maintainRelativePositionWeight",
                    source.MaintainRelativePositionWeight);
                SetMember(
                    effector,
                    "effectChildNodes",
                    source.EffectChildNodes);
            }

            var chains = KoikatsuFinalIkRuntime.GetArray(solver, "chain");

            for (var index = 0;
                 index < definition.Chains.Length && index < chains.Length;
                 index++)
            {
                var source = definition.Chains[index];
                var chain = chains.GetValue(index);
                SetMember(chain, "pin", source.Pin);
                SetMember(chain, "pull", source.Pull);
                SetMember(chain, "push", source.Push);
                SetMember(chain, "pushParent", source.PushParent);
                SetMember(chain, "reach", source.Reach);
                SetMember(
                    chain,
                    "reachSmoothing",
                    source.ReachSmoothing);
                SetMember(
                    chain,
                    "pushSmoothing",
                    source.PushSmoothing);
                var bendConstraint = KoikatsuFinalIkRuntime.GetMember(
                    chain,
                    "bendConstraint");
                if (bendConstraint != null && source.Bend != null)
                {
                    TryResolve(
                        transforms,
                        source.Bend.Target,
                        out var bendTarget);
                    SetMember(bendConstraint, "bendGoal", bendTarget);
                    SetMember(bendConstraint, "weight", source.Bend.Weight);
                }
            }

            var limbMappings = KoikatsuFinalIkRuntime.GetArray(
                solver,
                "limbMappings");

            for (var index = 0;
                 index < definition.LimbMappings.Length &&
                 index < limbMappings.Length;
                 index++)
            {
                var mapping = limbMappings.GetValue(index);
                SetMember(
                    mapping,
                    "weight",
                    definition.LimbMappings[index].Weight);
                SetMember(
                    mapping,
                    "maintainRotationWeight",
                    definition.LimbMappings[index].MaintainRotationWeight);
            }
        }

        private static void SetMember(
            object target,
            string name,
            object value)
        {
            KoikatsuFinalIkRuntime.SetMember(target, name, value);
        }

        private static EffectorDefinition[] ReadEffectors(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField field)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return Array.Empty<EffectorDefinition>();
            }

            var result = new EffectorDefinition[array.Children.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var effector = array.Children[index];
                result[index] = new EffectorDefinition(
                    GetTransformPath(
                        context,
                        rootPathId,
                        effector["target"]),
                    ReadFloat(effector["positionWeight"], 0f),
                    ReadFloat(effector["rotationWeight"], 0f),
                    ReadFloat(
                        effector["maintainRelativePositionWeight"],
                        0f),
                    ReadBool(effector["effectChildNodes"], true));
            }

            return result;
        }

        private static ChainDefinition[] ReadChains(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField field)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return Array.Empty<ChainDefinition>();
            }

            var result = new ChainDefinition[array.Children.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var chain = array.Children[index];
                var bend = chain["bendConstraint"];
                result[index] = new ChainDefinition(
                    ReadFloat(chain["pin"], 0f),
                    ReadFloat(chain["pull"], 1f),
                    ReadFloat(chain["push"], 0f),
                    ReadFloat(chain["pushParent"], 0f),
                    ReadFloat(chain["reach"], 0.1f),
                    ReadInt(chain["reachSmoothing"], 1),
                    ReadInt(chain["pushSmoothing"], 1),
                    bend.IsDummy
                        ? null
                        : new BendDefinition(
                            GetTransformPath(
                                context,
                                rootPathId,
                                bend["bendGoal"]),
                            ReadFloat(bend["weight"], 0f)));
            }

            return result;
        }

        private static LimbMappingDefinition[] ReadLimbMappings(
            AssetTypeValueField field)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return Array.Empty<LimbMappingDefinition>();
            }

            var result = new LimbMappingDefinition[array.Children.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var mapping = array.Children[index];
                result[index] = new LimbMappingDefinition(
                    ReadFloat(mapping["weight"], 1f),
                    ReadFloat(mapping["maintainRotationWeight"], 0f));
            }

            return result;
        }

        private static string GetOwnerPath(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField behaviour)
        {
            var owner = context.Resolve(behaviour["m_GameObject"]);
            if (owner.info == null)
            {
                return null;
            }

            var transform = KoikatsuClothesRendererMapLoader.FindTransform(
                context,
                owner.baseField);
            return transform.info == null
                ? null
                : KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                    context,
                    rootPathId,
                    transform);
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
            if (pointer == null || pointer.IsDummy)
            {
                return null;
            }

            var transform = context.Resolve(pointer);
            return transform.info == null ||
                   transform.info.TypeId != (int)AssetClassID.Transform
                ? null
                : KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                    context,
                    rootPathId,
                    transform);
        }

        private static bool TryResolve(
            IReadOnlyDictionary<string, Transform> transforms,
            string path,
            out Transform transform)
        {
            if (path != null && transforms.TryGetValue(path, out transform))
            {
                return true;
            }

            transform = null;
            return false;
        }

        private static bool TryResolveArray(
            IReadOnlyDictionary<string, Transform> transforms,
            IReadOnlyList<string> paths,
            out Transform[] result)
        {
            if (paths == null)
            {
                result = Array.Empty<Transform>();
                return true;
            }

            result = new Transform[paths.Count];
            for (var index = 0; index < paths.Count; index++)
            {
                if (!TryResolve(transforms, paths[index], out result[index]))
                {
                    result = null;
                    return false;
                }
            }

            return true;
        }

        private static bool ReadBool(AssetTypeValueField field, bool fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsBool;
        }

        private static int ReadInt(AssetTypeValueField field, int fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsInt;
        }

        private static float ReadFloat(
            AssetTypeValueField field,
            float fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsFloat;
        }

        private sealed class Definition
        {
            public Definition(
                string hostPath,
                bool enabled,
                bool fixTransforms,
                string root,
                string pelvis,
                string leftThigh,
                string leftCalf,
                string leftFoot,
                string rightThigh,
                string rightCalf,
                string rightFoot,
                string leftUpperArm,
                string leftForearm,
                string leftHand,
                string rightUpperArm,
                string rightForearm,
                string rightHand,
                string head,
                string[] spine,
                string[] eyes,
                string rootNode,
                float ikPositionWeight,
                int iterations,
                float spineStiffness,
                float pullBodyVertical,
                float pullBodyHorizontal,
                EffectorDefinition[] effectors,
                ChainDefinition[] chains,
                float spineTwistWeight,
                LimbMappingDefinition[] limbMappings)
            {
                HostPath = hostPath;
                Enabled = enabled;
                FixTransforms = fixTransforms;
                Root = root;
                Pelvis = pelvis;
                LeftThigh = leftThigh;
                LeftCalf = leftCalf;
                LeftFoot = leftFoot;
                RightThigh = rightThigh;
                RightCalf = rightCalf;
                RightFoot = rightFoot;
                LeftUpperArm = leftUpperArm;
                LeftForearm = leftForearm;
                LeftHand = leftHand;
                RightUpperArm = rightUpperArm;
                RightForearm = rightForearm;
                RightHand = rightHand;
                Head = head;
                Spine = spine;
                Eyes = eyes;
                RootNode = rootNode;
                IkPositionWeight = ikPositionWeight;
                Iterations = iterations;
                SpineStiffness = spineStiffness;
                PullBodyVertical = pullBodyVertical;
                PullBodyHorizontal = pullBodyHorizontal;
                Effectors = effectors;
                Chains = chains;
                SpineTwistWeight = spineTwistWeight;
                LimbMappings = limbMappings;
            }

            public string HostPath { get; }
            public bool Enabled { get; }
            public bool FixTransforms { get; }
            public string Root { get; }
            public string Pelvis { get; }
            public string LeftThigh { get; }
            public string LeftCalf { get; }
            public string LeftFoot { get; }
            public string RightThigh { get; }
            public string RightCalf { get; }
            public string RightFoot { get; }
            public string LeftUpperArm { get; }
            public string LeftForearm { get; }
            public string LeftHand { get; }
            public string RightUpperArm { get; }
            public string RightForearm { get; }
            public string RightHand { get; }
            public string Head { get; }
            public string[] Spine { get; }
            public string[] Eyes { get; }
            public string RootNode { get; }
            public float IkPositionWeight { get; }
            public int Iterations { get; }
            public float SpineStiffness { get; }
            public float PullBodyVertical { get; }
            public float PullBodyHorizontal { get; }
            public EffectorDefinition[] Effectors { get; }
            public ChainDefinition[] Chains { get; }
            public float SpineTwistWeight { get; }
            public LimbMappingDefinition[] LimbMappings { get; }
        }

        private sealed class EffectorDefinition
        {
            public EffectorDefinition(
                string target,
                float positionWeight,
                float rotationWeight,
                float maintainRelativePositionWeight,
                bool effectChildNodes)
            {
                Target = target;
                PositionWeight = positionWeight;
                RotationWeight = rotationWeight;
                MaintainRelativePositionWeight =
                    maintainRelativePositionWeight;
                EffectChildNodes = effectChildNodes;
            }

            public string Target { get; }
            public float PositionWeight { get; }
            public float RotationWeight { get; }
            public float MaintainRelativePositionWeight { get; }
            public bool EffectChildNodes { get; }
        }

        private sealed class ChainDefinition
        {
            public ChainDefinition(
                float pin,
                float pull,
                float push,
                float pushParent,
                float reach,
                int reachSmoothing,
                int pushSmoothing,
                BendDefinition bend)
            {
                Pin = pin;
                Pull = pull;
                Push = push;
                PushParent = pushParent;
                Reach = reach;
                ReachSmoothing = reachSmoothing;
                PushSmoothing = pushSmoothing;
                Bend = bend;
            }

            public float Pin { get; }
            public float Pull { get; }
            public float Push { get; }
            public float PushParent { get; }
            public float Reach { get; }
            public int ReachSmoothing { get; }
            public int PushSmoothing { get; }
            public BendDefinition Bend { get; }
        }

        private sealed class BendDefinition
        {
            public BendDefinition(string target, float weight)
            {
                Target = target;
                Weight = weight;
            }

            public string Target { get; }
            public float Weight { get; }
        }

        private sealed class LimbMappingDefinition
        {
            public LimbMappingDefinition(
                float weight,
                float maintainRotationWeight)
            {
                Weight = weight;
                MaintainRotationWeight = maintainRotationWeight;
            }

            public float Weight { get; }
            public float MaintainRotationWeight { get; }
        }
    }

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

    internal sealed class KoikatsuDynamicBoneColliderDefinition
    {
        public KoikatsuDynamicBoneColliderDefinition(
            string transformPath,
            Vector3 center,
            float radius,
            float height,
            int direction,
            int bound)
        {
            TransformPath = transformPath;
            Center = center;
            Radius = Mathf.Max(0f, radius);
            Height = Mathf.Max(0f, height);
            Direction = Mathf.Clamp(direction, 0, 2);
            Bound = Mathf.Clamp(bound, 0, 1);
        }

        public string TransformPath { get; }
        public Vector3 Center { get; }
        public float Radius { get; }
        public float Height { get; }
        public int Direction { get; }
        public int Bound { get; }
    }

    internal sealed class KoikatsuDynamicBoneCollider
    {
        private readonly Transform transform;
        private readonly Vector3 center;
        private readonly float radius;
        private readonly float height;
        private readonly int direction;
        private readonly int bound;

        public KoikatsuDynamicBoneCollider(
            Transform transform,
            KoikatsuDynamicBoneColliderDefinition definition)
        {
            this.transform = transform;
            center = definition.Center;
            radius = definition.Radius;
            height = definition.Height;
            direction = definition.Direction;
            bound = definition.Bound;
        }

        public void Collide(ref Vector3 particlePosition, float particleRadius)
        {
            if (transform == null || !transform.gameObject.activeInHierarchy)
            {
                return;
            }

            var scaledRadius = radius * Mathf.Abs(transform.lossyScale.z);
            var halfSegment = (height - radius) * 0.5f;
            if (halfSegment <= 0f)
            {
                if (bound == 0)
                {
                    OutsideSphere(
                        ref particlePosition,
                        particleRadius,
                        transform.TransformPoint(center),
                        scaledRadius);
                }
                else
                {
                    InsideSphere(
                        ref particlePosition,
                        particleRadius,
                        transform.TransformPoint(center),
                        scaledRadius);
                }

                return;
            }

            var first = center;
            var second = center;
            var offset = Axis(direction) * halfSegment;
            first -= offset;
            second += offset;
            var p0 = transform.TransformPoint(first);
            var p1 = transform.TransformPoint(second);
            if (bound == 0)
            {
                OutsideCapsule(
                    ref particlePosition,
                    particleRadius,
                    p0,
                    p1,
                    scaledRadius);
            }
            else
            {
                InsideCapsule(
                    ref particlePosition,
                    particleRadius,
                    p0,
                    p1,
                    scaledRadius);
            }
        }

        private static Vector3 Axis(int direction)
        {
            switch (direction)
            {
                case 1:
                    return Vector3.up;
                case 2:
                    return Vector3.forward;
                default:
                    return Vector3.right;
            }
        }

        private static void OutsideSphere(
            ref Vector3 position,
            float particleRadius,
            Vector3 sphereCenter,
            float sphereRadius)
        {
            var radius = sphereRadius + particleRadius;
            var offset = position - sphereCenter;
            var length = offset.magnitude;
            if (length > 0f && length < radius)
            {
                position = sphereCenter + offset * (radius / length);
            }
        }

        private static void InsideSphere(
            ref Vector3 position,
            float particleRadius,
            Vector3 sphereCenter,
            float sphereRadius)
        {
            var radius = sphereRadius + particleRadius;
            var offset = position - sphereCenter;
            var length = offset.magnitude;
            if (length > radius)
            {
                position = sphereCenter + offset * (radius / length);
            }
        }

        private static void OutsideCapsule(
            ref Vector3 position,
            float particleRadius,
            Vector3 p0,
            Vector3 p1,
            float capsuleRadius)
        {
            CollideCapsule(
                ref position,
                particleRadius,
                p0,
                p1,
                capsuleRadius,
                false);
        }

        private static void InsideCapsule(
            ref Vector3 position,
            float particleRadius,
            Vector3 p0,
            Vector3 p1,
            float capsuleRadius)
        {
            CollideCapsule(
                ref position,
                particleRadius,
                p0,
                p1,
                capsuleRadius,
                true);
        }

        private static void CollideCapsule(
            ref Vector3 position,
            float particleRadius,
            Vector3 p0,
            Vector3 p1,
            float capsuleRadius,
            bool inside)
        {
            var radius = capsuleRadius + particleRadius;
            var axis = p1 - p0;
            var offset = position - p0;
            var dot = Vector3.Dot(offset, axis);
            var axisLengthSquared = axis.sqrMagnitude;
            Vector3 closest;
            if (dot <= 0f || axisLengthSquared <= 0f)
            {
                closest = p0;
            }
            else if (dot >= axisLengthSquared)
            {
                closest = p1;
            }
            else
            {
                closest = p0 + axis * (dot / axisLengthSquared);
            }

            var fromSurface = position - closest;
            var length = fromSurface.magnitude;
            if (length <= 0f)
            {
                return;
            }

            if ((!inside && length < radius) || (inside && length > radius))
            {
                position = closest + fromSurface * (radius / length);
            }
        }
    }

    internal static class KoikatsuDynamicBoneMetadata
    {
        public static Dictionary<long, KoikatsuDynamicBoneColliderDefinition>
            ReadColliders(
                AssetsManager manager,
                AssetsFileInstance assets,
                KoikatsuClothesRendererMapLoader.ParseContext context,
                long rootPathId)
        {
            var result = new Dictionary<long, KoikatsuDynamicBoneColliderDefinition>();
            var behaviours = assets.file.GetAssetsOfType(
                AssetClassID.MonoBehaviour);
            for (var index = 0; index < behaviours.Count; index++)
            {
                var behaviour = manager.GetBaseField(
                    assets,
                    behaviours[index],
                    AssetReadFlags.None);
                var owner = context.Resolve(behaviour["m_GameObject"]);
                if (owner.info == null)
                {
                    continue;
                }

                var transform = KoikatsuClothesRendererMapLoader.FindTransform(
                    context,
                    owner.baseField);
                if (transform.info == null)
                {
                    continue;
                }

                var path = KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                    context,
                    rootPathId,
                    transform);
                if (path == null || behaviour["m_Center"].IsDummy ||
                    behaviour["m_Radius"].IsDummy ||
                    behaviour["m_Height"].IsDummy ||
                    behaviour["m_Direction"].IsDummy ||
                    behaviour["m_Bound"].IsDummy)
                {
                    continue;
                }

                result[behaviours[index].PathId] =
                    new KoikatsuDynamicBoneColliderDefinition(
                        path,
                        ReadVector3(behaviour["m_Center"]),
                        ReadFloat(behaviour["m_Radius"], 0.5f),
                        ReadFloat(behaviour["m_Height"], 0f),
                        ReadInt(behaviour["m_Direction"], 0),
                        ReadInt(behaviour["m_Bound"], 0));
            }

            return result;
        }

        public static KoikatsuDynamicBoneCollider[] Resolve(
            IReadOnlyList<KoikatsuDynamicBoneColliderDefinition> definitions,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            if (definitions == null || definitions.Count == 0)
            {
                return Array.Empty<KoikatsuDynamicBoneCollider>();
            }

            var result = new List<KoikatsuDynamicBoneCollider>();
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition != null && transforms.TryGetValue(
                        definition.TransformPath,
                        out var transform))
                {
                    result.Add(new KoikatsuDynamicBoneCollider(
                        transform,
                        definition));
                }
            }

            return result.ToArray();
        }

        public static IReadOnlyList<KoikatsuDynamicBoneColliderDefinition>
            ReadReferences(
                AssetTypeValueField field,
                IReadOnlyDictionary<long, KoikatsuDynamicBoneColliderDefinition>
                    colliders)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null || colliders == null)
            {
                return Array.Empty<KoikatsuDynamicBoneColliderDefinition>();
            }

            var result = new List<KoikatsuDynamicBoneColliderDefinition>();
            for (var index = 0; index < array.Children.Count; index++)
            {
                var pointer = array.Children[index];
                var pathId = GetPathId(pointer);
                if (colliders.TryGetValue(pathId, out var definition))
                {
                    result.Add(definition);
                }
            }

            return result.ToArray();
        }

        private static long GetPathId(AssetTypeValueField pointer)
        {
            if (pointer == null || pointer.IsDummy ||
                pointer["m_PathID"].IsDummy)
            {
                return 0;
            }

            return pointer["m_PathID"].AsLong;
        }

        private static float ReadFloat(AssetTypeValueField field, float fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsFloat;
        }

        private static int ReadInt(AssetTypeValueField field, int fallback)
        {
            return field == null || field.IsDummy ? fallback : field.AsInt;
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

                var colliders = KoikatsuDynamicBoneMetadata.Resolve(
                    definition.Colliders,
                    transforms);

                var spring = instance.AddComponent<KoikatsuSpringBone>();
                spring.enabled = false;
                spring.Configure(
                    root,
                    definition.UpdateRate,
                    definition.Damping,
                    definition.Elasticity,
                    definition.Stiffness,
                    definition.Inert,
                    definition.Radius,
                    definition.EndLength,
                    definition.EndOffset,
                    definition.Gravity,
                    definition.Force,
                    definition.FreezeAxis,
                    exclusions,
                    colliders,
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
                var colliderDefinitions =
                    KoikatsuDynamicBoneMetadata.ReadColliders(
                        manager,
                        assets,
                        context,
                        rootPathId);
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
                        ReadFloat(behaviour["m_Radius"], 0f),
                        ReadFloat(behaviour["m_EndLength"], 0f),
                        ReadVector3(behaviour["m_EndOffset"]),
                        ReadVector3(behaviour["m_Gravity"]),
                        ReadVector3(behaviour["m_Force"]),
                        ReadInt(behaviour["m_FreezeAxis"], 0),
                        ReadTransformPaths(
                            context,
                            rootPathId,
                            behaviour["m_Exclusions"]),
                        KoikatsuDynamicBoneMetadata.ReadReferences(
                            behaviour["m_Colliders"],
                            colliderDefinitions),
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
                float radius,
                float endLength,
                Vector3 endOffset,
                Vector3 gravity,
                Vector3 force,
                int freezeAxis,
                string[] exclusionPaths,
                IReadOnlyList<KoikatsuDynamicBoneColliderDefinition> colliders,
                bool enabled)
            {
                RootPath = rootPath;
                UpdateRate = updateRate;
                Damping = damping;
                Elasticity = elasticity;
                Stiffness = stiffness;
                Inert = inert;
                Radius = Mathf.Max(0f, radius);
                EndLength = endLength;
                EndOffset = endOffset;
                Gravity = gravity;
                Force = force;
                FreezeAxis = freezeAxis;
                ExclusionPaths = exclusionPaths ?? Array.Empty<string>();
                Colliders = colliders ??
                    Array.Empty<KoikatsuDynamicBoneColliderDefinition>();
                Enabled = enabled;
            }

            public string RootPath { get; }
            public float UpdateRate { get; }
            public float Damping { get; }
            public float Elasticity { get; }
            public float Stiffness { get; }
            public float Inert { get; }
            public float Radius { get; }
            public float EndLength { get; }
            public Vector3 EndOffset { get; }
            public Vector3 Gravity { get; }
            public Vector3 Force { get; }
            public int FreezeAxis { get; }
            public string[] ExclusionPaths { get; }
            public IReadOnlyList<KoikatsuDynamicBoneColliderDefinition> Colliders { get; }
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
                        // A self-reference feeds the previous physics result
                        // back in as the next frame's rest pose. Missing
                        // references must fall back to the captured authored
                        // transform instead.
                        references[boneIndex] = null;
                    }
                }

                if (!valid)
                {
                    continue;
                }

                var colliders = KoikatsuDynamicBoneMetadata.Resolve(
                    definition.Colliders,
                    transforms);

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
                    colliders,
                    allowed && definition.Enabled);
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
                var colliderDefinitions =
                    KoikatsuDynamicBoneMetadata.ReadColliders(
                        manager,
                        assets,
                        context,
                        rootPathId);
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
                        0f,
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
                        KoikatsuDynamicBoneMetadata.ReadReferences(
                            behaviour["Colliders"],
                            colliderDefinitions),
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
                ReadFloat(field["CollisionRadius"], 0f),
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
                IReadOnlyList<KoikatsuDynamicBoneColliderDefinition> colliders,
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
                Colliders = colliders ??
                    Array.Empty<KoikatsuDynamicBoneColliderDefinition>();
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
            public IReadOnlyList<KoikatsuDynamicBoneColliderDefinition> Colliders { get; }
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
            float collisionRadius,
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
            CollisionRadius = Mathf.Max(0f, collisionRadius);
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
        public float CollisionRadius { get; }
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

    [DefaultExecutionOrder(32010)]
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
        private float maximumAcceleration;
        private float accumulator;
        private Vector3 previousRootPosition;
        private bool configured;
        private IReadOnlyList<KoikatsuDynamicBoneCollider> colliders =
            Array.Empty<KoikatsuDynamicBoneCollider>();

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
            IReadOnlyList<KoikatsuDynamicBoneCollider> requestedColliders,
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
            reflectSpeed = Mathf.Clamp01(requestedReflectSpeed);
            maximumSteps = Mathf.Clamp(requestedMaximumSteps, 1, 4);
            gravity = requestedGravity;
            force = requestedForce;
            colliders = requestedColliders ??
                Array.Empty<KoikatsuDynamicBoneCollider>();
            Allowed = allowed;

            var values = new List<Particle>(bones.Count + 1);
            for (var index = 0; index < bones.Count; index++)
            {
                var parent = index > 0 ? values[index - 1] : null;
                values.Add(new Particle(
                    bones[index],
                    references[index],
                    definitions[index],
                    parent,
                    parent == null
                        ? Vector3.zero
                        : parent.Transform.InverseTransformPoint(
                            bones[index].position)));
            }

            values.Add(new Particle(
                null,
                null,
                endDefinition,
                values[values.Count - 1],
                endDefinition.EndOffset));
            particles = values.ToArray();
            maximumAcceleration = CalculateMaximumAcceleration(particles);
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
            var acceleration = Vector3.ClampMagnitude(
                (gravity + force) * objectScale,
                maximumAcceleration * objectScale);
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
                ApplyColliders(particle);
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

        private void ApplyColliders(Particle particle)
        {
            if (particle == null || colliders.Count == 0)
            {
                return;
            }

            var particleRadius = particle.Definition.CollisionRadius *
                                 Mathf.Abs(motionRoot.lossyScale.x);
            var position = particle.Position;
            for (var index = 0; index < colliders.Count; index++)
            {
                colliders[index].Collide(
                    ref position,
                    particleRadius);
            }
            var correction = position - particle.Position;
            particle.Position = position;
            particle.PreviousPosition += correction;
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

            particle.Transform.localScale = particle.InitialLocalScale +
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
                if (particle.Reference != null)
                {
                    particle.Transform.localPosition =
                        particle.Reference.localPosition;
                    particle.Transform.localRotation =
                        particle.Reference.localRotation;
                    particle.Transform.localScale =
                        particle.Reference.localScale;
                }
                else
                {
                    particle.Transform.localPosition =
                        particle.InitialLocalPosition;
                    particle.Transform.localRotation =
                        particle.InitialLocalRotation;
                    particle.Transform.localScale =
                        particle.InitialLocalScale;
                }
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

        private static float CalculateMaximumAcceleration(Particle[] values)
        {
            var maximumLength = 0f;
            for (var index = 1; index < values.Length; index++)
            {
                maximumLength = Mathf.Max(
                    maximumLength,
                    Vector3.Distance(
                        values[index - 1].Position,
                        values[index].Position));
            }

            return Mathf.Max(0.001f, maximumLength * 0.2f);
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
                InitialLocalPosition = transform != null
                    ? transform.localPosition
                    : Vector3.zero;
                InitialLocalRotation = transform != null
                    ? transform.localRotation
                    : Quaternion.identity;
                InitialLocalScale = transform != null
                    ? transform.localScale
                    : Vector3.one;
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
            public Vector3 InitialLocalPosition { get; }
            public Quaternion InitialLocalRotation { get; }
            public Vector3 InitialLocalScale { get; }
            public Vector3 Position { get; set; }
            public Vector3 PreviousPosition { get; set; }
        }
    }

    [DefaultExecutionOrder(32000)]
    internal sealed class KoikatsuSpringBone : MonoBehaviour
    {
        private const float Epsilon = 0.000001f;
        private const int MaximumSteps = 3;
        private const int ConstraintIterations = 2;

        private Transform springRoot;
        private Particle[] particles = Array.Empty<Particle>();
        private float updateRate;
        private float damping;
        private float elasticity;
        private float stiffness;
        private float inert;
        private float radius;
        private Vector3 gravity;
        private Vector3 force;
        private float maximumAcceleration;
        private int freezeAxis;
        private float accumulator;
        private Vector3 previousRootPosition;
        private bool configured;
        private IReadOnlyList<KoikatsuDynamicBoneCollider> colliders =
            Array.Empty<KoikatsuDynamicBoneCollider>();

        public bool Allowed { get; private set; }

        public void Configure(
            Transform root,
            float requestedUpdateRate,
            float requestedDamping,
            float requestedElasticity,
            float requestedStiffness,
            float requestedInert,
            float requestedRadius,
            float endLength,
            Vector3 endOffset,
            Vector3 requestedGravity,
            Vector3 requestedForce,
            int requestedFreezeAxis,
            IReadOnlyCollection<Transform> exclusions,
            IReadOnlyList<KoikatsuDynamicBoneCollider> requestedColliders,
            bool allowed)
        {
            springRoot = root ?? throw new ArgumentNullException(nameof(root));
            updateRate = Mathf.Max(0f, requestedUpdateRate);
            damping = Mathf.Clamp01(requestedDamping);
            elasticity = Mathf.Clamp01(requestedElasticity);
            stiffness = Mathf.Clamp01(requestedStiffness);
            inert = Mathf.Clamp01(requestedInert);
            radius = Mathf.Max(0f, requestedRadius);
            gravity = requestedGravity;
            force = requestedForce;
            freezeAxis = Mathf.Clamp(requestedFreezeAxis, 0, 3);
            colliders = requestedColliders ??
                Array.Empty<KoikatsuDynamicBoneCollider>();
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
            var maximumDepth = 1;
            for (var index = 0; index < particles.Length; index++)
            {
                maximumDepth = Mathf.Max(maximumDepth, particles[index].Depth);
            }
            for (var index = 0; index < particles.Length; index++)
            {
                particles[index].Radius = radius;
                particles[index].DepthRatio = Mathf.Clamp01(
                    (float)particles[index].Depth / maximumDepth);
            }
            maximumAcceleration = CalculateMaximumAcceleration(particles);
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

            var ownerMove = springRoot.position - previousRootPosition;
            previousRootPosition = springRoot.position;
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
            var acceleration = Vector3.ClampMagnitude(
                (gravity + force) * objectScale,
                maximumAcceleration * objectScale);
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
            for (var iteration = 0;
                 iteration < ConstraintIterations;
                 iteration++)
            {
                for (var index = 1; index < particles.Length; index++)
                {
                    var particle = particles[index];
                    var parent = particles[particle.ParentIndex];
                    var desired = parent.Position +
                                  parent.Transform.TransformVector(
                                      particle.LocalOffset);
                    var retention = Mathf.Lerp(
                        0.96f,
                        0.48f,
                        particle.DepthRatio);
                    retention *= Mathf.Lerp(0.85f, 1f, stiffness);
                    retention *= Mathf.Lerp(0.75f, 1f, elasticity);
                    retention = 1f - Mathf.Pow(
                        1f - Mathf.Clamp01(retention),
                        1f / ConstraintIterations);
                    particle.Position = Vector3.Lerp(
                        particle.Position,
                        desired,
                        retention);
                    ApplyRestCone(particle, parent, desired);
                    ApplyFreezeAxis(parent, particle);
                    EnforceLength(particle, parent);
                    ApplyColliders(particle);
                }
            }
        }

        private static void EnforceLength(Particle particle, Particle parent)
        {
            var direction = particle.Position - parent.Position;
            if (direction.sqrMagnitude > Epsilon)
            {
                particle.Position = parent.Position +
                                    direction.normalized * particle.Length;
            }
        }

        private static void ApplyRestCone(
            Particle particle,
            Particle parent,
            Vector3 desired)
        {
            var rest = desired - parent.Position;
            var current = particle.Position - parent.Position;
            if (rest.sqrMagnitude <= Epsilon || current.sqrMagnitude <= Epsilon)
            {
                return;
            }

            var maximumAngle = Mathf.Lerp(22f, 78f, particle.DepthRatio) *
                               Mathf.Deg2Rad;
            var angle = Vector3.Angle(rest, current) * Mathf.Deg2Rad;
            if (angle <= maximumAngle)
            {
                return;
            }

            current = Vector3.RotateTowards(
                rest,
                current,
                angle - maximumAngle,
                0f);
            particle.Position = parent.Position + current.normalized *
                                 particle.Length;
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

        private void ApplyColliders(Particle particle)
        {
            if (particle == null || colliders.Count == 0)
            {
                return;
            }

            var particleRadius = particle.Radius *
                                 Mathf.Abs(transform.lossyScale.x);
            var position = particle.Position;
            for (var index = 0; index < colliders.Count; index++)
            {
                colliders[index].Collide(
                    ref position,
                    particleRadius);
            }
            var correction = position - particle.Position;
            particle.Position = position;
            particle.PreviousPosition += correction;
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
            previousRootPosition = springRoot != null
                ? springRoot.position
                : transform.position;
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

        private static float CalculateMaximumAcceleration(Particle[] values)
        {
            var maximumLength = 0f;
            for (var index = 1; index < values.Length; index++)
            {
                maximumLength = Mathf.Max(
                    maximumLength,
                    Vector3.Distance(
                        values[index].Position,
                        values[index].Parent.Position));
            }

            return Mathf.Max(0.001f, maximumLength * 0.2f);
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
                Depth = parent == null ? 0 : parent.Depth + 1;
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
            public int Depth { get; }
            public float DepthRatio { get; set; }
            public Vector3 LocalOffset => Transform != null
                ? InitialLocalPosition
                : VirtualOffset;
            public float Length { get; set; }
            public float Radius { get; set; }
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
                // A legacy prefab can carry both a Cloth component and bone
                // spring metadata. Let one solver own that branch; running
                // both against the same skinned mesh causes unstable double
                // deformation.
                cloth[index].enabled = enabled &&
                                       !HasBoneSolverInBranch(
                                           cloth[index].transform,
                                           root.transform);
            }

            if (enabled)
            {
                var itemPoses = root.GetComponentsInChildren<
                    KoikatsuStudioItemPose>(true);
                for (var index = 0; index < itemPoses.Length; index++)
                {
                    itemPoses[index].SuppressFkPhysics();
                }
            }
        }

        private static bool HasBoneSolverInBranch(
            Transform start,
            Transform root)
        {
            var current = start;
            while (current != null)
            {
                if (current.GetComponent<KoikatsuSpringBone>() != null ||
                    current.GetComponent<KoikatsuVer02SpringBone>() != null)
                {
                    return true;
                }

                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            return false;
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
