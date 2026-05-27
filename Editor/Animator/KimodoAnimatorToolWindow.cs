using KimodoUnityMotionTools.Generation.Pipeline;
using KimodoUnityMotionTools.ProjectEditor.GenerationPipeline;
using KimodoUnityMotionTools.ProjectEditor.Manager;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor.AnimatorTooling
{
    public sealed class KimodoAnimatorToolWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Kimodo/Animator/Kimodo Animator Tool";
        private const float MinLeftWidth = 360f;

        private KimodoPlayableClip workingClip;
        private SerializedObject clipSo;
        private string lastStatus = string.Empty;
        private string lastError = string.Empty;
        private bool isGenerating;
        private bool managerSubscribed;
        private AnimationClip generatedClipForPreview;
        private AnimationClip lastSuccessfulGeneratedClipForApply;
        private AnimationClip originalClipForPreview;
        private Avatar retargetAvatarForPreview;
        private GameObject previewSourceTemplate;
        private GameObject originalPreviewInstance;
        private GameObject generatedPreviewInstance;
        private AnimationClip transitionMixedPreviewClip;
        private AnimatorStateTransition selectedTransition;
        private AnimatorState selectedState;
        private AnimatorState selectedFromState;
        private AnimatorController selectedController;
        private AnimatorStateMachine selectedStateMachine;
        private readonly KimodoAnimatorApplyService applyService = new KimodoAnimatorApplyService();
        private Vector2 rightScroll;
        private enum PreviewMode
        {
            Original = 0,
            Generated = 1
        }
        private PreviewMode previewMode = PreviewMode.Original;
        private KimodoAvatarPreviewCore avatarPreviewCore;
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
            EnsureWorkingClip();
            ResolveSelectionContext();
            SubscribeManagerEvents();
            avatarPreviewCore = new KimodoAvatarPreviewCore();
        }

        private void OnDisable()
        {
            UnsubscribeManagerEvents();
            DestroyPreviewInstances();
            DestroyTransitionMixedPreviewClip();
            avatarPreviewCore?.Dispose();
            avatarPreviewCore = null;
        }

        private void OnSelectionChange()
        {
            ResolveSelectionContext();
            BuildOrRefreshPreviewInstances();
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

        private void OnGUI()
        {
            EnsureWorkingClip();
            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPreviewPane();
                DrawRightPanel();
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

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Selection Source: Selection.activeObject", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                bool wantOriginal = GUILayout.Toggle(previewMode == PreviewMode.Original, "Show Original", EditorStyles.toolbarButton);
                bool wantGenerated = GUILayout.Toggle(previewMode == PreviewMode.Generated, "Show Generated", EditorStyles.toolbarButton);
                if (wantOriginal)
                {
                    previewMode = PreviewMode.Original;
                    avatarPreviewCore?.RestartFromZeroAndPlay();
                }
                else if (wantGenerated)
                {
                    previewMode = PreviewMode.Generated;
                    avatarPreviewCore?.RestartFromZeroAndPlay();
                }

                if (GUILayout.Button("Reset Preview", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                {
                    generatedClipForPreview = null;
                    lastSuccessfulGeneratedClipForApply = null;
                    previewMode = PreviewMode.Original;
                    lastStatus = "Generated preview cleared.";
                    lastError = string.Empty;
                }
            }
        }

        private void DrawLeftPreviewPane()
        {
            Rect leftRect = EditorGUILayout.GetControlRect(false, position.height - 70f, GUILayout.MinWidth(MinLeftWidth), GUILayout.ExpandWidth(true));
            GUI.Box(leftRect, GUIContent.none);
            Handles.BeginGUI();
            Handles.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            Handles.DrawLine(new Vector3(leftRect.xMax, leftRect.yMin), new Vector3(leftRect.xMax, leftRect.yMax));
            Handles.EndGUI();

            Rect renderRect = new Rect(leftRect.x + 8f, leftRect.y + 6f, leftRect.width - 16f, leftRect.height - 14f);
            if (avatarPreviewCore == null)
            {
                avatarPreviewCore = new KimodoAvatarPreviewCore();
            }

            if (previewMode == PreviewMode.Generated)
            {
                avatarPreviewCore.SetPreview(
                    generatedPreviewInstance,
                    generatedClipForPreview,
                    generatedClipForPreview == null ? "No generated animation." : "Generated preview unavailable.");
            }
            else
            {
                avatarPreviewCore.SetPreview(
                    originalPreviewInstance,
                    originalClipForPreview,
                    originalClipForPreview == null ? "No original animation." : "Original preview unavailable.");
            }

            avatarPreviewCore.Draw(renderRect);
        }

        private void DrawRightPanel()
        {
            float width = Mathf.Max(420f, position.width * 0.46f);
            using (var scroll = new EditorGUILayout.ScrollViewScope(rightScroll, GUILayout.Width(width)))
            {
                rightScroll = scroll.scrollPosition;
                if (workingClip == null)
                {
                    EditorGUILayout.HelpBox("Failed to initialize working KimodoPlayableClip instance.", MessageType.Error);
                    return;
                }

                clipSo.UpdateIfRequiredOrScript();
                DrawSelectionInfo();
                DrawGeneratePanel();
                DrawBakePanel();
                DrawGeneratedPanel();
                DrawAnimationClipPanel();
                DrawApplyPanel();
                clipSo.ApplyModifiedProperties();
            }
        }

        private void DrawSelectionInfo()
        {
            EditorGUILayout.LabelField("Selection Context", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            if (selectedTransition != null)
            {
                EditorGUILayout.LabelField("Mode: Transition");
                EditorGUILayout.ObjectField("Transition", selectedTransition, typeof(AnimatorStateTransition), false);
                EditorGUILayout.ObjectField("From", selectedFromState, typeof(AnimatorState), false);
                EditorGUILayout.ObjectField("To", selectedTransition.destinationState, typeof(AnimatorState), false);
            }
            else if (selectedState != null)
            {
                EditorGUILayout.LabelField("Mode: State");
                EditorGUILayout.ObjectField("State", selectedState, typeof(AnimatorState), false);
            }
            else
            {
                EditorGUILayout.HelpBox("Select AnimatorStateTransition or AnimatorState in Animator Controller inspector/graph.", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawGeneratePanel()
        {
            EditorGUILayout.LabelField("Generate Motion", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            DrawProp("generationBackend", "Backend");
            DrawProp("bridgeModelName", "Bridge Model");
            DrawProp("bridgeVramMode", "VRAM Mode");
            DrawProp("motionPrompt", "Prompt", true, 60f);
            DrawProp("generationFrames", "Duration (frames)");
            DrawProp("diffusionSteps", "Diffusion Steps");
            DrawProp("randomSeed", "Random");
            DrawProp("seed", "Seed");
            DrawProp("enableInbetweenInterpolation", "In-between Interpolation");
            DrawProp("showConstraint", "Show Constraint");
            DrawProp("workflowJsonAsset", "Workflow JSON Asset");

            bool canGenerate = !isGenerating && (selectedTransition != null || selectedState != null);
            EditorGUI.BeginDisabledGroup(!canGenerate);
            if (GUILayout.Button("Generate & Bake", GUILayout.Height(30f)))
            {
                StartGenerate();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!isGenerating);
            if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
            {
                KimodoEditorCommandManager.Dispatch(new CancelPlayableClipGenerationCommand(workingClip));
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        private void DrawBakePanel()
        {
            EditorGUILayout.LabelField("Animation Bake", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            DrawProp("autoRetargetOnBinding", "Auto Retarget On Binding");
            DrawProp("customRetargetAvatar", "Custom Avatar");
            DrawProp("curveFilterOptions", "Curve Filter Options", true);
            EditorGUILayout.EndVertical();
        }

        private void DrawGeneratedPanel()
        {
            EditorGUILayout.LabelField("Generated", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Generated Clip Preview", generatedClipForPreview, typeof(AnimationClip), false);
            }
            if (GUILayout.Button("Reset", GUILayout.Width(100)))
            {
                generatedClipForPreview = null;
                lastSuccessfulGeneratedClipForApply = null;
                previewMode = PreviewMode.Original;
                lastStatus = "Generated preview cleared.";
                lastError = string.Empty;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAnimationClipPanel()
        {
            EditorGUILayout.LabelField("Animation Clip", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            DrawProp("m_Clip", "Clip");
            DrawProp("m_ApplyFootIK", "Foot IK");
            DrawProp("m_Loop", "Loop");
            EditorGUILayout.EndVertical();
        }

        private void DrawApplyPanel()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Apply", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            bool canApply = lastSuccessfulGeneratedClipForApply != null && !isGenerating && (selectedTransition != null || selectedState != null);
            EditorGUI.BeginDisabledGroup(!canApply);
            if (GUILayout.Button("Apply", GUILayout.Height(28f)))
            {
                ApplyGeneratedResult();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        private void DrawProp(string propertyName, string label, bool multiLine = false, float height = 18f)
        {
            SerializedProperty p = clipSo.FindProperty(propertyName);
            if (p == null)
            {
                return;
            }
            if (multiLine && p.propertyType == SerializedPropertyType.String)
            {
                EditorGUILayout.LabelField(label);
                p.stringValue = EditorGUILayout.TextArea(p.stringValue ?? string.Empty, GUILayout.Height(height));
                return;
            }
            EditorGUILayout.PropertyField(p, new GUIContent(label), multiLine);
        }

        private void StartGenerate()
        {
            if (!TryBuildExternalConstraints(out string constraintsJson, out string error))
            {
                lastError = error;
                return;
            }

            bool accepted = KimodoEditorCommandManager.Dispatch(
                new GeneratePlayableClipCommand(
                    workingClip,
                    promptOverride: null,
                    externalConstraint: new KimodoExternalConstraintRequest
                    {
                        Enabled = true,
                        ConstraintsJson = constraintsJson,
                        RetargetAvatar = retargetAvatarForPreview
                    }));
            if (!accepted)
            {
                lastError = "Failed to dispatch generate command.";
                return;
            }

            isGenerating = true;
            lastError = string.Empty;
            lastStatus = "Queued generation...";
        }

        private bool TryBuildExternalConstraints(out string constraintsJson, out string error)
        {
            constraintsJson = string.Empty;
            error = string.Empty;

            if (!TryResolveAvatarAndMotion(out Avatar avatar, out AnimationClip sourceClip, out error))
            {
                return false;
            }

            string modelName = workingClip != null ? workingClip.bridgeModelName : "Kimodo-SOMA-RP-v1";
            var samples = new List<KimodoMarkerSampleResult>(2);
            if (!TrySampleAtNormalizedTime(avatar, sourceClip, modelName, 0.0, out KimodoMarkerSampleResult begin, out error))
            {
                return false;
            }
            if (!TrySampleAtNormalizedTime(avatar, sourceClip, modelName, 1.0, out KimodoMarkerSampleResult end, out error))
            {
                return false;
            }

            begin.constraintType = "fullbody";
            end.constraintType = "fullbody";
            begin.sampleTime = 0.0;
            end.sampleTime = Mathf.Max(1, workingClip.generationFrames) / 30.0;
            samples.Add(begin);
            samples.Add(end);
            constraintsJson = KimodoConstraintJsonExporter.ToConstraintsJson(samples, clipStartSeconds: 0.0, clipDurationSeconds: end.sampleTime);
            return true;
        }

        private bool TrySampleAtNormalizedTime(
            Avatar avatar,
            AnimationClip clip,
            string modelName,
            double normalizedTime,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            sample = null;
            error = string.Empty;
            if (avatar == null || clip == null)
            {
                error = "Avatar or source clip is null.";
                return false;
            }

            double duration = Math.Max(0.001, clip.length);
            double globalTime = Mathf.Clamp01((float)normalizedTime) * duration;
            Animator tempAnimator = CreateSamplingAnimatorFromAvatar(avatar, out GameObject tempRoot, out error);
            if (tempAnimator == null)
            {
                return false;
            }

            clip.SampleAnimation(tempAnimator.gameObject, (float)globalTime);
            bool ok = KimodoMarkerSamplingUtility.TrySampleMarker(
                tempAnimator,
                tempAnimator.transform,
                sourceClip: null,
                modelName,
                globalTime,
                "fullbody",
                out sample,
                out error);

            if (tempRoot != null)
            {
                DestroyImmediate(tempRoot);
            }

            return ok;
        }

        private bool TryResolveAvatarAndMotion(out Avatar avatar, out AnimationClip sourceClip, out string error)
        {
            avatar = null;
            sourceClip = null;
            error = string.Empty;

            if (selectedState != null)
            {
                sourceClip = selectedState.motion as AnimationClip;
                if (sourceClip == null)
                {
                    error = "State motion is not an AnimationClip.";
                    return false;
                }
                if (!TryPreparePreviewSource(sourceClip, out avatar, out error))
                {
                    return false;
                }
            }
            else if (selectedTransition != null)
            {
                AnimatorState from = selectedFromState != null ? selectedFromState : ResolveFromState(selectedStateMachine, selectedTransition);
                AnimatorState to = selectedTransition.destinationState;
                AnimationClip fromClip = from != null ? from.motion as AnimationClip : null;
                AnimationClip toClip = to != null ? to.motion as AnimationClip : null;
                if (fromClip == null || toClip == null)
                {
                    error = "Transition preview requires from/to state motions as AnimationClip.";
                    return false;
                }

                if (!TryPreparePreviewSource(fromClip, out avatar, out error))
                {
                    return false;
                }

                sourceClip = BuildTransitionMixedPreviewClip(fromClip, toClip, selectedTransition, out string mixError);
                if (sourceClip == null)
                {
                    error = string.IsNullOrWhiteSpace(mixError) ? "Failed to build transition mixed preview clip." : mixError;
                    return false;
                }
            }

            originalClipForPreview = sourceClip;
            retargetAvatarForPreview = avatar;
            BuildOrRefreshPreviewInstances();
            return true;
        }

        private static Animator CreateSamplingAnimatorFromAvatar(Avatar avatar, out GameObject root, out string error)
        {
            root = null;
            error = string.Empty;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                error = "Sampling avatar is null or invalid humanoid avatar.";
                return null;
            }

            root = new GameObject("KimodoAnimatorToolSamplingRoot");
            root.hideFlags = HideFlags.HideAndDontSave;
            if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(avatar, root.transform, out error))
            {
                DestroyImmediate(root);
                root = null;
                return null;
            }

            Transform samplingRoot = root.transform;
            Animator animator = samplingRoot.gameObject.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.enabled = false;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            return animator;
        }

        private void ApplyGeneratedResult()
        {
            if (lastSuccessfulGeneratedClipForApply == null)
            {
                lastError = "No generated clip available for apply.";
                return;
            }

            string error;
            bool ok;
            if (selectedTransition != null)
            {
                AnimatorState from = selectedFromState != null ? selectedFromState : ResolveFromState(selectedStateMachine, selectedTransition);
                AnimatorState to = selectedTransition.destinationState;
                ok = applyService.TryApplyTransition(
                    new KimodoAnimatorApplyService.TransitionApplyContext
                    {
                        Controller = selectedController,
                        StateMachine = selectedStateMachine,
                        FromState = from,
                        ToState = to,
                        OriginalTransition = selectedTransition,
                        GeneratedClip = lastSuccessfulGeneratedClipForApply,
                        NewStateName = $"{from?.name}_{to?.name}_KimodoInsert"
                    },
                    out error);
            }
            else if (selectedState != null)
            {
                ok = applyService.TryApplyState(
                    new KimodoAnimatorApplyService.StateApplyContext
                    {
                        Controller = selectedController,
                        State = selectedState,
                        GeneratedClip = lastSuccessfulGeneratedClipForApply
                    },
                    out error);
            }
            else
            {
                ok = false;
                error = "No valid selection context for apply.";
            }

            if (!ok)
            {
                lastError = error;
                return;
            }

            lastError = string.Empty;
            lastStatus = "Apply succeeded. Assets marked dirty (not auto-saved).";
        }

        private void EnsureWorkingClip()
        {
            if (workingClip != null && clipSo != null)
            {
                return;
            }

            workingClip = CreateInstance<KimodoPlayableClip>();
            clipSo = new SerializedObject(workingClip);
        }

        private void ResolveSelectionContext()
        {
            selectedTransition = null;
            selectedState = null;
            selectedFromState = null;
            selectedController = null;
            selectedStateMachine = null;
            originalClipForPreview = null;
            retargetAvatarForPreview = null;

            UnityEngine.Object obj = Selection.activeObject;
            if (obj is AnimatorStateTransition transition)
            {
                selectedTransition = transition;
                selectedController = FindControllerForObject(transition);
                selectedStateMachine = FindStateMachineForTransition(selectedController, transition, out selectedFromState);
                RefreshPreviewSourceFromSelection();
                return;
            }

            if (obj is AnimatorState state)
            {
                selectedState = state;
                selectedController = FindControllerForObject(state);
                selectedStateMachine = FindStateMachineForState(selectedController, state);
                RefreshPreviewSourceFromSelection();
            }
            else
            {
                DestroyPreviewInstances();
            }
        }

        private void RefreshPreviewSourceFromSelection()
        {
            if (workingClip == null)
            {
                return;
            }

            if (TryResolveAvatarAndMotion(out _, out _, out _))
            {
                BuildOrRefreshPreviewInstances();
            }
            else
            {
                DestroyPreviewInstances();
            }
        }

        private static AnimatorController FindControllerForObject(UnityEngine.Object target)
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

        private static AnimatorStateMachine FindStateMachineForState(AnimatorController controller, AnimatorState state)
        {
            if (controller == null || state == null)
            {
                return null;
            }

            for (int i = 0; i < controller.layers.Length; i++)
            {
                AnimatorStateMachine sm = controller.layers[i].stateMachine;
                ChildAnimatorState[] states = sm.states;
                for (int j = 0; j < states.Length; j++)
                {
                    if (states[j].state == state)
                    {
                        return sm;
                    }
                }
            }

            return null;
        }

        private static AnimatorStateMachine FindStateMachineForTransition(
            AnimatorController controller,
            AnimatorStateTransition transition,
            out AnimatorState fromState)
        {
            fromState = null;
            if (controller == null || transition == null)
            {
                return null;
            }

            for (int i = 0; i < controller.layers.Length; i++)
            {
                AnimatorStateMachine sm = controller.layers[i].stateMachine;
                ChildAnimatorState[] states = sm.states;
                for (int j = 0; j < states.Length; j++)
                {
                    AnimatorState s = states[j].state;
                    AnimatorStateTransition[] transitions = s.transitions;
                    for (int k = 0; k < transitions.Length; k++)
                    {
                        if (transitions[k] == transition)
                        {
                            fromState = s;
                            return sm;
                        }
                    }
                }
            }

            return null;
        }

        private static AnimatorState ResolveFromState(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            if (sm == null || transition == null)
            {
                return null;
            }

            ChildAnimatorState[] states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState s = states[i].state;
                AnimatorStateTransition[] ts = s.transitions;
                for (int j = 0; j < ts.Length; j++)
                {
                    if (ts[j] == transition)
                    {
                        return s;
                    }
                }
            }
            return null;
        }

        private void SubscribeManagerEvents()
        {
            if (managerSubscribed)
            {
                return;
            }

            KimodoEditorCommandManager.CommandProgress += OnCommandProgress;
            KimodoEditorCommandManager.CommandCompleted += OnCommandCompleted;
            KimodoEditorCommandManager.CommandFailed += OnCommandFailed;
            KimodoEditorCommandManager.CommandCanceled += OnCommandCanceled;
            managerSubscribed = true;
        }

        private void UnsubscribeManagerEvents()
        {
            if (!managerSubscribed)
            {
                return;
            }

            KimodoEditorCommandManager.CommandProgress -= OnCommandProgress;
            KimodoEditorCommandManager.CommandCompleted -= OnCommandCompleted;
            KimodoEditorCommandManager.CommandFailed -= OnCommandFailed;
            KimodoEditorCommandManager.CommandCanceled -= OnCommandCanceled;
            managerSubscribed = false;
        }

        private void OnCommandProgress(KimodoEditorCommandProgressEvent evt)
        {
            if (!IsCommandForWorkingClip(evt.Command))
            {
                return;
            }

            lastStatus = evt.Message;
            Repaint();
        }

        private void OnCommandCompleted(KimodoEditorCommandCompletedEvent evt)
        {
            if (!IsCommandForWorkingClip(evt.Command))
            {
                return;
            }

            isGenerating = false;
            if (evt.Payload is KimodoEditorGenerateResult gen)
            {
                generatedClipForPreview = gen.GeneratedClip;
                 lastSuccessfulGeneratedClipForApply = gen.GeneratedClip;
                previewMode = PreviewMode.Generated;
                lastStatus = "Generation complete.";
                lastError = string.Empty;
                BuildOrRefreshPreviewInstances();
                avatarPreviewCore?.RestartFromZeroAndPlay();
            }

            Repaint();
        }

        private void OnCommandFailed(KimodoEditorCommandFailedEvent evt)
        {
            if (!IsCommandForWorkingClip(evt.Command))
            {
                return;
            }

            isGenerating = false;
            generatedClipForPreview = null;
            lastError = evt.Message;
            lastStatus = "Generation failed.";
            Repaint();
        }

        private void OnCommandCanceled(KimodoEditorCommandCanceledEvent evt)
        {
            if (!IsCommandForWorkingClip(evt.Command))
            {
                return;
            }

            isGenerating = false;
            generatedClipForPreview = null;
            lastStatus = "Generation canceled.";
            Repaint();
        }

        private bool IsCommandForWorkingClip(IKimodoEditorCommand command)
        {
            if (command == null || workingClip == null)
            {
                return false;
            }
            return string.Equals(command.TargetKey, "clip:" + workingClip.GetInstanceID(), StringComparison.Ordinal);
        }

        private void DestroyPreviewInstances()
        {
            if (originalPreviewInstance != null)
            {
                DestroyImmediate(originalPreviewInstance);
            }
            if (generatedPreviewInstance != null)
            {
                DestroyImmediate(generatedPreviewInstance);
            }
            originalPreviewInstance = null;
            generatedPreviewInstance = null;
        }

        private void DestroyTransitionMixedPreviewClip()
        {
            if (transitionMixedPreviewClip != null)
            {
                DestroyImmediate(transitionMixedPreviewClip);
                transitionMixedPreviewClip = null;
            }
        }

        private void BuildOrRefreshPreviewInstances()
        {
            DestroyPreviewInstances();
            if (previewSourceTemplate == null)
            {
                return;
            }

            if (retargetAvatarForPreview == null || !retargetAvatarForPreview.isValid || !retargetAvatarForPreview.isHuman)
            {
                return;
            }

            originalPreviewInstance = CreatePreviewInstance("KimodoPreviewOriginal");
            generatedPreviewInstance = CreatePreviewInstance("KimodoPreviewGenerated");
        }

        private AnimationClip BuildTransitionMixedPreviewClip(
            AnimationClip fromClip,
            AnimationClip toClip,
            AnimatorStateTransition transition,
            out string error)
        {
            error = string.Empty;
            DestroyTransitionMixedPreviewClip();

            if (fromClip == null || toClip == null)
            {
                error = "Transition preview requires both from/to clips to be AnimationClip.";
                return null;
            }

            if (previewSourceTemplate == null)
            {
                error = "Preview source template is null.";
                return null;
            }

            GameObject fromObj = null;
            GameObject toObj = null;
            GameObject outObj = null;
            try
            {
                fromObj = CreatePreviewInstance("KimodoTransitionFromSample");
                toObj = CreatePreviewInstance("KimodoTransitionToSample");
                outObj = CreatePreviewInstance("KimodoTransitionOutSample");
                if (fromObj == null || toObj == null || outObj == null)
                {
                    error = "Failed to create transition sample instances.";
                    return null;
                }

                float fps = 30f;
                int frameCount = Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(0.1f, transition.duration) * fps));
                var recorder = new UnityEditor.Animations.GameObjectRecorder(outObj);
                recorder.BindComponentsOfType<Transform>(outObj, true);

                for (int i = 0; i < frameCount; i++)
                {
                    float t = frameCount > 1 ? (float)i / (frameCount - 1) : 0f;
                    float fromTime = Mathf.Clamp01(t) * Mathf.Max(0.0001f, fromClip.length);
                    float toTime = Mathf.Clamp01(t) * Mathf.Max(0.0001f, toClip.length);
                    fromClip.SampleAnimation(fromObj, fromTime);
                    toClip.SampleAnimation(toObj, toTime);

                    float w = ComputeTransitionWeight(t, transition);
                    BlendTransforms(fromObj.transform, toObj.transform, outObj.transform, w);
                    recorder.TakeSnapshot(1f / fps);
                }

                var mixed = new AnimationClip
                {
                    name = $"{fromClip.name}_{toClip.name}_TransitionPreviewMix",
                    frameRate = fps,
                    legacy = false
                };
                recorder.SaveToClip(mixed);
                transitionMixedPreviewClip = mixed;
                return transitionMixedPreviewClip;
            }
            catch (Exception ex)
            {
                error = $"Build transition mixed preview failed: {ex.Message}";
                return null;
            }
            finally
            {
                if (fromObj != null) DestroyImmediate(fromObj);
                if (toObj != null) DestroyImmediate(toObj);
                if (outObj != null) DestroyImmediate(outObj);
            }
        }

        private static float ComputeTransitionWeight(float normalizedTime, AnimatorStateTransition transition)
        {
            float t = Mathf.Clamp01(normalizedTime);
            float exitTime = Mathf.Clamp01(transition != null ? transition.exitTime : 0f);
            float duration = Mathf.Clamp01(transition != null ? transition.duration : 1f);
            float start = exitTime;
            float end = Mathf.Clamp01(exitTime + Mathf.Max(0.0001f, duration));
            if (end <= start)
            {
                return t >= end ? 1f : 0f;
            }

            return Mathf.InverseLerp(start, end, t);
        }

        private static void BlendTransforms(Transform from, Transform to, Transform output, float weight)
        {
            if (from == null || to == null || output == null)
            {
                return;
            }

            output.localPosition = Vector3.Lerp(from.localPosition, to.localPosition, weight);
            output.localRotation = Quaternion.Slerp(from.localRotation, to.localRotation, weight);
            output.localScale = Vector3.Lerp(from.localScale, to.localScale, weight);

            int childCount = Mathf.Min(from.childCount, Mathf.Min(to.childCount, output.childCount));
            for (int i = 0; i < childCount; i++)
            {
                BlendTransforms(from.GetChild(i), to.GetChild(i), output.GetChild(i), weight);
            }
        }

        private GameObject CreatePreviewInstance(string name)
        {
            if (previewSourceTemplate == null)
            {
                return null;
            }

            GameObject root = Instantiate(previewSourceTemplate);
            root.name = name;
            root.hideFlags = HideFlags.HideAndDontSave;
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                DestroyImmediate(root);
                return null;
            }

            animator.runtimeAnimatorController = null;
            animator.avatar = retargetAvatarForPreview;
            animator.enabled = false;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            return root;
        }

        private bool TryPreparePreviewSource(AnimationClip sourceClip, out Avatar avatar, out string error)
        {
            avatar = null;
            error = string.Empty;
            previewSourceTemplate = null;

            GameObject sourceRoot = FindScenePreviewSourceByController(selectedController);
            if (sourceRoot == null)
            {
                sourceRoot = LoadClipOwnerModelAsset(sourceClip);
            }

            if (sourceRoot == null)
            {
                string modelName = workingClip != null ? workingClip.bridgeModelName : string.Empty;
                if (KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out Avatar fallbackAvatar, out _)
                    && fallbackAvatar != null && fallbackAvatar.isValid && fallbackAvatar.isHuman)
                {
                    sourceRoot = BuildSkeletonTemplateFromAvatar(fallbackAvatar);
                }
            }

            if (sourceRoot == null)
            {
                sourceRoot = EditorGUIUtility.Load("Avatar/DefaultAvatar.fbx") as GameObject;
                if (sourceRoot == null)
                {
                    sourceRoot = EditorGUIUtility.Load("Avatar/DefaultGeneric.fbx") as GameObject;
                }
                if (sourceRoot == null)
                {
                    error = "Cannot resolve preview character source from scene/controller/clip/default avatar.";
                    return false;
                }
            }

            previewSourceTemplate = Instantiate(sourceRoot);
            previewSourceTemplate.name = "KimodoPreviewTemplate";
            previewSourceTemplate.hideFlags = HideFlags.HideAndDontSave;

            Animator animator = previewSourceTemplate.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = previewSourceTemplate.AddComponent<Animator>();
            }

            if (!KimodoLocalAvatarUtility.TryEnsureHumanoidAvatar(previewSourceTemplate, out avatar, out _, out error))
            {
                DestroyImmediate(previewSourceTemplate);
                previewSourceTemplate = null;
                return false;
            }

            animator.avatar = avatar;
            animator.enabled = false;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            return true;
        }

        private static GameObject FindScenePreviewSourceByController(AnimatorController controller)
        {
            if (controller == null)
            {
                return null;
            }

            Animator[] animators = FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator a = animators[i];
                if (a == null || a.runtimeAnimatorController == null)
                {
                    continue;
                }

                if (ReferenceEquals(a.runtimeAnimatorController, controller))
                {
                    return a.gameObject;
                }

                AnimatorOverrideController overrideController = a.runtimeAnimatorController as AnimatorOverrideController;
                if (overrideController != null && ReferenceEquals(overrideController.runtimeAnimatorController, controller))
                {
                    return a.gameObject;
                }
            }

            return null;
        }

        private static GameObject LoadClipOwnerModelAsset(AnimationClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            string clipPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(clipPath);
        }

        private static GameObject BuildSkeletonTemplateFromAvatar(Avatar avatar)
        {
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                return null;
            }

            GameObject root = new GameObject("KimodoPreviewSkeletonTemplate");
            root.hideFlags = HideFlags.HideAndDontSave;
            if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(avatar, root.transform, out _))
            {
                DestroyImmediate(root);
                return null;
            }

            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = root.AddComponent<Animator>();
            }
            animator.avatar = avatar;
            animator.enabled = false;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            return root;
        }

    }
}
