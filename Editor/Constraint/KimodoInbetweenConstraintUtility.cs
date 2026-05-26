using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoInbetweenConstraintUtility
    {
        private const string LogPrefix = "[Kimodo][InbetweenConstraint]";
        private const double NeighborSampleDeltaSeconds = 1.0 / 60.0;

        public static bool TryBuildConstraintsJson(
            TimelineClip sourceClip,
            bool enableInbetweenInterpolation,
            int generationFrames,
            out string constraintsJson,
            out string error)
        {
            constraintsJson = string.Empty;
            error = string.Empty;

            if (!KimodoConstraintExportUtility.TryBuildMarkerSamplesForExport(sourceClip, out List<KimodoMarkerSampleResult> samples, out error))
            {
                return false;
            }

            if (enableInbetweenInterpolation)
            {
                if (!TryAddAutoInbetweenSamples(sourceClip, Mathf.Max(1, generationFrames), samples, out string warning))
                {
                    if (!string.IsNullOrWhiteSpace(warning))
                    {
                        Debug.LogWarning($"{LogPrefix} {warning}");
                    }
                }
            }

            constraintsJson = KimodoConstraintJsonExporter.ToConstraintsJson(samples);
            return true;
        }

        private static bool TryAddAutoInbetweenSamples(
            TimelineClip sourceClip,
            int generationFrames,
            List<KimodoMarkerSampleResult> samples,
            out string warning)
        {
            warning = string.Empty;
            if (sourceClip == null)
            {
                warning = "source clip is null, skip inbetween interpolation.";
                return false;
            }

            TrackAsset track = sourceClip.GetParentTrack();
            if (track == null)
            {
                warning = "cannot resolve parent track, skip inbetween interpolation.";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                warning = "Timeline inspected director is null, skip inbetween interpolation.";
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null)
            {
                warning = "track has no Animator binding, skip inbetween interpolation.";
                return false;
            }

            Transform skeletonRoot = animator.transform;
            if (skeletonRoot == null)
            {
                warning = "Animator transform is null, skip inbetween interpolation.";
                return false;
            }

            FindNeighborClips(sourceClip, out TimelineClip leftNeighbor, out TimelineClip rightNeighbor);
            if (leftNeighbor == null && rightNeighbor == null)
            {
                warning = "no neighboring clips found, skip inbetween interpolation.";
                return true;
            }

            var occupiedManualFrames = new HashSet<int>();
            CollectManualFrames(samples, occupiedManualFrames);

            double originalTime = director.time;
            DirectorWrapMode originalWrapMode = director.extrapolationMode;

            try
            {
                director.extrapolationMode = DirectorWrapMode.Hold;

                if (leftNeighbor != null && !occupiedManualFrames.Contains(0))
                {
                    double evalTime = Math.Max(leftNeighbor.start, leftNeighbor.end - NeighborSampleDeltaSeconds);
                    if (TryCapturePoseAtTime(leftNeighbor, director, skeletonRoot, animator, evalTime, 0, "fullbody", out KimodoMarkerSampleResult pose, out string captureError))
                    {
                        samples.Add(pose);
                    }
                    else
                    {
                        Debug.LogWarning($"{LogPrefix} Failed to sample left neighbor end pose: {captureError}");
                    }
                }

                int endFrame = Math.Max(0, generationFrames - 1);
                if (rightNeighbor != null && !occupiedManualFrames.Contains(endFrame))
                {
                    double evalTime = rightNeighbor.start;
                    if (TryCapturePoseAtTime(rightNeighbor, director, skeletonRoot, animator, evalTime, endFrame, "fullbody", out KimodoMarkerSampleResult pose, out string captureError))
                    {
                        samples.Add(pose);
                    }
                    else
                    {
                        Debug.LogWarning($"{LogPrefix} Failed to sample right neighbor start pose: {captureError}");
                    }
                }
            }
            finally
            {
                director.time = originalTime;
                director.Evaluate();
                director.extrapolationMode = originalWrapMode;
            }

            return true;
        }

        private static void CollectManualFrames(List<KimodoMarkerSampleResult> samples, HashSet<int> output)
        {
            if (samples == null || output == null)
            {
                return;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                KimodoMarkerSampleResult s = samples[i];
                if (s == null)
                {
                    continue;
                }

                output.Add(s.frameIndex);
            }
        }

        private static void FindNeighborClips(TimelineClip sourceClip, out TimelineClip leftNeighbor, out TimelineClip rightNeighbor)
        {
            leftNeighbor = null;
            rightNeighbor = null;

            TrackAsset track = sourceClip.GetParentTrack();
            if (track == null)
            {
                return;
            }

            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip == null || clip == sourceClip)
                {
                    continue;
                }

                if (clip.end <= sourceClip.start)
                {
                    if (leftNeighbor == null || clip.end > leftNeighbor.end)
                    {
                        leftNeighbor = clip;
                    }
                }

                if (clip.start >= sourceClip.end)
                {
                    if (rightNeighbor == null || clip.start < rightNeighbor.start)
                    {
                        rightNeighbor = clip;
                    }
                }
            }
        }

        private static bool TryCapturePoseAtTime(
            TimelineClip sourceClip,
            PlayableDirector director,
            Transform skeletonRoot,
            Animator animator,
            double evalTime,
            int frameIndex,
            string markerType,
            out KimodoMarkerSampleResult pose,
            out string error)
        {
            pose = null;
            error = string.Empty;

            try
            {
                director.time = evalTime;
                director.Evaluate();
                if (!KimodoConstraintExportUtility.TrySamplePoseFromClipAsset(
                        sourceClip,
                        animator,
                        skeletonRoot,
                        evalTime,
                        frameIndex,
                        markerType,
                        out pose,
                        out error))
                {
                    return false;
                }

                return pose != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
