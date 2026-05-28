using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.AnimatorTooling
{
    // Test harness only. This window is not used by production preview flow.
    public sealed class KimodoImportedPreviewWindow : EditorWindow
    {
        private static readonly Vector2 MinSizeValue = new Vector2(700f, 420f);

        private GameObject previewPrefab;
        private AnimationClip previewClip;
        private GameObject previewInstance;
        private KimodoAvatarPreviewCore previewCore;

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
            previewClip = (AnimationClip)EditorGUILayout.ObjectField("Clip", previewClip, typeof(AnimationClip), false);

            if (GUILayout.Button("Load Preview", GUILayout.Height(24f)))
            {
                RebuildPreview();
            }

            Rect previewRect = GUILayoutUtility.GetRect(620f, 320f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(previewRect, GUIContent.none);
            Rect contentRect = new Rect(previewRect.x + 8f, previewRect.y + 6f, previewRect.width - 16f, previewRect.height - 14f);
            previewCore?.Draw(contentRect);
        }

        private void RebuildPreview()
        {
            CleanupPreview();
            if (previewPrefab == null || previewClip == null)
            {
                return;
            }

            previewInstance = KimodoPreviewEditorHelper.InstantiateGoByPrefab(previewPrefab, null);
            if (previewInstance == null)
            {
                return;
            }

            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            previewCore = new KimodoAvatarPreviewCore();
            previewCore.SetClipPreview(previewInstance, previewClip, "No clip.");
            previewCore.RestartFromZeroAndPlay();
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
            previewCore?.Dispose();
            previewCore = null;

            if (previewInstance != null)
            {
                DestroyImmediate(previewInstance);
                previewInstance = null;
            }
        }
    }
}
