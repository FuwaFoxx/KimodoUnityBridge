using System;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal static class FootRootMotionSamplingUtility
    {
        public static bool TrySampleClip(
            AnimationClip clip,
            GameObject prefab,
            FootRootMotionSolverSettings settings,
            out FootRootMotionFrame[] frames,
            out string error)
        {
            frames = null;
            error = string.Empty;

            if (clip == null)
            {
                error = "AnimationClip is null.";
                return false;
            }

            if (clip.length <= 0f)
            {
                error = "AnimationClip length must be greater than zero.";
                return false;
            }

            if (prefab == null)
            {
                error = "Humanoid prefab is null.";
                return false;
            }

            if (!KimodoLocalAvatarUtility.TryEnsureHumanoidAvatar(prefab, out Avatar avatar, out _, out error))
            {
                return false;
            }

            SkeletonCache cache = null;
            KimodoRetargetClipSamplingUtility.ClipSamplingContext context = null;
            try
            {
                if (!KimodoRetargetAvatarUtility.TryBuildSkeletonCache(
                        avatar,
                        prefab.name + "_FootRootMotionSampler",
                        out cache,
                        out error))
                {
                    return false;
                }

                KimodoRetargetClipSamplingUtility.ClipSamplingMode samplingMode =
                    KimodoRetargetClipSamplingUtility.ResolveClipSamplingMode(clip);
                if (!KimodoRetargetClipSamplingUtility.TryBuildClipSamplingContext(
                        clip,
                        cache,
                        prefab.name + "_FootRootMotionContext",
                        samplingMode,
                        out context,
                        out error))
                {
                    return false;
                }

                Transform leftFoot = KimodoRetargetHumanoidIkUtility.ResolveHumanBoneTransform(cache, HumanBodyBones.LeftFoot);
                Transform rightFoot = KimodoRetargetHumanoidIkUtility.ResolveHumanBoneTransform(cache, HumanBodyBones.RightFoot);
                Transform hips = KimodoRetargetHumanoidIkUtility.ResolveHumanBoneTransform(cache, HumanBodyBones.Hips);
                if (leftFoot == null || rightFoot == null || hips == null)
                {
                    error = "Humanoid avatar is missing LeftFoot, RightFoot, or Hips bone mapping.";
                    return false;
                }

                float fps = settings != null && settings.sampleRate > 0f
                    ? settings.sampleRate
                    : KimodoRetargetClipSamplingUtility.ResolveFrameRate(clip);
                fps = Mathf.Max(1f, fps);

                int frameCount = KimodoRetargetSamplingUtility.ResolveFrameCount(clip.length, fps);
                frames = new FootRootMotionFrame[frameCount];

                for (int i = 0; i < frameCount; i++)
                {
                    float time = Mathf.Min(clip.length, i / fps);
                    if (!KimodoRetargetClipSamplingUtility.TryEvaluateClipSamplingContext(context, time, out error))
                    {
                        frames = null;
                        return false;
                    }

                    MuscleSample muscleSample = null;
                    if (!KimodoRetargetSamplingUtility.TryCaptureMuscleSample(cache, out muscleSample, out _))
                    {
                        muscleSample = null;
                    }

                    if (!TryBuildFrameSample(
                            cache,
                            muscleSample,
                            hips,
                            time,
                            out frames[i],
                            out error))
                    {
                        frames = null;
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                frames = null;
                error = "Sampling failed: " + ex.Message;
                return false;
            }
            finally
            {
                if (context != null)
                {
                    KimodoRetargetClipSamplingUtility.DestroyClipSamplingContext(context);
                }

                cache?.Dispose();
            }
        }

        private static bool TryBuildFrameSample(
            SkeletonCache cache,
            MuscleSample muscleSample,
            Transform hips,
            float time,
            out FootRootMotionFrame frame,
            out string error)
        {
            frame = default(FootRootMotionFrame);
            error = string.Empty;

            if (cache == null)
            {
                error = "Skeleton cache is null.";
                return false;
            }

            if (muscleSample == null)
            {
                error = "Muscle sample is null.";
                return false;
            }

            HumanPose pose = muscleSample.pose;
            KimodoRetargetClipWriter.EnsureHumanPoseMuscles(ref pose);

            float humanScale = Mathf.Max(1e-6f, cache.humanScale);
            Vector3 rootPosition = pose.bodyPosition;
            Quaternion rootRotation = pose.bodyRotation;
            float rootYawRadians = FootRootMotionMathUtility.ExtractYawRadians(rootRotation);
            Quaternion rootYawRotation = Quaternion.AngleAxis(rootYawRadians * Mathf.Rad2Deg, Vector3.up);
            Quaternion inverseRootYawRotation = Quaternion.Inverse(rootYawRotation);

            Vector3 leftFootWorld = rootPosition + rootRotation * muscleSample.leftFootPosition;
            Quaternion leftFootWorldRotation = rootRotation * muscleSample.leftFootRotation;
            Vector3 rightFootWorld = rootPosition + rootRotation * muscleSample.rightFootPosition;
            Quaternion rightFootWorldRotation = rootRotation * muscleSample.rightFootRotation;

            Vector3 leftFootLocal = inverseRootYawRotation * (leftFootWorld - rootPosition);
            float leftFootLocalYawRadians = FootRootMotionMathUtility.DeltaYawRadians(
                rootYawRadians,
                FootRootMotionMathUtility.ExtractYawRadians(leftFootWorldRotation));
            Vector3 rightFootLocal = inverseRootYawRotation * (rightFootWorld - rootPosition);
            float rightFootLocalYawRadians = FootRootMotionMathUtility.DeltaYawRadians(
                rootYawRadians,
                FootRootMotionMathUtility.ExtractYawRadians(rightFootWorldRotation));

            Vector3 hipWorld = rootPosition;
            float hipLocalYawRadians = 0f;
            Vector3 hipLocal = Vector3.zero;
            if (hips != null)
            {
                hipWorld = hips.position / humanScale;
                hipLocal = inverseRootYawRotation * (hipWorld - rootPosition);
                hipLocalYawRadians = FootRootMotionMathUtility.DeltaYawRadians(
                    rootYawRadians,
                    FootRootMotionMathUtility.ExtractYawRadians(hips.rotation));
            }

            frame = new FootRootMotionFrame
            {
                time = time,
                muscleSample = muscleSample,
                sampledRootWorld = rootPosition,
                sampledRootRotation = rootRotation,
                hipWorld = hipWorld,
                leftFootWorld = leftFootWorld,
                leftFootWorldRotation = leftFootWorldRotation,
                rightFootWorld = rightFootWorld,
                rightFootWorldRotation = rightFootWorldRotation,
                hipLocal = hipLocal,
                hipLocalYawRadians = hipLocalYawRadians,
                leftFootLocal = leftFootLocal,
                leftFootLocalYawRadians = leftFootLocalYawRadians,
                rightFootLocal = rightFootLocal,
                rightFootLocalYawRadians = rightFootLocalYawRadians
            };
            return true;
        }
    }
}
