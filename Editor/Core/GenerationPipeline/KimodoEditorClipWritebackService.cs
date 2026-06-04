using KimodoBridge;
using System;
using UnityEditor;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal sealed class KimodoEditorClipWritebackService
    {
        internal const string GeneratedClipFolder = "Assets/KimodoGeneratedClips";
        internal const string GeneratedClipNamePrefix = "Kimodo_";

        public AnimationClip CreateGeneratedAnimationClipAsset()
        {
            var newAnimationClip = new AnimationClip
            {
                name = $"{GeneratedClipNamePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}"
            };

            if (!AssetDatabase.IsValidFolder(GeneratedClipFolder))
            {
                AssetDatabase.CreateFolder("Assets", "KimodoGeneratedClips");
            }

            string fileName = $"{newAnimationClip.name}.anim";
            string savePath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedClipFolder}/{fileName}");
            AssetDatabase.CreateAsset(newAnimationClip, savePath);
            EditorUtility.SetDirty(newAnimationClip);
            AssetDatabase.SaveAssets();
            return newAnimationClip;
        }

        public bool BakeMotionJsonToClip(AnimationClip targetClip, string motionJson, string modelName, out string error)
        {
            error = string.Empty;
            if (targetClip == null || string.IsNullOrWhiteSpace(motionJson))
            {
                error = "Clip / motion json is missing.";
                return false;
            }

            bool ok = KimodoRetargetToolsEditor.BakeIntoClip(
                targetClip: targetClip,
                motionJson: motionJson,
                skeletonType: KimodoPlayableClip.ResolveBakeSkeletonTypeFromModelName(modelName),
                modelName: modelName,
                curveFilterOptions: null,
                out error);

            if (!ok)
            {
                Debug.LogWarning($"[Kimodo] Bake failed: {error}");
                return false;
            }

            EditorUtility.SetDirty(targetClip);
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
