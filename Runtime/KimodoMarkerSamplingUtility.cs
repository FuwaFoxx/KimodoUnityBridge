using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools
{
    public static class KimodoMarkerSamplingUtility
    {
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

        private static readonly string[] G1Skel34Names =
        {
            "pelvis_skel",
            "left_hip_pitch_skel", "left_hip_roll_skel", "left_hip_yaw_skel", "left_knee_skel", "left_ankle_pitch_skel", "left_ankle_roll_skel", "left_toe_base",
            "right_hip_pitch_skel", "right_hip_roll_skel", "right_hip_yaw_skel", "right_knee_skel", "right_ankle_pitch_skel", "right_ankle_roll_skel", "right_toe_base",
            "waist_yaw_skel", "waist_roll_skel", "waist_pitch_skel",
            "left_shoulder_pitch_skel", "left_shoulder_roll_skel", "left_shoulder_yaw_skel", "left_elbow_skel", "left_wrist_roll_skel", "left_wrist_pitch_skel", "left_wrist_yaw_skel", "left_hand_roll_skel",
            "right_shoulder_pitch_skel", "right_shoulder_roll_skel", "right_shoulder_yaw_skel", "right_elbow_skel", "right_wrist_roll_skel", "right_wrist_pitch_skel", "right_wrist_yaw_skel", "right_hand_roll_skel"
        };

        private static readonly int[] G1Skel34Parents =
        {
            -1,
            0, 1, 2, 3, 4, 5, 6,
            0, 8, 9, 10, 11, 12, 13,
            0, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24,
            17, 26, 27, 28, 29, 30, 31, 32
        };

        private static readonly string[] Smplx22Names =
        {
            "pelvis",
            "left_hip", "right_hip", "spine1",
            "left_knee", "right_knee", "spine2",
            "left_ankle", "right_ankle", "spine3",
            "left_foot", "right_foot",
            "neck", "left_collar", "right_collar",
            "head", "left_shoulder", "right_shoulder",
            "left_elbow", "right_elbow",
            "left_wrist", "right_wrist"
        };

        private static readonly int[] Smplx22Parents =
        {
            -1,
            0, 0, 0,
            1, 2, 3,
            4, 5, 6,
            7, 8,
            9, 9, 9,
            12, 13, 14,
            16, 17,
            18, 19
        };

        private enum SkeletonProfile
        {
            Soma30 = 0,
            G1Skel34 = 1,
            Smplx22 = 2
        }

        public static string[] GetJointNamesForModel(string modelName)
        {
            ResolveProfile(modelName, out string[] names, out _);
            return names;
        }

        public static string GetRootJointNameForModel(string modelName)
        {
            ResolveProfile(modelName, out string[] names, out _);
            return names != null && names.Length > 0 ? names[0] : string.Empty;
        }

        public static bool TrySampleMarker(
            Animator animator,
            Transform skeletonRoot,
            TimelineClip sourceClip,
            string modelName,
            double globalTime,
            string markerType,
            out KimodoMarkerSampleResult result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (animator == null)
            {
                error = "Animator is null.";
                return false;
            }

            Transform root = skeletonRoot != null ? skeletonRoot : animator.transform;
            if (root == null)
            {
                error = "Skeleton root is null.";
                return false;
            }

            ResolveProfile(modelName, out string[] jointNames, out int[] parentIndices);
            string rootJointName = jointNames != null && jointNames.Length > 0 ? jointNames[0] : "Hips";
            Transform pelvis = TryResolveTransformByJointName(rootJointName, root, animator) ?? root;

            Vector3 unityRootPosition = pelvis.position;

            Vector3 forward = pelvis.forward;
            Vector2 unityHeading = new Vector2(forward.x, forward.z);
            if (unityHeading.sqrMagnitude <= 1e-8f)
            {
                unityHeading = new Vector2(1f, 0f);
            }
            else
            {
                unityHeading.Normalize();
            }

            Transform[] joints = ResolveJointTransforms(jointNames, root, animator);
            Quaternion[] worldRots = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                worldRots[i] = joints[i] != null ? joints[i].rotation : Quaternion.identity;
            }

            var unityLocalAxisAngles = new List<Vector3>(joints.Length);
            var sampledJointIndices = new List<int>(joints.Length);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                {
                    unityLocalAxisAngles.Add(Vector3.zero);
                    continue;
                }

                int parent = parentIndices[i];
                if (parent >= 0 && (parent >= joints.Length || joints[parent] == null))
                {
                    // Parent unresolved for this profile slot; skip this joint to avoid invalid local rotation.
                    unityLocalAxisAngles.Add(Vector3.zero);
                    continue;
                }

                Quaternion local = parent >= 0 && parent < worldRots.Length
                    ? Quaternion.Inverse(worldRots[parent]) * worldRots[i]
                    : worldRots[i];
                unityLocalAxisAngles.Add(KimodoRuntimeUtility.QuaternionToAxisAngleVector(local));
                sampledJointIndices.Add(i);
            }

            result = new KimodoMarkerSampleResult
            {
                constraintType = markerType ?? string.Empty,
                sampleTime = globalTime,
                rigType = ToConstraintRigType(ResolveProfileType(modelName)),
                hasRootHeading = true,
                rootPosition = unityRootPosition,
                rootHeading = unityHeading,
                jointNames = jointNames != null ? new List<string>(jointNames) : new List<string>(),
                localAxisAngles = unityLocalAxisAngles,
                sampledJointIndices = sampledJointIndices
            };
            return true;
        }

        private static KimodoConstraintRigType ToConstraintRigType(SkeletonProfile profile)
        {
            switch (profile)
            {
                case SkeletonProfile.G1Skel34:
                    return KimodoConstraintRigType.G1;
                case SkeletonProfile.Smplx22:
                    return KimodoConstraintRigType.Smplx;
                case SkeletonProfile.Soma30:
                default:
                    return KimodoConstraintRigType.Soma30;
            }
        }

        private static void ResolveProfile(string modelName, out string[] jointNames, out int[] parentIndices)
        {
            SkeletonProfile profile = ResolveProfileType(modelName);
            switch (profile)
            {
                case SkeletonProfile.G1Skel34:
                    jointNames = G1Skel34Names;
                    parentIndices = G1Skel34Parents;
                    return;
                case SkeletonProfile.Smplx22:
                    jointNames = Smplx22Names;
                    parentIndices = Smplx22Parents;
                    return;
                case SkeletonProfile.Soma30:
                default:
                    jointNames = Soma30Names;
                    parentIndices = Soma30Parents;
                    return;
            }
        }

        private static SkeletonProfile ResolveProfileType(string modelName)
        {
            string normalized = string.IsNullOrWhiteSpace(modelName) ? string.Empty : modelName.Trim().ToLowerInvariant();
            if (normalized.Contains("g1"))
            {
                return SkeletonProfile.G1Skel34;
            }

            if (normalized.Contains("smplx"))
            {
                return SkeletonProfile.Smplx22;
            }

            return SkeletonProfile.Soma30;
        }

        private static Transform[] ResolveJointTransforms(string[] names, Transform root, Animator animator)
        {
            int count = names != null ? names.Length : 0;
            var transforms = new Transform[count];
            if (root == null || count == 0)
            {
                return transforms;
            }

            for (int i = 0; i < count; i++)
            {
                string name = names[i];
                // Keep unresolved joints as null to avoid sampling wrong rotations from a fallback transform.
                transforms[i] = TryResolveTransformByJointName(name, root, animator);
            }

            return transforms;
        }

        private static Transform TryResolveTransformByJointName(string jointName, Transform searchRoot, Animator animator)
        {
            Transform byHuman = TryResolveViaHumanBone(jointName, animator);
            if (byHuman != null)
            {
                return byHuman;
            }

            return FindTransformByName(searchRoot, jointName);
        }

        private static Transform TryResolveViaHumanBone(string jointName, Animator animator)
        {
            if (animator == null || !animator.isHuman)
            {
                return null;
            }

            bool hasUpperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest) != null;
            switch (jointName)
            {
                // SOMA30 aliases
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

                // SMPLX22 aliases
                case "pelvis": return animator.GetBoneTransform(HumanBodyBones.Hips);
                case "spine1": return animator.GetBoneTransform(HumanBodyBones.Spine);
                case "spine2": return animator.GetBoneTransform(HumanBodyBones.Chest);
                case "spine3": return hasUpperChest
                    ? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    : animator.GetBoneTransform(HumanBodyBones.Chest);
                case "neck": return animator.GetBoneTransform(HumanBodyBones.Neck);
                case "head": return animator.GetBoneTransform(HumanBodyBones.Head);
                case "left_hip": return animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                case "left_knee": return animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                case "left_ankle": return animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                case "left_foot": return animator.GetBoneTransform(HumanBodyBones.LeftToes);
                case "right_hip": return animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                case "right_knee": return animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                case "right_ankle": return animator.GetBoneTransform(HumanBodyBones.RightFoot);
                case "right_foot": return animator.GetBoneTransform(HumanBodyBones.RightToes);
                case "left_collar": return animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
                case "left_shoulder": return animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                case "left_elbow": return animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                case "left_wrist": return animator.GetBoneTransform(HumanBodyBones.LeftHand);
                case "right_collar": return animator.GetBoneTransform(HumanBodyBones.RightShoulder);
                case "right_shoulder": return animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                case "right_elbow": return animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                case "right_wrist": return animator.GetBoneTransform(HumanBodyBones.RightHand);

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
                if (string.Equals(current.name, name, System.StringComparison.OrdinalIgnoreCase))
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

    }
}

