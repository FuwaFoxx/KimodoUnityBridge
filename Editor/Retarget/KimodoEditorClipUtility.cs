using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools
{
    internal static class KimodoEditorClipUtility
    {
        public static void CopyClipData(AnimationClip sourceClip, AnimationClip targetClip, bool forceNoLoopKeepY = false)
        {
            if (sourceClip == null || targetClip == null)
            {
                return;
            }

            targetClip.ClearCurves();
            targetClip.frameRate = sourceClip.frameRate > 0f ? sourceClip.frameRate : targetClip.frameRate;
            if (forceNoLoopKeepY)
            {
                AnimationUtility.SetAnimationClipSettings(
                    targetClip,
                    new AnimationClipSettings
                    {
                        loopTime = false,
                        keepOriginalPositionY = true
                    });
            }
            else
            {
                AnimationUtility.SetAnimationClipSettings(targetClip, AnimationUtility.GetAnimationClipSettings(sourceClip));
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceClip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                if (curve != null)
                {
                    targetClip.SetCurve(binding.path, binding.type, binding.propertyName, curve);
                }
            }

            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(sourceClip);
            for (int i = 0; i < objectBindings.Length; i++)
            {
                EditorCurveBinding binding = objectBindings[i];
                ObjectReferenceKeyframe[] curve = AnimationUtility.GetObjectReferenceCurve(sourceClip, binding);
                if (curve != null)
                {
                    AnimationUtility.SetObjectReferenceCurve(targetClip, binding, curve);
                }
            }
        }
    }
}
