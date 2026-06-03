using KimodoBridge;
using KimodoBridge.Editor;
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TimelineInject;

namespace KimodoBridge.Editor
{
    public sealed class KimodoAnimatorToolWindow : EditorWindow
    {
        private const string MenuPath = "Kimodo/Kimodo Animator Tool";

        private string lastStatus = string.Empty;
        private string lastError = string.Empty;
        private bool isGenerating;
        private KimodoGenerationBackend generationBackend = KimodoGenerationBackend.KimodoBridge;
        private string bridgeModelName = "Kimodo-SOMA-RP-v1";
        private KimodoBridgeVramMode bridgeVramMode = KimodoBridgeVramMode.Low;
        private string motionPrompt = "a man walk and say hello";
        private int generationFrames = KimodoPlayableClip.DEFAULT_FRAMES;
        private int diffusionSteps = 100;
        private bool randomSeed;
        private int seed = 42;
        private AnimationClip lastSuccessfulGeneratedClipForApply;
        private readonly KimodoAnimatorApplyService applyService = new KimodoAnimatorApplyService();
        private readonly KimodoEditorGeneratePipelineOrchestrator generatePipelineOrchestrator = new KimodoEditorGeneratePipelineOrchestrator();
        private KimodoAnimatorPreviewPane previewPane;
        private KimodoAnimatorEditorPane editorPane;
        private CancellationTokenSource generationCancellationTokenSource;
        private double lastPreviewRepaintTime;

        [MenuItem(MenuPath, priority = 110)]
        private static void OpenWindow()
        {
            KimodoAnimatorToolWindow window = GetWindow<KimodoAnimatorToolWindow>("Kimodo Animator Tool");
            window.minSize = new Vector2(1100f, 640f);
            window.Show();
        }

        private void OnEnable()
        {
            previewPane = new KimodoAnimatorPreviewPane();
            previewPane.Initialize();
            editorPane = new KimodoAnimatorEditorPane();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CancelGenerate();
            previewPane?.Dispose();
            previewPane = null;
            editorPane = null;
        }

        private void OnSelectionChange()
        {
            previewPane?.OnSelectionChange();
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastPreviewRepaintTime >= 0.2d)
            {
                lastPreviewRepaintTime = now;
                Repaint();
            }
        }

        private void OnEditorUpdate()
        {
            previewPane?.Tick();
            Repaint();
        }

        private void OnGUI()
        {
            if (previewPane == null)
            {
                previewPane = new KimodoAnimatorPreviewPane();
                previewPane.Initialize();
            }

            if (editorPane == null)
            {
                editorPane = new KimodoAnimatorEditorPane();
            }

            previewPane.DrawToolbar(ref lastStatus, ref lastError, OnResetAll);

            using (new EditorGUILayout.HorizontalScope())
            {
                previewPane.DrawPreviewPane(position.height);
                editorPane.Draw(
                    position.width,
                    previewPane,
                    ref generationBackend,
                    ref bridgeModelName,
                    ref bridgeVramMode,
                    ref motionPrompt,
                    ref generationFrames,
                    ref diffusionSteps,
                    ref randomSeed,
                    ref seed,
                    isGenerating,
                    StartGenerate,
                    CancelGenerate,
                    ApplyGeneratedResult,
                    ResetGenerated,
                    previewPane.GeneratedClipForPreview,
                    lastSuccessfulGeneratedClipForApply);
            }

            if (!string.IsNullOrWhiteSpace(lastError))
            {
                EditorGUILayout.HelpBox(lastError, MessageType.Error);
            }
            else if (!string.IsNullOrWhiteSpace(lastStatus))
            {
                EditorGUILayout.HelpBox(lastStatus, MessageType.Info);
            }
        }

        private void OnResetAll()
        {
            lastSuccessfulGeneratedClipForApply = null;
        }

        private void ResetGenerated()
        {
            previewPane?.ResetGeneratedOnly();
            lastSuccessfulGeneratedClipForApply = null;
            lastStatus = "Generated preview cleared.";
            lastError = string.Empty;
        }

        private void StartGenerate()
        {
            if (previewPane == null)
            {
                lastError = "Preview pane is not ready.";
                return;
            }

            if (isGenerating)
            {
                return;
            }

            if (!previewPane.TryBuildExternalConstraints(bridgeModelName, generationFrames, out string constraintsJson, out string error))
            {
                lastError = error;
                return;
            }

            if (previewPane.RetargetAvatarForPreview == null)
            {
                lastError = "Preview retarget avatar is not ready.";
                return;
            }

            DisposeGenerationCancellation();
            generationCancellationTokenSource = new CancellationTokenSource();
            CancellationTokenSource runCts = generationCancellationTokenSource;

            isGenerating = true;
            lastError = string.Empty;
            lastStatus = "Generating and baking...";
            _ = StartGenerateAsync(constraintsJson, previewPane.RetargetAvatarForPreview, runCts);
        }

        private async Task StartGenerateAsync(string constraintsJson, Avatar explicitRetargetAvatar, CancellationTokenSource runCts)
        {
            KimodoPlayableClip tempClip = null;
            try
            {
                tempClip = CreateTemporaryWorkingClip();
                KimodoEditorGenerateResult result = await generatePipelineOrchestrator.ExecuteGenerateAndBakeAsync(
                    tempClip,
                    promptOverride: null,
                    (stage, message) =>
                    {
                        RunOnEditorThread(() =>
                        {
                            lastStatus = string.IsNullOrWhiteSpace(message) ? stage.ToString() : message;
                            Repaint();
                        });
                    },
                    KimodoTimelinePreviewRefreshUtility.RefreshIfPreviewing,
                    constraintsJson,
                    useExternalConstraints: true,
                    explicitRetargetAvatar,
                    runCts.Token);

                RunOnEditorThread(() =>
                {
                    isGenerating = false;
                    previewPane?.OnGenerateSuccess(result.GeneratedClip);
                    lastSuccessfulGeneratedClipForApply = result.GeneratedClip;
                    lastStatus = "Generation complete.";
                    lastError = string.Empty;
                    Repaint();
                });
            }
            catch (OperationCanceledException)
            {
                RunOnEditorThread(() =>
                {
                    isGenerating = false;
                    previewPane?.OnGenerateFailedOrCanceled();
                    lastStatus = "Generation canceled.";
                    lastError = string.Empty;
                    Repaint();
                });
            }
            catch (Exception ex)
            {
                RunOnEditorThread(() =>
                {
                    isGenerating = false;
                    previewPane?.OnGenerateFailedOrCanceled();
                    lastError = ex.Message;
                    lastStatus = "Generation failed.";
                    Repaint();
                    RethrowOnNextEditorTick(ex);
                });
            }
            finally
            {
                RunOnEditorThread(() =>
                {
                    DisposeGenerationCancellation(runCts);
                    if (tempClip != null)
                    {
                        DestroyImmediate(tempClip);
                    }
                });
            }
        }

        private void CancelGenerate()
        {
            CancellationTokenSource cts = generationCancellationTokenSource;
            if (cts == null)
            {
                return;
            }

            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch
            {
                // Ignore cancellation errors.
            }
        }

        private void ApplyGeneratedResult()
        {
            if (previewPane == null || lastSuccessfulGeneratedClipForApply == null)
            {
                lastError = "No generated clip available to apply.";
                return;
            }

            bool success;
            string error;
            if (previewPane.SelectedTransition != null)
            {
                AnimatorState toState = previewPane.SelectedTransition.destinationState;
                string suggestedStateName = string.Format(
                    "{0}_{1}_KimodoInsert",
                    previewPane.SelectedFromState != null ? previewPane.SelectedFromState.name : "From",
                    toState != null ? toState.name : "To");

                success = applyService.TryApplyTransition(
                    new KimodoAnimatorApplyService.TransitionApplyContext
                    {
                        Controller = KimodoAnimatorSelectionResolver.FindControllerForObject(previewPane.SelectedTransition),
                        StateMachine = previewPane.SelectedStateMachine,
                        FromState = previewPane.SelectedFromState,
                        ToState = toState,
                        OriginalTransition = previewPane.SelectedTransition,
                        GeneratedClip = lastSuccessfulGeneratedClipForApply,
                        NewStateName = suggestedStateName
                    },
                    out error);
            }
            else if (previewPane.SelectedState != null)
            {
                success = applyService.TryApplyState(
                    new KimodoAnimatorApplyService.StateApplyContext
                    {
                        Controller = KimodoAnimatorSelectionResolver.FindControllerForObject(previewPane.SelectedState),
                        State = previewPane.SelectedState,
                        GeneratedClip = lastSuccessfulGeneratedClipForApply
                    },
                    out error);
            }
            else
            {
                lastError = "No selected transition or state to apply.";
                return;
            }

            if (!success)
            {
                lastError = error;
                return;
            }

            lastError = string.Empty;
            lastStatus = "Apply completed.";
        }

        private KimodoPlayableClip CreateTemporaryWorkingClip()
        {
            var clip = CreateInstance<KimodoPlayableClip>();
            clip.name = "KimodoAnimatorTool_WorkingClip";
            clip.generationBackend = generationBackend;
            clip.bridgeModelName = bridgeModelName;
            clip.bridgeVramMode = bridgeVramMode;
            clip.motionPrompt = motionPrompt ?? string.Empty;
            clip.generationFrames = Mathf.Clamp(generationFrames, KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES);
            clip.diffusionSteps = Mathf.Clamp(diffusionSteps, 1, 1000);
            clip.randomSeed = randomSeed;
            clip.seed = seed;
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        private void DisposeGenerationCancellation()
        {
            DisposeGenerationCancellation(generationCancellationTokenSource);
        }

        private void DisposeGenerationCancellation(CancellationTokenSource cts)
        {
            if (cts == null)
            {
                return;
            }

            if (ReferenceEquals(generationCancellationTokenSource, cts))
            {
                generationCancellationTokenSource = null;
            }

            cts.Dispose();
        }

        private static void RunOnEditorThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            EditorApplication.delayCall += () => action();
        }

        private static void RethrowOnNextEditorTick(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            ExceptionDispatchInfo dispatchInfo = ExceptionDispatchInfo.Capture(exception);
            EditorApplication.delayCall += () => dispatchInfo.Throw();
        }
    }

    internal static class KimodoAnimatorSelectionResolver
    {
        public static AnimatorController FindControllerForObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }
    }
}
