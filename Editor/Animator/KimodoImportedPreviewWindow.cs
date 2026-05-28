using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.AnimatorTooling
{
    // Test harness only. This window is not used by production preview flow.
    public sealed class KimodoImportedPreviewWindow : EditorWindow
    {
        private static readonly Vector2 MinSizeValue = new Vector2(700f, 420f);
        private static readonly Rect PreviewRect = new Rect(0f, 60f, 620f, 320f);

        private GameObject previewPrefab;
        private Motion previewMotion;
        private GameObject previewInstance;
        private Animator previewAnimator;
        private AnimatorController previewController;
        private AnimatorState previewState;
        private AnimationClip previewClip;
        private KimodoAvatarPreview avatarPreview;

        [MenuItem("Kimodo/Preview Test Harness", priority = 120)]
        private static void Open()
        {
            var window = GetWindow<KimodoImportedPreviewWindow>("Preview Test Harness");
            window.minSize = MinSizeValue;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kimodo Preview Test Harness", EditorStyles.boldLabel);
            previewPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", previewPrefab, typeof(GameObject), false);
            previewMotion = (Motion)EditorGUILayout.ObjectField("Motion", previewMotion, typeof(Motion), false);

            if (GUILayout.Button("Load Preview", GUILayout.Height(24f)))
            {
                RebuildPreview();
            }

            if (avatarPreview != null)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    avatarPreview.timeControl.loop = true;
                    avatarPreview.timeControl.Update();
                    if (previewClip != null)
                    {
                        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(previewClip);
                        float denom = Mathf.Max(0.0001f, settings.stopTime - settings.startTime);
                        float normalized = (avatarPreview.timeControl.currentTime - settings.startTime) / denom;
                        normalized = Mathf.Clamp01(normalized);
                        avatarPreview.Animator.Play(0, 0, normalized);
                        avatarPreview.Animator.Update(avatarPreview.timeControl.deltaTime);
                    }
                }

                avatarPreview.DoAvatarPreview(PreviewRect, KimodoPreviewConstants.PreviewBackgroundSolid);
            }
        }

        private void RebuildPreview()
        {
            CleanupPreview();
            if (previewPrefab == null || previewMotion == null)
            {
                return;
            }

            previewInstance = KimodoPreviewEditorHelper.InstantiateGoByPrefab(previewPrefab, null);
            if (previewInstance == null)
            {
                return;
            }

            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            TrySetPreviewTag(previewInstance);

            previewAnimator = previewInstance.GetComponent<Animator>();
            if (previewAnimator == null)
            {
                previewAnimator = previewInstance.AddComponent<Animator>();
            }

            previewController = new AnimatorController();
            previewController.AddLayer("Base Layer");
            previewState = previewController.layers[0].stateMachine.AddState("Preview");
            previewState.motion = previewMotion;
            previewAnimator.runtimeAnimatorController = previewController;

            previewClip = previewMotion as AnimationClip;
            avatarPreview = new KimodoAvatarPreview(previewAnimator, previewMotion);
            avatarPreview.ShowIKOnFeetButton = previewClip != null && previewClip.isHumanMotion;
            if (previewClip != null)
            {
                avatarPreview.fps = Mathf.RoundToInt(previewClip.frameRate);
                avatarPreview.timeControl.stopTime = previewClip.length;
            }
            avatarPreview.ResetPreviewFocus();
            if (avatarPreview.timeControl.currentTime == Mathf.NegativeInfinity)
            {
                avatarPreview.timeControl.Update();
            }
        }

        private static void TrySetPreviewTag(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            string[] tags = UnityEditorInternal.InternalEditorUtility.tags;
            bool hasTag = false;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == KimodoPreviewConstants.PreviewTag)
                {
                    hasTag = true;
                    break;
                }
            }

            if (hasTag)
            {
                go.tag = KimodoPreviewConstants.PreviewTag;
            }
        }

        private void OnDisable()
        {
            CleanupPreview();
        }

        private void OnDestroy()
        {
            CleanupPreview();
        }

        private void CleanupPreview()
        {
            if (avatarPreview != null)
            {
                avatarPreview.OnDisable();
                avatarPreview.OnDestroy();
                avatarPreview = null;
            }

            if (previewInstance != null)
            {
                DestroyImmediate(previewInstance);
                previewInstance = null;
            }

            previewAnimator = null;
            previewController = null;
            previewState = null;
            previewClip = null;
        }
    }
}
