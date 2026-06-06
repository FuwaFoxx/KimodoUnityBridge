using KimodoBridge;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using TimelineInject;

namespace KimodoBridge
{
    public static class KimodoRetargetTools
    {
        private delegate bool ClipSampleCallback<TSample>(
            KimodoRetargetClipSamplingUtility.ClipSamplingContext context,
            float sampleTime,
            out TSample sample,
            out string error);

        public static bool IsValidHumanoid(Avatar avatar)
        {
            return avatar != null && avatar.isValid && avatar.isHuman;
        }

        public static bool TryCreateTemporaryHumanoidRoot(
            Avatar avatar,
            string rootName,
            bool animatorEnabled,
            bool applyRootMotion,
            out GameObject root,
            out Animator animator,
            out string error)
        {
            root = null;
            animator = null;
            error = string.Empty;

            if (!IsValidHumanoid(avatar))
            {
                error = "Avatar is null/invalid/non-humanoid.";
                return false;
            }

            root = new GameObject(string.IsNullOrWhiteSpace(rootName) ? "KimodoTemporaryHumanoidRoot" : rootName);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(avatar, root.transform, out error))
            {
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
                return false;
            }

            KimodoRetargetClipSamplingUtility.SetHierarchyHideFlags(root.transform, HideFlags.HideAndDontSave);

            animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = root.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = applyRootMotion;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            animator.enabled = animatorEnabled;
            return true;
        }

        public static bool TryBuildSkeletonCache(Avatar avatar, string rootName, out SkeletonCache cache, out string error)
        {
            cache = null;
            error = string.Empty;

            if (!IsValidHumanoid(avatar))
            {
                error = "Avatar is null/invalid/non-humanoid.";
                return false;
            }

            if (!TryCreateTemporaryHumanoidRoot(
                avatar,
                string.IsNullOrWhiteSpace(rootName) ? "KimodoSkeletonCache" : rootName,
                animatorEnabled: true,
                applyRootMotion: true,
                out GameObject root,
                out Animator animator,
                out error))
            {
                return false;
            }

            string canonicalRootBoneName = KimodoRetargetAvatarUtility.ResolveSkeletonRootBoneName(avatar);
            if (!KimodoRetargetAvatarUtility.TryBuildBoneNameTable(root.transform, canonicalRootBoneName, out string[] bonePaths, out error))
            {
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            if (bonePaths == null || bonePaths.Length == 0)
            {
                error = "Skeleton cache bone table is empty.";
                UnityEngine.Object.DestroyImmediate(root);
                return false;
            }

            cache = new SkeletonCache
            {
                avatar = avatar,
                root = root,
                skeletonRoot = root.transform,
                rootLocalPosition = root.transform.localPosition,
                rootLocalRotation = root.transform.localRotation,
                rootLocalScale = root.transform.localScale,
                canonicalRootBoneName = canonicalRootBoneName,
                animator = animator,
                poseHandler = new HumanPoseHandler(avatar, root.transform),
                humanScale = Mathf.Max(1e-6f, animator.humanScale),
                bonePaths = bonePaths,
                boneTransforms = KimodoRetargetAvatarUtility.BuildBoneTransforms(root.transform, bonePaths, canonicalRootBoneName),
                boneCount = bonePaths.Length
            };

            KimodoRetargetClipSamplingUtility.CaptureSkeletonBindPose(cache);

            return true;
        }

        public static void DestroySkeletonCache(SkeletonCache cache)
        {
            cache?.Dispose();
        }

        public static bool SampleBoneClipToBoneSample(
            AnimationClip clip,
            SkeletonCache cache,
            float sampleTime,
            out BoneSample sample,
            out string error)
        {
            return TrySampleFromClip(
                clip,
                cache,
                sampleTime,
                "KimodoRetargetTools_SourceBoneSampler",
                KimodoRetargetClipSamplingUtility.ResolveClipSamplingMode(clip),
                TrySampleBoneClipToBoneSample,
                out sample,
                out error);
        }

        public static bool SampleMuscleClipToMuscleSample(
            AnimationClip clip,
            SkeletonCache cache,
            float sampleTime,
            out MuscleSample sample,
            out string error)
        {
            return TrySampleFromClip(
                clip,
                cache,
                sampleTime,
                "KimodoRetargetTools_SourceMuscleSampler",
                KimodoRetargetClipSamplingUtility.ResolveClipSamplingMode(clip),
                TrySampleMuscleClipToMuscleSample,
                out sample,
                out error);
        }

        public static bool TryBuildMuscleClipCache(
            AnimationClip clip,
            SkeletonCache cache,
            out MuscleClipCache muscleClipCache,
            out string error)
        {
            muscleClipCache = null;
            error = string.Empty;

            if (clip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (!ValidateRetargetCache(cache, out error))
            {
                return false;
            }

            float frameRate = clip.frameRate > 0f ? clip.frameRate : KimodoPlayableClip.FIXED_FRAME_RATE;
            float duration = Mathf.Max(0f, clip.length);
            int frameCount = ResolveFrameCount(duration, frameRate);
            if (!TryCollectMuscleSamplesFromClip(
                    clip,
                    cache,
                    frameCount,
                    duration,
                    KimodoRetargetClipSamplingUtility.ResolveClipSamplingMode(clip),
                    out MuscleSample[] samples,
                    out error))
            {
                return false;
            }

            if (!TryCreateTransientMuscleClip(samples, frameRate, out AnimationClip cachedMuscleClip, out error))
            {
                return false;
            }

            bool ownsMuscleClip = true;
#if UNITY_EDITOR
            if (TryReplaceWithEditorCachedMuscleClip(clip, samples, frameRate, cachedMuscleClip, out AnimationClip editorCachedClip, out string editorCacheError))
            {
                cachedMuscleClip = editorCachedClip;
                ownsMuscleClip = false;
            }
            else if (!string.IsNullOrWhiteSpace(editorCacheError))
            {
                Debug.LogWarning($"[Kimodo][Retarget] Failed to reuse editor muscle cache clip: {editorCacheError}");
            }
#endif

            cachedMuscleClip.name = BuildTransientMuscleClipName(clip);
            muscleClipCache = new MuscleClipCache
            {
                sourceClip = clip,
                sourceAvatar = cache.avatar,
                frameRate = frameRate,
                duration = duration,
                samples = samples,
                muscleClip = cachedMuscleClip,
                ownsMuscleClip = ownsMuscleClip
            };
            return true;
        }

        public static void DestroyMuscleClipCache(MuscleClipCache cache, bool destroyMuscleClip = false)
        {
            if (cache == null)
            {
                return;
            }

            if (destroyMuscleClip || cache.ownsMuscleClip)
            {
                cache.Dispose();
            }
        }

        public static void DestroyMuscleClipCacheAnimationClip(AnimationClip muscleClip)
        {
            if (muscleClip != null)
            {
                UnityEngine.Object.DestroyImmediate(muscleClip);
            }
        }

        public static bool TrySampleMuscleClipCache(
            MuscleClipCache cache,
            float sampleTime,
            out MuscleSample sample,
            out string error)
        {
            sample = null;
            error = string.Empty;

            if (cache == null || !cache.IsReady)
            {
                error = "Muscle clip cache is not initialized.";
                return false;
            }

            MuscleSample[] samples = cache.samples;
            if (samples == null || samples.Length == 0)
            {
                error = "Muscle clip cache samples are empty.";
                return false;
            }

            if (samples.Length == 1 || cache.duration <= 0f)
            {
                sample = CloneMuscleSample(samples[0]);
                return sample != null;
            }

            float clampedTime = Mathf.Clamp(sampleTime, 0f, cache.duration);
            float framePosition = clampedTime * Mathf.Max(1f, cache.frameRate);
            int lowerIndex = Mathf.Clamp(Mathf.FloorToInt(framePosition), 0, samples.Length - 1);
            int upperIndex = Mathf.Clamp(lowerIndex + 1, 0, samples.Length - 1);
            if (lowerIndex == upperIndex)
            {
                sample = CloneMuscleSample(samples[lowerIndex]);
                return sample != null;
            }

            float lowerTime = lowerIndex / Mathf.Max(1f, cache.frameRate);
            float upperTime = upperIndex / Mathf.Max(1f, cache.frameRate);
            float denom = Mathf.Max(1e-6f, upperTime - lowerTime);
            float t = Mathf.Clamp01((clampedTime - lowerTime) / denom);
            sample = LerpMuscleSample(samples[lowerIndex], samples[upperIndex], t);
            if (sample == null)
            {
                error = "Cannot interpolate muscle clip cache sample.";
                return false;
            }

            return true;
        }


        public static bool RetargetBoneSampleToMuscleSample(
            BoneSample sourceSample,
            SkeletonCache sourceCache,
            out MuscleSample targetSample,
            out string error)
        {
            targetSample = null;
            error = string.Empty;

            if (!ValidateBoneSample(sourceSample, out error))
            {
                return false;
            }

            if (!ValidateRetargetCache(sourceCache, out error))
            {
                return false;
            }

            if (!TryCreateTransientBoneClip(sourceSample, KimodoPlayableClip.FIXED_FRAME_RATE, out AnimationClip transientClip, out error))
            {
                return false;
            }

            try
            {
                return SampleMuscleClipToMuscleSample(
                    transientClip,
                    sourceCache,
                    0f,
                    out targetSample,
                    out error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(transientClip);
            }
        }


        public static bool RetargetMuscleSampleToBoneSample(
            MuscleSample sourceSample,
            SkeletonCache targetCache,
            out BoneSample targetSample,
            out MuscleSample targetMuscleSample,
            out string error)
        {
            targetSample = null;
            targetMuscleSample = null;
            error = string.Empty;

            if (sourceSample == null)
            {
                error = "Source muscle sample is null.";
                return false;
            }

            if (!ValidateRetargetCache(targetCache, out error))
            {
                return false;
            }

            if (!TryCreateTransientMuscleClip(new[] { sourceSample }, KimodoPlayableClip.FIXED_FRAME_RATE, out AnimationClip transientClip, out error))
            {
                return false;
            }

            try
            {
                return TrySampleTargetFromHumanoidClip(
                    transientClip,
                    targetCache,
                    0f,
                    out targetSample,
                    out targetMuscleSample,
                    out error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(transientClip);
            }
        }

        public static bool WriteMuscleSampleToMuscleClip(IReadOnlyList<MuscleSample> samples, AnimationClip clip, out string error)
        {
            error = string.Empty;
            if (clip == null)
            {
                error = "Target clip is null.";
                return false;
            }

            if (samples == null || samples.Count == 0)
            {
                error = "Muscle samples are empty.";
                return false;
            }

            clip.ClearCurves();
            if (!WriteMuscleCurves(samples, clip, out error))
            {
                return false;
            }

            clip.EnsureQuaternionContinuity();
            return true;
        }

        public static bool WriteBoneSampleToBoneClip(IReadOnlyList<BoneSample> samples, AnimationClip clip, out string error)
        {
            error = string.Empty;
            if (clip == null)
            {
                error = "Target clip is null.";
                return false;
            }

            if (samples == null || samples.Count == 0)
            {
                error = "Bone samples are empty.";
                return false;
            }

            clip.ClearCurves();
            if (!WriteBoneCurves(samples, clip, out error))
            {
                return false;
            }

            clip.EnsureQuaternionContinuity();
            return true;
        }


        public static bool TryRetargetNew(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            SkeletonCache targetCache,
            float sampleTime,
            out BoneSample targetSample,
            out MuscleSample targetMuscleSample,
            out string error)
        {
            targetSample = null;
            targetMuscleSample = null;
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

            if (!ValidateRetargetCache(targetCache, out error))
            {
                return false;
            }

            SkeletonCache sourceCache = null;
            MuscleClipCache sourceMuscleClipCache = null;
            try
            {
                return TryRetargetNew(
                    sourceClip,
                    sourceAvatar,
                    ref sourceCache,
                    ref sourceMuscleClipCache,
                    targetCache,
                    sampleTime,
                    out targetSample,
                    out targetMuscleSample,
                    out error);
            }
            finally
            {
                sourceMuscleClipCache?.Dispose();
                sourceCache?.Dispose();
            }
        }

        public static bool TryRetargetNew(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            ref SkeletonCache sourceCache,
            ref MuscleClipCache sourceMuscleClipCache,
            SkeletonCache targetCache,
            float sampleTime,
            out BoneSample targetSample,
            out MuscleSample targetMuscleSample,
            out string error)
        {
            targetSample = null;
            targetMuscleSample = null;
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

            if (!ValidateRetargetCache(targetCache, out error))
            {
                return false;
            }

            if (!TryResolveSourceHumanoidClip(
                    sourceClip,
                    sourceAvatar,
                    "KimodoRetargetTools_SourceClipSample",
                    null,
                    ref sourceCache,
                    ref sourceMuscleClipCache,
                    out AnimationClip sourceHumanoidClip,
                    out error))
            {
                return false;
            }

            return TrySampleTargetFromHumanoidClip(
                sourceHumanoidClip,
                targetCache,
                sampleTime,
                out targetSample,
                out targetMuscleSample,
                out error);
        }


        public static bool TryRetargetNew(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            Avatar targetAvatar,
            bool exportMuscleClip,
            out AnimationClip targetClip,
            out string error)
        {
            return TryRetargetNew(
                sourceClip,
                sourceAvatar,
                targetAvatar,
                exportMuscleClip,
                null,
                out targetClip,
                out error);
        }

        public static bool TryRetargetNew(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            Avatar targetAvatar,
            bool exportMuscleClip,
            AnimationClip cachedSourceMuscleClip,
            out AnimationClip targetClip,
            out string error)
        {
            SkeletonCache sourceCache = null;
            SkeletonCache targetCache = null;
            MuscleClipCache sourceMuscleClipCache = null;
            try
            {
                return TryRetargetNew(
                    sourceClip,
                    sourceAvatar,
                    ref sourceCache,
                    ref sourceMuscleClipCache,
                    targetAvatar,
                    ref targetCache,
                    exportMuscleClip,
                    cachedSourceMuscleClip,
                    out targetClip,
                    out error);
            }
            finally
            {
                sourceMuscleClipCache?.Dispose();
                targetCache?.Dispose();
                sourceCache?.Dispose();
            }
        }

        public static bool TryRetargetNew(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            ref SkeletonCache sourceCache,
            ref MuscleClipCache sourceMuscleClipCache,
            Avatar targetAvatar,
            ref SkeletonCache targetCache,
            bool exportMuscleClip,
            AnimationClip cachedSourceMuscleClip,
            out AnimationClip targetClip,
            out string error)
        {
            targetClip = sourceClip;
            error = string.Empty;

            if (sourceClip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (exportMuscleClip && sourceClip.isHumanMotion)
            {
                return true;
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

            float frameRate = sourceClip.frameRate > 0f ? sourceClip.frameRate : KimodoPlayableClip.FIXED_FRAME_RATE;
            float duration = Mathf.Max(0f, sourceClip.length);
            int frameCount = ResolveFrameCount(duration, frameRate);
            bool needsSourceCache = exportMuscleClip && !sourceClip.isHumanMotion;
            bool needsTargetCache = !exportMuscleClip;

            if (needsSourceCache && !ValidateRetargetCache(sourceCache, out _))
            {
                sourceCache = null;
                if (!TryBuildSkeletonCache(sourceAvatar, "KimodoRetargetTools_SourceClipBatch", out sourceCache, out error))
                {
                    return false;
                }
            }

            if (needsTargetCache && !ValidateRetargetCache(targetCache, out _))
            {
                targetCache = null;
                if (!TryBuildSkeletonCache(targetAvatar, "KimodoRetargetTools_TargetClipBatch", out targetCache, out error))
                {
                    return false;
                }
            }

            if (targetClip != null)
            {
                targetClip.frameRate = frameRate;
            }

            if (exportMuscleClip)
            {
                if (sourceClip.isHumanMotion)
                {
                    return true;
                }

                if (!TryCollectMuscleSamplesFromClip(
                        sourceClip,
                        sourceCache,
                        frameCount,
                        duration,
                        KimodoRetargetClipSamplingUtility.ResolveClipSamplingMode(sourceClip),
                        out MuscleSample[] targetMuscleSamples,
                        out error))
                {
                    return false;
                }

                return WriteMuscleSampleToMuscleClip(targetMuscleSamples, targetClip, out error);
            }

            if (!TryResolveSourceHumanoidClip(
                    sourceClip,
                    sourceAvatar,
                    "KimodoRetargetTools_SourceClipBatch",
                    cachedSourceMuscleClip,
                    ref sourceCache,
                    ref sourceMuscleClipCache,
                    out AnimationClip sourceHumanoidClip,
                    out error))
            {
                return false;
            }

            if (!TryCollectBoneSamplesFromClip(
                    sourceHumanoidClip,
                    targetCache,
                    frameCount,
                    duration,
                    KimodoRetargetClipSamplingUtility.ClipSamplingMode.Humanoid,
                    out BoneSample[] targetBoneSamples,
                    out error))
            {
                return false;
            }

            return WriteBoneSampleToBoneClip(targetBoneSamples, targetClip, out error);
        }


        private static bool TryResolveSourceHumanoidClip(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            string cacheRootName,
            AnimationClip cachedSourceHumanoidClip,
            ref SkeletonCache sourceCache,
            ref MuscleClipCache sourceMuscleClipCache,
            out AnimationClip sourceHumanoidClip,
            out string error)
        {
            sourceHumanoidClip = cachedSourceHumanoidClip ?? sourceClip;
            error = string.Empty;

            if (sourceHumanoidClip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (cachedSourceHumanoidClip != null || sourceClip.isHumanMotion)
            {
                return true;
            }

            if (!ValidateRetargetCache(sourceCache, out _))
            {
                sourceCache = null;
                if (!TryBuildSkeletonCache(sourceAvatar, cacheRootName, out sourceCache, out error))
                {
                    return false;
                }
            }

            bool needsRebuildMuscleCache =
                sourceMuscleClipCache == null ||
                !sourceMuscleClipCache.IsReady ||
                sourceMuscleClipCache.sourceClip != sourceClip ||
                sourceMuscleClipCache.sourceAvatar != sourceAvatar;

            if (needsRebuildMuscleCache)
            {
                sourceMuscleClipCache?.Dispose();
                sourceMuscleClipCache = null;
                if (!TryBuildMuscleClipCache(sourceClip, sourceCache, out sourceMuscleClipCache, out error))
                {
                    return false;
                }
            }

            sourceHumanoidClip = sourceMuscleClipCache.muscleClip;
            if (sourceHumanoidClip != null)
            {
                return true;
            }

            error = "Failed to build source humanoid clip.";
            return false;
        }


        private static bool TrySampleFromClip<TSample>(
            AnimationClip clip,
            SkeletonCache cache,
            float sampleTime,
            string rootName,
            KimodoRetargetClipSamplingUtility.ClipSamplingMode samplingMode,
            ClipSampleCallback<TSample> sampleCallback,
            out TSample sample,
            out string error)
        {
            sample = default;
            error = string.Empty;

            if (!KimodoRetargetClipSamplingUtility.TryBuildClipSamplingContext(
                    clip,
                    cache,
                    rootName,
                    samplingMode,
                    out KimodoRetargetClipSamplingUtility.ClipSamplingContext context,
                    out error))
            {
                return false;
            }

            try
            {
                KimodoRetargetClipSamplingUtility.ResetSkeletonCachePose(context.cache);
                return sampleCallback(context, sampleTime, out sample, out error);
            }
            finally
            {
                KimodoRetargetClipSamplingUtility.DestroyClipSamplingContext(context);
            }
        }

        private static bool TryCollectSamplesFromClip<TSample>(
            AnimationClip clip,
            SkeletonCache cache,
            int frameCount,
            float duration,
            string rootName,
            KimodoRetargetClipSamplingUtility.ClipSamplingMode samplingMode,
            ClipSampleCallback<TSample> sampleCallback,
            Func<TSample, TSample> cloneSample,
            out TSample[] samples,
            out string error)
        {
            samples = null;
            error = string.Empty;

            if (!KimodoRetargetClipSamplingUtility.TryBuildClipSamplingContext(
                    clip,
                    cache,
                    rootName,
                    samplingMode,
                    out KimodoRetargetClipSamplingUtility.ClipSamplingContext context,
                    out error))
            {
                return false;
            }

            try
            {
                KimodoRetargetClipSamplingUtility.ResetSkeletonCachePose(context.cache);
                samples = new TSample[frameCount];
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = FrameToTime(frame, frameCount, duration);
                    if (!sampleCallback(context, time, out TSample sample, out error))
                    {
                        return false;
                    }

                    samples[frame] = cloneSample(sample);
                }

                return true;
            }
            finally
            {
                KimodoRetargetClipSamplingUtility.DestroyClipSamplingContext(context);
            }
        }

        private static bool TrySampleBoneClipToBoneSample(
            KimodoRetargetClipSamplingUtility.ClipSamplingContext context,
            float sampleTime,
            out BoneSample sample,
            out string error)
        {
            sample = null;
            error = string.Empty;

            if (!KimodoRetargetClipSamplingUtility.TryEvaluateClipSamplingContext(context, sampleTime, out error))
            {
                return false;
            }

            sample = CaptureBoneSample(context.cache);
            return true;
        }

        private static bool TrySampleMuscleClipToMuscleSample(
            KimodoRetargetClipSamplingUtility.ClipSamplingContext context,
            float sampleTime,
            out MuscleSample sample,
            out string error)
        {
            sample = null;
            error = string.Empty;

            if (!KimodoRetargetClipSamplingUtility.TryEvaluateClipSamplingContext(context, sampleTime, out error))
            {
                return false;
            }

            return TryCaptureMuscleSample(context.cache, out sample, out error);
        }

        private static bool TryCollectBoneSamplesFromClip(
            AnimationClip clip,
            SkeletonCache cache,
            int frameCount,
            float duration,
            KimodoRetargetClipSamplingUtility.ClipSamplingMode samplingMode,
            out BoneSample[] samples,
            out string error)
        {
            return TryCollectSamplesFromClip(
                clip,
                cache,
                frameCount,
                duration,
                "KimodoRetargetTools_BatchBoneSampler",
                samplingMode,
                TrySampleBoneClipToBoneSample,
                CloneBoneSample,
                out samples,
                out error);
        }

        private static bool TryCollectMuscleSamplesFromClip(
            AnimationClip clip,
            SkeletonCache cache,
            int frameCount,
            float duration,
            KimodoRetargetClipSamplingUtility.ClipSamplingMode samplingMode,
            out MuscleSample[] samples,
            out string error)
        {
            return TryCollectSamplesFromClip(
                clip,
                cache,
                frameCount,
                duration,
                "KimodoRetargetTools_BatchMuscleSampler",
                samplingMode,
                TrySampleMuscleClipToMuscleSample,
                CloneMuscleSample,
                out samples,
                out error);
        }

        private static bool TrySampleTargetFromHumanoidClip(
            AnimationClip sourceHumanoidClip,
            SkeletonCache targetCache,
            float sampleTime,
            out BoneSample targetSample,
            out MuscleSample targetMuscleSample,
            out string error)
        {
            targetSample = null;
            targetMuscleSample = null;
            error = string.Empty;

            if (!ValidateRetargetCache(targetCache, out error))
            {
                return false;
            }

            if (sourceHumanoidClip == null)
            {
                error = "Source humanoid clip is null.";
                return false;
            }

            if (!KimodoRetargetClipSamplingUtility.TryBuildClipSamplingContext(
                    sourceHumanoidClip,
                    targetCache,
                    "KimodoRetargetTools_TargetHumanoidSample",
                    KimodoRetargetClipSamplingUtility.ClipSamplingMode.Humanoid,
                    out KimodoRetargetClipSamplingUtility.ClipSamplingContext context,
                    out error))
            {
                return false;
            }

            try
            {
                KimodoRetargetClipSamplingUtility.ResetSkeletonCachePose(context.cache);
                if (!TrySampleBoneClipToBoneSample(context, sampleTime, out targetSample, out error))
                {
                    return false;
                }

                if (!ValidateBoneSample(targetSample, out error))
                {
                    targetSample = null;
                    return false;
                }

                if (!TryCaptureMuscleSample(targetCache, out targetMuscleSample, out error))
                {
                    targetSample = null;
                    return false;
                }

                return true;
            }
            finally
            {
                KimodoRetargetClipSamplingUtility.DestroyClipSamplingContext(context);
            }
        }

        private static string BuildTransientMuscleClipName(AnimationClip sourceClip)
        {
            string sourceName = sourceClip != null && !string.IsNullOrWhiteSpace(sourceClip.name)
                ? sourceClip.name
                : "Clip";
            return $"{sourceName}_musclecache";
        }

#if UNITY_EDITOR
        private static bool TryReplaceWithEditorCachedMuscleClip(
            AnimationClip sourceClip,
            IReadOnlyList<MuscleSample> samples,
            float frameRate,
            AnimationClip transientClip,
            out AnimationClip cachedClip,
            out string error)
        {
            cachedClip = transientClip;
            error = string.Empty;

            if (sourceClip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            Type writebackType = Type.GetType("KimodoBridge.Editor.KimodoEditorClipWritebackService, KimodoTool.Editor");
            if (writebackType == null)
            {
                return false;
            }

            MethodInfo method = writebackType.GetMethod(
                "TryGetOrCreateClipCache",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(AnimationClip), typeof(AnimationClip).MakeByRefType(), typeof(string).MakeByRefType() },
                null);
            if (method == null)
            {
                return false;
            }

            object[] args = { sourceClip, null, string.Empty };
            bool success;
            try
            {
                success = (bool)method.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (!success)
            {
                error = args[2] as string ?? string.Empty;
                return false;
            }

            AnimationClip editorClip = args[1] as AnimationClip;
            if (editorClip == null)
            {
                error = "Editor clip cache returned null clip.";
                return false;
            }

            editorClip.frameRate = frameRate > 0f ? frameRate : editorClip.frameRate;
            if (!WriteMuscleSampleToMuscleClip(samples, editorClip, out error))
            {
                return false;
            }

            editorClip.name = BuildTransientMuscleClipName(sourceClip);

            if (transientClip != null)
            {
                UnityEngine.Object.DestroyImmediate(transientClip);
            }

            cachedClip = editorClip;
            return true;
        }
#endif

        private static int ResolveFrameCount(float duration, float frameRate)
        {
            return Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(0f, duration) * Mathf.Max(1f, frameRate)) + 1);
        }

        private static BoneSample CaptureBoneSample(SkeletonCache cache)
        {
            var sample = new BoneSample
            {
                boneNames = cache.bonePaths,
                localPositions = new Vector3[cache.bonePaths.Length],
                localRotations = new Quaternion[cache.bonePaths.Length]
            };

            for (int i = 0; i < cache.boneTransforms.Length; i++)
            {
                Transform transform = cache.boneTransforms[i];
                if (transform == null)
                {
                    sample.localPositions[i] = Vector3.zero;
                    sample.localRotations[i] = Quaternion.identity;
                    continue;
                }

                sample.localPositions[i] = transform.localPosition;
                sample.localRotations[i] = transform.localRotation;
            }

            return sample;
        }

        internal static bool ValidateRetargetCache(SkeletonCache cache, out string error)
        {
            error = string.Empty;

            if (cache == null)
            {
                error = "Skeleton cache is null.";
                return false;
            }

            if (!cache.IsReady)
            {
                error = "Skeleton cache is not initialized.";
                return false;
            }

            if (cache.avatar == null)
            {
                error = "Skeleton cache avatar is null.";
                return false;
            }

            if (cache.bonePaths == null || cache.boneTransforms == null)
            {
                error = "Skeleton cache bone mapping is missing.";
                return false;
            }

            if (cache.bonePaths.Length == 0 || cache.bonePaths.Length != cache.boneTransforms.Length)
            {
                error = "Skeleton cache bone mapping is invalid.";
                return false;
            }

            return true;
        }

        private static bool ValidateBoneSample(BoneSample sample, out string error)
        {
            error = string.Empty;

            if (sample == null)
            {
                error = "Bone sample is null.";
                return false;
            }

            if (!sample.IsValid)
            {
                error = "Bone sample is invalid.";
                return false;
            }

            if (sample.boneNames.Length == 0)
            {
                error = "Bone sample is empty.";
                return false;
            }

            return true;
        }

        private static BoneSample CloneBoneSample(BoneSample source)
        {
            if (source == null || !source.IsValid)
            {
                return null;
            }

            int count = source.boneNames.Length;
            var clone = new BoneSample
            {
                boneNames = new string[count],
                localPositions = new Vector3[count],
                localRotations = new Quaternion[count]
            };

            Array.Copy(source.boneNames, clone.boneNames, count);
            Array.Copy(source.localPositions, clone.localPositions, count);
            Array.Copy(source.localRotations, clone.localRotations, count);
            return clone;
        }

        private static MuscleSample CloneMuscleSample(MuscleSample source)
        {
            if (source == null)
            {
                return null;
            }

            HumanPose pose = source.pose;
            if (pose.muscles != null)
            {
                float[] muscles = new float[pose.muscles.Length];
                Array.Copy(pose.muscles, muscles, pose.muscles.Length);
                pose.muscles = muscles;
            }

            return new MuscleSample
            {
                pose = pose,
                leftFootPosition = source.leftFootPosition,
                leftFootRotation = source.leftFootRotation,
                rightFootPosition = source.rightFootPosition,
                rightFootRotation = source.rightFootRotation,
                leftHandPosition = source.leftHandPosition,
                leftHandRotation = source.leftHandRotation,
                rightHandPosition = source.rightHandPosition,
                rightHandRotation = source.rightHandRotation
            };
        }

        private static MuscleSample LerpMuscleSample(MuscleSample a, MuscleSample b, float t)
        {
            if (a == null || b == null)
            {
                return null;
            }

            HumanPose poseA = a.pose;
            HumanPose poseB = b.pose;
            KimodoRetargetClipWriter.EnsureHumanPoseMuscles(ref poseA);
            KimodoRetargetClipWriter.EnsureHumanPoseMuscles(ref poseB);

            var pose = new HumanPose
            {
                bodyPosition = Vector3.Lerp(poseA.bodyPosition, poseB.bodyPosition, t),
                bodyRotation = Quaternion.Slerp(poseA.bodyRotation, poseB.bodyRotation, t),
                muscles = new float[HumanTrait.MuscleCount]
            };

            for (int i = 0; i < pose.muscles.Length; i++)
            {
                float muscleA = i < poseA.muscles.Length ? poseA.muscles[i] : 0f;
                float muscleB = i < poseB.muscles.Length ? poseB.muscles[i] : 0f;
                pose.muscles[i] = Mathf.Lerp(muscleA, muscleB, t);
            }

            return new MuscleSample
            {
                pose = pose,
                leftFootPosition = Vector3.Lerp(a.leftFootPosition, b.leftFootPosition, t),
                leftFootRotation = Quaternion.Slerp(a.leftFootRotation, b.leftFootRotation, t),
                rightFootPosition = Vector3.Lerp(a.rightFootPosition, b.rightFootPosition, t),
                rightFootRotation = Quaternion.Slerp(a.rightFootRotation, b.rightFootRotation, t),
                leftHandPosition = Vector3.Lerp(a.leftHandPosition, b.leftHandPosition, t),
                leftHandRotation = Quaternion.Slerp(a.leftHandRotation, b.leftHandRotation, t),
                rightHandPosition = Vector3.Lerp(a.rightHandPosition, b.rightHandPosition, t),
                rightHandRotation = Quaternion.Slerp(a.rightHandRotation, b.rightHandRotation, t)
            };
        }

        private static float FrameToTime(int frame, int frameCount, float duration)
        {
            if (frameCount <= 1 || duration <= 0f)
            {
                return 0f;
            }

            float normalized = frame / Mathf.Max(1f, frameCount - 1f);
            return Mathf.Clamp01(normalized) * duration;
        }

        private static bool TryCaptureMuscleSample(SkeletonCache cache, out MuscleSample sample, out string error)
        {
            sample = null;
            error = string.Empty;

            if (!ValidateRetargetCache(cache, out error))
            {
                return false;
            }

            try
            {
                var pose = new HumanPose();
                cache.poseHandler.GetHumanPose(ref pose);
                KimodoRetargetClipWriter.EnsureHumanPoseMuscles(ref pose);
                sample = KimodoRetargetHumanoidIkUtility.BuildMuscleSampleFromPose(cache, pose);
                return sample != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryCreateTransientMuscleClip(
            IReadOnlyList<MuscleSample> samples,
            float frameRate,
            out AnimationClip clip,
            out string error)
        {
            clip = null;
            error = string.Empty;

            if (samples == null || samples.Count == 0)
            {
                error = "Muscle samples are empty.";
                return false;
            }

            clip = new AnimationClip
            {
                frameRate = frameRate > 0f ? frameRate : KimodoPlayableClip.FIXED_FRAME_RATE,
                hideFlags = HideFlags.HideAndDontSave,
                name = "KimodoTransientMuscleClip"
            };

            if (!WriteMuscleSampleToMuscleClip(samples, clip, out error))
            {
                UnityEngine.Object.DestroyImmediate(clip);
                clip = null;
                return false;
            }

            return true;
        }

        private static bool TryCreateTransientBoneClip(
            BoneSample sample,
            float frameRate,
            out AnimationClip clip,
            out string error)
        {
            clip = null;
            error = string.Empty;

            if (!ValidateBoneSample(sample, out error))
            {
                return false;
            }

            clip = new AnimationClip
            {
                frameRate = frameRate > 0f ? frameRate : KimodoPlayableClip.FIXED_FRAME_RATE,
                hideFlags = HideFlags.HideAndDontSave,
                name = "KimodoTransientBoneClip"
            };

            if (!WriteBoneSampleToBoneClip(new[] { sample }, clip, out error))
            {
                UnityEngine.Object.DestroyImmediate(clip);
                clip = null;
                return false;
            }

            return true;
        }

        private static bool WriteMuscleCurves(IReadOnlyList<MuscleSample> samples, AnimationClip clip, out string error)
        {
            return KimodoRetargetClipWriter.WriteMuscleCurves(samples, clip, out error);
        }

        private static bool WriteBoneCurves(IReadOnlyList<BoneSample> samples, AnimationClip clip, out string error)
        {
            return KimodoRetargetClipWriter.WriteBoneCurves(samples, clip, out error);
        }

        private static MuscleSample BuildMuscleSampleFromPose(SkeletonCache cache, HumanPose pose)
        {
            return KimodoRetargetHumanoidIkUtility.BuildMuscleSampleFromPose(cache, pose);
        }

    }
}
