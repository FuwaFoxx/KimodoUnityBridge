
using KimodoBridge;
using System;
using UnityEditor;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal sealed class KimodoAnimatorEditorPane
    {
        private Vector2 rightScroll;

        public void Draw(
            float windowWidth,
            KimodoAnimatorPreviewPane previewPane,
            ref KimodoGenerationBackend generationBackend,
            ref string bridgeModelName,
            ref KimodoBridgeVramMode bridgeVramMode,
            ref string motionPrompt,
            ref int generationFrames,
            ref int diffusionSteps,
            ref bool randomSeed,
            ref int seed,
            bool isGenerating,
            Action startGenerate,
            Action cancelGenerate,
            Action applyGeneratedResult,
            Action resetGenerated,
            AnimationClip generatedClipForPreview,
            AnimationClip lastSuccessfulGeneratedClipForApply)
        {
            float width = Mathf.Max(420f, windowWidth * 0.46f);
            using (var scroll = new EditorGUILayout.ScrollViewScope(rightScroll, GUILayout.Width(width)))
            {
                rightScroll = scroll.scrollPosition;

                if (previewPane != null)
                {
                    previewPane.DrawSelectionInfo();
                }

                DrawGeneratePanel(
                    previewPane != null && previewPane.HasSelection,
                    ref generationBackend,
                    ref bridgeModelName,
                    ref bridgeVramMode,
                    ref motionPrompt,
                    ref generationFrames,
                    ref diffusionSteps,
                    ref randomSeed,
                    ref seed,
                    isGenerating,
                    startGenerate,
                    cancelGenerate);
                DrawResultPanel(generatedClipForPreview, resetGenerated);
                DrawApplyPanel(
                    previewPane != null && previewPane.HasSelection,
                    isGenerating,
                    lastSuccessfulGeneratedClipForApply,
                    applyGeneratedResult);
            }
        }

        private static void DrawGeneratePanel(
            bool hasSelection,
            ref KimodoGenerationBackend generationBackend,
            ref string bridgeModelName,
            ref KimodoBridgeVramMode bridgeVramMode,
            ref string motionPrompt,
            ref int generationFrames,
            ref int diffusionSteps,
            ref bool randomSeed,
            ref int seed,
            bool isGenerating,
            Action startGenerate,
            Action cancelGenerate)
        {
            EditorGUILayout.LabelField("Generate", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            generationBackend = (KimodoGenerationBackend)EditorGUILayout.EnumPopup(new GUIContent("Backend"), generationBackend);

            if (generationBackend == KimodoGenerationBackend.KimodoBridge)
            {
                DrawBridgePanel(ref bridgeModelName, ref bridgeVramMode);
            }
            else
            {
                EditorGUILayout.HelpBox("ComfyUI backend uses existing clip defaults for host, port, and workflow.", MessageType.Info);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Prompt", EditorStyles.miniBoldLabel);
            motionPrompt = EditorGUILayout.TextArea(motionPrompt ?? string.Empty, GUILayout.Height(60f));

            generationFrames = EditorGUILayout.IntSlider(
                new GUIContent("Duration (frames)"),
                Mathf.Clamp(generationFrames, KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES),
                KimodoPlayableClip.MIN_FRAMES,
                KimodoPlayableClip.MAX_FRAMES);

            diffusionSteps = Mathf.Clamp(
                EditorGUILayout.IntField(new GUIContent("Diffusion Steps"), diffusionSteps),
                1,
                1000);

            EditorGUILayout.BeginHorizontal();
            randomSeed = EditorGUILayout.ToggleLeft(new GUIContent("Random"), randomSeed, GUILayout.Width(90f));
            using (new EditorGUI.DisabledScope(randomSeed))
            {
                seed = EditorGUILayout.IntField(new GUIContent("Seed"), seed);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            bool canGenerate = !isGenerating && hasSelection;
            EditorGUI.BeginDisabledGroup(!canGenerate);
            if (GUILayout.Button("Generate & Bake", GUILayout.Height(30f)))
            {
                startGenerate?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!isGenerating);
            if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
            {
                cancelGenerate?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private static void DrawBridgePanel(ref string bridgeModelName, ref KimodoBridgeVramMode bridgeVramMode)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Kimodo Bridge", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginVertical("box");

            string[] options = KimodoBridgeController.SupportedModelNames;
            if (options != null && options.Length > 0)
            {
                string current = string.IsNullOrWhiteSpace(bridgeModelName) ? options[0] : bridgeModelName.Trim();
                int currentIndex = Array.IndexOf(options, current);
                if (currentIndex < 0)
                {
                    currentIndex = 0;
                }

                int newIndex = EditorGUILayout.Popup(new GUIContent("Bridge Model"), currentIndex, options);
                bridgeModelName = options[Mathf.Clamp(newIndex, 0, options.Length - 1)];
            }
            else
            {
                bridgeModelName = EditorGUILayout.TextField(new GUIContent("Bridge Model"), bridgeModelName ?? string.Empty);
            }

            bridgeVramMode = (KimodoBridgeVramMode)EditorGUILayout.EnumPopup(new GUIContent("VRAM Mode"), bridgeVramMode);

            EditorGUILayout.EndVertical();
        }

        private static void DrawResultPanel(AnimationClip generatedClipForPreview, Action resetGenerated)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    new GUIContent("Generated Clip Preview"),
                    generatedClipForPreview,
                    typeof(AnimationClip),
                    false);
            }

            bool canReset = generatedClipForPreview != null;
            EditorGUI.BeginDisabledGroup(!canReset);
            if (GUILayout.Button("Reset", GUILayout.Width(100f)))
            {
                resetGenerated?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private static void DrawApplyPanel(
            bool hasSelection,
            bool isGenerating,
            AnimationClip lastSuccessfulGeneratedClipForApply,
            Action applyGeneratedResult)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Apply", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            bool canApply = lastSuccessfulGeneratedClipForApply != null && !isGenerating && hasSelection;
            EditorGUI.BeginDisabledGroup(!canApply);
            if (GUILayout.Button("Apply", GUILayout.Height(28f)))
            {
                applyGeneratedResult?.Invoke();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }
    }
}
