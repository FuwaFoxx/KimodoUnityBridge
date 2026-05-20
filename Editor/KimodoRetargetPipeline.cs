using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    public enum KimodoRetargetResultMode
    {
        SomaFallback = 0,
        HumanoidMuscle = 1,
        TargetBone = 2
    }

    public static class KimodoRetargetPipeline
    {
        public static bool TryRetargetBakedClip(
            KimodoPlayableClip playableClip,
            TimelineClip timelineClip,
            out KimodoRetargetResultMode mode,
            out string details)
        {
            mode = KimodoRetargetResultMode.SomaFallback;
            details = string.Empty;

            if (playableClip == null || playableClip.clip == null)
            {
                details = "PlayableClip or animation clip is null.";
                return false;
            }

            if (timelineClip == null)
            {
                details = "Timeline clip not found. Keep SOMA bake.";
                return false;
            }

            if (!TryResolveBoundAnimator(timelineClip, out Animator targetAnimator, out string bindError))
            {
                details = bindError;
                return false;
            }

            bool hadHumanoidAvatar = targetAnimator.avatar != null && targetAnimator.avatar.isValid && targetAnimator.avatar.isHuman;

            if (!KimodoLocalAvatarUtility.TryEnsureHumanoidAvatar(
                    targetAnimator,
                    out Avatar ensuredAvatar,
                    out string avatarSource,
                    out string avatarError))
            {
                details = $"Ensure target avatar failed: {avatarError}";
                return false;
            }

            if (!TryCreateSomaSamplingAnimator(out Animator somaAnimator, out GameObject somaTempRoot, out string somaError))
            {
                details = $"Ensure SOMA avatar failed: {somaError}";
                return false;
            }

            try
            {
                AnimationClip sourceSomaBoneClip = playableClip.clip;

                AnimationClip muscleClip = new AnimationClip
                {
                    name = $"{sourceSomaBoneClip.name}_Muscle",
                    legacy = false,
                    frameRate = sourceSomaBoneClip.frameRate > 0f ? sourceSomaBoneClip.frameRate : 30f
                };

                if (!SOMA2Avatar.BoneClipToMuscleClip(
                        sourceSomaBoneClip,
                        somaAnimator,
                        muscleClip,
                        out string toMuscleError,
                        sourceSomaBoneClip.frameRate))
                {
                    details = $"SOMA->Muscle failed: {toMuscleError}";
                    return false;
                }

                // Keep muscle clip only when binding object already had humanoid avatar.
                if (hadHumanoidAvatar)
                {
                    OverwriteClipCurves(playableClip.clip, muscleClip);
                    mode = KimodoRetargetResultMode.HumanoidMuscle;
                    details = $"Retarget ok (Avatar={avatarSource}, Mode=HumanoidMuscle).";
                    return true;
                }

                // Fallback path: convert muscle back to target skeleton bone clip.
                AnimationClip targetBoneClip = new AnimationClip
                {
                    name = $"{sourceSomaBoneClip.name}_TargetBone",
                    legacy = false,
                    frameRate = sourceSomaBoneClip.frameRate > 0f ? sourceSomaBoneClip.frameRate : 30f
                };

                Animator targetSamplingAnimator = CreateTempAnimatorForAvatar(targetAnimator, ensuredAvatar, out GameObject targetTempRoot);
                if (targetSamplingAnimator == null)
                {
                    details = "Failed to create target sampling animator.";
                    return false;
                }

                try
                {
                    if (!SOMA2Avatar.MuscleClipToSomaBoneClip(
                            muscleClip,
                            targetSamplingAnimator,
                            targetBoneClip,
                            out string toBoneError,
                            sourceSomaBoneClip.frameRate))
                    {
                        details = $"Muscle->TargetBone failed: {toBoneError}";
                        return false;
                    }

                    OverwriteClipCurves(playableClip.clip, targetBoneClip);
                    mode = KimodoRetargetResultMode.TargetBone;
                    details = $"Retarget ok (Avatar={avatarSource}, Mode=TargetBone).";
                    return true;
                }
                finally
                {
                    if (targetTempRoot != null)
                    {
                        UnityEngine.Object.DestroyImmediate(targetTempRoot);
                    }
                }
            }
            catch (Exception e)
            {
                details = $"Retarget exception: {e.Message}";
                return false;
            }
            finally
            {
                if (somaTempRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(somaTempRoot);
                }
            }
        }

        public static bool TryConvertPoseToSomaSpace(
            Animator targetAnimator,
            Transform skeletonRoot,
            out Vector3 rootPositionSoma,
            out Vector2 rootHeadingSoma,
            out List<Vector3> somaLocalAxisAngles,
            out string error)
        {
            rootPositionSoma = Vector3.zero;
            rootHeadingSoma = new Vector2(1f, 0f);
            somaLocalAxisAngles = new List<Vector3>();
            error = string.Empty;

            if (targetAnimator == null || skeletonRoot == null)
            {
                error = "Animator or skeleton root is null.";
                return false;
            }

            // Keep backward compatibility: if no human avatar available, keep old direct sampling.
            if (!KimodoLocalAvatarUtility.TryEnsureHumanoidAvatar(targetAnimator, out Avatar targetAvatar, out _, out _)
                || targetAvatar == null || !targetAvatar.isValid || !targetAvatar.isHuman)
            {
                return false;
            }

            if (!TryCreateSomaSamplingAnimator(out Animator somaAnimator, out GameObject somaTempRoot, out string somaError))
            {
                error = somaError;
                return false;
            }

            GameObject targetTempRoot = null;
            try
            {
                Animator targetTempAnimator = CreateTempAnimatorForAvatar(targetAnimator, targetAvatar, out targetTempRoot, keepCurrentPose: true);
                if (targetTempAnimator == null)
                {
                    error = "Failed to create target temp animator.";
                    return false;
                }

                HumanPoseHandler targetPoseHandler = new HumanPoseHandler(targetTempAnimator.avatar, targetTempAnimator.transform);
                HumanPoseHandler somaPoseHandler = new HumanPoseHandler(somaAnimator.avatar, somaAnimator.transform);

                HumanPose pose = new HumanPose();
                targetPoseHandler.GetHumanPose(ref pose);
                somaPoseHandler.SetHumanPose(ref pose);

                Transform somaRoot = somaAnimator.transform;
                Transform pelvis = FindTransformByName(somaRoot, "Hips") ?? somaRoot;
                Vector3 worldPos = pelvis.position;
                rootPositionSoma = new Vector3(-worldPos.x, worldPos.y, worldPos.z);

                Vector3 forward = pelvis.forward;
                Vector2 heading = new Vector2(-forward.x, forward.z);
                if (heading.sqrMagnitude <= 1e-8f)
                {
                    heading = new Vector2(1f, 0f);
                }
                else
                {
                    heading.Normalize();
                }
                rootHeadingSoma = heading;

                Transform[] somaJoints = ResolveSoma30Joints(somaRoot);
                Quaternion[] worldRots = new Quaternion[somaJoints.Length];
                for (int i = 0; i < somaJoints.Length; i++)
                {
                    worldRots[i] = somaJoints[i] != null ? somaJoints[i].rotation : Quaternion.identity;
                }

                for (int i = 0; i < somaJoints.Length; i++)
                {
                    int parent = Soma30Parents[i];
                    Quaternion local = (parent >= 0 && parent < worldRots.Length)
                        ? Quaternion.Inverse(worldRots[parent]) * worldRots[i]
                        : worldRots[i];

                    Quaternion q = new Quaternion(local.x, -local.y, -local.z, local.w);
                    somaLocalAxisAngles.Add(QuaternionToAxisAngleVector(q));
                }

                return true;
            }
            catch (Exception e)
            {
                error = $"Pose convert failed: {e.Message}";
                return false;
            }
            finally
            {
                if (targetTempRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(targetTempRoot);
                }

                if (somaTempRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(somaTempRoot);
                }
            }
        }

        private static bool TryResolveBoundAnimator(TimelineClip timelineClip, out Animator animator, out string error)
        {
            animator = null;
            error = string.Empty;

            if (timelineClip == null)
            {
                error = "Timeline clip is null.";
                return false;
            }

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
            {
                error = "Timeline parent track not found.";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null.";
                return false;
            }

            animator = director.GetGenericBinding(track) as Animator;
            if (animator == null)
            {
                error = "Animation track has no Animator binding.";
                return false;
            }

            return true;
        }

        private static bool TryCreateSomaSamplingAnimator(out Animator animator, out GameObject tempRoot, out string error)
        {
            animator = null;
            tempRoot = null;
            error = string.Empty;

            GameObject prefab = Resources.Load<GameObject>("SOMA_somaskel77_neutral");
            if (prefab == null)
            {
                error = "Resources/SOMA_somaskel77_neutral not found.";
                return false;
            }

            tempRoot = UnityEngine.Object.Instantiate(prefab);
            tempRoot.hideFlags = HideFlags.HideAndDontSave;
            tempRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tempRoot.transform.localScale = Vector3.one;

            animator = tempRoot.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                error = "SOMA prefab missing valid humanoid animator/avatar.";
                return false;
            }

            animator.enabled = false;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            return true;
        }

        private static Animator CreateTempAnimatorForAvatar(
            Animator sourceAnimator,
            Avatar avatar,
            out GameObject tempRoot,
            bool keepCurrentPose = false)
        {
            tempRoot = null;
            if (sourceAnimator == null)
            {
                return null;
            }

            tempRoot = UnityEngine.Object.Instantiate(sourceAnimator.gameObject);
            tempRoot.hideFlags = HideFlags.HideAndDontSave;
            tempRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tempRoot.transform.localScale = Vector3.one;

            Animator tempAnimator = tempRoot.GetComponent<Animator>();
            if (tempAnimator == null)
            {
                return null;
            }

            tempAnimator.avatar = avatar;
            tempAnimator.enabled = false;
            tempAnimator.applyRootMotion = false;
            if (!keepCurrentPose)
            {
                tempAnimator.Rebind();
                tempAnimator.Update(0f);
            }
            return tempAnimator;
        }

        private static void OverwriteClipCurves(AnimationClip dst, AnimationClip src)
        {
            dst.ClearCurves();

            EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(src);
            for (int i = 0; i < floatBindings.Length; i++)
            {
                EditorCurveBinding b = floatBindings[i];
                AnimationCurve c = AnimationUtility.GetEditorCurve(src, b);
                dst.SetCurve(b.path, b.type, b.propertyName, c);
            }

            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(src);
            for (int i = 0; i < objectBindings.Length; i++)
            {
                EditorCurveBinding b = objectBindings[i];
                ObjectReferenceKeyframe[] k = AnimationUtility.GetObjectReferenceCurve(src, b);
                AnimationUtility.SetObjectReferenceCurve(dst, b, k);
            }

            dst.frameRate = src.frameRate;
            dst.legacy = false;
            dst.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(dst);
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

        private static Transform[] ResolveSoma30Joints(Transform root)
        {
            var joints = new Transform[Soma30Names.Length];
            for (int i = 0; i < Soma30Names.Length; i++)
            {
                joints[i] = FindTransformByName(root, Soma30Names[i]) ?? root;
            }
            return joints;
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
    }
}
