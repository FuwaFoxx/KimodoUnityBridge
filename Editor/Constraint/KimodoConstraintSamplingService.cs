using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoConstraintSamplingService
    {
        internal static bool TrySampleMarkerDataFromMarker(
            KimodoConstraintMarkerBase marker,
            out KimodoMarkerSampleResult sampledData,
            out string error)
        {
            sampledData = null;
            error = string.Empty;

            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(marker, out TimelineClip clipRange) || clipRange == null)
            {
                error = "clip range not found";
                return false;
            }

            TrackAsset track = clipRange.GetParentTrack();
            if (track == null)
            {
                error = "parent track not found";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null || animator.transform == null)
            {
                error = "Animation track has no Animator binding.";
                return false;
            }

            int frameIndex = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, marker.time);
            double sampleTime = KimodoConstraintPosePipeline.ResolveSampleTimeFromFrameIndex(clipRange, frameIndex);

            double originalTime = director.time;
            DirectorWrapMode originalWrap = director.extrapolationMode;
            KimodoMarkerSampleResult sample;
            try
            {
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.time = sampleTime;
                director.Evaluate();

                if (!KimodoConstraintPosePipeline.TrySampleUnityPoseForMarkerContext(
                        clipRange,
                        animator,
                        animator.transform,
                        sampleTime,
                        marker.ConstraintType,
                        out sample,
                        out error))
                {
                    return false;
                }
            }
            finally
            {
                director.time = originalTime;
                director.Evaluate();
                director.extrapolationMode = originalWrap;
            }

            sampledData = KimodoConstraintMarkerPoseMapper.BuildSampleFromCapture(marker, frameIndex, sample);
            if (sampledData == null)
            {
                error = "failed to build marker sample";
                return false;
            }

            return true;
        }

    }
}
