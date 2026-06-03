using UnityEngine.Timeline;
using TimelineInject;

namespace KimodoBridge
{
    public static class KimodoRigProfileDatabase
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

        public static KimodoConstraintRigType ResolveRigTypeFromModelName(string modelName)
        {
            string normalized = NormalizeModelName(modelName);
            if (normalized.Contains("g1"))
            {
                return KimodoConstraintRigType.G1;
            }

            if (normalized.Contains("smplx"))
            {
                return KimodoConstraintRigType.Smplx;
            }

            return KimodoConstraintRigType.Soma30;
        }

        public static string[] GetJointNamesForModel(string modelName)
        {
            ResolveProfile(modelName, out _, out string[] jointNames, out _);
            return CloneStrings(jointNames);
        }

        public static int[] GetParentIndicesForModel(string modelName)
        {
            ResolveProfile(modelName, out _, out _, out int[] parentIndices);
            return CloneInts(parentIndices);
        }

        public static string GetRootJointNameForModel(string modelName)
        {
            ResolveProfile(modelName, out _, out string[] jointNames, out _);
            return jointNames != null && jointNames.Length > 0 ? jointNames[0] : string.Empty;
        }

        public static void ResolveProfile(
            string modelName,
            out KimodoConstraintRigType rigType,
            out string[] jointNames,
            out int[] parentIndices)
        {
            rigType = ResolveRigTypeFromModelName(modelName);
            switch (rigType)
            {
                case KimodoConstraintRigType.G1:
                    jointNames = G1Skel34Names;
                    parentIndices = G1Skel34Parents;
                    return;
                case KimodoConstraintRigType.Smplx:
                    jointNames = Smplx22Names;
                    parentIndices = Smplx22Parents;
                    return;
                case KimodoConstraintRigType.Soma30:
                default:
                    jointNames = Soma30Names;
                    parentIndices = Soma30Parents;
                    return;
            }
        }

        private static string NormalizeModelName(string modelName)
        {
            return string.IsNullOrWhiteSpace(modelName) ? string.Empty : modelName.Trim().ToLowerInvariant();
        }

        private static string[] CloneStrings(string[] source)
        {
            if (source == null || source.Length == 0)
            {
                return System.Array.Empty<string>();
            }

            return (string[])source.Clone();
        }

        private static int[] CloneInts(int[] source)
        {
            if (source == null || source.Length == 0)
            {
                return System.Array.Empty<int>();
            }

            return (int[])source.Clone();
        }
    }
}
