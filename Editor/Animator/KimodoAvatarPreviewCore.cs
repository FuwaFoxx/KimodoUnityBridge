using System;
using ToolKits;
using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.AnimatorTooling
{
    internal sealed class KimodoAvatarPreviewCore : IDisposable
    {
        private AvatarPreview avatarPreview;
        private Animator previewAnimator;
        private Motion currentMotion;
        private bool requestedRestart;

        public void Dispose()
        {
            if (avatarPreview != null)
            {
                avatarPreview.OnDisable();
                avatarPreview.OnDestroy();
                avatarPreview = null;
            }
            previewAnimator = null;
            currentMotion = null;
        }

        public void SetPreview(GameObject root, AnimationClip clip, string emptyStateMessage)
        {
            if (root == null || clip == null)
            {
                currentMotion = null;
                return;
            }

            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                currentMotion = null;
                return;
            }

            bool animatorChanged = !ReferenceEquals(previewAnimator, animator);
            bool motionChanged = !ReferenceEquals(currentMotion, clip);
            if (avatarPreview == null || animatorChanged || motionChanged)
            {
                if (avatarPreview != null)
                {
                    avatarPreview.OnDisable();
                    avatarPreview.OnDestroy();
                }

                previewAnimator = animator;
                currentMotion = clip;
                avatarPreview = new AvatarPreview(previewAnimator, currentMotion);
                avatarPreview.ShowIKOnFeetButton = clip.isHumanMotion;
                avatarPreview.fps = Mathf.RoundToInt(clip.frameRate);
                avatarPreview.timeControl.stopTime = Mathf.Max(0.001f, clip.length);
                avatarPreview.ResetPreviewFocus();
                if (avatarPreview.timeControl.currentTime == Mathf.NegativeInfinity)
                {
                    avatarPreview.timeControl.Update();
                }
                requestedRestart = true;
            }
        }

        public void RestartFromZeroAndPlay()
        {
            requestedRestart = true;
        }

        public void Draw(Rect rect)
        {
            if (avatarPreview == null || previewAnimator == null || currentMotion == null)
            {
                EditorGUI.DropShadowLabel(rect, "Preview not ready.");
                return;
            }

            if (requestedRestart)
            {
                avatarPreview.timeControl.currentTime = 0f;
                avatarPreview.timeControl.playing = true;
                requestedRestart = false;
            }

            if (Event.current.type == EventType.Repaint)
            {
                avatarPreview.timeControl.loop = true;
                avatarPreview.timeControl.Update();
                if (currentMotion is AnimationClip clip)
                {
                    AnimationClipSettings previewInfo = AnimationUtility.GetAnimationClipSettings(clip);
                    float normalizedTime = previewInfo.stopTime - previewInfo.startTime != 0f
                        ? (avatarPreview.timeControl.currentTime - previewInfo.startTime) /
                          (previewInfo.stopTime - previewInfo.startTime)
                        : 0f;
                    avatarPreview.Animator.Play(0, 0, normalizedTime);
                    avatarPreview.Animator.Update(avatarPreview.timeControl.deltaTime);
                }
            }

            avatarPreview.DoAvatarPreview(rect, PreviewWindowConstants.preBackgroundSolid);
        }
    }
}
