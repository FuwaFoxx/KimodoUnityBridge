using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace KimodoUnityMotionTools.ProjectEditor.Ai
{
    public static class KimodoEditorAiApi
    {
        public readonly struct ModelSetupStatusDto
        {
            public readonly bool Missing;
            public readonly int MissingPoints;
            public readonly int EstimatedMinutes;

            public ModelSetupStatusDto(bool missing, int missingPoints, int estimatedMinutes)
            {
                Missing = missing;
                MissingPoints = missingPoints;
                EstimatedMinutes = estimatedMinutes;
            }
        }

        public readonly struct ModelDirectoryDto
        {
            public readonly string Name;
            public readonly string DirectoryPath;

            public ModelDirectoryDto(string name, string directoryPath)
            {
                Name = name ?? string.Empty;
                DirectoryPath = directoryPath ?? string.Empty;
            }
        }

        public static string GetDefaultRuntimeRoot()
        {
            return KimodoBridgeController.GetRuntimeRootPath();
        }

        public static bool EnsureRuntimeInstalled()
        {
            return KimodoBridgeController.EnsureRuntimeRootExists();
        }

        public static string ResolveLauncherOrThrow(string runtimeRoot)
        {
            return KimodoBridgeController.ResolveStartScriptOrThrow(runtimeRoot);
        }

        public static ModelSetupStatusDto GetModelSetupStatus(
            string runtimeRoot,
            bool highVram,
            string modelName,
            string modelsRootOverride = null)
        {
            KimodoBridgeController.ModelSetupStatus s =
                KimodoBridgeController.EvaluateModelSetupStatus(runtimeRoot, highVram, modelName, modelsRootOverride);
            return new ModelSetupStatusDto(s.Missing, s.MissingPoints, s.EstimatedMinutes);
        }

        public static List<ModelDirectoryDto> QueryModels(string modelsRoot)
        {
            List<KimodoBridgeController.ModelDirectoryInfo> source = KimodoBridgeController.QueryDisplayableModelDirectories(modelsRoot);
            var result = new List<ModelDirectoryDto>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                result.Add(new ModelDirectoryDto(source[i].Name, source[i].DirectoryPath));
            }

            return result;
        }

        public static async Task GenerateForPlayableClipAsync(KimodoPlayableClip clip, CancellationToken token = default)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var editor = UnityEditor.Editor.CreateEditor(clip, typeof(KimodoPlayableClipEditor)) as KimodoPlayableClipEditor;
            if (editor == null)
            {
                throw new InvalidOperationException("Failed to create KimodoPlayableClipEditor.");
            }

            try
            {
                await editor.GenerateForTestsAsync();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }
    }
}
