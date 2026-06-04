using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using TimelineInject;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor
{
    internal static class KimodoPlayableClipGenerationHostService
    {
        private static readonly KimodoEditorConstraintProvider ConstraintProvider = new KimodoEditorConstraintProvider();
        private static readonly KimodoEditorClipWritebackService ClipWritebackService = new KimodoEditorClipWritebackService();

        public static KimodoEditorGenerateRequest BuildRequest(
            KimodoPlayableClip clip,
            string prompt,
            KimodoExternalConstraintRequest externalConstraint,
            CancellationToken token)
        {
            if (clip == null)
            {
                throw new InvalidOperationException("Playable clip is null.");
            }

            string constraintsJson;
            if (externalConstraint != null && externalConstraint.Enabled)
            {
                constraintsJson = externalConstraint.ConstraintsJson ?? string.Empty;
            }
            else
            {
                constraintsJson = ConstraintProvider.BuildConstraintsJsonOrThrow(clip);
            }

            EnsureTargetClip(clip);

            string resolvedModelName = string.IsNullOrWhiteSpace(clip.bridgeModelName) ? "Kimodo-SOMA-RP-v1" : clip.bridgeModelName.Trim();
            Avatar originRetargetAvatar = ResolveOriginRetargetAvatar(resolvedModelName);
            Avatar targetRetargetAvatar = ResolveTargetRetargetAvatar(clip, externalConstraint?.RetargetAvatar, out bool hasBindingAvatar);
            bool hasValidRetargetAvatar =
                IsValidHumanoid(originRetargetAvatar) &&
                hasBindingAvatar &&
                IsValidHumanoid(targetRetargetAvatar);

            GameObject bindingObject = ConstraintProvider.FindTimelineBindingObjectForAsset(clip);
            bool exportMuscleClip = hasValidRetargetAvatar && TryResolveBindingAnimatorAvatar(clip, out _);

            int effectiveSeed = ResolveEffectiveSeed(clip);
            return new KimodoEditorGenerateRequest
            {
                Prompt = prompt,
                ModelName = resolvedModelName,
                GenerationBackend = clip.generationBackend,
                BridgeVramMode = clip.bridgeVramMode,
                DurationSeconds = Mathf.Clamp(clip.generationFrames, KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES) / KimodoPlayableClip.FIXED_FRAME_RATE,
                DiffusionSteps = Mathf.Clamp(clip.diffusionSteps, 1, 1000),
                EffectiveSeed = effectiveSeed,
                ConstraintsJson = constraintsJson,
                OriginRetargetAvatar = originRetargetAvatar,
                TargetRetargetAvatar = targetRetargetAvatar,
                ExportMuscleClip = exportMuscleClip,
                DirectBindingRoot = hasValidRetargetAvatar ? null : bindingObject,
                ModelsRoot = KimodoPlayableClipGenerationSettings.instance.LocalModelsPath?.Trim() ?? string.Empty,
                ComfyHost = clip.comfyuiIP,
                ComfyPort = clip.comfyuiPort,
                GenerationTimeoutSeconds = KimodoPlayableClipGenerationSettings.instance.GenerationTimeoutSeconds,
                TargetClip = clip.clip,
                Token = token
            };
        }

        public static void FinalizeGeneration(
            KimodoPlayableClip clip,
            KimodoEditorGenerateRequest request,
            KimodoEditorGenerateResult result)
        {
            if (clip == null || request == null || result == null || result.GeneratedClip == null)
            {
                return;
            }

            clip.clip = result.GeneratedClip;
            ApplyGeneratedMetadata(clip, result.Prompt, result.MotionJsonCompact);
            TrimGeneratedClipsToLimit(clip);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(result.GeneratedClip);
            result.ConstraintsPath = string.IsNullOrWhiteSpace(request.ConstraintsJson) ? "(none)" : "(inline-json)";
            HandleGeneratedClipWritebackCompleted(clip);
        }

        public static IReadOnlyList<KimodoConstraintMarkerBase> GetLatestConstraintMarkers()
        {
            return KimodoEditorConstraintProvider.LatestMarkers;
        }

        private static void EnsureTargetClip(KimodoPlayableClip clip)
        {
            if (clip == null || clip.clip != null)
            {
                return;
            }

            clip.clip = ClipWritebackService.CreateGeneratedAnimationClipAsset();
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(clip.clip);
            AssetDatabase.SaveAssets();
        }

        private static void ApplyGeneratedMetadata(KimodoPlayableClip clip, string prompt, string motionJson)
        {
            if (clip == null || string.IsNullOrWhiteSpace(motionJson))
            {
                return;
            }

            JObject obj = JObject.Parse(motionJson);
            clip.lastGeneratedPrompt = prompt ?? string.Empty;
            clip.isGenerated = true;
            clip.frameCount = obj.Value<int?>("num_frames") ?? 0;
            clip.jointCount = obj.Value<int?>("num_joints") ?? 0;
            clip.fps = Mathf.RoundToInt(KimodoPlayableClip.FIXED_FRAME_RATE);
        }

        private static void TrimGeneratedClipsToLimit(KimodoPlayableClip clip)
        {
            int maxCount = Mathf.Clamp(
                KimodoPlayableClipGenerationSettings.instance.MaxGeneratedClips,
                KimodoPlayableClipGenerationSettings.MinGeneratedClipsLimit,
                KimodoPlayableClipGenerationSettings.MaxGeneratedClipsLimit);

            if (!AssetDatabase.IsValidFolder(KimodoEditorClipWritebackService.GeneratedClipFolder))
            {
                return;
            }

            string[] clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { KimodoEditorClipWritebackService.GeneratedClipFolder });
            if (clipGuids == null || clipGuids.Length <= maxCount)
            {
                return;
            }

            var clipPaths = new List<string>(clipGuids.Length);
            foreach (string guid in clipGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                if (!name.StartsWith(KimodoEditorClipWritebackService.GeneratedClipNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                clipPaths.Add(path);
            }

            if (clipPaths.Count <= maxCount)
            {
                return;
            }

            clipPaths.Sort(CompareGeneratedClipPathsByAgeOldestFirst);
            string activeClipPath = clip != null && clip.clip != null ? AssetDatabase.GetAssetPath(clip.clip) : string.Empty;

            foreach (string candidatePath in clipPaths)
            {
                if (!string.IsNullOrWhiteSpace(activeClipPath) &&
                    string.Equals(candidatePath, activeClipPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsAssetReferencedByOtherAssets(candidatePath))
                {
                    Debug.Log($"[Kimodo] Generated clip cleanup skipped referenced clip: {candidatePath}");
                    return;
                }

                if (AssetDatabase.DeleteAsset(candidatePath))
                {
                    AssetDatabase.SaveAssets();
                }

                return;
            }
        }

        private static int CompareGeneratedClipPathsByAgeOldestFirst(string leftPath, string rightPath)
        {
            string leftName = Path.GetFileNameWithoutExtension(leftPath) ?? string.Empty;
            string rightName = Path.GetFileNameWithoutExtension(rightPath) ?? string.Empty;
            string leftStamp = leftName.StartsWith(KimodoEditorClipWritebackService.GeneratedClipNamePrefix, StringComparison.Ordinal)
                ? leftName.Substring(KimodoEditorClipWritebackService.GeneratedClipNamePrefix.Length)
                : leftName;
            string rightStamp = rightName.StartsWith(KimodoEditorClipWritebackService.GeneratedClipNamePrefix, StringComparison.Ordinal)
                ? rightName.Substring(KimodoEditorClipWritebackService.GeneratedClipNamePrefix.Length)
                : rightName;
            return string.Compare(leftStamp, rightStamp, StringComparison.Ordinal);
        }

        private static bool IsAssetReferencedByOtherAssets(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            foreach (string path in allAssets)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    string.Equals(path, assetPath, StringComparison.OrdinalIgnoreCase) ||
                    !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] dependencies;
                try
                {
                    dependencies = AssetDatabase.GetDependencies(path, false);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < dependencies.Length; i++)
                {
                    if (string.Equals(dependencies[i], assetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void HandleGeneratedClipWritebackCompleted(KimodoPlayableClip playableClip)
        {
            try
            {
                KimodoTimelinePreviewRefreshUtility.RefreshIfPreviewing();
                TryMatchOffsetsToPreviousClip(playableClip);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Kimodo] Generated clip post-writeback failed: {ex.Message}");
            }
        }

        private static void TryMatchOffsetsToPreviousClip(KimodoPlayableClip playableClip)
        {
            if (playableClip == null || TimelineEditor.inspectedDirector == null)
            {
                return;
            }

            TimelineClip timelineClip = KimodoTimelineClipResolver.FindTimelineClipForAsset(playableClip);
            if (timelineClip == null || FindPreviousClip(timelineClip) == null)
            {
                return;
            }

            if (!TryInvokeTimelineMatchClipsToPrevious(timelineClip, out string error))
            {
                Debug.LogWarning($"[Kimodo] Match Offsets to Previous Clip failed for '{playableClip.name}': {error}");
                return;
            }

            EditorUtility.SetDirty(playableClip);
            if (timelineClip.GetParentTrack() != null)
            {
                EditorUtility.SetDirty(timelineClip.GetParentTrack());
            }

            if (TimelineEditor.inspectedAsset != null)
            {
                EditorUtility.SetDirty(TimelineEditor.inspectedAsset);
            }
        }

        private static TimelineClip FindPreviousClip(TimelineClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            TrackAsset parentTrack = clip.GetParentTrack();
            if (parentTrack == null)
            {
                return null;
            }

            TimelineClip previousClip = null;
            foreach (TimelineClip candidate in parentTrack.GetClips())
            {
                if (candidate == null || candidate == clip || candidate.start >= clip.start)
                {
                    continue;
                }

                if (previousClip == null || candidate.start >= previousClip.start)
                {
                    previousClip = candidate;
                }
            }

            return previousClip;
        }

        private static bool TryInvokeTimelineMatchClipsToPrevious(TimelineClip clip, out string error)
        {
            error = string.Empty;
            Type animationOffsetMenuType = typeof(TimelineEditor).Assembly.GetType("UnityEditor.Timeline.AnimationOffsetMenu");
            if (animationOffsetMenuType == null)
            {
                error = "UnityEditor.Timeline.AnimationOffsetMenu not found.";
                return false;
            }

            MethodInfo matchMethod = animationOffsetMenuType.GetMethod(
                "MatchClipsToPrevious",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (matchMethod == null)
            {
                error = "MatchClipsToPrevious method not found.";
                return false;
            }

            try
            {
                matchMethod.Invoke(null, new object[] { new[] { clip } });
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                error = inner.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static Avatar ResolveOriginRetargetAvatar(string modelName)
        {
            if (!KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out Avatar avatar, out _))
            {
                return null;
            }

            return IsValidHumanoid(avatar) ? avatar : null;
        }

        private static Avatar ResolveTargetRetargetAvatar(KimodoPlayableClip clip, Avatar explicitRetargetAvatar, out bool hasBindingAvatar)
        {
            hasBindingAvatar = false;
            if (explicitRetargetAvatar != null && explicitRetargetAvatar.isValid && explicitRetargetAvatar.isHuman)
            {
                hasBindingAvatar = true;
                return explicitRetargetAvatar;
            }

            GameObject bindingObject = ConstraintProvider.FindTimelineBindingObjectForAsset(clip);
            if (bindingObject != null)
            {
                KimodoLocalAvatarUtility.AvatarResolveResult result = KimodoLocalAvatarUtility.ResolveAvatarFromGameObject(bindingObject);
                if (result.IsHumanoid && result.Avatar != null)
                {
                    Animator animator = bindingObject.GetComponent<Animator>();
                    hasBindingAvatar = animator != null && animator.avatar != null;
                    return result.Avatar;
                }
            }

            if (clip.CustomRetargetAvatar != null && clip.CustomRetargetAvatar.isValid && clip.CustomRetargetAvatar.isHuman)
            {
                return clip.CustomRetargetAvatar;
            }

            return null;
        }

        private static bool TryResolveBindingAnimatorAvatar(KimodoPlayableClip clip, out Avatar avatar)
        {
            avatar = null;
            GameObject bindingObject = ConstraintProvider.FindTimelineBindingObjectForAsset(clip);
            if (bindingObject == null)
            {
                return false;
            }

            KimodoLocalAvatarUtility.AvatarResolveResult result = KimodoLocalAvatarUtility.ResolveAvatarFromGameObject(bindingObject);
            if (!result.IsHumanoid || result.Avatar == null)
            {
                return false;
            }

            if (!string.Equals(result.Source, "Animator", StringComparison.Ordinal))
            {
                return false;
            }

            avatar = result.Avatar;
            return true;
        }

        private static int ResolveEffectiveSeed(KimodoPlayableClip clip)
        {
            int effectiveSeed = clip.randomSeed
                ? Guid.NewGuid().GetHashCode() & int.MaxValue
                : clip.seed;

            if (clip.randomSeed || clip.seed != effectiveSeed)
            {
                clip.seed = effectiveSeed;
                EditorUtility.SetDirty(clip);
            }

            return effectiveSeed;
        }

        private static bool IsValidHumanoid(Avatar avatar)
        {
            return avatar != null && avatar.isValid && avatar.isHuman;
        }
    }
}
