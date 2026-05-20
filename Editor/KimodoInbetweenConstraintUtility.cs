using System;
using System.Collections.Generic;
using System.IO;
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

        private static readonly string[] Soma30Names =
        {
            "Hips", "Spine1", "Spine2", "Chest", "Neck1", "Neck2", "Head", "Jaw", "LeftEye", "RightEye",
            "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandThumbEnd", "LeftHandMiddleEnd",
            "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandThumbEnd", "RightHandMiddleEnd",
            "LeftLeg", "LeftShin", "LeftFoot", "LeftToeBase", "RightLeg", "RightShin", "RightFoot", "RightToeBase"
        };

        private static readonly int[] Soma30Parents =
        {
            -1, 0, 1, 2, 3, 4, 5, 6, 6, 6, 3, 10, 11, 12, 13, 13, 3, 16, 17, 18, 19, 19, 0, 22, 23, 24, 0, 26, 27, 28
        };

        public static bool TryBuildAndWriteConstraintsFile(
            TimelineClip sourceClip,
            bool enableInbetweenInterpolation,
            int generationFrames,
            out string absolutePath,
            out string error)
        {
            absolutePath = string.Empty;
            error = string.Empty;

            if (!KimodoConstraintExportUtility.TryBuildAndWriteConstraintsFile(sourceClip, out string markerConstraintsPath, out error))
            {
                return false;
            }

            if (!enableInbetweenInterpolation)
            {
                absolutePath = markerConstraintsPath ?? string.Empty;
                return true;
            }

            List<KimodoConstraintJson> constraints = LoadConstraints(markerConstraintsPath);
            bool changed = false;

            if (enableInbetweenInterpolation)
            {
                if (TryBuildAutoInbetweenFullBodyConstraints(sourceClip, Mathf.Max(1, generationFrames), constraints, out bool autoAdded, out string autoWarning))
                {
                    changed = autoAdded;
                }
                else if (!string.IsNullOrWhiteSpace(autoWarning))
                {
                    Debug.LogWarning($"{LogPrefix} {autoWarning}");
                }
            }

            if (constraints.Count == 0)
            {
                absolutePath = string.Empty;
                return true;
            }

            if (!changed && !string.IsNullOrWhiteSpace(markerConstraintsPath) && File.Exists(markerConstraintsPath))
            {
                absolutePath = markerConstraintsPath;
                return true;
            }

            string tempDir = Path.Combine(Application.dataPath, "KimodoTemp");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string fileName = $"constraints_{DateTime.Now:yyyyMMdd_HHmmss_fff}_with_inbetween.json";
            absolutePath = Path.Combine(tempDir, fileName);
            string json = JsonConvert.SerializeObject(
                constraints,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            File.WriteAllText(absolutePath, json);
            Debug.Log($"{LogPrefix} Exported {constraints.Count} constraint set(s) to: {absolutePath}");
            return true;
        }

        private static List<KimodoConstraintJson> LoadConstraints(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new List<KimodoConstraintJson>();
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<KimodoConstraintJson>();
                }

                List<KimodoConstraintJson> parsed = JsonConvert.DeserializeObject<List<KimodoConstraintJson>>(json);
                return parsed ?? new List<KimodoConstraintJson>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix} Failed to parse existing constraints file ({path}), fallback to empty: {ex.Message}");
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

            var autoFrames = new List<(int Frame, SkeletonPose Pose)>();
            double originalTime = director.time;
            DirectorWrapMode originalWrapMode = director.extrapolationMode;

            try
            {
                director.extrapolationMode = DirectorWrapMode.Hold;

                if (leftNeighbor != null && !occupiedManualFrames.Contains(0))
                {
                    double evalTime = Math.Max(leftNeighbor.start, leftNeighbor.end - NeighborSampleDeltaSeconds);
                    if (TryCapturePoseAtTime(director, skeletonRoot, animator, evalTime, out SkeletonPose pose, out string captureError))
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
                    if (TryCapturePoseAtTime(director, skeletonRoot, animator, evalTime, out SkeletonPose pose, out string captureError))
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

        private static KimodoConstraintJson BuildAutoFullBodyConstraint(List<(int Frame, SkeletonPose Pose)> framePoses)
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
                SkeletonPose pose = framePoses[i].Pose;
                json.frame_indices.Add(frame);
                json.smooth_root_2d.Add(new[] { pose.RootPosition.x, pose.RootPosition.z });
                json.root_positions.Add(new[] { pose.RootPosition.x, pose.RootPosition.y, pose.RootPosition.z });
                json.local_joints_rot.Add(ToAxisAngleArray(pose.LocalAxisAngles));
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
            PlayableDirector director,
            Transform skeletonRoot,
            Animator animator,
            double evalTime,
            out SkeletonPose pose,
            out string error)
        {
            pose = null;
            error = string.Empty;

            try
            {
                director.time = evalTime;
                director.Evaluate();
                pose = CapturePose(skeletonRoot, animator);
                if (pose.LocalAxisAngles.Count == 0)
                {
                    error = $"sampled empty local rotations at {evalTime:F3}s.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static SkeletonPose CapturePose(Transform root, Animator animator)
        {
            var pose = new SkeletonPose();
            if (root == null)
            {
                return pose;
            }

            Transform pelvis = TryResolveTransformBySomaName("Hips", root, animator) ?? root;

            Vector3 worldPos = pelvis.position;
            pose.RootPosition = new Vector3(-worldPos.x, worldPos.y, worldPos.z);

            Transform somaRoot = root.Find("SOMA");
            if (somaRoot == null)
            {
                somaRoot = root;
            }

            Transform[] joints = ResolveSoma30JointTransforms(somaRoot, animator);
            Quaternion[] worldRots = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                worldRots[i] = joints[i] != null ? joints[i].rotation : Quaternion.identity;
            }

            for (int i = 0; i < joints.Length; i++)
            {
                int parent = Soma30Parents[i];
                Quaternion local = (parent >= 0 && parent < worldRots.Length)
                    ? Quaternion.Inverse(worldRots[parent]) * worldRots[i]
                    : worldRots[i];

                Quaternion q = local;
                q = new Quaternion(q.x, -q.y, -q.z, q.w);
                pose.LocalAxisAngles.Add(QuaternionToAxisAngleVector(q));
            }

            return pose;
        }

        private static Transform[] ResolveSoma30JointTransforms(Transform root, Animator animator)
        {
            var transforms = new Transform[Soma30Names.Length];
            if (root == null)
            {
                return transforms;
            }

            for (int i = 0; i < Soma30Names.Length; i++)
            {
                transforms[i] = TryResolveTransformBySomaName(Soma30Names[i], root, animator) ?? root;
            }
            return transforms;
        }

        private static Transform TryResolveTransformBySomaName(string somaName, Transform searchRoot, Animator animator)
        {
            Transform byHuman = TryResolveViaHumanBone(somaName, animator);
            if (byHuman != null)
            {
                return byHuman;
            }

            return FindTransformByName(searchRoot, somaName);
        }

        private static Transform TryResolveViaHumanBone(string somaName, Animator animator)
        {
            if (animator == null || !animator.isHuman)
            {
                return null;
            }

            bool hasUpperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest) != null;
            switch (somaName)
            {
                case "Hips": return animator.GetBoneTransform(HumanBodyBones.Hips);
                case "Spine1": return animator.GetBoneTransform(HumanBodyBones.Spine);
                case "Spine2": return animator.GetBoneTransform(HumanBodyBones.Chest);
                case "Chest": return hasUpperChest
                    ? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    : animator.GetBoneTransform(HumanBodyBones.Chest);
                case "Neck1": return animator.GetBoneTransform(HumanBodyBones.Neck);
                case "Neck2": return animator.GetBoneTransform(HumanBodyBones.Neck);
                case "Head": return animator.GetBoneTransform(HumanBodyBones.Head);
                case "Jaw": return animator.GetBoneTransform(HumanBodyBones.Jaw);
                case "LeftEye": return animator.GetBoneTransform(HumanBodyBones.LeftEye);
                case "RightEye": return animator.GetBoneTransform(HumanBodyBones.RightEye);
                case "LeftShoulder": return animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
                case "LeftArm": return animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                case "LeftForeArm": return animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                case "LeftHand": return animator.GetBoneTransform(HumanBodyBones.LeftHand);
                case "LeftHandThumbEnd": return animator.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
                case "LeftHandMiddleEnd": return animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal);
                case "RightShoulder": return animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                case "RightArm": return animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                case "RightForeArm": return animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                case "RightHand": return animator.GetBoneTransform(HumanBodyBones.RightHand);
                case "RightHandThumbEnd": return animator.GetBoneTransform(HumanBodyBones.RightThumbDistal);
                case "RightHandMiddleEnd": return animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
                case "LeftLeg": return animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                case "LeftShin": return animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                case "LeftFoot": return animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                case "LeftToeBase": return animator.GetBoneTransform(HumanBodyBones.LeftToes);
                case "RightLeg": return animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                case "RightShin": return animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                case "RightFoot": return animator.GetBoneTransform(HumanBodyBones.RightFoot);
                case "RightToeBase": return animator.GetBoneTransform(HumanBodyBones.RightToes);
                default: return null;
            }
        }

        private static Transform FindTransformByName(Transform root, string name)
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

        private static Vector3 QuaternionToAxisAngleVector(Quaternion q)
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

        private sealed class SkeletonPose
        {
            public Vector3 RootPosition;
            public readonly List<Vector3> LocalAxisAngles = new List<Vector3>();
        }
    }
}
