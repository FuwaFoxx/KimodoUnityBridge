using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor
{
    public sealed class FootRootMotionPreviewWindow : EditorWindow
    {
        private const string MenuPath = "Kimodo/Foot Root Motion Preview";
        private const string ApplyUndoName = "Kimodo Apply Root Motion Clip";

        private enum PreviewMode
        {
            Original,
            Generated
        }

        private enum SelectionKind
        {
            AnimatorState,
            KimodoPlayableClip
        }

        private sealed class SelectionContext
        {
            public SelectionKind Kind;
            public UnityEngine.Object TargetObject;
            public AnimationClip SourceClip;
            public GameObject ModelRoot;
            public bool OwnsModelRoot;
            public Avatar Avatar;
            public string ModelName = string.Empty;
            public string DisplayName = string.Empty;
            public string ContainerName = string.Empty;
            public AnimatorState AnimatorState;
            public AnimatorController AnimatorController;
            public KimodoPlayableClip PlayableClip;
        }

        private readonly KimodoEditorConstraintProvider constraintProvider = new KimodoEditorConstraintProvider();
        private readonly KimodoAnimatorApplyService animatorApplyService = new KimodoAnimatorApplyService();

        private FootRootMotionSolverSettings settings;
        private SelectionContext selectionContext;
        private string selectionKey = string.Empty;
        private FootRootMotionFrame[] sampledFrames;
        private FootRootMotionResult solvedResult;
        private AnimationClip generatedPreviewClip;
        private string lastError = string.Empty;
        private string lastStatus = string.Empty;
        private bool showSettings = true;
        private bool showDebug = true;
        private PreviewMode previewMode;
        private double lastRepaintAt;
        private float lastAppliedPreviewTime = float.NaN;

        private GameObject previewInstance;
        private Animator previewAnimator;
        private KimodoAvatarPreview avatarPreview;

        [MenuItem(MenuPath, priority = 111)]
        private static void OpenWindow()
        {
            FootRootMotionPreviewWindow window = GetWindow<FootRootMotionPreviewWindow>("Foot Root Motion");
            window.minSize = new Vector2(1180f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            settings ??= new FootRootMotionSolverSettings();
            EditorApplication.update += OnEditorUpdate;
            RefreshSelectionIfNeeded(force: true);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanupPreview();
            ClearSelectionContext();
        }

        private void OnSelectionChange()
        {
            RefreshSelectionIfNeeded(force: true);
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (!IsPreviewInteractionActive())
            {
                RefreshSelectionIfNeeded(force: false);
            }

            bool previewChanged = TickPreview();

            double now = EditorApplication.timeSinceStartup;
            if (previewChanged || now - lastRepaintAt > 0.05d)
            {
                lastRepaintAt = now;
                Repaint();
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawPreviewPanel();
            }

            DrawStatus();
        }

        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(360f)))
            {
                DrawSelectionInfo();

                showSettings = EditorGUILayout.Foldout(showSettings, "Solver Settings", true);
                if (showSettings)
                {
                    settings.sampleRate = EditorGUILayout.FloatField("Sample Rate", settings.sampleRate);
                    settings.supportSwitchWindowFrames = EditorGUILayout.IntSlider("Switch Window Frames", settings.supportSwitchWindowFrames, 2, 30);
                    settings.smoothing = EditorGUILayout.Slider("Smoothing", settings.smoothing, 0f, 1f);
                    settings.dampingSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Damping Speed", settings.dampingSpeed));
                    settings.dampingAngleSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Damping Angle Speed", settings.dampingAngleSpeed));
                    settings.keepOriginMotion = EditorGUILayout.Toggle("Keep Origin Motion", settings.keepOriginMotion);
                }

                showDebug = EditorGUILayout.Foldout(showDebug, "Debug Metrics", true);
                if (showDebug)
                {
                    DrawDebugMetrics();
                }

                DrawActionButtons();

                Rect chartRect = GUILayoutUtility.GetRect(340f, 230f, GUILayout.ExpandWidth(true));
                DrawTopDownChart(chartRect);
            }
        }

        private void DrawSelectionInfo()
        {
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (selectionContext == null)
                {
                    EditorGUILayout.HelpBox("Select an Animator State or KimodoPlayableClip.", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("Type", selectionContext.Kind.ToString());
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Target", selectionContext.TargetObject, typeof(UnityEngine.Object), false);
                    EditorGUILayout.ObjectField("Clip", selectionContext.SourceClip, typeof(AnimationClip), false);
                    EditorGUILayout.ObjectField("Model", selectionContext.ModelRoot, typeof(GameObject), true);
                    EditorGUILayout.ObjectField("Avatar", selectionContext.Avatar, typeof(Avatar), false);
                }

                if (!string.IsNullOrWhiteSpace(selectionContext.ModelName))
                {
                    EditorGUILayout.LabelField("Model Name", selectionContext.ModelName);
                }

                if (!string.IsNullOrWhiteSpace(selectionContext.ContainerName))
                {
                    EditorGUILayout.LabelField("Apply Target", selectionContext.ContainerName);
                }
            }
        }

        private void DrawDebugMetrics()
        {
            if (sampledFrames == null)
            {
                EditorGUILayout.HelpBox("No sampled frames.", MessageType.Info);
                return;
            }

            float duration = sampledFrames.Length > 0 ? sampledFrames[sampledFrames.Length - 1].time : 0f;
            EditorGUILayout.LabelField("Frames", sampledFrames.Length.ToString());
            EditorGUILayout.LabelField("Duration", duration.ToString("F3") + " s");
            EditorGUILayout.LabelField("Sample Rate", settings.sampleRate.ToString("F1") + " FPS");

            if (solvedResult == null || solvedResult.rootXZ == null || solvedResult.rootXZ.Length == 0 || solvedResult.debug == null)
            {
                return;
            }

            Vector2 total = solvedResult.rootXZ[solvedResult.rootXZ.Length - 1] - solvedResult.rootXZ[0];
            float avgSpeed = duration > 1e-4f ? total.magnitude / duration : 0f;
            int leftPlants = CountPlants(solvedResult.debug.leftPlant);
            int rightPlants = CountPlants(solvedResult.debug.rightPlant);
            float predictionRatio = ComputePredictionRatio(solvedResult.debug.usedPrediction);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Total Root Delta XZ", total.ToString("F3"));
            EditorGUILayout.LabelField("Average Speed", avgSpeed.ToString("F3") + " m/s");
            EditorGUILayout.LabelField("Left Support Frames", leftPlants.ToString());
            EditorGUILayout.LabelField("Right Support Frames", rightPlants.ToString());
            EditorGUILayout.LabelField("Zero-Speed Ratio", (predictionRatio * 100f).ToString("F1") + " %");
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                bool hasSelection = selectionContext != null && selectionContext.SourceClip != null && selectionContext.ModelRoot != null;
                bool hasGenerated = generatedPreviewClip != null;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasSelection || sampledFrames == null || sampledFrames.Length == 0))
                    {
                        if (GUILayout.Button("Generate Preview", GUILayout.Height(26f)))
                        {
                            GeneratePreview();
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasGenerated))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Height(26f)))
                        {
                            ApplyGeneratedPreview();
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    PreviewMode nextMode = previewMode;
                    bool wantOriginal = GUILayout.Toggle(previewMode == PreviewMode.Original, "Original", EditorStyles.toolbarButton);
                    if (wantOriginal && previewMode != PreviewMode.Original)
                    {
                        nextMode = PreviewMode.Original;
                    }

                    using (new EditorGUI.DisabledScope(!hasGenerated))
                    {
                        bool wantGenerated = GUILayout.Toggle(previewMode == PreviewMode.Generated, "Generated", EditorStyles.toolbarButton);
                        if (wantGenerated && previewMode != PreviewMode.Generated)
                        {
                            nextMode = PreviewMode.Generated;
                        }
                    }

                    if (nextMode != previewMode)
                    {
                        previewMode = nextMode;
                        RebuildPreview();
                    }
                }
            }
        }

        private void DrawPreviewPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EnsurePreview();
                Rect previewRect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f, 1f));
                }

                if (avatarPreview == null)
                {
                    EditorGUI.DropShadowLabel(previewRect, "Select an Animator State or KimodoPlayableClip to preview root motion.");
                    return;
                }

                avatarPreview.DoAvatarPreview(previewRect, KimodoPreviewConstants.PreviewBackgroundSolid);
                DrawOverlayGizmos(previewRect);
            }
        }

        private void DrawOverlayGizmos(Rect rect)
        {
            if (Event.current.type != EventType.Repaint || sampledFrames == null)
            {
                return;
            }

            Rect overlayRect = new Rect(rect.x + 10f, rect.y + 10f, 270f, 104f);
            GUI.Box(overlayRect, GUIContent.none, EditorStyles.helpBox);
            Rect lineRect = new Rect(overlayRect.x + 8f, overlayRect.y + 6f, overlayRect.width - 16f, 18f);
            GUI.Label(lineRect, "Preview Mode: " + previewMode);
            lineRect.y += 20f;
            GUI.Label(lineRect, "Time: " + GetCurrentTime().ToString("F3"));
            lineRect.y += 20f;
            int frameIndex = GetCurrentFrameIndex();
            GUI.Label(lineRect, "Frame: " + frameIndex + " / " + Mathf.Max(0, sampledFrames.Length - 1));
            if (solvedResult != null && solvedResult.rootXZ != null && frameIndex >= 0 && frameIndex < solvedResult.rootXZ.Length)
            {
                lineRect.y += 20f;
                GUI.Label(lineRect, "Solved Root XZ: " + solvedResult.rootXZ[frameIndex].ToString("F3"));
            }
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                EditorGUILayout.HelpBox(lastError, MessageType.Error);
            }
            else if (!string.IsNullOrWhiteSpace(lastStatus))
            {
                EditorGUILayout.HelpBox(lastStatus, MessageType.Info);
            }
        }

        private void DrawTopDownChart(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.11f, 1f));
            }

            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (sampledFrames == null || sampledFrames.Length == 0)
            {
                EditorGUI.DropShadowLabel(rect, "XZ chart appears after selection sampling.");
                return;
            }

            Vector2 min = ToXZ(sampledFrames[0].leftFootWorld);
            Vector2 max = min;
            ExpandBounds(ref min, ref max, sampledFrames[0].rightFootWorld);

            for (int i = 0; i < sampledFrames.Length; i++)
            {
                ExpandBounds(ref min, ref max, sampledFrames[i].leftFootWorld);
                ExpandBounds(ref min, ref max, sampledFrames[i].rightFootWorld);
                if (solvedResult != null && solvedResult.rootXZ != null && i < solvedResult.rootXZ.Length)
                {
                    Vector2 root = solvedResult.rootXZ[i];
                    min = Vector2.Min(min, root);
                    max = Vector2.Max(max, root);
                }
            }

            Vector2 size = max - min;
            if (size.x < 0.001f) size.x = 0.001f;
            if (size.y < 0.001f) size.y = 0.001f;

            Handles.BeginGUI();
            DrawPolyline(rect, sampledFrames, frame => ToXZ(frame.leftFootWorld), min, size, new Color(0.3f, 0.85f, 1f, 1f), 2f);
            DrawPolyline(rect, sampledFrames, frame => ToXZ(frame.rightFootWorld), min, size, new Color(1f, 0.55f, 0.3f, 1f), 2f);

            if (solvedResult != null && solvedResult.rootXZ != null && solvedResult.rootXZ.Length > 0)
            {
                DrawPolyline(rect, solvedResult.rootXZ, point => point, min, size, new Color(0.5f, 1f, 0.45f, 1f), 2.5f);
                if (solvedResult.debug != null)
                {
                    DrawPlantAnchors(rect, solvedResult.debug.leftAnchors, solvedResult.debug.leftPlant, min, size, new Color(0.2f, 0.7f, 1f, 0.95f));
                    DrawPlantAnchors(rect, solvedResult.debug.rightAnchors, solvedResult.debug.rightPlant, min, size, new Color(1f, 0.45f, 0.25f, 0.95f));
                }
            }

            Handles.color = new Color(1f, 1f, 1f, 0.4f);
            Handles.DrawLine(new Vector3(rect.x + 8f, rect.center.y), new Vector3(rect.xMax - 8f, rect.center.y));
            Handles.DrawLine(new Vector3(rect.center.x, rect.y + 8f), new Vector3(rect.center.x, rect.yMax - 8f));
            Handles.EndGUI();

            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 180f, 18f), "XZ Trajectories", EditorStyles.boldLabel);
        }

        private void RefreshSelectionIfNeeded(bool force)
        {
            string currentKey = BuildCurrentSelectionKey();
            if (!force && string.Equals(currentKey, selectionKey, StringComparison.Ordinal))
            {
                return;
            }

            selectionKey = currentKey;
            ResolveSelectionAndSample();
        }

        private string BuildCurrentSelectionKey()
        {
            UnityEngine.Object active = Selection.activeObject;
            if (active is AnimatorState state)
            {
                int motionId = state.motion != null ? state.motion.GetInstanceID() : 0;
                return $"state:{state.GetInstanceID()}:{motionId}";
            }

            if (active is KimodoPlayableClip activePlayable)
            {
                int clipId = activePlayable.clip != null ? activePlayable.clip.GetInstanceID() : 0;
                return $"playable:{activePlayable.GetInstanceID()}:{clipId}:{KimodoPlayableClip.NormalizeBridgeModelName(activePlayable.bridgeModelName)}";
            }

            if (TryGetSelectedTimelinePlayableClip(out KimodoPlayableClip timelinePlayable))
            {
                int clipId = timelinePlayable.clip != null ? timelinePlayable.clip.GetInstanceID() : 0;
                return $"playable:{timelinePlayable.GetInstanceID()}:{clipId}:{KimodoPlayableClip.NormalizeBridgeModelName(timelinePlayable.bridgeModelName)}";
            }

            return active == null ? "none" : $"unsupported:{active.GetInstanceID()}";
        }

        private void ResolveSelectionAndSample()
        {
            CleanupPreview();
            ClearSelectionContext();
            sampledFrames = null;
            solvedResult = null;
            generatedPreviewClip = null;
            previewMode = PreviewMode.Original;
            lastError = string.Empty;
            lastStatus = string.Empty;

            if (!TryResolveCurrentSelection(out selectionContext, out string error))
            {
                lastError = error;
                Repaint();
                return;
            }

            SampleCurrentSelection();
        }

        private bool TryResolveCurrentSelection(out SelectionContext context, out string error)
        {
            context = null;
            error = string.Empty;

            UnityEngine.Object active = Selection.activeObject;
            if (active is AnimatorState state)
            {
                return TryResolveAnimatorState(state, out context, out error);
            }

            if (active is KimodoPlayableClip activePlayable)
            {
                return TryResolvePlayableClip(activePlayable, out context, out error);
            }

            if (TryGetSelectedTimelinePlayableClip(out KimodoPlayableClip timelinePlayable))
            {
                return TryResolvePlayableClip(timelinePlayable, out context, out error);
            }

            if (active is AnimatorStateTransition)
            {
                error = "Select an Animator State. Transition selection is not supported by the root motion window.";
                return false;
            }

            error = "Select an Animator State or KimodoPlayableClip.";
            return false;
        }

        private bool TryResolveAnimatorState(AnimatorState state, out SelectionContext context, out string error)
        {
            context = null;
            error = string.Empty;

            if (state == null)
            {
                error = "Animator State is null.";
                return false;
            }

            if (state.motion is not AnimationClip sourceClip)
            {
                error = "Animator State motion is not an AnimationClip.";
                return false;
            }

            AnimatorController controller = KimodoAnimatorSelectionUtility.FindControllerForObject(state);
            GameObject modelRoot = null;
            Avatar avatar = null;
            if (!TryResolveScenePreviewSourceByController(controller, out modelRoot, out avatar))
            {
                modelRoot = LoadClipOwnerModelAsset(sourceClip);
                if (modelRoot != null && !TryResolveAvatarForModelRoot(modelRoot, out avatar, out error))
                {
                    return false;
                }
            }

            if (modelRoot == null)
            {
                error = "Cannot resolve a humanoid model for the selected Animator State.";
                return false;
            }

            context = new SelectionContext
            {
                Kind = SelectionKind.AnimatorState,
                TargetObject = state,
                SourceClip = sourceClip,
                ModelRoot = modelRoot,
                Avatar = avatar,
                AnimatorState = state,
                AnimatorController = controller,
                DisplayName = state.name,
                ContainerName = controller != null ? controller.name : "Animator Controller"
            };
            return true;
        }

        private bool TryResolvePlayableClip(KimodoPlayableClip playableClip, out SelectionContext context, out string error)
        {
            context = null;
            error = string.Empty;

            if (playableClip == null)
            {
                error = "KimodoPlayableClip is null.";
                return false;
            }

            if (playableClip.clip == null)
            {
                error = "KimodoPlayableClip has no baked AnimationClip to process.";
                return false;
            }

            string modelName = KimodoPlayableClip.NormalizeBridgeModelName(playableClip.bridgeModelName);
            GameObject modelRoot = constraintProvider.FindTimelineBindingObjectForAsset(playableClip);
            Avatar avatar = null;
            bool ownsModelRoot = false;

            if (modelRoot != null && !TryResolveAvatarForModelRoot(modelRoot, out avatar, out error))
            {
                return false;
            }

            if (modelRoot == null)
            {
                avatar = playableClip.CustomRetargetAvatar;
                if (!KimodoRetargetCoreUtility.IsValidHumanoid(avatar) &&
                    !KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out avatar, out _))
                {
                    avatar = null;
                }

                if (!KimodoRetargetCoreUtility.IsValidHumanoid(avatar))
                {
                    error = "Cannot resolve a humanoid avatar from Timeline binding, custom avatar, or bridge model.";
                    return false;
                }

                if (!KimodoRetargetAvatarUtility.TryCreateTemporaryHumanoidRoot(
                        avatar,
                        "FootRootMotionPlayableSource",
                        animatorEnabled: false,
                        applyRootMotion: true,
                        out modelRoot,
                        out _,
                        out error))
                {
                    return false;
                }

                ownsModelRoot = true;
            }

            context = new SelectionContext
            {
                Kind = SelectionKind.KimodoPlayableClip,
                TargetObject = playableClip,
                SourceClip = playableClip.clip,
                ModelRoot = modelRoot,
                OwnsModelRoot = ownsModelRoot,
                Avatar = avatar,
                ModelName = modelName,
                PlayableClip = playableClip,
                DisplayName = playableClip.name,
                ContainerName = "Timeline KimodoPlayableClip"
            };
            return true;
        }

        private void SampleCurrentSelection()
        {
            lastError = string.Empty;
            solvedResult = null;
            generatedPreviewClip = null;
            previewMode = PreviewMode.Original;

            if (selectionContext == null)
            {
                lastError = "No supported selection.";
                return;
            }

            if (!FootRootMotionSamplingUtility.TrySampleClip(
                    selectionContext.SourceClip,
                    selectionContext.ModelRoot,
                    settings,
                    out sampledFrames,
                    out string error))
            {
                sampledFrames = null;
                lastError = error;
                lastStatus = string.Empty;
                return;
            }

            lastStatus = $"Sampled {sampledFrames.Length} frames from '{selectionContext.SourceClip.name}'.";
            RebuildPreview();
        }

        private void GeneratePreview()
        {
            lastError = string.Empty;
            if (selectionContext == null)
            {
                lastError = "No supported selection.";
                return;
            }

            if (sampledFrames == null || sampledFrames.Length == 0)
            {
                SampleCurrentSelection();
                if (sampledFrames == null || sampledFrames.Length == 0)
                {
                    return;
                }
            }

            solvedResult = FootRootMotionSolver.Solve(sampledFrames, settings);
            if (!TryCreateGeneratedRootMotionClip(
                    selectionContext,
                    sampledFrames,
                    solvedResult,
                    settings.keepOriginMotion,
                    out generatedPreviewClip,
                    out string error))
            {
                lastError = error;
                generatedPreviewClip = null;
                return;
            }

            previewMode = PreviewMode.Generated;
            lastStatus = $"Generated root motion preview: {generatedPreviewClip.name}.";
            RebuildPreview();
        }

        private void ApplyGeneratedPreview()
        {
            lastError = string.Empty;
            if (selectionContext == null || generatedPreviewClip == null)
            {
                lastError = "Generate a preview before applying.";
                return;
            }

            bool ok;
            switch (selectionContext.Kind)
            {
                case SelectionKind.AnimatorState:
                    ok = ApplyToAnimatorState(selectionContext, generatedPreviewClip, out lastError);
                    break;
                case SelectionKind.KimodoPlayableClip:
                    ok = ApplyToPlayableClip(selectionContext, generatedPreviewClip, out lastError);
                    break;
                default:
                    ok = false;
                    break;
            }

            if (!ok)
            {
                if (string.IsNullOrWhiteSpace(lastError))
                {
                    lastError = "Apply failed.";
                }
                return;
            }

            lastStatus = $"Applied '{generatedPreviewClip.name}'.";
            UpdateSelectionSourceAfterApply(selectionContext, generatedPreviewClip);
            if (selectionContext != null)
            {
                selectionContext.SourceClip = generatedPreviewClip;
            }

            sampledFrames = null;
            solvedResult = null;
            generatedPreviewClip = null;
            previewMode = PreviewMode.Original;
            selectionKey = BuildCurrentSelectionKey();
            CleanupPreview();
            RebuildPreview();
        }

        private static void UpdateSelectionSourceAfterApply(SelectionContext context, AnimationClip generatedClip)
        {
            if (context == null || generatedClip == null)
            {
                return;
            }

            switch (context.Kind)
            {
                case SelectionKind.AnimatorState:
                    if (context.AnimatorState != null)
                    {
                        context.SourceClip = context.AnimatorState.motion as AnimationClip ?? generatedClip;
                    }
                    break;
                case SelectionKind.KimodoPlayableClip:
                    if (context.PlayableClip != null)
                    {
                        context.SourceClip = context.PlayableClip.clip ?? generatedClip;
                    }
                    break;
            }
        }

        private bool ApplyToAnimatorState(SelectionContext context, AnimationClip generatedClip, out string error)
        {
            error = string.Empty;
            if (context == null || context.AnimatorState == null || generatedClip == null)
            {
                error = "Animator State apply context is invalid.";
                return false;
            }

            AnimatorController controller = context.AnimatorController ?? KimodoAnimatorSelectionUtility.FindControllerForObject(context.AnimatorState);
            return animatorApplyService.TryApplyState(
                new KimodoAnimatorApplyService.StateApplyContext
                {
                    Controller = controller,
                    State = context.AnimatorState,
                    GeneratedClip = generatedClip
                },
                out error);
        }

        private static bool ApplyToPlayableClip(SelectionContext context, AnimationClip generatedClip, out string error)
        {
            error = string.Empty;
            if (context == null || context.PlayableClip == null || generatedClip == null)
            {
                error = "KimodoPlayableClip apply context is invalid.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(ApplyUndoName);
            Undo.RecordObject(context.PlayableClip, ApplyUndoName);

            TimelineClip timelineClip = KimodoTimelineClipResolver.FindTimelineClipForAsset(context.PlayableClip);
            TrackAsset track = timelineClip?.GetParentTrack();
            if (track != null)
            {
                Undo.RecordObject(track, ApplyUndoName);
            }

            context.PlayableClip.clip = generatedClip;
            EditorUtility.SetDirty(context.PlayableClip);
            EditorUtility.SetDirty(generatedClip);
            if (track != null)
            {
                EditorUtility.SetDirty(track);
            }

            if (TimelineEditor.inspectedAsset != null)
            {
                EditorUtility.SetDirty(TimelineEditor.inspectedAsset);
            }

            TimelineEditor.Refresh(RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate | RefreshReason.WindowNeedsRedraw);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
            return true;
        }

        private static bool TryCreateGeneratedRootMotionClip(
            SelectionContext context,
            FootRootMotionFrame[] frames,
            FootRootMotionResult solved,
            bool keepOriginMotion,
            out AnimationClip generatedClip,
            out string error)
        {
            generatedClip = null;
            error = string.Empty;

            if (context == null || context.SourceClip == null)
            {
                error = "Source clip is null.";
                return false;
            }

            if (frames == null || solved == null || solved.rootXZ == null || solved.rootXZ.Length == 0)
            {
                error = "Solved root motion result is empty.";
                return false;
            }

            generatedClip = KimodoEditorClipWritebackService.CreateGeneratedAnimationClipAsset(
                $"RootMotion_{context.SourceClip.name}_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
            try
            {
                KimodoEditorClipUtility.CopyClipData(context.SourceClip, generatedClip, forceNoLoopKeepY: false);
                generatedClip.legacy = context.SourceClip.legacy;
                generatedClip.frameRate = context.SourceClip.frameRate > 0f ? context.SourceClip.frameRate : KimodoPlayableClip.FIXED_FRAME_RATE;

                if (!RewriteRootMotionCurves(generatedClip, frames, solved, keepOriginMotion, out error))
                {
                    KimodoEditorClipWritebackService.TryDeleteGeneratedAnimationClipAsset(generatedClip);
                    generatedClip = null;
                    return false;
                }

                generatedClip.EnsureQuaternionContinuity();
                EditorUtility.SetDirty(generatedClip);
                KimodoEditorClipWritebackService.FlushWritebackAssets();
                return true;
            }
            catch (Exception ex)
            {
                KimodoEditorClipWritebackService.TryDeleteGeneratedAnimationClipAsset(generatedClip);
                generatedClip = null;
                error = $"Create generated root motion clip failed: {ex.Message}";
                return false;
            }
        }

        private static bool RewriteRootMotionCurves(
            AnimationClip clip,
            FootRootMotionFrame[] frames,
            FootRootMotionResult solved,
            bool keepOriginMotion,
            out string error)
        {
            error = string.Empty;
            int frameCount = Mathf.Min(
                frames != null ? frames.Length : 0,
                Mathf.Min(
                    solved != null && solved.rootXZ != null ? solved.rootXZ.Length : 0,
                    solved != null && solved.rootYawRadians != null ? solved.rootYawRadians.Length : 0));
            if (clip == null || frameCount <= 0)
            {
                error = "No solved frames available.";
                return false;
            }

            AnimationCurve rootTx = new AnimationCurve();
            AnimationCurve rootTy = BuildOriginalRootYCurve(clip, frames, frameCount);
            AnimationCurve rootTz = new AnimationCurve();
            AnimationCurve rootQx = new AnimationCurve();
            AnimationCurve rootQy = new AnimationCurve();
            AnimationCurve rootQz = new AnimationCurve();
            AnimationCurve rootQw = new AnimationCurve();

            for (int i = 0; i < frameCount; i++)
            {
                float time = frames[i].time;
                Vector2 solvedXZ = solved.rootXZ[i];
                Quaternion yawOnly = Quaternion.AngleAxis(solved.rootYawRadians[i] * Mathf.Rad2Deg, Vector3.up);

                rootTx.AddKey(time, solvedXZ.x);
                rootTz.AddKey(time, solvedXZ.y);
                rootQx.AddKey(time, yawOnly.x);
                rootQy.AddKey(time, yawOnly.y);
                rootQz.AddKey(time, yawOnly.z);
                rootQw.AddKey(time, yawOnly.w);
            }

            SetAnimatorFloatCurve(clip, "RootT.x", rootTx);
            SetAnimatorFloatCurve(clip, "RootT.y", rootTy);
            SetAnimatorFloatCurve(clip, "RootT.z", rootTz);
            SetAnimatorFloatCurve(clip, "RootQ.x", rootQx);
            SetAnimatorFloatCurve(clip, "RootQ.y", rootQy);
            SetAnimatorFloatCurve(clip, "RootQ.z", rootQz);
            SetAnimatorFloatCurve(clip, "RootQ.w", rootQw);
            if (!keepOriginMotion)
            {
                ClearAnimatorFloatCurve(clip, "MotionT.x");
                ClearAnimatorFloatCurve(clip, "MotionT.y");
                ClearAnimatorFloatCurve(clip, "MotionT.z");
                ClearAnimatorFloatCurve(clip, "MotionQ.x");
                ClearAnimatorFloatCurve(clip, "MotionQ.y");
                ClearAnimatorFloatCurve(clip, "MotionQ.z");
                ClearAnimatorFloatCurve(clip, "MotionQ.w");
            }

            return true;
        }

        private static void SetAnimatorFloatCurve(AnimationClip clip, string propertyName, AnimationCurve curve)
        {
            clip.SetCurve(string.Empty, typeof(Animator), propertyName, curve);
        }

        private static void ClearAnimatorFloatCurve(AnimationClip clip, string propertyName)
        {
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), propertyName),
                null);
        }

        private static AnimationCurve BuildOriginalRootYCurve(AnimationClip clip, FootRootMotionFrame[] frames, int frameCount)
        {
            AnimationCurve original = AnimationUtility.GetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), "RootT.y"));
            if (original != null && original.length > 0)
            {
                return new AnimationCurve(original.keys)
                {
                    preWrapMode = original.preWrapMode,
                    postWrapMode = original.postWrapMode
                };
            }

            AnimationCurve fallback = new AnimationCurve();
            for (int i = 0; i < frameCount; i++)
            {
                fallback.AddKey(frames[i].time, frames[i].sampledRootWorld.y);
            }

            return fallback;
        }

        private void EnsurePreview()
        {
            if (avatarPreview != null || selectionContext == null || GetPreviewClip() == null)
            {
                return;
            }

            RebuildPreview();
        }

        private void RebuildPreview()
        {
            CleanupPreview();
            if (selectionContext == null || selectionContext.ModelRoot == null)
            {
                return;
            }

            AnimationClip previewClip = GetPreviewClip();
            if (previewClip == null)
            {
                return;
            }

            previewInstance = UnityEngine.Object.Instantiate(selectionContext.ModelRoot);
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            previewAnimator = previewInstance.GetComponentInChildren<Animator>(true);
            if (previewAnimator == null)
            {
                previewAnimator = previewInstance.AddComponent<Animator>();
            }

            if (KimodoRetargetCoreUtility.IsValidHumanoid(selectionContext.Avatar))
            {
                previewAnimator.avatar = selectionContext.Avatar;
            }

            previewAnimator.applyRootMotion = true;
            previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            previewAnimator.enabled = true;
            previewAnimator.Rebind();
            previewAnimator.Update(0f);

            avatarPreview = new KimodoAvatarPreview(previewAnimator, previewClip);
            avatarPreview.ShowIKOnFeetButton = previewClip.isHumanMotion;
            if (avatarPreview.timeControl != null)
            {
                avatarPreview.timeControl.startTime = 0f;
                avatarPreview.timeControl.stopTime = Mathf.Max(0.001f, previewClip.length);
                avatarPreview.timeControl.currentTime = 0f;
                avatarPreview.timeControl.loop = true;
                avatarPreview.timeControl.playing = false;
            }

            ApplyPreviewClipAtTime(0f);
            avatarPreview.ResetPreviewFocus();
        }

        private bool TickPreview()
        {
            if (avatarPreview?.timeControl == null || avatarPreview.PreviewObject == null)
            {
                return false;
            }

            AnimationClip previewClip = GetPreviewClip();
            if (previewClip == null)
            {
                return false;
            }

            KimodoPreviewTimeControl timeControl = avatarPreview.timeControl;
            bool wasPlaying = timeControl.playing;
            bool hadPendingManualStep = timeControl.HasPendingManualTimeStep;
            bool wasScrubbing = timeControl.IsScrubbing;
            float previousTime = timeControl.currentTime;

            timeControl.Update();

            float currentTime = Mathf.Clamp(timeControl.currentTime, 0f, previewClip.length);
            bool timeChanged =
                float.IsNaN(lastAppliedPreviewTime) ||
                !Mathf.Approximately(previousTime, currentTime) ||
                !Mathf.Approximately(lastAppliedPreviewTime, currentTime);

            if (!timeChanged && !wasPlaying && !hadPendingManualStep && !wasScrubbing)
            {
                return false;
            }

            ApplyPreviewClipAtTime(currentTime);
            return true;
        }

        private bool IsPreviewInteractionActive()
        {
            if (GUIUtility.hotControl != 0)
            {
                return true;
            }

            KimodoPreviewTimeControl timeControl = avatarPreview?.timeControl;
            return timeControl != null &&
                   (timeControl.playing || timeControl.IsScrubbing || timeControl.HasPendingManualTimeStep);
        }

        private void ApplyPreviewClipAtTime(float time)
        {
            AnimationClip previewClip = GetPreviewClip();
            GameObject previewObject = avatarPreview?.PreviewObject;
            if (previewClip == null || previewObject == null)
            {
                return;
            }

            float sampleTime = Mathf.Clamp(time, 0f, previewClip.length);
            previewClip.SampleAnimation(previewObject, sampleTime);
            lastAppliedPreviewTime = sampleTime;
        }

        private AnimationClip GetPreviewClip()
        {
            if (previewMode == PreviewMode.Generated && generatedPreviewClip != null)
            {
                return generatedPreviewClip;
            }

            return selectionContext != null ? selectionContext.SourceClip : null;
        }

        private void CleanupPreview()
        {
            lastAppliedPreviewTime = float.NaN;
            if (avatarPreview != null)
            {
                avatarPreview.OnDestroy();
                avatarPreview = null;
            }

            if (previewInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(previewInstance);
                previewInstance = null;
            }

            previewAnimator = null;
        }

        private void ClearSelectionContext()
        {
            if (selectionContext != null && selectionContext.OwnsModelRoot && selectionContext.ModelRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(selectionContext.ModelRoot);
            }

            selectionContext = null;
        }

        private float GetCurrentTime()
        {
            AnimationClip previewClip = GetPreviewClip();
            return avatarPreview?.timeControl != null
                ? Mathf.Clamp(avatarPreview.timeControl.currentTime, 0f, previewClip != null ? previewClip.length : 0f)
                : 0f;
        }

        private int GetCurrentFrameIndex()
        {
            if (sampledFrames == null || sampledFrames.Length == 0)
            {
                return 0;
            }

            float t = GetCurrentTime();
            int bestIndex = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < sampledFrames.Length; i++)
            {
                float d = Mathf.Abs(sampledFrames[i].time - t);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool TryGetSelectedTimelinePlayableClip(out KimodoPlayableClip playableClip)
        {
            playableClip = null;
            TimelineClip[] selectedClips = TimelineEditor.selectedClips;
            if (selectedClips == null)
            {
                return false;
            }

            for (int i = 0; i < selectedClips.Length; i++)
            {
                if (selectedClips[i]?.asset is KimodoPlayableClip clip)
                {
                    playableClip = clip;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveAvatarForModelRoot(GameObject modelRoot, out Avatar avatar, out string error)
        {
            avatar = null;
            error = string.Empty;
            if (modelRoot == null)
            {
                error = "Model root is null.";
                return false;
            }

            KimodoLocalAvatarUtility.AvatarResolveResult result = KimodoLocalAvatarUtility.ResolveAvatarFromGameObject(modelRoot);
            if (!result.IsHumanoid || result.Avatar == null)
            {
                error = string.IsNullOrWhiteSpace(result.Error) ? "Model does not resolve to a valid humanoid avatar." : result.Error;
                return false;
            }

            avatar = result.Avatar;
            return true;
        }

        private static bool TryResolveScenePreviewSourceByController(AnimatorController controller, out GameObject sourceRoot, out Avatar avatar)
        {
            sourceRoot = null;
            avatar = null;
            if (controller == null)
            {
                return false;
            }

            Animator[] animators = Resources.FindObjectsOfTypeAll<Animator>();
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (!IsUsableSceneAnimator(animator) || !IsControllerMatch(animator, controller))
                {
                    continue;
                }

                GameObject candidateRoot = animator.avatarRoot != null ? animator.avatarRoot.gameObject : animator.gameObject;
                Avatar candidateAvatar = animator.avatar;
                if (candidateRoot == null || !KimodoRetargetCoreUtility.IsValidHumanoid(candidateAvatar))
                {
                    continue;
                }

                if (!KimodoLocalAvatarUtility.CheckAvatarValid(candidateAvatar, candidateRoot))
                {
                    continue;
                }

                sourceRoot = candidateRoot;
                avatar = candidateAvatar;
                return true;
            }

            return false;
        }

        private static bool IsUsableSceneAnimator(Animator animator)
        {
            if (animator == null)
            {
                return false;
            }

            GameObject gameObject = animator.gameObject;
            if (gameObject == null || !gameObject.scene.IsValid())
            {
                return false;
            }

            if (EditorUtility.IsPersistent(animator) || EditorUtility.IsPersistent(gameObject))
            {
                return false;
            }

            if ((animator.hideFlags & HideFlags.HideAndDontSave) != 0 ||
                (gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsControllerMatch(Animator animator, AnimatorController controller)
        {
            if (animator == null || controller == null)
            {
                return false;
            }

            RuntimeAnimatorController runtimeController = ResolveAnimatorController(animator);
            if (runtimeController == null)
            {
                return false;
            }

            if (ReferenceEquals(runtimeController, controller))
            {
                return true;
            }

            AnimatorOverrideController overrideController = runtimeController as AnimatorOverrideController;
            return overrideController != null && ReferenceEquals(overrideController.runtimeAnimatorController, controller);
        }

        private static RuntimeAnimatorController ResolveAnimatorController(Animator animator)
        {
            if (animator == null)
            {
                return null;
            }

            if (animator.runtimeAnimatorController != null)
            {
                return animator.runtimeAnimatorController;
            }

            var serializedObject = new SerializedObject(animator);
            var controllerProperty = serializedObject.FindProperty("m_Controller");
            return controllerProperty != null ? controllerProperty.objectReferenceValue as RuntimeAnimatorController : null;
        }

        private static GameObject LoadClipOwnerModelAsset(AnimationClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            string clipPath = AssetDatabase.GetAssetPath(clip);
            return string.IsNullOrWhiteSpace(clipPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(clipPath);
        }

        private static int CountPlants(bool[] mask)
        {
            if (mask == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i])
                {
                    count++;
                }
            }

            return count;
        }

        private static float ComputePredictionRatio(bool[] mask)
        {
            if (mask == null || mask.Length == 0)
            {
                return 0f;
            }

            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i])
                {
                    count++;
                }
            }

            return (float)count / mask.Length;
        }

        private static void ExpandBounds(ref Vector2 min, ref Vector2 max, Vector3 point)
        {
            Vector2 p = ToXZ(point);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        private static Vector2 ToXZ(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        private static Vector2 ChartPoint(Rect rect, Vector2 point, Vector2 min, Vector2 size)
        {
            const float padding = 16f;
            float x = rect.x + padding + ((point.x - min.x) / size.x) * (rect.width - padding * 2f);
            float y = rect.yMax - padding - ((point.y - min.y) / size.y) * (rect.height - padding * 2f);
            return new Vector2(x, y);
        }

        private static void DrawPolyline(Rect rect, FootRootMotionFrame[] frames, Func<FootRootMotionFrame, Vector2> selector, Vector2 min, Vector2 size, Color color, float width)
        {
            if (frames == null || frames.Length < 2)
            {
                return;
            }

            Handles.color = color;
            Vector3[] points = new Vector3[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                Vector2 p = ChartPoint(rect, selector(frames[i]), min, size);
                points[i] = new Vector3(p.x, p.y, 0f);
            }
            Handles.DrawAAPolyLine(width, points);
        }

        private static void DrawPolyline(Rect rect, Vector2[] pointsSource, Func<Vector2, Vector2> selector, Vector2 min, Vector2 size, Color color, float width)
        {
            if (pointsSource == null || pointsSource.Length < 2)
            {
                return;
            }

            Handles.color = color;
            Vector3[] points = new Vector3[pointsSource.Length];
            for (int i = 0; i < pointsSource.Length; i++)
            {
                Vector2 p = ChartPoint(rect, selector(pointsSource[i]), min, size);
                points[i] = new Vector3(p.x, p.y, 0f);
            }
            Handles.DrawAAPolyLine(width, points);
        }

        private static void DrawPlantAnchors(Rect rect, Vector2[] anchors, bool[] plantMask, Vector2 min, Vector2 size, Color color)
        {
            if (anchors == null || plantMask == null)
            {
                return;
            }

            Handles.color = color;
            for (int i = 0; i < anchors.Length && i < plantMask.Length; i++)
            {
                if (!plantMask[i])
                {
                    continue;
                }

                Vector2 p = ChartPoint(rect, anchors[i], min, size);
                Rect marker = new Rect(p.x - 2f, p.y - 2f, 4f, 4f);
                EditorGUI.DrawRect(marker, color);
            }
        }
    }
}
