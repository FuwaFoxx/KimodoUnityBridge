using UnityEngine;

namespace KimodoBridge
{
    public sealed class BoneSample
    {
        public string[] boneNames;
        public Vector3[] localPositions;
        public Quaternion[] localRotations;

        public bool IsValid =>
            boneNames != null &&
            localPositions != null &&
            localRotations != null &&
            boneNames.Length == localPositions.Length &&
            boneNames.Length == localRotations.Length;
    }

    public sealed class MuscleSample
    {
        public HumanPose pose;
        public Vector3 leftFootPosition;
        public Quaternion leftFootRotation;
        public Vector3 rightFootPosition;
        public Quaternion rightFootRotation;
        public Vector3 leftHandPosition;
        public Quaternion leftHandRotation;
        public Vector3 rightHandPosition;
        public Quaternion rightHandRotation;
    }

    public sealed class MuscleClipCache
    {
        public AnimationClip sourceClip;
        public Avatar sourceAvatar;
        public float frameRate;
        public float duration;
        public MuscleSample[] samples;
        public AnimationClip muscleClip;

        public bool IsReady =>
            sourceClip != null &&
            sourceAvatar != null &&
            frameRate > 0f &&
            duration >= 0f &&
            samples != null &&
            samples.Length > 0 &&
            muscleClip != null;
    }

    public sealed class SkeletonCache
    {
        public Avatar avatar;
        public GameObject root;
        public Transform skeletonRoot;
        public string canonicalRootBoneName;
        public Animator animator;
        public HumanPoseHandler poseHandler;
        public float humanScale;
        public string[] bonePaths;
        public Transform[] boneTransforms;
        public int boneCount;

        public bool IsReady =>
            root != null &&
            skeletonRoot != null &&
            animator != null &&
            poseHandler != null &&
            bonePaths != null &&
            boneTransforms != null &&
            bonePaths.Length == boneTransforms.Length;
    }
}
