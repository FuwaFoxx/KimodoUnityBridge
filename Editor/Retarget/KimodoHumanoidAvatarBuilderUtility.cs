using System;
using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoHumanoidAvatarBuilderUtility
    {
        internal static bool TryLoadImporterAvatar(GameObject gameObject, out Avatar avatar, out string modelImporterPath)
        {
            avatar = null;
            modelImporterPath = string.Empty;
            if (!TryGetModelImporter(gameObject, out ModelImporter importer, out modelImporterPath))
            {
                return false;
            }

            avatar = AssetDatabase.LoadAssetAtPath<Avatar>(modelImporterPath);
            return avatar != null;
        }

        internal static Avatar GenerateHumanoidAvatar(
            GameObject sourceRoot,
            bool includeExtendedNameAliases,
            bool normalizeSourceTransformBeforeClone,
            bool forceUnitScaleOnClone,
            string avatarNameSuffix,
            out string error)
        {
            error = string.Empty;
            if (sourceRoot == null)
            {
                error = "Avatar root object is null.";
                return null;
            }

            _ = includeExtendedNameAliases;

            _ = normalizeSourceTransformBeforeClone;
            _ = forceUnitScaleOnClone;
            _ = avatarNameSuffix;

            try
            {
                GameObject rootObject = sourceRoot;
                if (sourceRoot.TryGetComponent(out Animator animator) && animator.avatarRoot != null)
                {
                    rootObject = animator.avatarRoot.gameObject;
                }

                return AvatarSetupToolExtension.AutoGenerateHumanoidAvatarFromModelOrThrow(rootObject, forceReimport: true);
            }
            catch (Exception e)
            {
                error = $"GenerateHumanoidAvatar failed: {e.Message}";
                return null;
            }
        }

        private static bool TryGetModelImporter(GameObject gameObject, out ModelImporter importer, out string modelImporterPath)
        {
            importer = null;
            modelImporterPath = string.Empty;
            if (gameObject == null)
            {
                return false;
            }

            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return false;
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return false;
            }

            PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
            if (prefabAssetType == PrefabAssetType.Variant)
            {
                GameObject parentVariant = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
                if (parentVariant == null)
                {
                    return false;
                }

                string parentPath = AssetDatabase.GetAssetPath(parentVariant);
                modelImporterPath = parentPath;
                importer = AssetImporter.GetAtPath(parentPath) as ModelImporter;
                return importer != null;
            }

            modelImporterPath = prefabPath;
            importer = AssetImporter.GetAtPath(prefabPath) as ModelImporter;
            return importer != null;
        }
    }
}
