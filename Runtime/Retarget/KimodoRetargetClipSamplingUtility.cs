using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KimodoBridge
{
    internal static class KimodoRetargetClipSamplingUtility
    {
        internal enum ClipSamplingMode
        {
            Humanoid = 0,
            RawTransform = 1
        }

        internal sealed class ClipSamplingContext
        {
            public SkeletonCache cache;
            public PlayableGraph graph;
            public AnimationClipPlayable clipPlayable;
            public bool restoreAnimatorAvatar;
            public Avatar originalAnimatorAvatar;

            public bool IsReady =>
                cache != null &&
                cache.IsReady &&
                graph.IsValid() &&
                clipPlayable.IsValid();
        }

        internal static void SetHierarchyHideFlags(Transform root, HideFlags hideFlags)
        {
            if (root == null)
            {
                return;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.hideFlags = hideFlags;
            }
        }

        internal static void CaptureSkeletonBindPose(SkeletonCache cache)
        {
            if (cache == null || cache.root == null || cache.boneTransforms == null)
            {
                return;
            }

            Transform rootTransform = cache.root.transform;
            cache.rootLocalPosition = rootTransform.localPosition;
            cache.rootLocalRotation = rootTransform.localRotation;
            cache.rootLocalScale = rootTransform.localScale;

            int count = cache.boneTransforms.Length;
            cache.bindLocalPositions = new Vector3[count];
            cache.bindLocalRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                Transform bone = cache.boneTransforms[i];
                if (bone == null)
                {
                    cache.bindLocalPositions[i] = Vector3.zero;
                    cache.bindLocalRotations[i] = Quaternion.identity;
                    continue;
                }

                cache.bindLocalPositions[i] = bone.localPosition;
                cache.bindLocalRotations[i] = bone.localRotation;
            }
        }

        internal static void ResetSkeletonCachePose(SkeletonCache cache)
        {
            if (!KimodoRetargetTools.ValidateRetargetCache(cache, out _))
            {
                return;
            }

            Transform rootTransform = cache.root != null ? cache.root.transform : null;
            if (rootTransform != null)
            {
                rootTransform.localPosition = cache.rootLocalPosition;
                rootTransform.localRotation = cache.rootLocalRotation;
                rootTransform.localScale = cache.rootLocalScale;
            }

            Transform[] bones = cache.boneTransforms;
            Vector3[] bindPositions = cache.bindLocalPositions;
            Quaternion[] bindRotations = cache.bindLocalRotations;
            if (bones == null || bindPositions == null || bindRotations == null)
            {
                return;
            }

            int count = Mathf.Min(bones.Length, Mathf.Min(bindPositions.Length, bindRotations.Length));
            for (int i = 0; i < count; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                bone.localPosition = bindPositions[i];
                bone.localRotation = bindRotations[i];
            }
        }

        internal static ClipSamplingMode ResolveClipSamplingMode(AnimationClip clip)
        {
            return clip != null && clip.isHumanMotion
                ? ClipSamplingMode.Humanoid
                : ClipSamplingMode.RawTransform;
        }

        internal static bool TryBuildClipSamplingContext(
            AnimationClip clip,
            SkeletonCache cache,
            string rootName,
            ClipSamplingMode samplingMode,
            out ClipSamplingContext context,
            out string error)
        {
            context = null;
            error = string.Empty;

            if (clip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (!KimodoRetargetTools.ValidateRetargetCache(cache, out error))
            {
                return false;
            }

            PlayableGraph graph = default;
            Avatar originalAnimatorAvatar = null;
            bool restoreAnimatorAvatar = false;
            try
            {
                if (!TryConfigureAnimatorForClipSampling(cache, samplingMode, out originalAnimatorAvatar, out restoreAnimatorAvatar, out error))
                {
                    return false;
                }

                graph = PlayableGraph.Create(rootName + "Graph");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(graph, clip);
                clipPlayable.SetApplyFootIK(true);
                clipPlayable.SetApplyPlayableIK(true);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, rootName + "Output", cache.animator);
                output.SetSourcePlayable(clipPlayable);
                graph.Play();

                context = new ClipSamplingContext
                {
                    cache = cache,
                    graph = graph,
                    clipPlayable = clipPlayable,
                    restoreAnimatorAvatar = restoreAnimatorAvatar,
                    originalAnimatorAvatar = originalAnimatorAvatar
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (graph.IsValid())
                {
                    graph.Destroy();
                }

                if (restoreAnimatorAvatar)
                {
                    RestoreAnimatorAfterClipSampling(cache, originalAnimatorAvatar);
                }

                return false;
            }
        }

        internal static void DestroyClipSamplingContext(ClipSamplingContext context)
        {
            if (context == null)
            {
                return;
            }

            if (context.graph.IsValid())
            {
                context.graph.Destroy();
            }

            if (context.restoreAnimatorAvatar)
            {
                RestoreAnimatorAfterClipSampling(context.cache, context.originalAnimatorAvatar);
            }
        }

        internal static bool TryEvaluateClipSamplingContext(ClipSamplingContext context, float sampleTime, out string error)
        {
            error = string.Empty;

            if (context == null || !context.IsReady)
            {
                error = "Clip sampling context is not initialized.";
                return false;
            }

            try
            {
                context.clipPlayable.SetTime(Mathf.Max(0f, sampleTime));
                context.graph.Evaluate(0f);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryConfigureAnimatorForClipSampling(
            SkeletonCache cache,
            ClipSamplingMode samplingMode,
            out Avatar originalAnimatorAvatar,
            out bool restoreAnimatorAvatar,
            out string error)
        {
            originalAnimatorAvatar = null;
            restoreAnimatorAvatar = false;
            error = string.Empty;

            if (!KimodoRetargetTools.ValidateRetargetCache(cache, out error))
            {
                return false;
            }

            Animator animator = cache.animator;
            if (animator == null)
            {
                error = "Skeleton cache animator is null.";
                return false;
            }

            originalAnimatorAvatar = animator.avatar;
            Avatar desiredAvatar = samplingMode == ClipSamplingMode.Humanoid ? cache.avatar : null;
            restoreAnimatorAvatar = !ReferenceEquals(originalAnimatorAvatar, desiredAvatar);

            ResetSkeletonCachePose(cache);
            animator.avatar = desiredAvatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);

            if (desiredAvatar != null)
            {
                cache.humanScale = Mathf.Max(1e-6f, animator.humanScale);
            }

            return true;
        }

        internal static void RestoreAnimatorAfterClipSampling(SkeletonCache cache, Avatar avatar)
        {
            if (cache?.animator == null)
            {
                return;
            }

            Animator animator = cache.animator;
            animator.avatar = avatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);

            if (avatar != null)
            {
                cache.humanScale = Mathf.Max(1e-6f, animator.humanScale);
            }
        }
    }
}
