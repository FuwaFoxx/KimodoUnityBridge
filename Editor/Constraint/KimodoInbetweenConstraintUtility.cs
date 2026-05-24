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

            if (!KimodoConstraintExportUtility.TryBuildConstraintsJson(sourceClip, out string markerConstraintsJson, out error))
            {
                return false;
            }

            if (!enableInbetweenInterpolation)
            {
                constraintsJson = markerConstraintsJson ?? string.Empty;
                return true;
            }

            List<KimodoConstraintJson> constraints = LoadConstraintsFromJson(markerConstraintsJson);
            if (TryBuildAutoInbetweenFullBodyConstraints(sourceClip, Mathf.Max(1, generationFrames), constraints, out bool _, out string autoWarning))
            {
                // no-op
            }
            else if (!string.IsNullOrWhiteSpace(autoWarning))
            {
                Debug.LogWarning($"{LogPrefix} {autoWarning}");
            }

            if (constraints.Count == 0)
            {
                constraintsJson = string.Empty;
                return true;
            }

            constraintsJson = JsonConvert.SerializeObject(
                constraints,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return true;
        }

        private static List<KimodoConstraintJson> LoadConstraintsFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<KimodoConstraintJson>();
            }

            try
            {
                List<KimodoConstraintJson> parsed = JsonConvert.DeserializeObject<List<KimodoConstraintJson>>(json);
                return parsed ?? new List<KimodoConstraintJson>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Failed to parse constraints json string, fallback to empty: {ex.Message}");
                return new List<KimodoConstraintJson>();
            }
        }

        private static bool TryBuildAutoInbetweenFullBodyConstraints(
            TimelineClip sourceClip,
            int generationFrames,
            List<KimodoConstraintJson> existingConstraints,
            out bool autoAdded,
            out string warning)
        {
            autoAdded = false;
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
            CollectManualFrames(existingConstraints, occupiedManualFrames);

            var autoFrames = new List<(int Frame, KimodoMarkerSampleResult Pose)>();
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
                        autoFrames.Add((0, pose));
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
                        autoFrames.Add((endFrame, pose));
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

            if (autoFrames.Count == 0)
            {
                warning = "no usable sampled neighboring poses; fallback to manual constraints only.";
                return true;
            }

            KimodoConstraintJson autoFullbody = BuildAutoFullBodyConstraint(autoFrames);
            existingConstraints.Add(autoFullbody);
            autoAdded = true;
            Debug.Log($"{LogPrefix} Added inbetween fullbody constraints at frames: {string.Join(",", autoFullbody.frame_indices)}");
            return true;
        }

        private static void CollectManualFrames(List<KimodoConstraintJson> constraints, HashSet<int> output)
        {
            if (constraints == null || output == null)
            {
                return;
            }

            for (int i = 0; i < constraints.Count; i++)
            {
                KimodoConstraintJson c = constraints[i];
                if (c == null || c.frame_indices == null || c.frame_indices.Count == 0)
                {
                    continue;
                }

                for (int j = 0; j < c.frame_indices.Count; j++)
                {
                    output.Add(c.frame_indices[j]);
                }
            }
        }

        private static KimodoConstraintJson BuildAutoFullBodyConstraint(List<(int Frame, KimodoMarkerSampleResult Pose)> framePoses)
        {
            framePoses.Sort((a, b) => a.Frame.CompareTo(b.Frame));

            var json = new KimodoConstraintJson
            {
                type = "fullbody",
                frame_indices = new List<int>(),
                smooth_root_2d = new List<float[]>(),
                root_positions = new List<float[]>(),
                local_joints_rot = new List<float[][]>()
            };

            for (int i = 0; i < framePoses.Count; i++)
            {
                int frame = framePoses[i].Frame;
                KimodoMarkerSampleResult pose = framePoses[i].Pose;
                json.frame_indices.Add(frame);
                json.smooth_root_2d.Add(new[] { pose.rootPosition.x, pose.rootPosition.z });
                json.root_positions.Add(new[] { pose.rootPosition.x, pose.rootPosition.y, pose.rootPosition.z });
                json.local_joints_rot.Add(ToAxisAngleArray(pose.localAxisAngles));
            }

            return json;
        }

        private static float[][] ToAxisAngleArray(List<Vector3> axisAngles)
        {
            float[][] data = new float[axisAngles.Count][];
            for (int i = 0; i < axisAngles.Count; i++)
            {
                Vector3 v = axisAngles[i];
                data[i] = new[] { v.x, v.y, v.z };
            }
            return data;
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
                return KimodoConstraintExportUtility.TrySamplePoseFromClipAsset(
                    sourceClip,
                    animator,
                    skeletonRoot,
                    evalTime,
                    frameIndex,
                    markerType,
                    out pose,
                    out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

    }
}
