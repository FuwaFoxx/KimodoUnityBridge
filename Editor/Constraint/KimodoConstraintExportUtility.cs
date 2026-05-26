using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    public static class KimodoConstraintExportUtility
    {
        private const string LogPrefix = "[Kimodo][ConstraintExport]";

        public static bool TryBuildConstraintsJson(
            TimelineClip sourceClip,
            out string constraintsJson,
            out string error)
        {
            constraintsJson = string.Empty;
            error = string.Empty;

            if (!TryBuildMarkerSamplesForExport(sourceClip, out List<KimodoMarkerSampleResult> samples, out error))
            {
                return false;
            }

            if (samples.Count == 0)
            {
                return true;
            }

            List<KimodoConstraintJson> merged = KimodoConstraintJsonExporter.BuildConstraints(samples, mergeByType: true);
            if (!ValidateConstraints(merged, out error))
            {
                return false;
            }

            constraintsJson = JsonConvert.SerializeObject(
                merged,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return true;
        }

        internal static bool TrySamplePoseFromClipAsset(
            TimelineClip sourceClip,
            Animator animator,
            Transform skeletonRoot,
            double globalTime,
            int frameIndex,
            string markerType,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            sample = null;
            error = string.Empty;

            if (sourceClip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            double sampleTime = KimodoConstraintPosePipeline.ResolveSampleTimeFromFrameIndex(sourceClip, frameIndex);
            if (!KimodoConstraintPosePipeline.TrySampleUnityPoseForMarkerContext(
                    sourceClip,
                    animator,
                    skeletonRoot,
                    sampleTime,
                    markerType,
                    out sample,
                    out error))
            {
                return false;
            }

            if (sample == null)
            {
                error = "sample result is null";
                return false;
            }

            sample.constraintType = markerType ?? string.Empty;
            sample.frameIndex = frameIndex;
            return true;
        }

        internal static bool TryBuildMarkerSamplesForExport(
            TimelineClip sourceClip,
            out List<KimodoMarkerSampleResult> samples,
            out string error)
        {
            samples = new List<KimodoMarkerSampleResult>();
            error = string.Empty;

            if (sourceClip == null)
            {
                error = "No selected timeline clip for constraint export.";
                return false;
            }

            TrackAsset track = sourceClip.GetParentTrack();
            if (track == null)
            {
                error = "Cannot resolve parent animation track.";
                return false;
            }

            List<KimodoConstraintMarkerBase> markers = GatherKimodoMarkers(track, sourceClip);
            if (markers.Count == 0)
            {
                return true;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null.";
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null)
            {
                error = "Animation track has no Animator binding.";
                return false;
            }

            Transform skeletonRoot = animator.transform;
            if (skeletonRoot == null)
            {
                error = "Animator transform is null.";
                return false;
            }

            double originalTime = director.time;
            DirectorWrapMode originalWrap = director.extrapolationMode;

            try
            {
                director.extrapolationMode = DirectorWrapMode.Hold;
                for (int i = 0; i < markers.Count; i++)
                {
                    if (!TryBuildMarkerSample(markers[i], sourceClip, skeletonRoot, animator, director, out KimodoMarkerSampleResult sample, out error))
                    {
                        return false;
                    }

                    samples.Add(sample);
                }
            }
            finally
            {
                director.time = originalTime;
                director.Evaluate();
                director.extrapolationMode = originalWrap;
            }

            return true;
        }

        private static bool TryBuildMarkerSample(
            KimodoConstraintMarkerBase marker,
            TimelineClip sourceClip,
            Transform skeletonRoot,
            Animator animator,
            PlayableDirector director,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            sample = null;
            error = string.Empty;
            if (marker == null)
            {
                error = "Marker is null.";
                return false;
            }

            bool isCustomEndEffector = marker is KimodoEndEffectorConstraintMarker ee &&
                                       string.Equals(ee.ConstraintType, "end-effector", StringComparison.OrdinalIgnoreCase);
            if (marker.useOverride && !isCustomEndEffector)
            {
                if (!KimodoConstraintMarkerPoseMapper.TryReadSample(marker, out sample, out error))
                {
                    return false;
                }
                return true;
            }

            int frameIndex = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(sourceClip, marker.time);
            double sampleTime = KimodoConstraintPosePipeline.ResolveSampleTimeFromFrameIndex(sourceClip, frameIndex);
            director.time = sampleTime;
            director.Evaluate();

            if (!TrySamplePoseFromClipAsset(
                    sourceClip,
                    animator,
                    skeletonRoot,
                    sampleTime,
                    frameIndex,
                    marker.ConstraintType,
                    out KimodoMarkerSampleResult captured,
                    out error))
            {
                return false;
            }

            sample = KimodoConstraintMarkerPoseMapper.BuildSampleFromCapture(marker, frameIndex, captured);
            if (sample == null)
            {
                error = "failed to map sampled pose to marker sample data";
                return false;
            }

            return true;
        }

        private static List<KimodoConstraintMarkerBase> GatherKimodoMarkers(TrackAsset track, TimelineClip clipRange)
        {
            var markers = new List<KimodoConstraintMarkerBase>();
            double minTime = clipRange != null ? clipRange.start : double.MinValue;
            double maxTime = clipRange != null ? clipRange.end : double.MaxValue;
            foreach (IMarker marker in track.GetMarkers())
            {
                if (marker is KimodoConstraintMarkerBase kimodoMarker)
                {
                    if (kimodoMarker.time < minTime || kimodoMarker.time > maxTime)
                    {
                        continue;
                    }

                    markers.Add(kimodoMarker);
                }
            }

            markers.Sort((a, b) => a.time.CompareTo(b.time));
            return markers;
        }

        private static bool ValidateConstraints(List<KimodoConstraintJson> constraints, out string error)
        {
            error = string.Empty;
            if (constraints == null)
            {
                error = "constraint list is null";
                return false;
            }

            for (int i = 0; i < constraints.Count; i++)
            {
                if (!ValidateConstraint(constraints[i], out error))
                {
                    error = $"constraint[{i}] invalid: {error}";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateConstraint(KimodoConstraintJson json, out string error)
        {
            error = string.Empty;
            if (json == null)
            {
                error = "constraint is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(json.type))
            {
                error = "type is empty";
                return false;
            }

            if (json.frame_indices == null || json.frame_indices.Count == 0)
            {
                error = "frame_indices is empty";
                return false;
            }

            return true;
        }

    }
}
