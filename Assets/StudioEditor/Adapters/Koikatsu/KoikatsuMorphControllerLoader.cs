using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using StudioEditor.Characters;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuMorphControllerLoader
    {
        public static CharacterEyebrowController AttachEyebrow(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                instance == null)
            {
                return null;
            }

            try
            {
                var definition = ParseFaceMorph(
                    source,
                    assetName,
                    "EyebrowCtrl");
                if (definition == null)
                {
                    return null;
                }

                var targets = BindMorphTargets(
                    instance,
                    definition.Targets);
                if (targets.Count == 0)
                {
                    return null;
                }

                var controller =
                    instance.AddComponent<CharacterEyebrowController>();
                controller.Configure(
                    targets,
                    null,
                    definition.OpenMin,
                    definition.OpenMax);
                return controller;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not bind Koikatsu eyebrow morphs for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message,
                    instance);
                return null;
            }
        }

        public static CharacterMouthController AttachMouth(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                instance == null)
            {
                return null;
            }

            try
            {
                var definition = ParseFaceMorph(
                    source,
                    assetName,
                    "MouthCtrl");
                if (definition == null)
                {
                    return null;
                }

                var targets = BindMorphTargets(
                    instance,
                    definition.Targets);
                if (targets.Count == 0)
                {
                    return null;
                }

                var controller = instance.AddComponent<CharacterMouthController>();
                controller.Configure(
                    targets,
                    null,
                    definition.OpenMin,
                    definition.OpenMax);
                return controller;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not bind Koikatsu mouth morphs for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message,
                    instance);
                return null;
            }
        }

        public static CharacterEyeOpenController AttachEyeOpen(
            KoikatsuBundleSource source,
            string assetName,
            GameObject instance)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                instance == null)
            {
                return null;
            }

            try
            {
                var definition = ParseFaceMorph(
                    source,
                    assetName,
                    "EyesCtrl");
                if (definition == null)
                {
                    return null;
                }

                var targets = BindMorphTargets(
                    instance,
                    definition.Targets);
                if (targets.Count == 0)
                {
                    return null;
                }

                var controller =
                    instance.AddComponent<CharacterEyeOpenController>();
                controller.Configure(
                    targets,
                    null,
                    definition.OpenMin,
                    Mathf.Min(definition.OpenMax, 0.92f));
                return controller;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not bind Koikatsu eye morphs for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message,
                    instance);
                return null;
            }
        }

        private static IReadOnlyList<CharacterMorphTarget> BindMorphTargets(
            GameObject instance,
            IReadOnlyList<FaceMorphTargetDefinition> definitions)
        {
            var targets = new List<CharacterMorphTarget>();
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var target = FindTransform(
                    instance.transform,
                    definition.TransformPath);
                var renderer = target != null
                    ? target.GetComponent<SkinnedMeshRenderer>()
                    : null;
                if (renderer != null && renderer.sharedMesh != null)
                {
                    targets.Add(new CharacterMorphTarget(
                        renderer,
                        definition.Patterns));
                }
            }

            return targets.AsReadOnly();
        }

        public static CharacterHandPoseController CreateHands(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            GameObject characterRoot,
            CharacterSkeleton skeleton,
            CharacterPoseCoordinator coordinator)
        {
            if (string.IsNullOrWhiteSpace(abdataRoot) || catalog == null ||
                characterRoot == null || skeleton == null || coordinator == null)
            {
                return null;
            }

            try
            {
                var poses = ReadShapeHandPoses(
                    abdataRoot,
                    catalog,
                    skeleton);
                if (poses[0].Count == 0 && poses[1].Count == 0)
                {
                    Debug.LogWarning(
                        "Could not bind Koikatsu hand shapes: " +
                        "cf_anmShapeHand contains no usable finger poses.",
                        characterRoot);
                    return null;
                }

                var controller =
                    characterRoot.AddComponent<CharacterHandPoseController>();
                controller.Configure(coordinator, poses[0], poses[1]);
                return controller;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not bind Koikatsu hand shapes: " +
                    exception.Message,
                    characterRoot);
                return null;
            }
        }

        public static CharacterEyeLookController AttachEyes(
            KoikatsuBundleSource source,
            string assetName,
            GameObject characterRoot,
            CharacterSkeleton skeleton,
            CharacterPoseCoordinator coordinator)
        {
            if (source == null || string.IsNullOrWhiteSpace(assetName) ||
                characterRoot == null || skeleton == null || coordinator == null)
            {
                return null;
            }

            try
            {
                var definition = ParseEyes(source, assetName);
                if (definition == null)
                {
                    return null;
                }

                var reference = FindSkeletonTransform(
                    skeleton,
                    definition.ReferenceName);
                var leftEye = FindSkeletonTransform(
                    skeleton,
                    definition.LeftEyeName);
                var rightEye = FindSkeletonTransform(
                    skeleton,
                    definition.RightEyeName);
                if (reference == null || leftEye == null || rightEye == null)
                {
                    return null;
                }

                var pupils = FindPupilMaterials(characterRoot);
                if (pupils.Count == 0)
                {
                    Debug.LogWarning(
                        $"Koikatsu pupil materials were not found for " +
                        $"'{assetName}'.",
                        characterRoot);
                    return null;
                }

                var controller =
                    characterRoot.AddComponent<CharacterEyeLookController>();
                controller.Configure(
                    coordinator,
                    reference,
                    leftEye,
                    rightEye,
                    definition.ReferenceForward,
                    definition.ReferenceUp);
                controller.ConfigureLimits(
                    definition.LeftMinHorizontal,
                    definition.LeftMaxHorizontal,
                    definition.RightMinHorizontal,
                    definition.RightMaxHorizontal,
                    definition.UpAngleLimit,
                    definition.DownAngleLimit,
                    definition.BendingThreshold,
                    definition.MaxAngleDifference,
                    definition.BendingMultiplier,
                    definition.Response);
                controller.ConfigurePupils(pupils);
                return controller;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not bind Koikatsu eye look for prefab " +
                    $"'{assetName}' in '{source.DisplayName}': " +
                    exception.Message,
                    characterRoot);
                return null;
            }
        }

        private static IReadOnlyList<CharacterHandPose>[] ReadShapeHandPoses(
            string abdataRoot,
            KoikatsuListCatalog catalog,
            CharacterSkeleton skeleton)
        {
            var source = new KoikatsuBundleSource(
                KoikatsuAssetPath.ResolveAbdataPath(
                    abdataRoot,
                    "chara/oo_hand.unity3d"));
            using (var lease = KoikatsuAssetBundleCache.Acquire(source))
            {
                var data = lease.Bundle.LoadAsset<TextAsset>(
                    "cf_anmShapeHand");
                if (data == null || data.bytes == null || data.bytes.Length == 0)
                {
                    throw new InvalidDataException(
                        "TextAsset 'cf_anmShapeHand' was not found in " +
                        "chara/oo_hand.unity3d.");
                }

                var poseBones = new[]
                {
                    new List<List<CharacterHandBonePose>>(),
                    new List<List<CharacterHandBonePose>>(),
                };
                using (var stream = new MemoryStream(data.bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    var boneCount = reader.ReadInt32();
                    if (boneCount < 0 || boneCount > 256)
                    {
                        throw new InvalidDataException(
                            $"Invalid Koikatsu hand bone count: {boneCount}.");
                    }

                    for (var bone = 0; bone < boneCount; bone++)
                    {
                        var boneName = reader.ReadString();
                        var keyCount = reader.ReadInt32();
                        if (keyCount < 0 || keyCount > 1024)
                        {
                            throw new InvalidDataException(
                                $"Invalid Koikatsu hand key count for " +
                                $"'{boneName}': {keyCount}.");
                        }

                        var hand = GetHandIndex(boneName);
                        var boneIndex = FindSkeletonBoneIndex(
                            skeleton,
                            boneName);
                        EnsurePoseSlots(poseBones, hand, keyCount);
                        for (var key = 0; key < keyCount; key++)
                        {
                            reader.ReadInt32();
                            reader.ReadSingle();
                            reader.ReadSingle();
                            reader.ReadSingle();
                            var rotation = new Vector3(
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle());
                            reader.ReadSingle();
                            reader.ReadSingle();
                            reader.ReadSingle();
                            if (hand >= 0 && boneIndex >= 0 &&
                                float.IsFinite(rotation.x) &&
                                float.IsFinite(rotation.y) &&
                                float.IsFinite(rotation.z))
                            {
                                poseBones[hand][key].Add(
                                    new CharacterHandBonePose(
                                        boneIndex,
                                        Quaternion.Euler(rotation)));
                            }
                        }
                    }
                }

                var result = new IReadOnlyList<CharacterHandPose>[2];
                for (var hand = 0; hand < result.Length; hand++)
                {
                    if (!HasPoseBones(poseBones[hand]))
                    {
                        result[hand] = Array.Empty<CharacterHandPose>();
                        continue;
                    }

                    var entries = catalog.GetHandPoses(hand);
                    var poses = new List<CharacterHandPose>(
                        poseBones[hand].Count);
                    for (var pose = 0;
                         pose < poseBones[hand].Count;
                         pose++)
                    {
                        poses.Add(new CharacterHandPose(
                            GetHandPoseName(entries, pose),
                            poseBones[hand][pose]));
                    }

                    result[hand] = poses.AsReadOnly();
                }

                return result;
            }
        }

        private static void EnsurePoseSlots(
            IReadOnlyList<List<List<CharacterHandBonePose>>> poseBones,
            int hand,
            int count)
        {
            if (hand < 0 || hand >= poseBones.Count)
            {
                return;
            }

            while (poseBones[hand].Count < count)
            {
                poseBones[hand].Add(new List<CharacterHandBonePose>());
            }
        }

        private static bool HasPoseBones(
            IReadOnlyList<List<CharacterHandBonePose>> poses)
        {
            for (var index = 0; index < poses.Count; index++)
            {
                if (poses[index].Count != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetHandIndex(string boneName)
        {
            if (boneName != null &&
                boneName.EndsWith("_L", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return boneName != null &&
                   boneName.EndsWith(
                       "_R",
                       StringComparison.OrdinalIgnoreCase)
                ? 1
                : -1;
        }

        private static int FindSkeletonBoneIndex(
            CharacterSkeleton skeleton,
            string boneName)
        {
            for (var index = 0; index < skeleton.BoneCount; index++)
            {
                var transform = skeleton.Bones[index].Transform;
                if (transform != null && string.Equals(
                        transform.name,
                        boneName,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetHandPoseName(
            IReadOnlyList<KoikatsuHandPoseEntry> entries,
            int pose)
        {
            if (entries != null)
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    if (entry.Id != pose)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Name))
                    {
                        return entry.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.ClipName))
                    {
                        return entry.ClipName;
                    }

                    break;
                }
            }

            return $"Shape {pose + 1}";
        }

        private static Transform FindSkeletonTransform(
            CharacterSkeleton skeleton,
            string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            Transform fallback = null;
            for (var index = 0; index < skeleton.BoneCount; index++)
            {
                var transform = skeleton.Bones[index].Transform;
                if (transform == null)
                {
                    continue;
                }

                if (string.Equals(
                        transform.name,
                        name,
                        StringComparison.Ordinal))
                {
                    return transform;
                }

                if (fallback == null && string.Equals(
                        transform.name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    fallback = transform;
                }
            }

            return fallback;
        }

        private static IReadOnlyList<CharacterPupilMaterialTarget>
            FindPupilMaterials(GameObject characterRoot)
        {
            var result = new List<CharacterPupilMaterialTarget>();
            var mapped = new HashSet<Material>();
            var renderers = characterRoot.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (!TryGetPupilEye(renderer.name, out var eye))
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null || !mapped.Add(material))
                    {
                        continue;
                    }

                    var property = material.HasProperty("_BaseMap")
                        ? "_BaseMap"
                        : material.HasProperty("_MainTex")
                            ? "_MainTex"
                            : null;
                    if (property != null)
                    {
                        result.Add(new CharacterPupilMaterialTarget(
                            material,
                            property,
                            eye));
                    }
                }
            }

            return result.AsReadOnly();
        }

        private static bool TryGetPupilEye(
            string rendererName,
            out CharacterEye eye)
        {
            var key = (rendererName ?? string.Empty).ToLowerInvariant();
            if (key == "cf_ohitomi_l02" || key.Contains("hitomi_l02"))
            {
                eye = CharacterEye.Left;
                return true;
            }

            if (key == "cf_ohitomi_r02" || key.Contains("hitomi_r02"))
            {
                eye = CharacterEye.Right;
                return true;
            }

            eye = default(CharacterEye);
            return false;
        }

        private static EyeLookDefinition ParseEyes(
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

                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var index = 0; index < behaviours.Count; index++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[index],
                        AssetReadFlags.None);
                    var eyeObjects =
                        KoikatsuClothesRendererMapLoader.GetArray(
                            behaviour["eyeObjs"]);
                    if (eyeObjects == null || eyeObjects.Children.Count != 2 ||
                        !IsBehaviourUnderRoot(
                            context,
                            rootPathId,
                            behaviour))
                    {
                        continue;
                    }

                    var referenceName = ReadTransformName(
                        context,
                        rootPathId,
                        behaviour["rootNode"]);
                    if (string.IsNullOrEmpty(referenceName))
                    {
                        referenceName = ReadBehaviourTransformName(
                            context,
                            rootPathId,
                            behaviour);
                    }

                    var eyeNames = new string[2];
                    for (var eyeIndex = 0;
                         eyeIndex < eyeObjects.Children.Count;
                         eyeIndex++)
                    {
                        var eye = eyeObjects.Children[eyeIndex];
                        var side = ReadInt(eye["eyeLR"], -1);
                        if (side >= 0 && side < eyeNames.Length)
                        {
                            eyeNames[side] = ReadTransformName(
                                context,
                                rootPathId,
                                eye["eyeTransform"]);
                        }
                    }

                    var forward = ReadVector3(behaviour["headLookVector"]);
                    var up = ReadVector3(behaviour["headUpVector"]);
                    var settings = ReadTargetEyeLookSettings(
                        behaviour["eyeTypeStates"]);
                    if (!string.IsNullOrEmpty(referenceName) &&
                        !string.IsNullOrEmpty(eyeNames[0]) &&
                        !string.IsNullOrEmpty(eyeNames[1]) &&
                        forward.sqrMagnitude > 0.000001f &&
                        up.sqrMagnitude > 0.000001f)
                    {
                        return new EyeLookDefinition(
                            referenceName,
                            eyeNames[0],
                            eyeNames[1],
                            forward,
                            up,
                            settings);
                    }
                }

                return null;
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        private static EyeLookSettingsDefinition ReadTargetEyeLookSettings(
            AssetTypeValueField field)
        {
            var states = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (states != null)
            {
                for (var index = 0; index < states.Children.Count; index++)
                {
                    var state = states.Children[index];
                    if (ReadInt(state["lookType"], -1) != 1)
                    {
                        continue;
                    }

                    var minimum = ReadFloat(
                        state["minBendingAngle"],
                        -18f);
                    var maximum = ReadFloat(
                        state["maxBendingAngle"],
                        18f);
                    return new EyeLookSettingsDefinition(
                        minimum,
                        maximum,
                        -maximum,
                        -minimum,
                        Mathf.Abs(ReadFloat(
                            state["upBendingAngle"],
                            -12f)),
                        Mathf.Abs(ReadFloat(
                            state["downBendingAngle"],
                            12f)),
                        ReadFloat(
                            state["thresholdAngleDifference"],
                            0f),
                        ReadFloat(
                            state["maxAngleDifference"],
                            0f),
                        ReadFloat(
                            state["bendingMultiplier"],
                            1f),
                        ReadFloat(state["leapSpeed"], 12f));
                }
            }

            return EyeLookSettingsDefinition.Default;
        }

        private static bool IsBehaviourUnderRoot(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField behaviour)
        {
            return ReadBehaviourTransformName(
                context,
                rootPathId,
                behaviour) != null;
        }

        private static string ReadBehaviourTransformName(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField behaviour)
        {
            var owner = context.Resolve(behaviour["m_GameObject"]);
            var transform = owner.info != null
                ? KoikatsuClothesRendererMapLoader.FindTransform(
                    context,
                    owner.baseField)
                : default(AssetExternal);
            return transform.info != null &&
                   KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                       context,
                       rootPathId,
                       transform) != null
                ? owner.baseField["m_Name"].AsString
                : null;
        }

        private static string ReadTransformName(
            KoikatsuClothesRendererMapLoader.ParseContext context,
            long rootPathId,
            AssetTypeValueField pointer)
        {
            var transform = context.Resolve(pointer);
            if (transform.info == null ||
                transform.info.TypeId != (int)AssetClassID.Transform ||
                KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                    context,
                    rootPathId,
                    transform) == null)
            {
                return null;
            }

            var owner = context.Resolve(transform.baseField["m_GameObject"]);
            return owner.info != null
                ? owner.baseField["m_Name"].AsString
                : null;
        }

        private static Vector3 ReadVector3(AssetTypeValueField field)
        {
            if (field == null || field.IsDummy)
            {
                return Vector3.zero;
            }

            return new Vector3(
                ReadFloat(field["x"]),
                ReadFloat(field["y"]),
                ReadFloat(field["z"]));
        }

        private static float ReadFloat(AssetTypeValueField field)
        {
            return field == null || field.IsDummy ? 0f : field.AsFloat;
        }

        private static FaceMorphDefinition ParseFaceMorph(
            KoikatsuBundleSource source,
            string assetName,
            string controllerField)
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

                var behaviours = assets.file.GetAssetsOfType(
                    AssetClassID.MonoBehaviour);
                for (var behaviourIndex = 0;
                     behaviourIndex < behaviours.Count;
                     behaviourIndex++)
                {
                    var behaviour = manager.GetBaseField(
                        assets,
                        behaviours[behaviourIndex],
                        AssetReadFlags.None);
                    if (!IsBehaviourUnderRoot(
                            context,
                            rootPathId,
                            behaviour))
                    {
                        continue;
                    }

                    var controller = behaviour[controllerField];
                    if (controller == null || controller.IsDummy)
                    {
                        continue;
                    }

                    var targets = KoikatsuClothesRendererMapLoader.GetArray(
                        controller["FBSTarget"]);
                    if (targets == null)
                    {
                        continue;
                    }

                    var result = new List<FaceMorphTargetDefinition>();
                    for (var targetIndex = 0;
                         targetIndex < targets.Children.Count;
                         targetIndex++)
                    {
                        var target = targets.Children[targetIndex];
                        var objectPointer = target["ObjTarget"];
                        if (objectPointer == null || objectPointer.IsDummy)
                        {
                            continue;
                        }

                        var renderer = context.Resolve(objectPointer);
                        if (renderer.info == null ||
                            renderer.info.TypeId !=
                            (int)AssetClassID.GameObject)
                        {
                            continue;
                        }

                        var transform =
                            KoikatsuClothesRendererMapLoader.FindTransform(
                                context,
                                renderer.baseField);
                        var path = transform.info != null
                            ? KoikatsuClothesRendererMapLoader.CreateSerializedPath(
                                context,
                                rootPathId,
                                transform)
                            : null;
                        var patterns = ReadPatterns(target["PtnSet"]);
                        if (path != null && patterns.Count != 0)
                        {
                            result.Add(new FaceMorphTargetDefinition(
                                path,
                                patterns));
                        }
                    }

                    if (result.Count != 0)
                    {
                        return new FaceMorphDefinition(
                            result.AsReadOnly(),
                            ReadFloat(controller["OpenMin"], 0f),
                            ReadFloat(controller["OpenMax"], 1f));
                    }
                }

                return null;
            }
            finally
            {
                manager.UnloadAll(true);
                ownedStream?.Dispose();
            }
        }

        private static IReadOnlyList<CharacterMorphPair> ReadPatterns(
            AssetTypeValueField field)
        {
            var array = KoikatsuClothesRendererMapLoader.GetArray(field);
            if (array == null)
            {
                return Array.Empty<CharacterMorphPair>();
            }

            var result = new CharacterMorphPair[array.Children.Count];
            for (var index = 0; index < result.Length; index++)
            {
                var value = array.Children[index];
                result[index] = new CharacterMorphPair(
                    ReadInt(value["Close"], -1),
                    ReadInt(value["Open"], -1));
            }

            return Array.AsReadOnly(result);
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

        private static Transform FindTransform(Transform root, string path)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (string.Equals(
                        KoikatsuClothesRendererMapLoader.CreateRuntimePath(
                            root,
                            transforms[index]),
                        path,
                        StringComparison.Ordinal))
                {
                    return transforms[index];
                }
            }

            return null;
        }

        private sealed class FaceMorphDefinition
        {
            public FaceMorphDefinition(
                IReadOnlyList<FaceMorphTargetDefinition> targets,
                float openMin,
                float openMax)
            {
                Targets = targets;
                OpenMin = openMin;
                OpenMax = openMax;
            }

            public IReadOnlyList<FaceMorphTargetDefinition> Targets { get; }

            public float OpenMin { get; }

            public float OpenMax { get; }
        }

        private sealed class FaceMorphTargetDefinition
        {
            public FaceMorphTargetDefinition(
                string transformPath,
                IReadOnlyList<CharacterMorphPair> patterns)
            {
                TransformPath = transformPath;
                Patterns = patterns;
            }

            public string TransformPath { get; }

            public IReadOnlyList<CharacterMorphPair> Patterns { get; }
        }

        private sealed class EyeLookDefinition
        {
            public EyeLookDefinition(
                string referenceName,
                string leftEyeName,
                string rightEyeName,
                Vector3 referenceForward,
                Vector3 referenceUp,
                EyeLookSettingsDefinition settings)
            {
                ReferenceName = referenceName;
                LeftEyeName = leftEyeName;
                RightEyeName = rightEyeName;
                ReferenceForward = referenceForward;
                ReferenceUp = referenceUp;
                LeftMinHorizontal = settings.LeftMinHorizontal;
                LeftMaxHorizontal = settings.LeftMaxHorizontal;
                RightMinHorizontal = settings.RightMinHorizontal;
                RightMaxHorizontal = settings.RightMaxHorizontal;
                UpAngleLimit = settings.UpAngleLimit;
                DownAngleLimit = settings.DownAngleLimit;
                BendingThreshold = settings.BendingThreshold;
                MaxAngleDifference = settings.MaxAngleDifference;
                BendingMultiplier = settings.BendingMultiplier;
                Response = settings.Response;
            }

            public string ReferenceName { get; }

            public string LeftEyeName { get; }

            public string RightEyeName { get; }

            public Vector3 ReferenceForward { get; }

            public Vector3 ReferenceUp { get; }

            public float LeftMinHorizontal { get; }

            public float LeftMaxHorizontal { get; }

            public float RightMinHorizontal { get; }

            public float RightMaxHorizontal { get; }

            public float UpAngleLimit { get; }

            public float DownAngleLimit { get; }

            public float BendingThreshold { get; }

            public float MaxAngleDifference { get; }

            public float BendingMultiplier { get; }

            public float Response { get; }
        }

        private readonly struct EyeLookSettingsDefinition
        {
            public EyeLookSettingsDefinition(
                float leftMinHorizontal,
                float leftMaxHorizontal,
                float rightMinHorizontal,
                float rightMaxHorizontal,
                float upAngleLimit,
                float downAngleLimit,
                float bendingThreshold,
                float maxAngleDifference,
                float bendingMultiplier,
                float response)
            {
                LeftMinHorizontal = leftMinHorizontal;
                LeftMaxHorizontal = leftMaxHorizontal;
                RightMinHorizontal = rightMinHorizontal;
                RightMaxHorizontal = rightMaxHorizontal;
                UpAngleLimit = upAngleLimit;
                DownAngleLimit = downAngleLimit;
                BendingThreshold = bendingThreshold;
                MaxAngleDifference = maxAngleDifference;
                BendingMultiplier = bendingMultiplier;
                Response = response;
            }

            public static EyeLookSettingsDefinition Default =>
                new EyeLookSettingsDefinition(
                    -18f,
                    18f,
                    -18f,
                    18f,
                    12f,
                    12f,
                    0f,
                    0f,
                    1f,
                    12f);

            public float LeftMinHorizontal { get; }
            public float LeftMaxHorizontal { get; }
            public float RightMinHorizontal { get; }
            public float RightMaxHorizontal { get; }
            public float UpAngleLimit { get; }
            public float DownAngleLimit { get; }
            public float BendingThreshold { get; }
            public float MaxAngleDifference { get; }
            public float BendingMultiplier { get; }
            public float Response { get; }
        }

    }
}
