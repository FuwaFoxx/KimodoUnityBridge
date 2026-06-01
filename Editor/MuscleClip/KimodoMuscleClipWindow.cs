#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.MuscleClip
{
    public sealed class KimodoMuscleClipWindow : EditorWindow
    {
        private const string MenuPath = "Kimodo/Muscle Clip Writer";
        private const string DefaultOutputFolder = "Assets/MuscleClips";
        private const string DefaultPrefix = "MuscleClip_";

        [SerializeField] private AnimationClip sourceClip;
        [SerializeField] private Avatar sourceAvatar;
        [SerializeField] private string outputFolder = DefaultOutputFolder;
        [SerializeField] private string clipNamePrefix = DefaultPrefix;
        [SerializeField] private float sampleRate = 30f;

        private string status;
        private string error;

        [MenuItem(MenuPath, priority = 126)]
        private static void Open()
        {
            GetWindow<KimodoMuscleClipWindow>("Muscle Clip Writer");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Generate Humanoid Muscle Clip", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            sourceClip = (AnimationClip)EditorGUILayout.ObjectField("Source Clip", sourceClip, typeof(AnimationClip), false);
            sourceAvatar = (Avatar)EditorGUILayout.ObjectField("Source Avatar", sourceAvatar, typeof(Avatar), false);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            clipNamePrefix = EditorGUILayout.TextField("Clip Prefix", clipNamePrefix);
            sampleRate = EditorGUILayout.FloatField("Sample Rate", sampleRate);

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(sourceClip == null || sourceAvatar == null))
            {
                if (GUILayout.Button("Generate Muscle Clip", GUILayout.Height(30f)))
                {
                    Generate();
                }
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, MessageType.Info);
            }
        }

        private void Generate()
        {
            error = string.Empty;
            status = string.Empty;

            if (!KimodoMuscleClipWriter.TryCreateMuscleClipAsset(
                    sourceClip,
                    sourceAvatar,
                    outputFolder,
                    clipNamePrefix,
                    sampleRate,
                    out AnimationClip outputClip,
                    out string assetPath,
                    out string writeError))
            {
                error = writeError;
                return;
            }

            status = $"Generated: {assetPath}";
            Selection.activeObject = outputClip;
            EditorGUIUtility.PingObject(outputClip);
        }
    }
}
#endif
