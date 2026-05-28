using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.AnimatorTooling
{
    internal sealed class KimodoAvatarPreviewCore : IDisposable
    {
        private enum PreviewBindingMode
        {
            None = 0,
            Clip = 1,
            Transition = 2
        }

        private KimodoAvatarPreview avatarPreview;
        private Animator previewAnimator;
        private AnimatorController previewController;
        private string previewControllerAssetPath;
        private AnimationClip activeClip;
        private string activeStateName;
        private int activeRootId;
        private bool restartRequested;
        private float lastSampledTime = float.NaN;
        private PreviewBindingMode activeMode;
        private int activeClipId;
        private int activeFromClipId;
        private int activeToClipId;
        private int activeTransitionId;

        public void Dispose()
        {
            if (avatarPreview != null)
            {
                avatarPreview.OnDisable();
                avatarPreview.OnDestroy();
                avatarPreview = null;
            }

            previewAnimator = null;
            activeClip = null;
            activeStateName = null;
            activeRootId = 0;
            activeMode = PreviewBindingMode.None;
            activeClipId = 0;
            activeFromClipId = 0;
            activeToClipId = 0;
            activeTransitionId = 0;
            lastSampledTime = float.NaN;

            if (previewController != null)
            {
                UnityEngine.Object.DestroyImmediate(previewController);
                previewController = null;
            }
        }

        public void SetClipPreview(GameObject root, AnimationClip clip, string emptyStateMessage)
        {
            if (!TryBindAnimator(root, clip))
            {
                activeClip = null;
                activeStateName = null;
                return;
            }

            int rootId = previewAnimator != null ? previewAnimator.gameObject.GetInstanceID() : 0;
            int clipId = clip.GetInstanceID();
            bool unchanged = activeMode == PreviewBindingMode.Clip &&
                             activeRootId == rootId &&
                             activeClipId == clipId &&
                             !string.IsNullOrEmpty(activeStateName);
            if (unchanged)
            {
                return;
            }

            EnsurePreviewController();
            string stateName = EnsureClipState(clip);
            BindControllerAndPlay(stateName, 0f);
            EnsureAvatarPreview(clip);
            activeMode = PreviewBindingMode.Clip;
            activeClipId = clipId;
            activeFromClipId = 0;
            activeToClipId = 0;
            activeTransitionId = 0;
        }

        public void SetTransitionPreview(GameObject root, AnimationClip fromClip, AnimationClip toClip, AnimatorStateTransition transition, string emptyStateMessage)
        {
            if (!TryBindAnimator(root, fromClip) || toClip == null || transition == null)
            {
                activeClip = null;
                activeStateName = null;
                return;
            }

            int rootId = previewAnimator != null ? previewAnimator.gameObject.GetInstanceID() : 0;
            int fromId = fromClip.GetInstanceID();
            int toId = toClip.GetInstanceID();
            int transitionId = transition.GetInstanceID();
            bool unchanged = activeMode == PreviewBindingMode.Transition &&
                             activeRootId == rootId &&
                             activeFromClipId == fromId &&
                             activeToClipId == toId &&
                             activeTransitionId == transitionId &&
                             !string.IsNullOrEmpty(activeStateName);
            if (unchanged)
            {
                return;
            }

            EnsurePreviewController();
            string fromStateName = EnsureTransitionGraph(fromClip, toClip, transition);
            BindControllerAndPlay(fromStateName, 0f);
            EnsureAvatarPreview(fromClip);
            activeMode = PreviewBindingMode.Transition;
            activeClipId = 0;
            activeFromClipId = fromId;
            activeToClipId = toId;
            activeTransitionId = transitionId;
        }

        public void RestartFromZeroAndPlay()
        {
            restartRequested = true;
        }

        public void Draw(Rect rect)
        {
            if (avatarPreview == null || previewAnimator == null || activeClip == null)
            {
                EditorGUI.DropShadowLabel(rect, "Preview not ready.");
                return;
            }

            if (restartRequested)
            {
                avatarPreview.timeControl.currentTime = 0f;
                avatarPreview.timeControl.playing = true;
                lastSampledTime = float.NaN;
                restartRequested = false;
            }

            if (Event.current.type == EventType.Repaint)
            {
                avatarPreview.timeControl.loop = true;
                avatarPreview.timeControl.Update();

                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(activeClip);
                float denom = Mathf.Max(0.0001f, settings.stopTime - settings.startTime);
                float normalized = (avatarPreview.timeControl.currentTime - settings.startTime) / denom;
                normalized = Mathf.Clamp01(normalized);

                bool timeChanged = float.IsNaN(lastSampledTime) || !Mathf.Approximately(lastSampledTime, avatarPreview.timeControl.currentTime);
                bool shouldSample = avatarPreview.timeControl.playing || timeChanged;
                if (!string.IsNullOrEmpty(activeStateName) && shouldSample)
                {
                    previewAnimator.Play(activeStateName, 0, normalized);
                    previewAnimator.Update(avatarPreview.timeControl.playing ? avatarPreview.timeControl.deltaTime : 0f);
                    lastSampledTime = avatarPreview.timeControl.currentTime;
                }
            }

            avatarPreview.DoAvatarPreview(rect, KimodoPreviewConstants.PreviewBackgroundSolid);
        }

        private bool TryBindAnimator(GameObject root, AnimationClip clip)
        {
            if (root == null || clip == null)
            {
                return false;
            }

            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                return false;
            }

            previewAnimator = animator;
            activeClip = clip;
            activeRootId = animator.gameObject.GetInstanceID();
            return true;
        }

        private void EnsurePreviewController()
        {
            if (previewController != null)
            {
                return;
            }

            if (string.IsNullOrEmpty(previewControllerAssetPath))
            {
                previewControllerAssetPath = "Assets/Temp/KimodoPreview_" + Guid.NewGuid().ToString("N") + ".controller";
            }

            string dir = Path.GetDirectoryName(previewControllerAssetPath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }

            previewController = AnimatorController.CreateAnimatorControllerAtPath(previewControllerAssetPath);
            if (previewController.layers == null || previewController.layers.Length == 0)
            {
                previewController.AddLayer("Base Layer");
            }
        }

        private string EnsureClipState(AnimationClip clip)
        {
            AnimatorStateMachine sm = previewController.layers[0].stateMachine;
            string stateName = "Clip_" + clip.GetInstanceID();
            AnimatorState state = FindState(sm, stateName) ?? sm.AddState(stateName);
            state.motion = clip;
            sm.defaultState = state;
            EditorUtility.SetDirty(previewController);
            return stateName;
        }

        private string EnsureTransitionGraph(AnimationClip fromClip, AnimationClip toClip, AnimatorStateTransition source)
        {
            AnimatorStateMachine sm = previewController.layers[0].stateMachine;
            string fromName = "TransitionFrom_" + fromClip.GetInstanceID();
            string toName = "TransitionTo_" + toClip.GetInstanceID();

            AnimatorState from = FindState(sm, fromName) ?? sm.AddState(fromName);
            AnimatorState to = FindState(sm, toName) ?? sm.AddState(toName);
            from.motion = fromClip;
            to.motion = toClip;

            RemoveTransitionsTo(from, to);
            RemoveTransitionsTo(to, from);

            AnimatorStateTransition fromTo = from.AddTransition(to);
            CopyTransitionWithoutConditions(fromTo, source);
            if (!fromTo.hasExitTime)
            {
                fromTo.hasExitTime = true;
                fromTo.exitTime = 1f;
            }

            AnimatorStateTransition toFrom = to.AddTransition(from);
            toFrom.hasExitTime = true;
            toFrom.exitTime = 1f;
            toFrom.hasFixedDuration = true;
            toFrom.duration = 0f;
            toFrom.offset = 0f;
            toFrom.interruptionSource = TransitionInterruptionSource.None;
            toFrom.orderedInterruption = false;
            toFrom.canTransitionToSelf = false;

            sm.defaultState = from;
            EditorUtility.SetDirty(previewController);
            return fromName;
        }

        private void BindControllerAndPlay(string stateName, float normalized)
        {
            activeStateName = stateName;
            previewAnimator.runtimeAnimatorController = previewController;
            previewAnimator.enabled = true;
            previewAnimator.applyRootMotion = false;
            previewAnimator.Rebind();
            previewAnimator.Update(0f);
            previewAnimator.Play(stateName, 0, normalized);
            previewAnimator.Update(0f);
        }

        private void EnsureAvatarPreview(AnimationClip clip)
        {
            bool needRecreate = avatarPreview == null || previewAnimator == null || activeClip != clip || activeRootId != previewAnimator.gameObject.GetInstanceID();
            if (!needRecreate)
            {
                return;
            }

            if (avatarPreview != null)
            {
                avatarPreview.OnDisable();
                avatarPreview.OnDestroy();
                avatarPreview = null;
            }

            avatarPreview = new KimodoAvatarPreview(previewAnimator, clip);
            avatarPreview.ShowIKOnFeetButton = clip.isHumanMotion;
            avatarPreview.fps = Mathf.RoundToInt(clip.frameRate);
            avatarPreview.timeControl.stopTime = Mathf.Max(0.001f, clip.length);
            avatarPreview.ResetPreviewFocus();
            if (avatarPreview.timeControl.currentTime == Mathf.NegativeInfinity)
            {
                avatarPreview.timeControl.Update();
            }
            restartRequested = true;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string stateName)
        {
            ChildAnimatorState[] states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState s = states[i].state;
                if (s != null && s.name == stateName)
                {
                    return s;
                }
            }
            return null;
        }

        private static void RemoveTransitionsTo(AnimatorState from, AnimatorState to)
        {
            for (int i = from.transitions.Length - 1; i >= 0; i--)
            {
                if (from.transitions[i].destinationState == to)
                {
                    from.RemoveTransition(from.transitions[i]);
                }
            }
        }

        private static void CopyTransitionWithoutConditions(AnimatorStateTransition dst, AnimatorStateTransition src)
        {
            dst.hasExitTime = src.hasExitTime;
            dst.exitTime = src.exitTime;
            dst.duration = src.duration;
            dst.hasFixedDuration = src.hasFixedDuration;
            dst.offset = src.offset;
            dst.interruptionSource = src.interruptionSource;
            dst.orderedInterruption = src.orderedInterruption;
            dst.canTransitionToSelf = src.canTransitionToSelf;
        }
    }
}
