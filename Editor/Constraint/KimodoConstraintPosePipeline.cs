using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoConstraintPosePipeline
    {
        internal static bool TrySampleUnityPoseForMarkerContext(
            TimelineClip clipRange,
            Animator animator,
            Transform skeletonRoot,
            double sampleTime,
            string markerType,
            out KimodoMarkerSampleResult pose,
            out string error)
        {
            pose = null;
            error = string.Empty;

            if (clipRange == null)
            {
                error = "clip range is null";
                return false;
            }

            if (animator == null)
            {
                error = "animator is null";
                return false;
            }

            Transform root = skeletonRoot != null ? skeletonRoot : animator.transform;
            if (root == null)
            {
                error = "skeleton root is null";
                return false;
            }

            int frameIndex = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, sampleTime);
            double snappedSampleTime = ResolveSampleTimeFromFrameIndex(clipRange, frameIndex);
            if (!KimodoMarkerSamplingUtility.TrySampleMarker(
                    animator,
                    root,
                    clipRange,
                    ResolveModelNameFromClip(clipRange),
                    snappedSampleTime,
                    frameIndex,
                    markerType ?? string.Empty,
                    out pose,
                    out error))
            {
                return false;
            }

            return pose != null;
        }

        internal static double ResolveSampleTimeFromFrameIndex(TimelineClip clipRange, int frameIndex)
        {
            if (clipRange == null)
            {
                return 0.0;
            }

            int clampedFrame = Mathf.Clamp(frameIndex, 0, KimodoConstraintMarkerEditorUtility.GetMaxKimodoFrameIndex(clipRange));
            double sampleTime = clipRange.start + (clampedFrame / KimodoConstraintMarkerEditorUtility.KimodoFps);
            if (sampleTime < clipRange.start)
            {
                return clipRange.start;
            }

            if (sampleTime > clipRange.end)
            {
                return clipRange.end;
            }

            return sampleTime;
        }

        private static string ResolveModelNameFromClip(TimelineClip clipRange)
        {
            if (clipRange?.asset is KimodoPlayableClip playableClip)
            {
                return playableClip.bridgeModelName;
            }

            return "Kimodo-SOMA-RP-v1";
        }
    }
}
