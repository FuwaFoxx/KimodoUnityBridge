using System;
using UnityEditor;
using UnityEngine;
using TimelineInject;

namespace KimodoBridge.Editor
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
                return TryGetModelImporterFromFirstMeshAsset(gameObject, out importer, out modelImporterPath);
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return TryGetModelImporterFromFirstMeshAsset(gameObject, out importer, out modelImporterPath);
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
            if (importer != null)
            {
                return true;
            }

            return TryGetModelImporterFromFirstMeshAsset(gameObject, out importer, out modelImporterPath);
        }

        private static bool TryGetModelImporterFromFirstMeshAsset(GameObject gameObject, out ModelImporter importer, out string modelImporterPath)
        {
            importer = null;
            modelImporterPath = string.Empty;
            if (gameObject == null)
            {
                return false;
            }

            if (!TryGetFirstMeshCarrier(gameObject, out Transform meshCarrier))
            {
                return false;
            }

            GameObject current = meshCarrier.gameObject;
            while (current != null)
            {
                string candidatePath = AssetDatabase.GetAssetPath(current);
                if (!string.IsNullOrEmpty(candidatePath))
                {
                    importer = AssetImporter.GetAtPath(candidatePath) as ModelImporter;
                    if (importer != null)
                    {
                        modelImporterPath = candidatePath;
                        return true;
                    }
                }

                current = current.transform.parent != null ? current.transform.parent.gameObject : null;
            }

            if (TryGetMeshAssetPath(meshCarrier.gameObject, out string meshAssetPath))
            {
                importer = AssetImporter.GetAtPath(meshAssetPath) as ModelImporter;
                if (importer != null)
                {
                    modelImporterPath = meshAssetPath;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetFirstMeshCarrier(GameObject root, out Transform carrier)
        {
            carrier = null;
            if (root == null)
            {
                return false;
            }

            SkinnedMeshRenderer[] skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i] != null && skins[i].sharedMesh != null)
                {
                    carrier = skins[i].transform;
                    return true;
                }
            }

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] != null && filters[i].sharedMesh != null)
                {
                    carrier = filters[i].transform;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetMeshAssetPath(GameObject meshCarrier, out string meshAssetPath)
        {
            meshAssetPath = string.Empty;
            if (meshCarrier == null)
            {
                return false;
            }

            SkinnedMeshRenderer skin = meshCarrier.GetComponent<SkinnedMeshRenderer>();
            if (skin != null && skin.sharedMesh != null)
            {
                string path = AssetDatabase.GetAssetPath(skin.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                {
                    meshAssetPath = path;
                    return true;
                }
            }

            MeshFilter filter = meshCarrier.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                {
                    meshAssetPath = path;
                    return true;
                }
            }

            return false;
        }
    }
}
