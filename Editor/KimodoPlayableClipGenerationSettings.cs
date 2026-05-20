using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor
{
    [FilePath("ProjectSettings/KimodoPlayableClipGenerationSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class KimodoPlayableClipGenerationSettings : ScriptableSingleton<KimodoPlayableClipGenerationSettings>
    {
        internal const int MinGeneratedClipsLimit = 1;
        internal const int MaxGeneratedClipsLimit = 1000;
        internal const int DefaultGeneratedClipsLimit = 400;

        [SerializeField] private int maxGeneratedClips = DefaultGeneratedClipsLimit;

        internal int MaxGeneratedClips
        {
            get => Mathf.Clamp(maxGeneratedClips, MinGeneratedClipsLimit, MaxGeneratedClipsLimit);
            set => maxGeneratedClips = Mathf.Clamp(value, MinGeneratedClipsLimit, MaxGeneratedClipsLimit);
        }

        internal void SaveSettings()
        {
            maxGeneratedClips = Mathf.Clamp(maxGeneratedClips, MinGeneratedClipsLimit, MaxGeneratedClipsLimit);
            Save(true);
        }
    }

    internal sealed class KimodoPlayableClipGenerationSettingsProvider : SettingsProvider
    {
        private KimodoPlayableClipGenerationSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new KimodoPlayableClipGenerationSettingsProvider("Project/Kimodo Playable Clip", SettingsScope.Project)
            {
                keywords = new System.Collections.Generic.HashSet<string>(new[] { "Kimodo", "Playable", "Clip", "Animation", "Limit", "History" })
            };
        }

        public override void OnGUI(string searchContext)
        {
            KimodoPlayableClipGenerationSettings settings = KimodoPlayableClipGenerationSettings.instance;
            EditorGUILayout.LabelField("Kimodo Playable Clip", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox("每次生成都会创建一个新动画。超过上限后会从最旧结果开始删除；若动画被其他资源引用则跳过。", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            int newLimit = EditorGUILayout.IntSlider(
                new GUIContent("Max Generated Clips", "Range: 1-1000"),
                settings.MaxGeneratedClips,
                KimodoPlayableClipGenerationSettings.MinGeneratedClipsLimit,
                KimodoPlayableClipGenerationSettings.MaxGeneratedClipsLimit);

            if (EditorGUI.EndChangeCheck())
            {
                settings.MaxGeneratedClips = newLimit;
                settings.SaveSettings();
            }

            EditorGUILayout.LabelField($"Current Limit: {settings.MaxGeneratedClips}", EditorStyles.miniLabel);
        }
    }
}
