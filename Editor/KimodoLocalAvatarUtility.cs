using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoLocalAvatarUtility
    {
        private const string AvatarCacheFolder = "Assets/KimodoGenerated/Avatars";

        public static bool TryEnsureHumanoidAvatar(
            Animator animator,
            out Avatar avatar,
            out string source,
            out string error)
        {
            avatar = null;
            source = string.Empty;
            error = string.Empty;

            if (animator == null)
            {
                error = "Animator is null.";
                return false;
            }

            GameObject avatarRoot = animator.avatarRoot != null ? animator.avatarRoot.gameObject : animator.gameObject;
            if (avatarRoot == null)
            {
                error = "Avatar root is null.";
                return false;
            }

            if (IsValidHumanoid(animator.avatar) && CheckAvatarValid(animator.avatar, avatarRoot))
            {
                avatar = animator.avatar;
                source = "Animator";
                return true;
            }

            if (TryGetImporterAvatar(avatarRoot, out Avatar importerAvatar) &&
                IsValidHumanoid(importerAvatar) &&
                CheckAvatarValid(importerAvatar, avatarRoot))
            {
                avatar = importerAvatar;
                source = "Importer";
                return true;
            }

            EnsureFolderExists(AvatarCacheFolder);
            string cachePath = BuildAvatarCachePath(avatarRoot);
            if (File.Exists(cachePath))
            {
                Avatar cached = AssetDatabase.LoadAssetAtPath<Avatar>(cachePath);
                if (IsValidHumanoid(cached) && CheckAvatarValid(cached, avatarRoot))
                {
                    avatar = cached;
                    source = "Cache";
                    return true;
                }
            }

            Avatar generated = GenerateHumanoidAvatar(avatarRoot, out string generateError);
            if (!IsValidHumanoid(generated) || !CheckAvatarValid(generated, avatarRoot))
            {
                error = string.IsNullOrWhiteSpace(generateError)
                    ? "Generated avatar is invalid."
                    : generateError;
                return false;
            }

            try
            {
                if (File.Exists(cachePath))
                {
                    AssetDatabase.DeleteAsset(cachePath);
                }

                AssetDatabase.CreateAsset(generated, cachePath);
                AssetDatabase.SaveAssets();
                Avatar saved = AssetDatabase.LoadAssetAtPath<Avatar>(cachePath);
                if (IsValidHumanoid(saved))
                {
                    avatar = saved;
                    source = "GeneratedCache";
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Kimodo][Avatar] Save generated avatar failed: {e.Message}");
            }

            avatar = generated;
            source = "GeneratedTemp";
            return true;
        }

        public static bool CheckAvatarValid(Avatar avatar, GameObject gameObject)
        {
            if (!IsValidHumanoid(avatar) || gameObject == null)
            {
                return false;
            }

            var allBones = gameObject.GetComponentsInChildren<Transform>(true).ToArray();
            HumanBone[] humanBones = avatar.humanDescription.human;
            for (int i = 0; i < humanBones.Length; i++)
            {
                string boneName = humanBones[i].boneName;
                bool found = false;
                for (int j = 0; j < allBones.Length; j++)
                {
                    if (string.Equals(allBones[j].name, boneName, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidHumanoid(Avatar avatar)
        {
            return avatar != null && avatar.isValid && avatar.isHuman;
        }

        private static bool TryGetImporterAvatar(GameObject gameObject, out Avatar avatar)
        {
            avatar = null;
            if (gameObject == null)
            {
                return false;
            }

            ModelImporter importer = GetModelImporter(gameObject, out string modelPath);
            if (importer == null || string.IsNullOrWhiteSpace(modelPath))
            {
                return false;
            }

            avatar = AssetDatabase.LoadAssetAtPath<Avatar>(modelPath);
            return avatar != null;
        }

        private static ModelImporter GetModelImporter(GameObject gameObject, out string modelImporterPath)
        {
            modelImporterPath = string.Empty;
            if (gameObject == null)
            {
                return null;
            }

            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return null;
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                return null;
            }

            PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
            if (prefabAssetType == PrefabAssetType.Variant)
            {
                GameObject parentVariant = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
                if (parentVariant == null)
                {
                    return null;
                }

                string parentPath = AssetDatabase.GetAssetPath(parentVariant);
                modelImporterPath = parentPath;
                return AssetImporter.GetAtPath(parentPath) as ModelImporter;
            }

            modelImporterPath = prefabPath;
            return AssetImporter.GetAtPath(prefabPath) as ModelImporter;
        }

        private static Avatar GenerateHumanoidAvatar(GameObject gameObject, out string error)
        {
            error = string.Empty;
            if (gameObject == null)
            {
                error = "Avatar root object is null.";
                return null;
            }

            Vector3 oldPos = gameObject.transform.position;
            Quaternion oldRot = gameObject.transform.rotation;
            GameObject editableGameObject = null;

            try
            {
                GameObject rootObject = gameObject;
                gameObject.transform.position = Vector3.zero;
                gameObject.transform.rotation = Quaternion.identity;

                if (gameObject.TryGetComponent(out Animator animator) && animator.avatarRoot != null)
                {
                    rootObject = animator.avatarRoot.gameObject;
                }

                editableGameObject = UnityEngine.Object.Instantiate(rootObject);
                editableGameObject.name = rootObject.name;
                editableGameObject.hideFlags = HideFlags.HideAndDontSave;

                TryForceTPoseReflective(editableGameObject);
                Dictionary<int, Transform> mapping = TryMappingHumanoidLikeReflective(editableGameObject.transform);
                if (mapping == null || mapping.Count == 0)
                {
                    mapping = BuildFallbackBoneMapping(editableGameObject.transform);
                }

                if (mapping == null || mapping.Count == 0)
                {
                    error = $"Failed to create humanoid mapping for {rootObject.name}.";
                    return null;
                }

                HumanBone[] humanBones = mapping
                    .Select(pair => CreateHumanBone(pair.Key, pair.Value))
                    .Where(h => h.boneName != null)
                    .ToArray();

                if (humanBones.Length == 0)
                {
                    error = "No valid human bones mapped.";
                    return null;
                }

                Transform[] allBones = editableGameObject.GetComponentsInChildren<Transform>(true);
                SkeletonBone[] skeletonBones = allBones.Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                }).ToArray();

                var humanDescription = new HumanDescription
                {
                    upperArmTwist = 1f,
                    lowerArmTwist = 0f,
                    upperLegTwist = 1f,
                    lowerLegTwist = 0f,
                    armStretch = 0f,
                    legStretch = 0f,
                    feetSpacing = 0f,
                    hasTranslationDoF = false,
                    human = humanBones,
                    skeleton = skeletonBones
                };

                Avatar generated = AvatarBuilder.BuildHumanAvatar(editableGameObject, humanDescription);
                if (generated == null || !generated.isValid || !generated.isHuman)
                {
                    error = "AvatarBuilder.BuildHumanAvatar returned invalid avatar.";
                    return null;
                }

                generated.name = $"{rootObject.name}_Humanoid";
                return generated;
            }
            catch (Exception e)
            {
                error = $"GenerateHumanoidAvatar failed: {e.Message}";
                return null;
            }
            finally
            {
                if (editableGameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(editableGameObject);
                }

                gameObject.transform.position = oldPos;
                gameObject.transform.rotation = oldRot;
            }
        }

        private static HumanBone CreateHumanBone(int humanBoneId, Transform bone)
        {
            if (bone == null || humanBoneId < 0 || humanBoneId >= HumanTrait.BoneCount)
            {
                return default;
            }

            var hb = new HumanBone
            {
                boneName = bone.name,
                humanName = HumanTrait.BoneName[humanBoneId]
            };
            hb.limit.useDefaultValues = true;
            return hb;
        }

        private static void TryForceTPoseReflective(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                Type setupTool = GetAvatarSetupToolType();
                if (setupTool == null)
                {
                    return;
                }

                MethodInfo forceTPose = setupTool.GetMethod(
                    "ForceTPose",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(GameObject), typeof(Transform) },
                    null);

                forceTPose?.Invoke(null, new object[] { root, root.transform });
            }
            catch
            {
                // Fallback path will still work for many rigs.
            }
        }

        private static Dictionary<int, Transform> TryMappingHumanoidLikeReflective(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            try
            {
                Type setupTool = GetAvatarSetupToolType();
                if (setupTool == null)
                {
                    return null;
                }

                MethodInfo mappingMethod = setupTool.GetMethod(
                    "MappingHumanoidLike",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(Transform) },
                    null);

                if (mappingMethod == null)
                {
                    return null;
                }

                object result = mappingMethod.Invoke(null, new object[] { root });
                if (result is IDictionary dictionary)
                {
                    var output = new Dictionary<int, Transform>();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Key is int key && entry.Value is Transform value && !output.ContainsKey(key))
                        {
                            output[key] = value;
                        }
                    }

                    return output;
                }
            }
            catch
            {
                // Fallback mapping path.
            }

            return null;
        }

        private static Type GetAvatarSetupToolType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType("UnityEditor.AvatarSetupTool");
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static Dictionary<int, Transform> BuildFallbackBoneMapping(Transform root)
        {
            var output = new Dictionary<int, Transform>();
            Dictionary<string, Transform> nameLookup = BuildNameLookup(root);

            AddBone(output, HumanBodyBones.Hips, nameLookup, "Hips", "Pelvis", "hip", "hips", "pelvis");
            AddBone(output, HumanBodyBones.Spine, nameLookup, "Spine", "Spine1", "spine", "spine1");
            AddBone(output, HumanBodyBones.Chest, nameLookup, "Chest", "Spine2", "Spine3", "chest", "spine2", "spine3");
            AddBone(output, HumanBodyBones.UpperChest, nameLookup, "UpperChest", "upperchest");
            AddBone(output, HumanBodyBones.Neck, nameLookup, "Neck", "Neck1", "neck", "neck1");
            AddBone(output, HumanBodyBones.Head, nameLookup, "Head", "head");

            AddBone(output, HumanBodyBones.LeftShoulder, nameLookup, "LeftShoulder", "L_Clavicle", "Shoulder_L", "leftshoulder", "l_clavicle");
            AddBone(output, HumanBodyBones.LeftUpperArm, nameLookup, "LeftArm", "LeftUpperArm", "L_Shoulder", "upperarm_l", "leftarm", "l_shoulder");
            AddBone(output, HumanBodyBones.LeftLowerArm, nameLookup, "LeftForeArm", "LeftLowerArm", "L_Elbow", "lowerarm_l", "leftforearm", "l_elbow");
            AddBone(output, HumanBodyBones.LeftHand, nameLookup, "LeftHand", "L_Hand", "hand_l", "lefthand", "l_hand");

            AddBone(output, HumanBodyBones.RightShoulder, nameLookup, "RightShoulder", "R_Clavicle", "Shoulder_R", "rightshoulder", "r_clavicle");
            AddBone(output, HumanBodyBones.RightUpperArm, nameLookup, "RightArm", "RightUpperArm", "R_Shoulder", "upperarm_r", "rightarm", "r_shoulder");
            AddBone(output, HumanBodyBones.RightLowerArm, nameLookup, "RightForeArm", "RightLowerArm", "R_Elbow", "lowerarm_r", "rightforearm", "r_elbow");
            AddBone(output, HumanBodyBones.RightHand, nameLookup, "RightHand", "R_Hand", "hand_r", "righthand", "r_hand");

            AddBone(output, HumanBodyBones.LeftUpperLeg, nameLookup, "LeftUpLeg", "LeftLeg", "L_Hip", "thigh_l", "leftupleg", "leftleg", "l_hip");
            AddBone(output, HumanBodyBones.LeftLowerLeg, nameLookup, "LeftLeg", "LeftShin", "L_Knee", "calf_l", "leftshin", "l_knee");
            AddBone(output, HumanBodyBones.LeftFoot, nameLookup, "LeftFoot", "L_Foot", "foot_l", "leftfoot", "l_foot");
            AddBone(output, HumanBodyBones.LeftToes, nameLookup, "LeftToeBase", "L_Toes", "toe_l", "lefttoebase", "l_toes");

            AddBone(output, HumanBodyBones.RightUpperLeg, nameLookup, "RightUpLeg", "RightLeg", "R_Hip", "thigh_r", "rightupleg", "rightleg", "r_hip");
            AddBone(output, HumanBodyBones.RightLowerLeg, nameLookup, "RightLeg", "RightShin", "R_Knee", "calf_r", "rightshin", "r_knee");
            AddBone(output, HumanBodyBones.RightFoot, nameLookup, "RightFoot", "R_Foot", "foot_r", "rightfoot", "r_foot");
            AddBone(output, HumanBodyBones.RightToes, nameLookup, "RightToeBase", "R_Toes", "toe_r", "righttoebase", "r_toes");

            return output;
        }

        private static Dictionary<string, Transform> BuildNameLookup(Transform root)
        {
            var dict = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            if (root == null)
            {
                return dict;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (!dict.ContainsKey(t.name))
                {
                    dict[t.name] = t;
                }
            }

            return dict;
        }

        private static void AddBone(
            Dictionary<int, Transform> output,
            HumanBodyBones humanBodyBone,
            Dictionary<string, Transform> nameLookup,
            params string[] candidates)
        {
            int id = (int)humanBodyBone;
            if (id < 0 || id >= HumanTrait.BoneCount || output.ContainsKey(id))
            {
                return;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (nameLookup.TryGetValue(candidate, out Transform t) && t != null)
                {
                    output[id] = t;
                    return;
                }
            }
        }

        private static string BuildAvatarCachePath(GameObject avatarRoot)
        {
            string safeName = SanitizeName(avatarRoot != null ? avatarRoot.name : "Avatar");
            int hash = ComputeHierarchyHash(avatarRoot != null ? avatarRoot.transform : null);
            return $"{AvatarCacheFolder}/{safeName}_{hash:X8}.asset";
        }

        private static int ComputeHierarchyHash(Transform root)
        {
            unchecked
            {
                int hash = 5381;
                if (root == null)
                {
                    return hash;
                }

                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string path = AnimationUtility.CalculateTransformPath(all[i], root);
                    string name = $"{all[i].name}|{path}";
                    for (int j = 0; j < name.Length; j++)
                    {
                        hash = ((hash << 5) + hash) ^ name[j];
                    }
                }

                return hash;
            }
        }

        private static string SanitizeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "Avatar";
            }

            char[] chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
