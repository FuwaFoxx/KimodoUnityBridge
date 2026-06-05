using System;
using UnityEngine;

namespace KimodoBridge
{
    public static class KimodoProfileSkeletonUtility
    {
        public static bool TryResolveProfileSkeleton(
            string modelName,
            Transform root,
            out string[] jointNames,
            out int[] parentIndices,
            out Transform[] jointTransforms,
            out string error)
        {
            error = string.Empty;
            jointTransforms = Array.Empty<Transform>();
            KimodoRigProfileDatabase.ResolveProfile(modelName, out _, out jointNames, out parentIndices);
            if (jointNames == null || jointNames.Length == 0)
            {
                error = $"Profile joint layout not found for '{modelName}'.";
                return false;
            }

            if (root == null)
            {
                error = "Skeleton root is null.";
                return false;
            }

            jointTransforms = new Transform[jointNames.Length];
            for (int i = 0; i < jointNames.Length; i++)
            {
                string jointName = jointNames[i];
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    error = $"Profile joint at index {i} is empty.";
                    return false;
                }

                if (!TryFindUniqueTransformByName(root, jointName, out jointTransforms[i], out bool ambiguous))
                {
                    error = ambiguous
                        ? $"Profile joint '{jointName}' matches multiple transforms under '{root.name}'."
                        : $"Profile joint '{jointName}' was not found under '{root.name}'.";
                    jointTransforms = Array.Empty<Transform>();
                    return false;
                }
            }

            return true;
        }

        private static bool TryFindUniqueTransformByName(
            Transform root,
            string jointName,
            out Transform result,
            out bool ambiguous)
        {
            result = null;
            ambiguous = false;
            if (root == null || string.IsNullOrWhiteSpace(jointName))
            {
                return false;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];
                if (candidate == null || !string.Equals(candidate.name, jointName, StringComparison.OrdinalIgnoreCase))
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
    }
}
