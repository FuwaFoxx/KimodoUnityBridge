using KimodoUnityMotionTools.Generation.Pipeline;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KimodoUnityMotionTools
{
    public static class KimodoRetargetTools
    {
        public enum SourcePoseKind
        {
            BoneClip = 0,
            MuscleClip = 1
        }

        public sealed class BoneFrame
        {
            public string[] boneNames;
            public Vector3[] localPositions;
            public Quaternion[] localRotations;
        }

        public sealed class RetargetResult
        {
            public string[] boneNames;
            public BoneFrame[] frames;
            public HumanPose[] poses;
            public MuscleFrameData[] muscleFrames;
            public Vector3 rootPosition;
            public Quaternion rootRotation;
        }

        public sealed class MuscleFrameData
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

        public sealed class FrameData
        {
            public float sampleTime;
            public SourcePoseKind sourceKind;
            public BoneFrame sourceBoneFrame;
            public HumanPose sourcePose;
            public HumanPose originBonePose;
            public HumanPose originMusclePose;
            public HumanPose targetMusclePose;
            public HumanPose targetBonePose;
            public BoneFrame originBoneFrame;
            public BoneFrame originMuscleFrame;
            public BoneFrame targetMuscleFrame;
            public BoneFrame targetBoneFrame;
            public MuscleFrameData targetMuscleFrameData;
            public MuscleFrameData targetBoneFrameData;
        }

        public sealed class RetargetFrameContext
        {
            public Avatar originAvatar;
            public Avatar targetAvatar;
            public bool isMuscleClip;
            public KimodoRetargetStageCache originBone;
            public KimodoRetargetStageCache originMuscle;
            public KimodoRetargetStageCache targetMuscle;
            public KimodoRetargetStageCache targetBone;
        }

        public sealed class KimodoRetargetStageCache
        {
            public Avatar avatar;
            public GameObject root;
            public Transform skeletonRoot;
            public string canonicalRootBoneName;
            public Animator animator;
            public HumanPoseHandler poseHandler;
            public string[] bonePaths;
            public Transform[] boneTransforms;
            public HumanPose pose;
            public BoneFrame boneFrame;
        }

        public static bool TryCreateRetargetFrameContext(
            Avatar originAvatar,
            Avatar targetAvatar,
            bool isMuscleClip,
            bool isExportMuscle,
            out RetargetFrameContext context,
            out string error)
        {
            context = null;
            error = string.Empty;

            if (!IsValidHumanoid(targetAvatar))
            {
                error = "Target avatar is null/invalid/non-humanoid.";
                return false;
            }

            if (!isMuscleClip && !IsValidHumanoid(originAvatar))
            {
                error = "Origin avatar is null/invalid/non-humanoid for bone clip retarget.";
                return false;
            }

            context = new RetargetFrameContext
            {
                originAvatar = originAvatar,
                targetAvatar = targetAvatar,
                isMuscleClip = isMuscleClip
            };

            if (!isMuscleClip)
            {
                if (!TryBuildStage(originAvatar, "KimodoRetargetTools_OriginBone", out context.originBone, out error))
                {
                    TryDestroyRetargetFrameContext(context);
                    context = null;
                    return false;
                }

                if (!TryBuildStage(originAvatar, "KimodoRetargetTools_OriginMuscle", out context.originMuscle, out error))
                {
                    TryDestroyRetargetFrameContext(context);
                    context = null;
                    return false;
                }
            }

            if (!TryBuildStage(targetAvatar, "KimodoRetargetTools_TargetMuscle", out context.targetMuscle, out error))
            {
                TryDestroyRetargetFrameContext(context);
                context = null;
                return false;
            }

            if (!isExportMuscle &&
                !TryBuildStage(targetAvatar, "KimodoRetargetTools_TargetBone", out context.targetBone, out error))
            {
                TryDestroyRetargetFrameContext(context);
                context = null;
                return false;
            }

            return true;
        }

        public static void TryDestroyRetargetFrameContext(RetargetFrameContext context)
        {
            if (context == null)
            {
                return;
            }

            DestroyStage(context.originBone);
            DestroyStage(context.originMuscle);
            DestroyStage(context.targetMuscle);
            DestroyStage(context.targetBone);
            context.originBone = null;
            context.originMuscle = null;
            context.targetMuscle = null;
            context.targetBone = null;
        }

        public static bool TryRetarget(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            Avatar targetAvatar,
            out RetargetResult result,
            out string error)
        {
            return TryRetarget(sourceClip, sourceAvatar, targetAvatar, false, out result, out error);
        }

        public static bool TryRetarget(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            Avatar targetAvatar,
            bool isExportMuscle,
            out RetargetResult result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (sourceClip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (!IsValidHumanoid(sourceAvatar))
            {
                error = "Source avatar is null/invalid/non-humanoid.";
                return false;
            }

            if (!IsValidHumanoid(targetAvatar))
            {
                error = "Target avatar is null/invalid/non-humanoid.";
                return false;
            }

            if (!TryCreateSourceClipSampleContext(sourceClip, sourceAvatar, out SourceClipSampleContext sourceSampleContext, out error))
            {
                return false;
            }

            try
            {
                return TryRetarget(
                    sourceSampleContext,
                    sourceAvatar,
                    targetAvatar,
                    sourceClip.isHumanMotion,
                    sourceClip.frameRate > 0f ? sourceClip.frameRate : 30f,
                    sourceClip.length,
                    isExportMuscle,
                    out result,
                    out error);
            }
            finally
            {
                sourceSampleContext?.Dispose();
            }
        }

        public static bool TryRetarget(
            SourceClipSampleContext sourceSampleContext,
            Avatar sourceAvatar,
            Avatar targetAvatar,
            bool isMuscleClip,
            float sampleRate,
            float duration,
            out RetargetResult result,
            out string error)
        {
            return TryRetarget(sourceSampleContext, sourceAvatar, targetAvatar, isMuscleClip, sampleRate, duration, false, out result, out error);
        }

        public static bool TryRetarget(
            SourceClipSampleContext sourceSampleContext,
            Avatar sourceAvatar,
            Avatar targetAvatar,
            bool isMuscleClip,
            float sampleRate,
            float duration,
            bool isExportMuscle,
            out RetargetResult result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (sourceSampleContext == null)
            {
                error = "Source sample context is null.";
                return false;
            }

            if (!IsValidHumanoid(targetAvatar))
            {
                error = "Target avatar is null/invalid/non-humanoid.";
                return false;
            }

            if (!isMuscleClip && !IsValidHumanoid(sourceAvatar))
            {
                error = "Source avatar is null/invalid/non-humanoid for bone clip retarget.";
                return false;
            }

            float effectiveRate = sampleRate > 0f ? sampleRate : 30f;
            float effectiveDuration = duration > 0f ? duration : 1f / Mathf.Max(1f, effectiveRate);
            int frameCount = Mathf.Max(2, Mathf.RoundToInt(effectiveDuration * effectiveRate) + 1);

            RetargetFrameContext context = null;
            try
            {
                if (!TryCreateRetargetFrameContext(sourceAvatar, targetAvatar, isMuscleClip, isExportMuscle, out context, out error))
                {
                    return false;
                }

                var frames = new BoneFrame[frameCount];
                var poses = new HumanPose[frameCount];
                var muscleFrames = new MuscleFrameData[frameCount];
                var pose = new HumanPose();
                Vector3 rootPosition = Vector3.zero;
                Quaternion rootRotation = Quaternion.identity;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = FrameToTime(frame, frameCount, effectiveDuration);
                    if (isMuscleClip)
                    {
                        if (!sourceSampleContext.TryGetHumanPose(time, ref pose, out error))
                        {
                            error = string.IsNullOrWhiteSpace(error) ? "Failed to sample source pose." : error;
                            return false;
                        }

                        if (!TryRetargetFrame(pose, context, time, isExportMuscle, out FrameData frameData, out error))
                        {
                            return false;
                        }

                        frames[frame] = isExportMuscle ? frameData.targetMuscleFrame : frameData.targetBoneFrame;
                        poses[frame] = isExportMuscle ? frameData.targetMusclePose : frameData.targetBonePose;
                        muscleFrames[frame] = isExportMuscle ? frameData.targetMuscleFrameData : frameData.targetBoneFrameData;
                        if (frame == 0)
                        {
                            HumanPose finalPose = isExportMuscle ? frameData.targetMusclePose : frameData.targetBonePose;
                            rootPosition = finalPose.bodyPosition;
                            rootRotation = finalPose.bodyRotation;
                        }
                    }
                    else
                    {
                        if (!sourceSampleContext.TryGetBoneFrame(time, out BoneFrame sourceBoneFrame, out error))
                        {
                            error = string.IsNullOrWhiteSpace(error) ? "Failed to sample source bone frame." : error;
                            return false;
                        }

                        if (!TryRetargetFrame(sourceBoneFrame, context, time, isExportMuscle, out FrameData frameData, out error))
                        {
                            return false;
                        }

                        frames[frame] = isExportMuscle ? frameData.targetMuscleFrame : frameData.targetBoneFrame;
                        poses[frame] = isExportMuscle ? frameData.targetMusclePose : frameData.targetBonePose;
                        muscleFrames[frame] = isExportMuscle ? frameData.targetMuscleFrameData : frameData.targetBoneFrameData;
                        if (frame == 0)
                        {
                            HumanPose finalPose = isExportMuscle ? frameData.targetMusclePose : frameData.targetBonePose;
                            rootPosition = finalPose.bodyPosition;
                            rootRotation = finalPose.bodyRotation;
                        }
                    }
                }

                result = new RetargetResult
                {
                    boneNames = frames[0] != null ? frames[0].boneNames : Array.Empty<string>(),
                    frames = frames,
                    poses = poses,
                    muscleFrames = muscleFrames,
                    rootPosition = rootPosition,
                    rootRotation = rootRotation
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                TryDestroyRetargetFrameContext(context);
            }
        }

        public static bool TryRetargetFrame(
            HumanPose sourcePose,
            RetargetFrameContext context,
            float sampleTime,
            out FrameData frameData,
            out string error)
        {
            return TryRetargetFrame(sourcePose, context, sampleTime, false, out frameData, out error);
        }

        public static bool TryRetargetFrame(
            HumanPose sourcePose,
            RetargetFrameContext context,
            float sampleTime,
            bool isExportMuscle,
            out FrameData frameData,
            out string error)
        {
            frameData = null;
            error = string.Empty;

            if (context == null)
            {
                error = "Retarget frame context is null.";
                return false;
            }

            if (!TryCopyHumanPose(sourcePose, out HumanPose copiedSourcePose))
            {
                error = "Failed to copy source pose.";
                return false;
            }

            bool isMuscleClip = context.isMuscleClip;
            HumanPose originBonePose = default;
            HumanPose originMusclePose = default;
            HumanPose targetMusclePose = copiedSourcePose;
            HumanPose targetBonePose = copiedSourcePose;
            BoneFrame originBoneFrame = null;
            BoneFrame originMuscleFrame = null;
            BoneFrame targetMuscleFrame = null;
            BoneFrame targetBoneFrame = null;

            if (!isMuscleClip)
            {
                if (context.originBone == null || context.originMuscle == null)
                {
                    error = "Origin stages are missing for bone clip retarget.";
                    return false;
                }

                if (!ApplyPoseToStage(context.originBone, copiedSourcePose, out originBoneFrame, out error))
                {
                    return false;
                }

                if (!TryReadPoseFromStage(context.originBone, out originBonePose, out error))
                {
                    return false;
                }

                if (!ApplyPoseToStage(context.originMuscle, originBonePose, out originMuscleFrame, out error))
                {
                    return false;
                }

                if (!TryReadPoseFromStage(context.originMuscle, out originMusclePose, out error))
                {
                    return false;
                }

                targetMusclePose = originMusclePose;
                if (!ApplyPoseToStage(context.targetMuscle, targetMusclePose, out targetMuscleFrame, out error))
                {
                    return false;
                }

                if (!TryReadPoseFromStage(context.targetMuscle, out targetMusclePose, out error))
                {
                    return false;
                }

                if (!isExportMuscle)
                {
                    targetBonePose = targetMusclePose;
                    if (!ApplyPoseToStage(context.targetBone, targetBonePose, out targetBoneFrame, out error))
                    {
                        return false;
                    }

                    if (!TryReadPoseFromStage(context.targetBone, out targetBonePose, out error))
                    {
                        return false;
                    }
                }
                else
                {
                    targetBonePose = targetMusclePose;
                    targetBoneFrame = targetMuscleFrame;
                }
            }
            else
            {
                if (context.targetMuscle == null || (!isExportMuscle && context.targetBone == null))
                {
                    error = "Target stages are missing for muscle clip retarget.";
                    return false;
                }

                if (!ApplyPoseToStage(context.targetMuscle, copiedSourcePose, out targetMuscleFrame, out error))
                {
                    return false;
                }

                if (!TryReadPoseFromStage(context.targetMuscle, out targetMusclePose, out error))
                {
                    return false;
                }

                if (!isExportMuscle)
                {
                    if (!ApplyPoseToStage(context.targetBone, targetMusclePose, out targetBoneFrame, out error))
                    {
                        return false;
                    }

                    if (!TryReadPoseFromStage(context.targetBone, out targetBonePose, out error))
                    {
                        return false;
                    }
                }
                else
                {
                    targetBonePose = targetMusclePose;
                    targetBoneFrame = targetMuscleFrame;
                }
            }

            frameData = new FrameData
            {
                sampleTime = sampleTime,
                sourceKind = isMuscleClip ? SourcePoseKind.MuscleClip : SourcePoseKind.BoneClip,
                sourcePose = copiedSourcePose,
                sourceBoneFrame = null,
                originBonePose = originBonePose,
                originMusclePose = originMusclePose,
                targetMusclePose = targetMusclePose,
                targetBonePose = targetBonePose,
                originBoneFrame = originBoneFrame,
                originMuscleFrame = originMuscleFrame,
                targetMuscleFrame = targetMuscleFrame,
                targetBoneFrame = targetBoneFrame,
                targetMuscleFrameData = CaptureMuscleFrameData(context.targetMuscle, targetMusclePose),
                targetBoneFrameData = !isExportMuscle
                    ? CaptureMuscleFrameData(context.targetBone, targetBonePose)
                    : CaptureMuscleFrameData(context.targetMuscle, targetMusclePose)
            };
            return true;
        }

        public static bool TryRetargetFrame(
            BoneFrame sourceBoneFrame,
            RetargetFrameContext context,
            float sampleTime,
            out FrameData frameData,
            out string error)
        {
            return TryRetargetFrame(sourceBoneFrame, context, sampleTime, false, out frameData, out error);
        }

        public static bool TryRetargetFrame(
            BoneFrame sourceBoneFrame,
            RetargetFrameContext context,
            float sampleTime,
            bool isExportMuscle,
            out FrameData frameData,
            out string error)
        {
            frameData = null;
            error = string.Empty;

            if (context == null)
            {
                error = "Retarget frame context is null.";
                return false;
            }

            if (context.isMuscleClip)
            {
                error = "Bone frame retarget requires bone clip context.";
                return false;
            }

            if (sourceBoneFrame == null || sourceBoneFrame.boneNames == null || sourceBoneFrame.localPositions == null || sourceBoneFrame.localRotations == null)
            {
                error = "Source bone frame is invalid.";
                return false;
            }

            if (context.originBone == null || context.originMuscle == null || context.targetMuscle == null || (!isExportMuscle && context.targetBone == null))
            {
                error = "Retarget stages are missing.";
                return false;
            }

            if (!TryApplyBoneFrameToStage(context.originBone, sourceBoneFrame, out BoneFrame originBoneFrame, out error))
            {
                return false;
            }

            if (!TryReadPoseFromStage(context.originBone, out HumanPose originBonePose, out error))
            {
                return false;
            }

            if (!ApplyPoseToStage(context.originMuscle, originBonePose, out BoneFrame originMuscleFrame, out error))
            {
                return false;
            }

            if (!TryReadPoseFromStage(context.originMuscle, out HumanPose originMusclePose, out error))
            {
                return false;
            }

            if (!ApplyPoseToStage(context.targetMuscle, originMusclePose, out BoneFrame targetMuscleFrame, out error))
            {
                return false;
            }

            if (!TryReadPoseFromStage(context.targetMuscle, out HumanPose targetMusclePose, out error))
            {
                return false;
            }

            HumanPose targetBonePose = default;
            BoneFrame targetBoneFrame = null;
            if (!isExportMuscle)
            {
                if (!ApplyPoseToStage(context.targetBone, targetMusclePose, out targetBoneFrame, out error))
                {
                    return false;
                }

                if (!TryReadPoseFromStage(context.targetBone, out targetBonePose, out error))
                {
                    return false;
                }
            }
            else
            {
                targetBonePose = targetMusclePose;
                targetBoneFrame = targetMuscleFrame;
            }

            frameData = new FrameData
            {
                sampleTime = sampleTime,
                sourceKind = SourcePoseKind.BoneClip,
                sourceBoneFrame = CloneBoneFrame(sourceBoneFrame),
                sourcePose = originBonePose,
                originBonePose = originBonePose,
                originMusclePose = originMusclePose,
                targetMusclePose = targetMusclePose,
                targetBonePose = targetBonePose,
                originBoneFrame = originBoneFrame,
                originMuscleFrame = originMuscleFrame,
                targetMuscleFrame = targetMuscleFrame,
                targetBoneFrame = targetBoneFrame,
                targetMuscleFrameData = CaptureMuscleFrameData(context.targetMuscle, targetMusclePose),
                targetBoneFrameData = !isExportMuscle
                    ? CaptureMuscleFrameData(context.targetBone, targetBonePose)
                    : CaptureMuscleFrameData(context.targetMuscle, targetMusclePose)
            };

            return true;
        }

        public static bool TryWriteRetargetResultToClip(RetargetResult result, AnimationClip clip, out string error)
        {
            return TryWriteRetargetResultToClip(result, clip, false, out error);
        }

        public static bool TryWriteRetargetResultToClip(RetargetResult result, AnimationClip clip, bool isExportMuscle, out string error)
        {
            error = string.Empty;
            if (result == null)
            {
                error = "Retarget result is empty.";
                return false;
            }

            if (isExportMuscle)
            {
                if (result.poses == null || result.poses.Length == 0)
                {
                    error = "Retarget muscle poses are empty.";
                    return false;
                }
            }
            else if (result.frames == null || result.frames.Length == 0)
            {
                error = "Retarget bone frames are empty.";
                return false;
            }

            clip.ClearCurves();
            if (isExportMuscle)
            {
                string[] muscleNames = HumanTrait.MuscleName;
                int muscleCount = Mathf.Min(HumanTrait.MuscleCount, muscleNames != null ? muscleNames.Length : 0);
                if (muscleCount <= 0)
                {
                    error = "HumanTrait muscle list is empty.";
                    return false;
                }

                var rootTx = new AnimationCurve();
                var rootTy = new AnimationCurve();
                var rootTz = new AnimationCurve();
                var rootQx = new AnimationCurve();
                var rootQy = new AnimationCurve();
                var rootQz = new AnimationCurve();
                var rootQw = new AnimationCurve();
                var muscleCurves = new AnimationCurve[muscleCount];
                for (int i = 0; i < muscleCount; i++)
                {
                    muscleCurves[i] = new AnimationCurve();
                }

                for (int frame = 0; frame < result.poses.Length; frame++)
                {
                    float t = clip.frameRate > 0f ? frame / clip.frameRate : frame / 30f;
                    HumanPose pose = result.poses[frame];
                    if (pose.muscles == null)
                    {
                        continue;
                    }

                    rootTx.AddKey(t, pose.bodyPosition.x);
                    rootTy.AddKey(t, pose.bodyPosition.y);
                    rootTz.AddKey(t, pose.bodyPosition.z);
                    rootQx.AddKey(t, pose.bodyRotation.x);
                    rootQy.AddKey(t, pose.bodyRotation.y);
                    rootQz.AddKey(t, pose.bodyRotation.z);
                    rootQw.AddKey(t, pose.bodyRotation.w);

                    for (int muscle = 0; muscle < muscleCount; muscle++)
                    {
                        float value = muscle < pose.muscles.Length ? pose.muscles[muscle] : 0f;
                        muscleCurves[muscle].AddKey(t, value);
                    }
                }

                TryBuildGoalCurves(result, clip.frameRate, out AnimationCurve leftFootTx, out AnimationCurve leftFootTy, out AnimationCurve leftFootTz, out AnimationCurve leftFootQx, out AnimationCurve leftFootQy, out AnimationCurve leftFootQz, out AnimationCurve leftFootQw,
                    out AnimationCurve rightFootTx, out AnimationCurve rightFootTy, out AnimationCurve rightFootTz, out AnimationCurve rightFootQx, out AnimationCurve rightFootQy, out AnimationCurve rightFootQz, out AnimationCurve rightFootQw,
                    out AnimationCurve leftHandTx, out AnimationCurve leftHandTy, out AnimationCurve leftHandTz, out AnimationCurve leftHandQx, out AnimationCurve leftHandQy, out AnimationCurve leftHandQz, out AnimationCurve leftHandQw,
                    out AnimationCurve rightHandTx, out AnimationCurve rightHandTy, out AnimationCurve rightHandTz, out AnimationCurve rightHandQx, out AnimationCurve rightHandQy, out AnimationCurve rightHandQz, out AnimationCurve rightHandQw);

                clip.SetCurve(string.Empty, typeof(Animator), "RootT.x", rootTx);
                clip.SetCurve(string.Empty, typeof(Animator), "RootT.y", rootTy);
                clip.SetCurve(string.Empty, typeof(Animator), "RootT.z", rootTz);
                clip.SetCurve(string.Empty, typeof(Animator), "RootQ.x", rootQx);
                clip.SetCurve(string.Empty, typeof(Animator), "RootQ.y", rootQy);
                clip.SetCurve(string.Empty, typeof(Animator), "RootQ.z", rootQz);
                clip.SetCurve(string.Empty, typeof(Animator), "RootQ.w", rootQw);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootT.x", leftFootTx);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootT.y", leftFootTy);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootT.z", leftFootTz);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootQ.x", leftFootQx);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootQ.y", leftFootQy);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootQ.z", leftFootQz);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftFootQ.w", leftFootQw);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootT.x", rightFootTx);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootT.y", rightFootTy);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootT.z", rightFootTz);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootQ.x", rightFootQx);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootQ.y", rightFootQy);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootQ.z", rightFootQz);
                clip.SetCurve(string.Empty, typeof(Animator), "RightFootQ.w", rightFootQw);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandT.x", leftHandTx);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandT.y", leftHandTy);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandT.z", leftHandTz);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandQ.x", leftHandQx);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandQ.y", leftHandQy);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandQ.z", leftHandQz);
                clip.SetCurve(string.Empty, typeof(Animator), "LeftHandQ.w", leftHandQw);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandT.x", rightHandTx);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandT.y", rightHandTy);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandT.z", rightHandTz);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandQ.x", rightHandQx);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandQ.y", rightHandQy);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandQ.z", rightHandQz);
                clip.SetCurve(string.Empty, typeof(Animator), "RightHandQ.w", rightHandQw);

                for (int muscle = 0; muscle < muscleCount; muscle++)
                {
                    string muscleName = muscleNames[muscle];
                    if (string.IsNullOrWhiteSpace(muscleName))
                    {
                        continue;
                    }

                    clip.SetCurve(string.Empty, typeof(Animator), muscleName, muscleCurves[muscle]);
                }
            }
            else
            {
                string[] boneNames = result.boneNames ?? System.Array.Empty<string>();
                for (int i = 0; i < boneNames.Length; i++)
                {
                    string path = boneNames[i];
                    var posX = new AnimationCurve();
                    var posY = new AnimationCurve();
                    var posZ = new AnimationCurve();
                    var rotX = new AnimationCurve();
                    var rotY = new AnimationCurve();
                    var rotZ = new AnimationCurve();
                    var rotW = new AnimationCurve();

                    for (int frame = 0; frame < result.frames.Length; frame++)
                    {
                        float t = clip.frameRate > 0f ? frame / clip.frameRate : frame / 30f;
                        BoneFrame f = result.frames[frame];
                        if (f == null || f.localPositions == null || f.localRotations == null || i >= f.localPositions.Length || i >= f.localRotations.Length)
                        {
                            continue;
                        }

                        Vector3 lp = f.localPositions[i];
                        Quaternion lr = f.localRotations[i];
                        posX.AddKey(t, lp.x);
                        posY.AddKey(t, lp.y);
                        posZ.AddKey(t, lp.z);
                        rotX.AddKey(t, lr.x);
                        rotY.AddKey(t, lr.y);
                        rotZ.AddKey(t, lr.z);
                        rotW.AddKey(t, lr.w);
                    }

                    clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", posX);
                    clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", posY);
                    clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z", posZ);
                    clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x", rotX);
                    clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y", rotY);
                    clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z", rotZ);
                    clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w", rotW);
                }
            }

            clip.EnsureQuaternionContinuity();
            return true;
        }

        public static bool TryCreateSourceClipSampleContext(
            AnimationClip clip,
            Avatar avatar,
            out SourceClipSampleContext sampleContext,
            out string error)
        {
            sampleContext = null;
            error = string.Empty;

            if (clip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (!IsValidHumanoid(avatar))
            {
                error = "Avatar is null/invalid/non-humanoid.";
                return false;
            }

            sampleContext = new SourceClipSampleContext(clip, avatar, out error);
            if (sampleContext == null || !string.IsNullOrEmpty(error))
            {
                sampleContext?.Dispose();
                sampleContext = null;
                return false;
            }

            return true;
        }

        public static bool IsValidHumanoid(Avatar avatar)
        {
            return avatar != null && avatar.isValid && avatar.isHuman;
        }

        private static float FrameToTime(int frame, int frameCount, float duration)
        {
            if (frameCount <= 1)
            {
                return 0f;
            }

            float normalized = frame / (frameCount - 1f);
            return Mathf.Clamp01(normalized) * Mathf.Max(0f, duration);
        }

        private static BoneFrame CaptureBoneFrame(Transform root, string[] boneNames, string canonicalRootBoneName)
        {
            var boneMap = BuildPathMap(root, root, canonicalRootBoneName);
            var frame = new BoneFrame
            {
                boneNames = boneNames,
                localPositions = new Vector3[boneNames.Length],
                localRotations = new Quaternion[boneNames.Length]
            };

            for (int i = 0; i < boneNames.Length; i++)
            {
                string path = boneNames[i];
                if (!boneMap.TryGetValue(path, out Transform t) || t == null)
                {
                    frame.localPositions[i] = Vector3.zero;
                    frame.localRotations[i] = Quaternion.identity;
                    continue;
                }

                frame.localPositions[i] = t.localPosition;
                frame.localRotations[i] = t.localRotation;
            }

            return frame;
        }

        private static bool TryBuildStage(Avatar avatar, string rootName, out KimodoRetargetStageCache cache, out string error)
        {
            cache = null;
            error = string.Empty;

            if (!IsValidHumanoid(avatar))
            {
                error = $"Avatar is null/invalid/non-humanoid for stage '{rootName}'.";
                return false;
            }

            GameObject root = new GameObject(rootName);
            root.hideFlags = HideFlags.None;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(avatar, root.transform, out error))
            {
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            SetHierarchyHideFlags(root.transform, HideFlags.None);
            Transform skeletonRoot = root.transform;
            string canonicalRootBoneName = ResolveSkeletonRootBoneName(avatar);

            var animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = root.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.enabled = false;
            animator.Rebind();
            animator.Update(0f);

            cache = new KimodoRetargetStageCache
            {
                avatar = avatar,
                root = root,
                skeletonRoot = skeletonRoot,
                canonicalRootBoneName = canonicalRootBoneName,
                animator = animator,
                poseHandler = new HumanPoseHandler(avatar, root.transform)
            };

            var gizmo = root.AddComponent<KimodoTransformGizmoVisualizer>();
            gizmo.pointRadius = 0.015f;
            gizmo.drawNames = false;
            gizmo.drawOnlyWhenSelected = false;

            if (!TryBuildBoneNameTable(skeletonRoot, canonicalRootBoneName, out cache.bonePaths, out error))
            {
                UnityEngine.Object.DestroyImmediate(root);
                cache = null;
                return false;
            }

            cache.boneTransforms = BuildBoneTransforms(skeletonRoot, cache.bonePaths, cache.canonicalRootBoneName);
            cache.boneFrame = new BoneFrame
            {
                boneNames = cache.bonePaths,
                localPositions = new Vector3[cache.bonePaths.Length],
                localRotations = new Quaternion[cache.bonePaths.Length]
            };
            return true;
        }

        private static void DestroyStage(KimodoRetargetStageCache cache)
        {
            if (cache?.root != null)
            {
                UnityEngine.Object.DestroyImmediate(cache.root);
            }
        }

        private static void SetHierarchyHideFlags(Transform root, HideFlags hideFlags)
        {
            if (root == null)
            {
                return;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i].gameObject;
                if (go != null)
                {
                    go.hideFlags = hideFlags;
                }
            }
        }

        private static bool ApplyPoseToStage(KimodoRetargetStageCache cache, HumanPose pose, out BoneFrame frame, out string error)
        {
            frame = null;
            error = string.Empty;

            if (cache == null || cache.root == null || cache.poseHandler == null)
            {
                error = "Stage cache is not initialized.";
                return false;
            }

            try
            {
                cache.pose = pose;
                cache.poseHandler.SetHumanPose(ref cache.pose);
                frame = CaptureBoneFrame(cache.skeletonRoot, cache.bonePaths, cache.canonicalRootBoneName);
                cache.boneFrame = frame;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryApplyBoneFrameToStage(KimodoRetargetStageCache cache, BoneFrame sourceBoneFrame, out BoneFrame frame, out string error)
        {
            frame = null;
            error = string.Empty;

            if (cache == null || cache.root == null || cache.skeletonRoot == null || cache.bonePaths == null || cache.boneTransforms == null)
            {
                error = "Stage cache is not initialized.";
                return false;
            }

            if (sourceBoneFrame == null || sourceBoneFrame.boneNames == null || sourceBoneFrame.localPositions == null || sourceBoneFrame.localRotations == null)
            {
                error = "Source bone frame is invalid.";
                return false;
            }

            int count = Mathf.Min(sourceBoneFrame.boneNames.Length, Mathf.Min(sourceBoneFrame.localPositions.Length, sourceBoneFrame.localRotations.Length));
            var sourceIndexByPath = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string path = sourceBoneFrame.boneNames[i];
                if (string.IsNullOrEmpty(path) || sourceIndexByPath.ContainsKey(path))
                {
                    continue;
                }

                sourceIndexByPath[path] = i;
            }

            for (int i = 0; i < cache.bonePaths.Length; i++)
            {
                Transform bone = cache.boneTransforms[i];
                if (bone == null)
                {
                    continue;
                }

                if (!sourceIndexByPath.TryGetValue(cache.bonePaths[i], out int sourceIndex))
                {
                    continue;
                }

                bone.localPosition = sourceBoneFrame.localPositions[sourceIndex];
                bone.localRotation = sourceBoneFrame.localRotations[sourceIndex];
            }

            frame = CaptureBoneFrame(cache.skeletonRoot, cache.bonePaths, cache.canonicalRootBoneName);
            cache.boneFrame = frame;
            return true;
        }

        private static bool TryReadPoseFromStage(KimodoRetargetStageCache cache, out HumanPose pose, out string error)
        {
            pose = new HumanPose();
            error = string.Empty;

            if (cache == null || cache.poseHandler == null)
            {
                error = "Stage cache is not initialized.";
                return false;
            }

            try
            {
                cache.poseHandler.GetHumanPose(ref pose);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static MuscleFrameData CaptureMuscleFrameData(KimodoRetargetStageCache cache, HumanPose pose)
        {
            var data = new MuscleFrameData
            {
                pose = pose,
                leftFootPosition = Vector3.zero,
                leftFootRotation = Quaternion.identity,
                rightFootPosition = Vector3.zero,
                rightFootRotation = Quaternion.identity,
                leftHandPosition = Vector3.zero,
                leftHandRotation = Quaternion.identity,
                rightHandPosition = Vector3.zero,
                rightHandRotation = Quaternion.identity
            };

            if (cache == null || cache.animator == null || !cache.animator.isHuman)
            {
                return data;
            }

            Vector3 bodyPos = pose.bodyPosition;
            Quaternion bodyRot = pose.bodyRotation;
            float humanScale = Mathf.Max(1e-6f, cache.animator.humanScale);

            TryGetHumanoidIkGoalPose(cache, AvatarIKGoal.LeftFoot, bodyPos, bodyRot, humanScale, out data.leftFootPosition, out data.leftFootRotation);
            TryGetHumanoidIkGoalPose(cache, AvatarIKGoal.RightFoot, bodyPos, bodyRot, humanScale, out data.rightFootPosition, out data.rightFootRotation);
            TryGetHumanoidIkGoalPose(cache, AvatarIKGoal.LeftHand, bodyPos, bodyRot, humanScale, out data.leftHandPosition, out data.leftHandRotation);
            TryGetHumanoidIkGoalPose(cache, AvatarIKGoal.RightHand, bodyPos, bodyRot, humanScale, out data.rightHandPosition, out data.rightHandRotation);
            return data;
        }

        private static void TryGetHumanoidIkGoalPose(
            KimodoRetargetStageCache cache,
            AvatarIKGoal avatarIKGoal,
            Vector3 bodyPosition,
            Quaternion bodyRotation,
            float humanScale,
            out Vector3 goalPosition,
            out Quaternion goalRotation)
        {
            goalPosition = Vector3.zero;
            goalRotation = Quaternion.identity;

            if (cache == null || cache.animator == null || cache.avatar == null)
            {
                return;
            }

            HumanBodyBones bone = HumanBodyBoneFromAvatarIKGoal(avatarIKGoal);
            if (bone == HumanBodyBones.LastBone)
            {
                return;
            }

            Transform t = cache.animator.GetBoneTransform(bone);
            if (t == null)
            {
                return;
            }

            int humanId = (int)bone;
            Quaternion postRotation = AvatarRuntimeAccess.GetAvatarPostRotationOrIdentity(cache.avatar, humanId);
            Quaternion worldGoalQ = t.rotation * postRotation;
            Vector3 worldGoalT = t.position;

            if (avatarIKGoal == AvatarIKGoal.LeftFoot || avatarIKGoal == AvatarIKGoal.RightFoot)
            {
                float axisLength = AvatarRuntimeAccess.GetAvatarAxisLengthOrZero(cache.avatar, humanId);
                worldGoalT += worldGoalQ * new Vector3(axisLength, 0f, 0f);
            }

            Quaternion invBodyQ = Quaternion.Inverse(bodyRotation);
            goalPosition = invBodyQ * (worldGoalT - bodyPosition);
            goalRotation = invBodyQ * worldGoalQ;
            goalPosition /= Mathf.Max(1e-6f, humanScale);
        }

        private static HumanBodyBones HumanBodyBoneFromAvatarIKGoal(AvatarIKGoal avatarIKGoal)
        {
            switch (avatarIKGoal)
            {
                case AvatarIKGoal.LeftFoot:
                    return HumanBodyBones.LeftFoot;
                case AvatarIKGoal.RightFoot:
                    return HumanBodyBones.RightFoot;
                case AvatarIKGoal.LeftHand:
                    return HumanBodyBones.LeftHand;
                case AvatarIKGoal.RightHand:
                    return HumanBodyBones.RightHand;
                default:
                    return HumanBodyBones.LastBone;
            }
        }

        private static void TryBuildGoalCurves(
            RetargetResult result,
            float clipFrameRate,
            out AnimationCurve leftFootTx,
            out AnimationCurve leftFootTy,
            out AnimationCurve leftFootTz,
            out AnimationCurve leftFootQx,
            out AnimationCurve leftFootQy,
            out AnimationCurve leftFootQz,
            out AnimationCurve leftFootQw,
            out AnimationCurve rightFootTx,
            out AnimationCurve rightFootTy,
            out AnimationCurve rightFootTz,
            out AnimationCurve rightFootQx,
            out AnimationCurve rightFootQy,
            out AnimationCurve rightFootQz,
            out AnimationCurve rightFootQw,
            out AnimationCurve leftHandTx,
            out AnimationCurve leftHandTy,
            out AnimationCurve leftHandTz,
            out AnimationCurve leftHandQx,
            out AnimationCurve leftHandQy,
            out AnimationCurve leftHandQz,
            out AnimationCurve leftHandQw,
            out AnimationCurve rightHandTx,
            out AnimationCurve rightHandTy,
            out AnimationCurve rightHandTz,
            out AnimationCurve rightHandQx,
            out AnimationCurve rightHandQy,
            out AnimationCurve rightHandQz,
            out AnimationCurve rightHandQw)
        {
            leftFootTx = new AnimationCurve();
            leftFootTy = new AnimationCurve();
            leftFootTz = new AnimationCurve();
            leftFootQx = new AnimationCurve();
            leftFootQy = new AnimationCurve();
            leftFootQz = new AnimationCurve();
            leftFootQw = new AnimationCurve();

            rightFootTx = new AnimationCurve();
            rightFootTy = new AnimationCurve();
            rightFootTz = new AnimationCurve();
            rightFootQx = new AnimationCurve();
            rightFootQy = new AnimationCurve();
            rightFootQz = new AnimationCurve();
            rightFootQw = new AnimationCurve();

            leftHandTx = new AnimationCurve();
            leftHandTy = new AnimationCurve();
            leftHandTz = new AnimationCurve();
            leftHandQx = new AnimationCurve();
            leftHandQy = new AnimationCurve();
            leftHandQz = new AnimationCurve();
            leftHandQw = new AnimationCurve();

            rightHandTx = new AnimationCurve();
            rightHandTy = new AnimationCurve();
            rightHandTz = new AnimationCurve();
            rightHandQx = new AnimationCurve();
            rightHandQy = new AnimationCurve();
            rightHandQz = new AnimationCurve();
            rightHandQw = new AnimationCurve();

            MuscleFrameData[] frames = result != null ? result.muscleFrames : null;
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            float rate = clipFrameRate > 0f ? clipFrameRate : 30f;
            for (int i = 0; i < frames.Length; i++)
            {
                MuscleFrameData f = frames[i];
                if (f == null)
                {
                    continue;
                }

                float t = i / rate;
                leftFootTx.AddKey(t, f.leftFootPosition.x);
                leftFootTy.AddKey(t, f.leftFootPosition.y);
                leftFootTz.AddKey(t, f.leftFootPosition.z);
                leftFootQx.AddKey(t, f.leftFootRotation.x);
                leftFootQy.AddKey(t, f.leftFootRotation.y);
                leftFootQz.AddKey(t, f.leftFootRotation.z);
                leftFootQw.AddKey(t, f.leftFootRotation.w);

                rightFootTx.AddKey(t, f.rightFootPosition.x);
                rightFootTy.AddKey(t, f.rightFootPosition.y);
                rightFootTz.AddKey(t, f.rightFootPosition.z);
                rightFootQx.AddKey(t, f.rightFootRotation.x);
                rightFootQy.AddKey(t, f.rightFootRotation.y);
                rightFootQz.AddKey(t, f.rightFootRotation.z);
                rightFootQw.AddKey(t, f.rightFootRotation.w);

                leftHandTx.AddKey(t, f.leftHandPosition.x);
                leftHandTy.AddKey(t, f.leftHandPosition.y);
                leftHandTz.AddKey(t, f.leftHandPosition.z);
                leftHandQx.AddKey(t, f.leftHandRotation.x);
                leftHandQy.AddKey(t, f.leftHandRotation.y);
                leftHandQz.AddKey(t, f.leftHandRotation.z);
                leftHandQw.AddKey(t, f.leftHandRotation.w);

                rightHandTx.AddKey(t, f.rightHandPosition.x);
                rightHandTy.AddKey(t, f.rightHandPosition.y);
                rightHandTz.AddKey(t, f.rightHandPosition.z);
                rightHandQx.AddKey(t, f.rightHandRotation.x);
                rightHandQy.AddKey(t, f.rightHandRotation.y);
                rightHandQz.AddKey(t, f.rightHandRotation.z);
                rightHandQw.AddKey(t, f.rightHandRotation.w);
            }
        }

        private static bool TryBuildBoneNameTable(Transform root, string rootBoneName, out string[] boneNames, out string error)
        {
            error = string.Empty;
            boneNames = null;
            if (root == null)
            {
                error = "Target root is null.";
                return false;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var names = new List<string>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                string path = CalculateTransformPath(all[i], root, rootBoneName);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                names.Add(path);
            }

            boneNames = names.ToArray();
            return true;
        }

        private static Transform[] BuildBoneTransforms(Transform root, string[] bonePaths, string rootBoneName)
        {
            var transforms = new Transform[bonePaths.Length];
            for (int i = 0; i < bonePaths.Length; i++)
            {
                transforms[i] = FindByPath(root, bonePaths[i], rootBoneName);
            }

            return transforms;
        }

        private static Transform FindByPath(Transform root, string path, string rootBoneName)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (string.Equals(root.name, path, StringComparison.Ordinal) || string.Equals(rootBoneName, path, StringComparison.Ordinal))
            {
                return root;
            }

            string[] segments = path.Split('/');
            Transform current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                if (current == null)
                {
                    return null;
                }

                if (i == 0 && (string.Equals(current.name, segments[i], StringComparison.Ordinal) || string.Equals(rootBoneName, segments[i], StringComparison.Ordinal)))
                {
                    continue;
                }

                current = current.Find(segments[i]);
            }

            return current;
        }

        public sealed class SourceClipSampleContext : IDisposable
        {
            private readonly AnimationClip clip;
            private readonly Avatar avatar;
            private readonly GameObject skeletonRoot;
            private readonly PlayableGraph graph;
            private readonly AnimationClipPlayable clipPlayable;
            private readonly string canonicalRootBoneName;
            private HumanPoseHandler handler;
            private string[] bonePaths;
            private bool disposed;
            private bool ready;

            public SourceClipSampleContext(AnimationClip clip, Avatar avatar, out string error)
            {
                error = string.Empty;
                this.clip = clip;
                this.avatar = avatar;
                canonicalRootBoneName = ResolveSkeletonRootBoneName(avatar);

                skeletonRoot = new GameObject("KimodoRetargetTools_SourceClipSampleContext_SkeletonRoot");
                skeletonRoot.hideFlags = HideFlags.HideAndDontSave;
                skeletonRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                skeletonRoot.transform.localScale = Vector3.one;

                if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(avatar, skeletonRoot.transform, out error))
                {
                    return;
                }

                var animator = skeletonRoot.AddComponent<Animator>();
                //animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);

                graph = PlayableGraph.Create("KimodoRetargetTools_SourceClipSampleContext");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                clipPlayable = AnimationClipPlayable.Create(graph, clip);
                clipPlayable.SetApplyFootIK(false);
                var output = AnimationPlayableOutput.Create(graph, "KimodoRetargetTools_SourceClipSampleContextOutput", animator);
                output.SetSourcePlayable(clipPlayable);
                graph.Play();

                if (!TryBuildBoneNameTable(skeletonRoot.transform, canonicalRootBoneName, out bonePaths, out error))
                {
                    return;
                }

                ready = true;
            }

            public bool TryGetHumanPose(float time, ref HumanPose pose, out string error)
            {
                error = string.Empty;
                if (disposed)
                {
                    error = "Pose data provider disposed.";
                    return false;
                }

                if (!ready || clip == null || skeletonRoot == null)
                {
                    error = "Source clip or sampler rig is null.";
                    return false;
                }

                try
                {
                    if (handler == null)
                    {
                        handler = new HumanPoseHandler(avatar, skeletonRoot.transform);
                    }

                    if (!graph.IsValid())
                    {
                        error = "Playable graph is not initialized.";
                        return false;
                    }

                    clipPlayable.SetTime(Mathf.Max(0f, time));
                    graph.Evaluate(0f);
                    handler.GetHumanPose(ref pose);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public bool TryGetBoneFrame(float time, out BoneFrame frame, out string error)
            {
                frame = null;
                error = string.Empty;
                if (disposed)
                {
                    error = "Pose data provider disposed.";
                    return false;
                }

                if (!ready || clip == null || skeletonRoot == null || bonePaths == null || bonePaths.Length == 0)
                {
                    error = "Source clip or sampler rig is null.";
                    return false;
                }

                try
                {
                    clip.SampleAnimation(skeletonRoot, Mathf.Max(0f, time));
                    frame = CaptureBoneFrame(skeletonRoot.transform, bonePaths, canonicalRootBoneName);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public bool IsReady => ready && !disposed;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                if (graph.IsValid())
                {
                    graph.Destroy();
                }

                if (skeletonRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(skeletonRoot);
                }
            }
        }

        private static Dictionary<string, Transform> BuildPathMap(Transform current, Transform root, string rootBoneName)
        {
            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            if (current == null || root == null)
            {
                return map;
            }

            Transform[] all = current.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                string path = CalculateTransformPath(t, root, rootBoneName);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!map.ContainsKey(path))
                {
                    map.Add(path, t);
                }
            }

            return map;
        }

        private static BoneFrame CloneBoneFrame(BoneFrame source)
        {
            if (source == null)
            {
                return null;
            }

            return new BoneFrame
            {
                boneNames = source.boneNames != null ? (string[])source.boneNames.Clone() : Array.Empty<string>(),
                localPositions = source.localPositions != null ? (Vector3[])source.localPositions.Clone() : Array.Empty<Vector3>(),
                localRotations = source.localRotations != null ? (Quaternion[])source.localRotations.Clone() : Array.Empty<Quaternion>()
            };
        }

        public static bool TryCopyHumanPose(HumanPose source, out HumanPose copy)
        {
            copy = new HumanPose
            {
                bodyPosition = source.bodyPosition,
                bodyRotation = source.bodyRotation,
                muscles = source.muscles != null ? (float[])source.muscles.Clone() : Array.Empty<float>()
            };
            return true;
        }

        private static string CalculateTransformPath(Transform target, Transform root, string rootBoneName)
        {
            if (target == null || root == null)
            {
                return null;
            }

            if (target == root)
            {
                return string.IsNullOrWhiteSpace(rootBoneName) ? target.name : rootBoneName;
            }

            var names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return null;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string ResolveSkeletonRootBoneName(Avatar avatar)
        {
            if (!IsValidHumanoid(avatar))
            {
                return "Hips";
            }

            SkeletonBone[] skeleton = avatar.humanDescription.skeleton;
            if (skeleton == null || skeleton.Length == 0)
            {
                return "Hips";
            }

            int rootIndex = FindSkeletonRootIndex(skeleton);
            if (rootIndex >= 0 && rootIndex < skeleton.Length)
            {
                string name = skeleton[rootIndex].name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }

            return "Hips";
        }

        private static int FindSkeletonRootIndex(SkeletonBone[] skeleton)
        {
            if (skeleton == null || skeleton.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < skeleton.Length; i++)
            {
                string parentName = AvatarRuntimeAccess.GetSkeletonBoneParentNameOrEmpty(skeleton[i]);
                if (string.IsNullOrWhiteSpace(parentName))
                {
                    return i;
                }
            }

            return 0;
        }

    }
}
