using System;
using System.Collections.Generic;
using TimelineInject;
using UnityEngine;

namespace KimodoBridge
{
    public static class KimodoRetargetAvatarUtility
    {
        internal static bool TryCreateTemporaryHumanoidRoot(
            Avatar avatar,
            string rootName,
            bool animatorEnabled,
            bool applyRootMotion,
            out GameObject root,
            out Animator animator,
            out string error)
        {
            root = null;
            animator = null;
            error = string.Empty;

            if (!KimodoRetargetCoreUtility.IsValidHumanoid(avatar))
            {
                error = "Avatar is null/invalid/non-humanoid.";
                return false;
            }

            root = new GameObject(string.IsNullOrWhiteSpace(rootName) ? "KimodoTemporaryHumanoidRoot" : rootName);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(avatar, root.transform, out error))
            {
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
                return false;
            }

            KimodoRetargetClipSamplingUtility.SetHierarchyHideFlags(root.transform, HideFlags.HideAndDontSave);

            animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = root.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = applyRootMotion;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            animator.enabled = animatorEnabled;
            return true;
        }

        internal static bool TryBuildSkeletonCache(
            Avatar avatar,
            string rootName,
            out SkeletonCache cache,
            out string error)
        {
            cache = null;
            error = string.Empty;

            if (!KimodoRetargetCoreUtility.IsValidHumanoid(avatar))
            {
                error = "Avatar is null/invalid/non-humanoid.";
                return false;
            }

            if (!TryCreateTemporaryHumanoidRoot(
                    avatar,
                    string.IsNullOrWhiteSpace(rootName) ? "KimodoSkeletonCache" : rootName,
                    animatorEnabled: true,
                    applyRootMotion: true,
                    out GameObject root,
                    out Animator animator,
                    out error))
            {
                return false;
            }

            string canonicalRootBoneName = ResolveSkeletonRootBoneName(avatar);
            if (!TryBuildBoneNameTable(root.transform, canonicalRootBoneName, out string[] bonePaths, out error))
            {
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            if (bonePaths == null || bonePaths.Length == 0)
            {
                error = "Skeleton cache bone table is empty.";
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            cache = new SkeletonCache
            {
                avatar = avatar,
                root = root,
                skeletonRoot = root.transform,
                rootLocalPosition = root.transform.localPosition,
                rootLocalRotation = root.transform.localRotation,
                rootLocalScale = root.transform.localScale,
                canonicalRootBoneName = canonicalRootBoneName,
                animator = animator,
                poseHandler = new HumanPoseHandler(avatar, root.transform),
                humanScale = Mathf.Max(1e-6f, animator.humanScale),
                bonePaths = bonePaths,
                boneTransforms = BuildBoneTransforms(root.transform, bonePaths, canonicalRootBoneName),
                boneCount = bonePaths.Length
            };

            KimodoRetargetClipSamplingUtility.CaptureSkeletonBindPose(cache);
            return true;
        }

        internal static bool ValidateRetargetCache(SkeletonCache cache, out string error)
        {
            error = string.Empty;

            if (cache == null)
            {
                error = "Skeleton cache is null.";
                return false;
            }

            if (!cache.IsReady)
            {
                error = "Skeleton cache is not initialized.";
                return false;
            }

            if (cache.avatar == null)
            {
                error = "Skeleton cache avatar is null.";
                return false;
            }

            if (cache.bonePaths == null || cache.boneTransforms == null)
            {
                error = "Skeleton cache bone mapping is missing.";
                return false;
            }

            if (cache.bonePaths.Length == 0 || cache.bonePaths.Length != cache.boneTransforms.Length)
            {
                error = "Skeleton cache bone mapping is invalid.";
                return false;
            }

            return true;
        }

        public static string ResolveSkeletonRootBoneName(Avatar avatar)
        {
            if (!KimodoRetargetCoreUtility.IsValidHumanoid(avatar))
            {
                return "Hips";
            }

            SkeletonBone[] skeleton = avatar.humanDescription.skeleton;
            if (skeleton == null || skeleton.Length == 0)
            {
                return "Hips";
            }

            int rootIndex = FindSkeletonRootIndex(skeleton);
            if (rootIndex >= 0 && rootIndex < skeleton.Length)
            {
                string name = skeleton[rootIndex].name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }

            return "Hips";
        }

        public static bool TryBuildBoneNameTable(Transform root, string canonicalRootBoneName, out string[] boneNames, out string error)
        {
            error = string.Empty;
            boneNames = null;
            if (root == null)
            {
                error = "Target root is null.";
                return false;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var names = new List<string>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                string path = CalculateTransformPath(all[i], root, canonicalRootBoneName);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                names.Add(path);
            }

            boneNames = names.ToArray();
            return true;
        }

        public static Transform[] BuildBoneTransforms(Transform root, string[] bonePaths, string canonicalRootBoneName)
        {
            if (bonePaths == null)
            {
                return Array.Empty<Transform>();
            }

            var transforms = new Transform[bonePaths.Length];
            for (int i = 0; i < bonePaths.Length; i++)
            {
                transforms[i] = FindByPath(root, bonePaths[i], canonicalRootBoneName);
            }

            return transforms;
        }


        public static Transform FindByPath(Transform root, string path, string canonicalRootBoneName)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (string.Equals(root.name, path, StringComparison.Ordinal) || string.Equals(canonicalRootBoneName, path, StringComparison.Ordinal))
            {
                return root;
            }

            string[] segments = path.Split('/');
            Transform current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                if (current == null)
                {
                    return null;
                }

                if (i == 0 && (string.Equals(current.name, segments[i], StringComparison.Ordinal) || string.Equals(canonicalRootBoneName, segments[i], StringComparison.Ordinal)))
                {
                    continue;
                }

                current = current.Find(segments[i]);
            }

            return current;
        }

        public static Transform FindTransformByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (string.Equals(current.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            return null;
        }

        public static bool TryFindUniqueTransformByName(
            Transform root,
            string name,
            out Transform result,
            out bool ambiguous)
        {
            result = null;
            ambiguous = false;
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];
                if (candidate == null || !string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (result != null && result != candidate)
                {
                    result = null;
                    ambiguous = true;
                    return false;
                }

                result = candidate;
            }

            return result != null;
        }

        public static bool TryGetProfileRootJointTransform(
            Dictionary<string, Transform> nameMap,
            string modelName,
            out Transform profileRootJoint)
        {
            profileRootJoint = null;
            if (nameMap == null)
            {
                return false;
            }

            string profileRootJointName = KimodoRigProfileDatabase.GetProfileRootJointNameForModel(modelName);
            if (string.IsNullOrWhiteSpace(profileRootJointName))
            {
                return false;
            }

            return nameMap.TryGetValue(profileRootJointName, out profileRootJoint) && profileRootJoint != null;
        }

        public static bool TryApplyMarkerSampleToTransformMap(
            TimelineInject.KimodoMarkerSampleResult sample,
            string modelName,
            Transform root,
            Dictionary<string, Transform> nameMap,
            out string error)
        {
            error = string.Empty;

            if (sample == null || root == null || nameMap == null)
            {
                error = "invalid sample or transform map";
                return false;
            }

            string[] modelJointNames = KimodoRigProfileDatabase.GetJointNamesForModel(modelName);
            if (modelJointNames == null || modelJointNames.Length == 0)
            {
                error = $"model joint layout not found for '{modelName}'";
                return false;
            }

            root.position = sample.unityRootPos;
            root.rotation = sample.unityRootRot;

            int count = sample.localAxisAngles != null ? sample.localAxisAngles.Count : 0;
            int applyCount = Mathf.Min(modelJointNames.Length, count);
            for (int i = 0; i < applyCount; i++)
            {
                string jointName = modelJointNames[i];
                if (!nameMap.TryGetValue(jointName, out Transform t) || t == null)
                {
                    error = $"joint '{jointName}' missing on pose rig";
                    return false;
                }

                t.localRotation = AxisAngleToQuaternion(sample.localAxisAngles[i]);
            }

            if (TryGetProfileRootJointTransform(nameMap, modelName, out Transform profileRootJoint))
            {
                profileRootJoint.position = sample.kimodoRootPosition;
            }

            return true;
        }

        public static string CalculateTransformPath(Transform target, Transform root, string canonicalRootBoneName)
        {
            if (target == null || root == null)
            {
                return null;
            }

            if (target == root)
            {
                return string.IsNullOrWhiteSpace(canonicalRootBoneName) ? target.name : canonicalRootBoneName;
            }

            var names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return null;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static Quaternion AxisAngleToQuaternion(Vector3 axisAngle)
        {
            float angleRad = axisAngle.magnitude;
            if (angleRad <= 1e-8f)
            {
                return Quaternion.identity;
            }

            Vector3 axis = axisAngle / angleRad;
            return Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
        }

        private static int FindSkeletonRootIndex(SkeletonBone[] skeleton)
        {
            if (skeleton == null || skeleton.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < skeleton.Length; i++)
            {
                string parentName = AvatarRuntimeAccess.GetSkeletonBoneParentNameOrEmpty(skeleton[i]);
                if (string.IsNullOrWhiteSpace(parentName))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
