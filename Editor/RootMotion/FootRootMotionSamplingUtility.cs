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

            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.name = prefab.name + "_FootRootMotionSampler";

                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = instance.AddComponent<Animator>();
                }

                if (!KimodoRetargetCoreUtility.IsValidHumanoid(animator.avatar))
                {
                    if (!KimodoLocalAvatarUtility.TryEnsureHumanoidAvatar(
                            instance,
                            out Avatar avatar,
                            out _,
                            out error))
                    {
                        return false;
                    }

                    animator.avatar = avatar;
                }

                animator.applyRootMotion = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);

                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (leftFoot == null || rightFoot == null || hips == null)
                {
                    error = "Humanoid avatar is missing LeftFoot, RightFoot, or Hips bone mapping.";
                    return false;
                }

                float fps = settings != null && settings.sampleRate > 0f
                    ? settings.sampleRate
                    : (clip.frameRate > 0f ? clip.frameRate : 60f);
                fps = Mathf.Max(1f, fps);

                int frameCount = Mathf.Max(2, Mathf.CeilToInt(clip.length * fps) + 1);
                frames = new FootRootMotionFrame[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    float t = Mathf.Min(clip.length, i / fps);
                    clip.SampleAnimation(instance, t);

                    Vector3 hipsForward = Vector3.ProjectOnPlane(hips.rotation * Vector3.forward, Vector3.up);
                    if (hipsForward.sqrMagnitude < 1e-6f)
                    {
                        hipsForward = Vector3.forward;
                    }

                frames[i] = new FootRootMotionFrame
                {
                    time = t,
                    leftFootWorld = leftFoot.position,
                    leftFootWorldRotation = leftFoot.rotation,
                    rightFootWorld = rightFoot.position,
                    rightFootWorldRotation = rightFoot.rotation,
                    hipWorld = hips.position,
                    rootYawRadians = Mathf.Atan2(hipsForward.x, hipsForward.z),
                    sampledRootWorld = instance.transform.position,
                    sampledRootRotation = instance.transform.rotation
                };
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
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }
    }
}
