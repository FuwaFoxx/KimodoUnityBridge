#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace KimodoUnityMotionTools
{
    public static class KimodoRuntimeUtility
    {
        public static string SanitizeName(string input, string defaultName = "joint")
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.IsNullOrWhiteSpace(defaultName) ? "joint" : defaultName;
            }

            return input.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
        }

        public static Vector3 QuaternionToAxisAngleVector(Quaternion q)
        {
            q.Normalize();
            q.ToAngleAxis(out float degrees, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis == Vector3.zero)
            {
                return Vector3.zero;
            }

            if (degrees > 180f)
            {
                degrees -= 360f;
            }

            float radians = degrees * Mathf.Deg2Rad;
            return axis.normalized * radians;
        }

#if UNITY_EDITOR
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
#endif
    }
}
