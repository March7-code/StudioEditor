using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BodyEditor.Editor
{
    [InitializeOnLoad]
    public static class DefaultHumanoidSkeletonBuilder
    {
        public const string PrefabPath = "Assets/BodyEditor/Templates/DefaultHumanoidSkeleton.prefab";

        static DefaultHumanoidSkeletonBuilder()
        {
            EditorApplication.delayCall += EnsureDefaultTemplateDelayed;
        }

        private static void EnsureDefaultTemplateDelayed()
        {
            var prefab = EnsureDefaultTemplate();
            if (TryValidateTemplate(prefab, out var message))
            {
                Debug.Log(message, prefab);
            }
            else
            {
                Debug.LogError(message, prefab);
            }
        }

        public static GameObject EnsureDefaultTemplate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab != null ? prefab : BuildDefaultTemplate();
        }

        public static GameObject BuildDefaultTemplate()
        {
            EnsureFolder("Assets/BodyEditor");
            EnsureFolder("Assets/BodyEditor/Templates");

            var root = HumanoidSkeletonFactory.CreateDefault().gameObject;

            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (!TryValidateTemplate(prefab, out var message))
                {
                    Debug.LogError(message, prefab);
                }

                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Tools/Body Editor/Rebuild Default Humanoid Skeleton")]
        private static void RebuildDefaultTemplateMenu()
        {
            Selection.activeObject = BuildDefaultTemplate();
        }

        [MenuItem("GameObject/Body Editor/Default Humanoid Skeleton", false, 10)]
        private static void CreateDefaultSkeletonInstance(MenuCommand command)
        {
            var prefab = EnsureDefaultTemplate();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(instance, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(instance, "Create Default Humanoid Skeleton");
            Selection.activeObject = instance;
        }

        public static bool TryValidateTemplate(GameObject prefab, out string message)
        {
            if (prefab == null)
            {
                message = "The default humanoid skeleton prefab does not exist.";
                return false;
            }

            var skeleton = prefab.GetComponent<HumanoidSkeleton>();
            if (skeleton == null)
            {
                message = "The default humanoid skeleton prefab has no HumanoidSkeleton component.";
                return false;
            }

            var errors = new List<string>();
            if (!skeleton.Validate(errors))
            {
                message = string.Join("\n", errors);
                return false;
            }

            message = $"Body Editor default skeleton is ready with {skeleton.BoneCount} bones.";
            return true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var separator = path.LastIndexOf('/');
            var parent = path.Substring(0, separator);
            var folderName = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
