using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using TimelineInject;
using UnityEngine;

namespace KimodoBridge
{
    public sealed class KimodoRawMotionData
    {
        internal readonly string[] jointNames;
        internal readonly int[] jointParents;
        internal readonly List<List<List<float>>> positions;
        internal readonly List<float> localRotQuats;
        internal readonly int rootJointIndex;

        internal KimodoRawMotionData(
            int frameCount,
            int jointCount,
            float frameRate,
            string[] jointNames,
            int[] jointParents,
            List<List<List<float>>> positions,
            List<float> localRotQuats,
            int rootJointIndex)
        {
            FrameCount = frameCount;
            JointCount = jointCount;
            FrameRate = frameRate > 0f ? frameRate : KimodoPlayableClip.FIXED_FRAME_RATE;
            this.jointNames = jointNames ?? Array.Empty<string>();
            this.jointParents = jointParents ?? Array.Empty<int>();
            this.positions = positions;
            this.localRotQuats = localRotQuats;
            this.rootJointIndex = Mathf.Clamp(rootJointIndex, 0, Mathf.Max(0, jointCount - 1));
        }

        public int FrameCount { get; }
        public int JointCount { get; }
        public float FrameRate { get; }
        public float DurationSeconds => FrameCount > 0 ? FrameCount / FrameRate : 0f;
        public float LastFrameTimeSeconds => FrameCount > 1 ? (FrameCount - 1) / FrameRate : 0f;
        public int RootJointIndex => rootJointIndex;
        public IReadOnlyList<string> JointNames => jointNames;

        internal bool TryReadUnityPosition(int frameIndex, int jointIndex, out Vector3 value)
        {
            value = default;
            if (positions == null ||
                frameIndex < 0 ||
                frameIndex >= positions.Count ||
                jointIndex < 0 ||
                jointIndex >= positions[frameIndex].Count)
            {
                return false;
            }

            List<float> raw = positions[frameIndex][jointIndex];
            if (raw == null || raw.Count < 3)
            {
                return false;
            }

            value = new Vector3(-raw[0], raw[1], raw[2]);
            return true;
        }

        internal bool TryReadUnityLocalRotation(int frameIndex, int jointIndex, int rotationJointCount, out Quaternion value)
        {
            value = Quaternion.identity;
            if (localRotQuats == null ||
                frameIndex < 0 ||
                frameIndex >= FrameCount ||
                jointIndex < 0 ||
                jointIndex >= rotationJointCount)
            {
                return false;
            }

            int baseIndex = (frameIndex * rotationJointCount + jointIndex) * 4;
            if (baseIndex < 0 || baseIndex + 3 >= localRotQuats.Count)
            {
                return false;
            }

            float w = localRotQuats[baseIndex + 0];
            float x = localRotQuats[baseIndex + 1];
            float y = localRotQuats[baseIndex + 2];
            float z = localRotQuats[baseIndex + 3];
            Quaternion source = new Quaternion(x, y, z, w).normalized;
            value = new Quaternion(source.x, -source.y, -source.z, source.w);
            return true;
        }
    }

    public sealed class KimodoRawMotionPlaybackBinding
    {
        internal readonly KimodoRawMotionData motion;
        internal readonly Transform[] joints;
        internal readonly int[] motionJointIndices;

        internal KimodoRawMotionPlaybackBinding(
            KimodoRawMotionData motion,
            Transform[] joints,
            int[] motionJointIndices)
        {
            this.motion = motion;
            this.joints = joints ?? Array.Empty<Transform>();
            this.motionJointIndices = motionJointIndices ?? Array.Empty<int>();
        }

        public KimodoRawMotionData Motion => motion;
        public int JointCount => joints.Length;
        public float DurationSeconds => motion != null ? motion.DurationSeconds : 0f;
    }

    public static class KimodoRawMotionUtility
    {
        private const string FullBodyConstraintType = "fullbody";

        [Serializable]
        private sealed class MotionJsonData
        {
            public int num_frames;
            public int num_joints;
            public int fps;
            public string[] joint_names;
            public int[] joint_parents;
            public List<List<List<float>>> positions;
            public List<float> local_rot_quats;
        }

        public static bool TryParse(string motionJson, out KimodoRawMotionData motion, out string error)
        {
            motion = null;
            error = string.Empty;

            MotionJsonData data;
            try
            {
                data = ParseMotionJsonFlexible(motionJson);
            }
            catch (Exception ex)
            {
                error = $"Failed to parse motion json: {ex.Message}";
                return false;
            }

            if (!ValidateData(data, out error))
            {
                return false;
            }

            int positionFrames = data.positions != null ? data.positions.Count : 0;
            int frameHint = data.num_frames > 0 ? data.num_frames : positionFrames;
            int frameCount = positionFrames > 0 ? Mathf.Min(frameHint, positionFrames) : Mathf.Max(2, frameHint);
            int jointCount = Mathf.Min(data.joint_names.Length, data.num_joints > 0 ? data.num_joints : data.joint_names.Length);
            int rotationJointCount = ResolveRotationJointCount(data, frameCount, jointCount);
            jointCount = Mathf.Min(jointCount, rotationJointCount > 0 ? Mathf.Max(jointCount, rotationJointCount) : jointCount);
            int rootJoint = FindRootJointIndex(data, jointCount);

            motion = new KimodoRawMotionData(
                frameCount,
                jointCount,
                data.fps > 0 ? data.fps : KimodoPlayableClip.FIXED_FRAME_RATE,
                data.joint_names,
                data.joint_parents,
                data.positions,
                data.local_rot_quats,
                rootJoint);
            return true;
        }

        public static bool TryApplyFrame(
            KimodoRawMotionData motion,
            string modelName,
            Transform profileSkeletonRoot,
            int frameIndex,
            out string error,
            bool applyRootPosition = true,
            bool allowPartialJoints = false)
        {
            if (!TryCreatePlaybackBinding(motion, modelName, profileSkeletonRoot, out KimodoRawMotionPlaybackBinding binding, out error, allowPartialJoints))
            {
                return false;
            }

            return TryApplyFrame(binding, frameIndex, out error, applyRootPosition);
        }

        public static bool TryApplyTime(
            KimodoRawMotionData motion,
            string modelName,
            Transform profileSkeletonRoot,
            float timeSeconds,
            out string error,
            bool loop = false,
            bool applyRootPosition = true,
            bool allowPartialJoints = false)
        {
            if (!TryCreatePlaybackBinding(motion, modelName, profileSkeletonRoot, out KimodoRawMotionPlaybackBinding binding, out error, allowPartialJoints))
            {
                return false;
            }

            return TryApplyTime(binding, timeSeconds, out error, loop, applyRootPosition);
        }

        public static bool TryCreatePlaybackBinding(
            KimodoRawMotionData motion,
            string modelName,
            Transform profileSkeletonRoot,
            out KimodoRawMotionPlaybackBinding binding,
            out string error,
            bool allowPartialJoints = false)
        {
            binding = null;
            if (!TryResolvePlaybackTargets(motion, modelName, profileSkeletonRoot, allowPartialJoints, out Transform[] joints, out int[] motionJointIndices, out error))
            {
                return false;
            }

            binding = new KimodoRawMotionPlaybackBinding(motion, joints, motionJointIndices);
            return true;
        }

        public static bool TryApplyFrame(
            KimodoRawMotionPlaybackBinding binding,
            int frameIndex,
            out string error,
            bool applyRootPosition = true)
        {
            error = string.Empty;
            if (!ValidateBinding(binding, out error))
            {
                return false;
            }

            KimodoRawMotionData motion = binding.motion;
            int frame = Mathf.Clamp(frameIndex, 0, Mathf.Max(0, motion.FrameCount - 1));
            int rotationJointCount = ResolveRotationJointCount(motion);
            for (int i = 0; i < binding.joints.Length; i++)
            {
                Transform joint = binding.joints[i];
                int motionJoint = binding.motionJointIndices[i];
                if (joint == null || motionJoint < 0)
                {
                    continue;
                }

                if (motion.TryReadUnityLocalRotation(frame, motionJoint, rotationJointCount, out Quaternion localRotation))
                {
                    joint.localRotation = localRotation;
                }
            }

            if (applyRootPosition && binding.joints.Length > 0 && binding.joints[0] != null)
            {
                int rootMotionJoint = binding.motionJointIndices.Length > 0 && binding.motionJointIndices[0] >= 0
                    ? binding.motionJointIndices[0]
                    : motion.RootJointIndex;
                if (motion.TryReadUnityPosition(frame, rootMotionJoint, out Vector3 rootPosition))
                {
                    binding.joints[0].localPosition = rootPosition;
                }
            }

            return true;
        }

        public static bool TryApplyTime(
            KimodoRawMotionPlaybackBinding binding,
            float timeSeconds,
            out string error,
            bool loop = false,
            bool applyRootPosition = true)
        {
            error = string.Empty;
            if (!ValidateBinding(binding, out error))
            {
                return false;
            }

            KimodoRawMotionData motion = binding.motion;
            ResolveSampleFrames(motion, timeSeconds, loop, out int frame0, out int frame1, out float blend);
            int rotationJointCount = ResolveRotationJointCount(motion);
            for (int i = 0; i < binding.joints.Length; i++)
            {
                Transform joint = binding.joints[i];
                int motionJoint = binding.motionJointIndices[i];
                if (joint == null || motionJoint < 0)
                {
                    continue;
                }

                if (!motion.TryReadUnityLocalRotation(frame0, motionJoint, rotationJointCount, out Quaternion q0))
                {
                    continue;
                }

                if (blend > 0f && motion.TryReadUnityLocalRotation(frame1, motionJoint, rotationJointCount, out Quaternion q1))
                {
                    joint.localRotation = Quaternion.Slerp(q0, q1, blend);
                }
                else
                {
                    joint.localRotation = q0;
                }
            }

            if (applyRootPosition && binding.joints.Length > 0 && binding.joints[0] != null)
            {
                int rootMotionJoint = binding.motionJointIndices.Length > 0 && binding.motionJointIndices[0] >= 0
                    ? binding.motionJointIndices[0]
                    : motion.RootJointIndex;
                if (motion.TryReadUnityPosition(frame0, rootMotionJoint, out Vector3 p0))
                {
                    if (blend > 0f && motion.TryReadUnityPosition(frame1, rootMotionJoint, out Vector3 p1))
                    {
                        binding.joints[0].localPosition = Vector3.Lerp(p0, p1, blend);
                    }
                    else
                    {
                        binding.joints[0].localPosition = p0;
                    }
                }
            }

            return true;
        }

        public static bool TryExtractTailMarkerSample(
            string motionJson,
            string modelName,
            out KimodoMarkerSampleResult sample,
            out string error,
            string constraintType = FullBodyConstraintType,
            double sampleTime = 0.0,
            bool allowPartialJoints = false)
        {
            sample = null;
            if (!TryParse(motionJson, out KimodoRawMotionData motion, out error))
            {
                return false;
            }

            return TryExtractMarkerSample(
                motion,
                modelName,
                Mathf.Max(0, motion.FrameCount - 1),
                out sample,
                out error,
                constraintType,
                sampleTime,
                allowPartialJoints);
        }

        public static bool TryExtractTailMarkerSample(
            KimodoRawMotionData motion,
            string modelName,
            out KimodoMarkerSampleResult sample,
            out string error,
            string constraintType = FullBodyConstraintType,
            double sampleTime = 0.0,
            bool allowPartialJoints = false)
        {
            return TryExtractMarkerSample(
                motion,
                modelName,
                motion != null ? Mathf.Max(0, motion.FrameCount - 1) : 0,
                out sample,
                out error,
                constraintType,
                sampleTime,
                allowPartialJoints);
        }

        public static bool TryExtractMarkerSample(
            KimodoRawMotionData motion,
            string modelName,
            int frameIndex,
            out KimodoMarkerSampleResult sample,
            out string error,
            string constraintType = FullBodyConstraintType,
            double sampleTime = 0.0,
            bool allowPartialJoints = false)
        {
            sample = null;
            error = string.Empty;
            if (motion == null)
            {
                error = "Motion data is null.";
                return false;
            }

            KimodoRigProfileDatabase.ResolveProfile(modelName, out KimodoConstraintRigType rigType, out string[] profileJointNames, out _);
            if (!TryResolveMotionJointIndices(motion, profileJointNames, allowPartialJoints, out int[] motionJointIndices, out error))
            {
                return false;
            }

            int frame = Mathf.Clamp(frameIndex, 0, Mathf.Max(0, motion.FrameCount - 1));
            int rotationJointCount = ResolveRotationJointCount(motion);
            var localAxisAngles = new List<Vector3>(profileJointNames.Length);
            var sampledJointIndices = new List<int>(profileJointNames.Length);
            for (int i = 0; i < profileJointNames.Length; i++)
            {
                int motionJoint = motionJointIndices[i];
                if (motionJoint >= 0 &&
                    motion.TryReadUnityLocalRotation(frame, motionJoint, rotationJointCount, out Quaternion localRotation))
                {
                    localAxisAngles.Add(KimodoRuntimeUtility.QuaternionToAxisAngleVector(localRotation));
                    sampledJointIndices.Add(i);
                }
                else
                {
                    localAxisAngles.Add(Vector3.zero);
                }
            }

            int rootMotionJoint = motionJointIndices.Length > 0 && motionJointIndices[0] >= 0
                ? motionJointIndices[0]
                : motion.RootJointIndex;
            Vector3 rootPosition = Vector3.zero;
            _ = motion.TryReadUnityPosition(frame, rootMotionJoint, out rootPosition);

            Vector2 heading = Vector2.right;
            if (motion.TryReadUnityLocalRotation(frame, rootMotionJoint, rotationJointCount, out Quaternion rootRotation))
            {
                Vector3 forward = rootRotation * Vector3.forward;
                heading = new Vector2(forward.x, forward.z);
                if (heading.sqrMagnitude <= 1e-8f)
                {
                    heading = Vector2.right;
                }
                else
                {
                    heading.Normalize();
                }
            }

            sample = new KimodoMarkerSampleResult
            {
                constraintType = string.IsNullOrWhiteSpace(constraintType) ? FullBodyConstraintType : constraintType,
                sampleTime = sampleTime,
                rigType = rigType,
                hasRootHeading = true,
                kimodoRootPosition = rootPosition,
                rootHeading = heading,
                unityRootPos = rootPosition,
                unityRootRot = Quaternion.identity,
                jointNames = new List<string>(profileJointNames),
                localAxisAngles = localAxisAngles,
                sampledJointIndices = sampledJointIndices
            };
            return true;
        }

        private static bool ValidateBinding(KimodoRawMotionPlaybackBinding binding, out string error)
        {
            error = string.Empty;
            if (binding == null)
            {
                error = "Motion playback binding is null.";
                return false;
            }

            if (binding.motion == null)
            {
                error = "Motion playback binding has no motion data.";
                return false;
            }

            if (binding.joints == null || binding.motionJointIndices == null || binding.joints.Length != binding.motionJointIndices.Length)
            {
                error = "Motion playback binding joint mapping is invalid.";
                return false;
            }

            return true;
        }

        private static MotionJsonData ParseMotionJsonFlexible(string motionJson)
        {
            if (string.IsNullOrWhiteSpace(motionJson))
            {
                throw new Exception("motion json is empty.");
            }

            JToken token = JToken.Parse(motionJson);
            if (token is not JObject obj)
            {
                throw new Exception("motion json root is not an object.");
            }

            MotionJsonData data = obj.ToObject<MotionJsonData>() ?? new MotionJsonData();
            if (data.positions != null && data.positions.Count > 0)
            {
                return data;
            }

            JToken posed = obj["posed_joints"];
            if (posed is JArray)
            {
                data.positions = posed.ToObject<List<List<List<float>>>>();
                if (data.positions != null && data.positions.Count > 0)
                {
                    if (data.num_frames <= 0)
                    {
                        data.num_frames = data.positions.Count;
                    }

                    if (data.num_joints <= 0 && data.positions[0] != null)
                    {
                        data.num_joints = data.positions[0].Count;
                    }

                    return data;
                }
            }

            JToken flat = obj["joints"];
            if (flat is JArray)
            {
                List<float> flatValues = flat.ToObject<List<float>>();
                int frames = data.num_frames;
                int joints = data.num_joints;
                if (frames > 0 && joints > 0 && flatValues != null && flatValues.Count >= frames * joints * 3)
                {
                    data.positions = new List<List<List<float>>>(frames);
                    int ptr = 0;
                    for (int frameIndex = 0; frameIndex < frames; frameIndex++)
                    {
                        var frame = new List<List<float>>(joints);
                        for (int jointIndex = 0; jointIndex < joints; jointIndex++)
                        {
                            frame.Add(new List<float>
                            {
                                flatValues[ptr],
                                flatValues[ptr + 1],
                                flatValues[ptr + 2]
                            });
                            ptr += 3;
                        }

                        data.positions.Add(frame);
                    }

                    return data;
                }
            }

            return data;
        }

        private static bool ValidateData(MotionJsonData data, out string error)
        {
            error = string.Empty;
            if (data == null)
            {
                error = "Parsed motion data is null.";
                return false;
            }

            if ((data.positions == null || data.positions.Count == 0) &&
                (data.local_rot_quats == null || data.local_rot_quats.Count == 0))
            {
                error = "No positions or local_rot_quats in motion data.";
                return false;
            }

            if (data.joint_names == null || data.joint_names.Length == 0)
            {
                error = "No joint_names in motion data.";
                return false;
            }

            int positionFrames = data.positions != null ? data.positions.Count : 0;
            int frameHint = data.num_frames > 0 ? data.num_frames : positionFrames;
            if (frameHint < 2)
            {
                error = "Need at least 2 frames in motion data.";
                return false;
            }

            return true;
        }

        private static bool TryResolvePlaybackTargets(
            KimodoRawMotionData motion,
            string modelName,
            Transform profileSkeletonRoot,
            bool allowPartialJoints,
            out Transform[] joints,
            out int[] motionJointIndices,
            out string error)
        {
            joints = Array.Empty<Transform>();
            motionJointIndices = Array.Empty<int>();
            error = string.Empty;
            if (motion == null)
            {
                error = "Motion data is null.";
                return false;
            }

            if (!KimodoProfileSkeletonUtility.TryResolveProfileSkeleton(
                    modelName,
                    profileSkeletonRoot,
                    out string[] profileJointNames,
                    out _,
                    out joints,
                    out error))
            {
                return false;
            }

            return TryResolveMotionJointIndices(motion, profileJointNames, allowPartialJoints, out motionJointIndices, out error);
        }

        private static bool TryResolveMotionJointIndices(
            KimodoRawMotionData motion,
            string[] profileJointNames,
            bool allowPartialJoints,
            out int[] motionJointIndices,
            out string error)
        {
            motionJointIndices = Array.Empty<int>();
            error = string.Empty;
            if (motion == null)
            {
                error = "Motion data is null.";
                return false;
            }

            if (profileJointNames == null || profileJointNames.Length == 0)
            {
                error = "Profile joint names are empty.";
                return false;
            }

            var sourceByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < motion.jointNames.Length; i++)
            {
                AddJointLookup(sourceByName, motion.jointNames[i], i);
                AddJointLookup(sourceByName, KimodoRuntimeUtility.SanitizeName(motion.jointNames[i]), i);
            }

            motionJointIndices = new int[profileJointNames.Length];
            for (int i = 0; i < motionJointIndices.Length; i++)
            {
                motionJointIndices[i] = -1;
            }

            var missing = new List<string>();
            bool sameJointCount = motion.JointCount == profileJointNames.Length;
            for (int i = 0; i < profileJointNames.Length; i++)
            {
                string profileName = profileJointNames[i];
                if (!string.IsNullOrWhiteSpace(profileName) &&
                    sourceByName.TryGetValue(profileName, out int sourceIndex))
                {
                    motionJointIndices[i] = sourceIndex;
                    continue;
                }

                if (sameJointCount && i < motion.JointCount)
                {
                    motionJointIndices[i] = i;
                    continue;
                }

                missing.Add(profileName ?? $"Joint_{i}");
            }

            if (!allowPartialJoints && missing.Count > 0)
            {
                error = $"Motion json is missing profile joints for '{string.Join("', '", missing)}'.";
                motionJointIndices = Array.Empty<int>();
                return false;
            }

            return true;
        }

        private static void AddJointLookup(Dictionary<string, int> lookup, string name, int index)
        {
            if (lookup == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string key = name.Trim();
            if (!lookup.ContainsKey(key))
            {
                lookup[key] = index;
            }
        }

        private static void ResolveSampleFrames(KimodoRawMotionData motion, float timeSeconds, bool loop, out int frame0, out int frame1, out float blend)
        {
            float sampleTime = Mathf.Max(0f, timeSeconds);
            if (loop && motion.DurationSeconds > 1e-6f)
            {
                sampleTime = Mathf.Repeat(sampleTime, motion.DurationSeconds);
            }
            else
            {
                sampleTime = Mathf.Min(sampleTime, motion.LastFrameTimeSeconds);
            }

            float frameFloat = sampleTime * motion.FrameRate;
            frame0 = Mathf.Clamp(Mathf.FloorToInt(frameFloat), 0, Mathf.Max(0, motion.FrameCount - 1));
            frame1 = Mathf.Clamp(frame0 + 1, 0, Mathf.Max(0, motion.FrameCount - 1));
            blend = Mathf.Clamp01(frameFloat - frame0);
        }

        private static int ResolveRotationJointCount(KimodoRawMotionData motion)
        {
            if (motion == null || motion.localRotQuats == null || motion.FrameCount <= 0)
            {
                return 0;
            }

            return Mathf.Min(motion.JointCount, motion.localRotQuats.Count / (motion.FrameCount * 4));
        }

        private static int ResolveRotationJointCount(MotionJsonData data, int frameCount, int jointCount)
        {
            if (data == null || data.local_rot_quats == null || frameCount <= 0)
            {
                return 0;
            }

            return Mathf.Min(jointCount, data.local_rot_quats.Count / (frameCount * 4));
        }

        private static int FindRootJointIndex(MotionJsonData data, int jointCount)
        {
            if (jointCount <= 0)
            {
                return 0;
            }

            if (data.joint_parents != null && data.joint_parents.Length >= jointCount)
            {
                for (int i = 0; i < jointCount; i++)
                {
                    if (data.joint_parents[i] < 0)
                    {
                        return i;
                    }
                }
            }

            return 0;
        }
    }
}
