using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KimodoUnityMotionTools.Generation;
using KimodoUnityMotionTools.ProjectEditor.GenerationPipeline;
using KimodoUnityMotionTools.ProjectEditor.Manager;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    public static class KimodoAnimatorTransitionSplitInsertTool
    {
        internal const string TopMenuPath = "Tools/Kimodo/Animator/Split Transition And Insert Generated Motion...";
        internal const string ContextMenuPath = "CONTEXT/AnimatorStateTransition/Split Transition And Insert Generated Motion...";
        internal const string DefaultOutputFolder = "Assets/KimodoGeneratedClips";
        internal const string DefaultModelName = "Kimodo-SOMA-RP-v1";
        internal const float TargetFps = 30f;
        internal const float DefaultInsertedExitTime = 0.95f;

        [MenuItem(TopMenuPath)]
        private static void OpenFromTopMenu()
        {
            if (!TryGetSelectedTransition(out AnimatorStateTransition transition))
            {
                EditorUtility.DisplayDialog("Kimodo", "No Animator transition selected.", "OK");
                return;
            }

            OpenWindowForTransition(transition);
        }

        [MenuItem(TopMenuPath, true)]
        private static bool ValidateTopMenu()
        {
            return TryGetSelectedTransition(out AnimatorStateTransition transition)
                && TryResolveContext(transition, out _, out _);
        }

        [MenuItem(ContextMenuPath)]
        private static void OpenFromContextMenu(MenuCommand command)
        {
            if (!TryGetTransitionFromCommand(command, out AnimatorStateTransition transition))
            {
                EditorUtility.DisplayDialog("Kimodo", "No Animator transition context found.", "OK");
                return;
            }

            OpenWindowForTransition(transition);
        }

        [MenuItem(ContextMenuPath, true)]
        private static bool ValidateContextMenu(MenuCommand command)
        {
            return TryGetTransitionFromCommand(command, out AnimatorStateTransition transition)
                && TryResolveContext(transition, out _, out _);
        }

        private static void OpenWindowForTransition(AnimatorStateTransition transition)
        {
            KimodoAnimatorTransitionSplitInsertWindow.OpenForTransition(transition);
        }

        internal static bool TryGetSelectedTransition(out AnimatorStateTransition transition)
        {
            transition = Selection.activeObject as AnimatorStateTransition;
            return transition != null;
        }

        internal static bool TryGetTransitionFromCommand(MenuCommand command, out AnimatorStateTransition transition)
        {
            transition = command?.context as AnimatorStateTransition;
            if (transition != null)
            {
                return true;
            }

            return TryGetSelectedTransition(out transition);
        }

        internal static bool TryResolveContext(
            AnimatorStateTransition transition,
            out TransitionContext context,
            out string error)
        {
            context = default;
            error = string.Empty;

            if (transition == null)
            {
                error = "Transition is null.";
                return false;
            }

            AnimatorController controller = ResolveControllerFromObject(transition);
            if (controller == null)
            {
                error = "Cannot resolve AnimatorController for selected transition.";
                return false;
            }

            if (!TryFindTransitionOwner(controller, transition, out AnimatorStateMachine ownerMachine, out AnimatorState sourceState, out bool anyStateTransition))
            {
                error = "Cannot locate source state for selected transition.";
                return false;
            }

            if (anyStateTransition)
            {
                error = "AnyState transition is not supported for split-insert.";
                return false;
            }

            AnimatorState destinationState = transition.destinationState;
            if (destinationState == null)
            {
                error = "Transition destination state is null or points to Exit/StateMachine.";
                return false;
            }

            context = new TransitionContext(controller, ownerMachine, sourceState, destinationState, transition);
            return true;
        }

        internal static int CalculateGenerationFrames(TransitionContext context, out float durationSeconds, out string warning)
        {
            warning = string.Empty;
            durationSeconds = Mathf.Max(1f / TargetFps, context.Transition.duration);

            if (!context.Transition.hasFixedDuration)
            {
                float sourceClipLength = GetSourceStatePrimaryClipLength(context.SourceState, out bool usedFallback);
                durationSeconds = Mathf.Max(1f / TargetFps, context.Transition.duration * sourceClipLength);
                if (usedFallback)
                {
                    warning = "Source state has no readable primary clip length; fallback to 1 second for normalized transition duration.";
                }
            }

            return Mathf.Clamp(Mathf.RoundToInt(durationSeconds * TargetFps), KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES);
        }

        internal static async Task<string> GenerateMotionJsonAsync(
            KimodoTransitionInsertOptions options,
            int generationFrames,
            Action<string> progress,
            CancellationToken token)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (GenerateMotionJsonOverrideForTests != null)
            {
                return await GenerateMotionJsonOverrideForTests(options, generationFrames, progress, token);
            }

            int? effectiveSeed = options.UseRandomSeed
                ? (Guid.NewGuid().GetHashCode() & int.MaxValue)
                : options.Seed;

            KimodoGenerationResultDto result = await KimodoPoseGuidedGenerationUtility.GenerateFromPromptWithOptionalBoundaryPosesAsync(
                prompt: options.Prompt ?? string.Empty,
                frames: generationFrames,
                steps: options.Steps,
                seed: effectiveSeed,
                modelName: options.ModelName,
                vramMode: options.VramMode,
                startPose: options.StartPose,
                endPose: options.EndPose,
                progress: progress,
                token: token);

            if (result == null || string.IsNullOrWhiteSpace(result.motionJsonCompact))
            {
                throw new InvalidOperationException(result?.message ?? "Kimodo generation returned empty motion data.");
            }

            return result.motionJsonCompact;
        }

        internal static async Task<SplitInsertResult> GeneratePreviewAsync(
            AnimatorStateTransition transition,
            KimodoTransitionInsertOptions options,
            Action<string> progress,
            CancellationToken token)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!TryResolveContext(transition, out TransitionContext context, out string contextError))
            {
                throw new InvalidOperationException(contextError);
            }

            if (string.IsNullOrWhiteSpace(options.Prompt))
            {
                throw new InvalidOperationException("Prompt is empty.");
            }

            int generationFrames = CalculateGenerationFrames(context, out float durationSeconds, out string durationWarning);
            if (!string.IsNullOrWhiteSpace(durationWarning))
            {
                progress?.Invoke(durationWarning);
            }

            if (!TrySampleTransitionBoundaryPoses(context, options.ModelName, out options.StartPose, out options.EndPose, out string boundaryError))
            {
                throw new InvalidOperationException(boundaryError);
            }

            progress?.Invoke($"Generating motion ({generationFrames} frames, {durationSeconds:F3}s) with auto boundary poses...");
            string motionJson = await GenerateMotionJsonAsync(options, generationFrames, progress, token);

            token.ThrowIfCancellationRequested();
            progress?.Invoke("Creating preview clip...");
            var writeback = new KimodoEditorClipWritebackService();
            string clipPath = writeback.CreateGeneratedClipFromMotionJson(
                motionJson,
                options.ModelName,
                options.OutputFolderAssetPath,
                "Kimodo_Insert_");

            return new SplitInsertResult
            {
                GeneratedClipAssetPath = clipPath,
                GeneratedFrames = generationFrames,
                GeneratedDurationSeconds = durationSeconds
            };
        }

        internal static bool ApplyPreviewToTransition(AnimatorStateTransition transition, string generatedClipAssetPath, out string error)
        {
            error = string.Empty;
            if (!TryResolveContext(transition, out TransitionContext context, out string contextError))
            {
                error = contextError;
                return false;
            }

            if (string.IsNullOrWhiteSpace(generatedClipAssetPath))
            {
                error = "Generated clip path is empty.";
                return false;
            }

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedClipAssetPath);
            if (clip == null)
            {
                error = $"Generated clip not found at path: {generatedClipAssetPath}";
                return false;
            }

            RewireTransitionWithInsertedState(context, generatedClipAssetPath);
            return true;
        }

        private static bool TrySampleTransitionBoundaryPoses(
            TransitionContext context,
            string modelName,
            out KimodoMarkerSampleResult startPose,
            out KimodoMarkerSampleResult endPose,
            out string error)
        {
            startPose = null;
            endPose = null;
            error = string.Empty;

            if (context.SourceState == null)
            {
                error = "Source state is null.";
                return false;
            }

            if (!(context.SourceState.motion is AnimationClip sourceClip) || sourceClip == null)
            {
                error = "Source state motion is not an AnimationClip.";
                return false;
            }

            Animator animator = FindBoundAnimatorForController(context.Controller);
            if (animator == null)
            {
                error = "Cannot find a scene Animator bound to this AnimatorController.";
                return false;
            }

            double startTime = 0.0;
            double endTime = ResolveTransitionEndSampleTime(sourceClip, context.Transition);

            GameObject samplerRoot = null;
            try
            {
                samplerRoot = BuildSamplerHierarchyFromSourceClip(sourceClip, modelName);
                if (samplerRoot == null)
                {
                    error = "Failed to build source clip sampler hierarchy.";
                    return false;
                }

                if (!TrySampleBoundaryPoseFromClip(sourceClip, samplerRoot, modelName, startTime, "fullbody", out startPose, out error))
                {
                    return false;
                }

                if (!TrySampleBoundaryPoseFromClip(sourceClip, samplerRoot, modelName, endTime, "fullbody", out endPose, out error))
                {
                    return false;
                }
            }
            finally
            {
                if (samplerRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(samplerRoot);
                }
            }

            return true;
        }

        private static Animator FindBoundAnimatorForController(AnimatorController controller)
        {
            if (controller == null)
            {
                return null;
            }

            PlayableDirector inspectedDirector = TimelineEditor.inspectedDirector;
            if (inspectedDirector != null && inspectedDirector.playableAsset is TimelineAsset inspectedTimeline)
            {
                foreach (TrackAsset track in inspectedTimeline.GetOutputTracks())
                {
                    Animator animator = inspectedDirector.GetGenericBinding(track) as Animator;
                    if (animator == null || animator.runtimeAnimatorController == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(animator.runtimeAnimatorController, controller))
                    {
                        return animator;
                    }
                }
            }

            Animator[] animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator != null && ReferenceEquals(animator.runtimeAnimatorController, controller))
                {
                    return animator;
                }
            }

            return null;
        }

        private static double ResolveTransitionEndSampleTime(AnimationClip sourceClip, AnimatorStateTransition transition)
        {
            if (sourceClip == null || transition == null)
            {
                return 0.0;
            }

            float clipLength = Mathf.Max(1f / TargetFps, sourceClip.length);
            if (transition.hasFixedDuration)
            {
                return Mathf.Clamp(transition.duration, 0f, clipLength);
            }

            float normalized = Mathf.Clamp01(transition.duration);
            return clipLength * normalized;
        }

        private static GameObject BuildSamplerHierarchyFromSourceClip(AnimationClip sourceClip, string modelName)
        {
            if (sourceClip == null)
            {
                return null;
            }

            string[] modelJointNames = KimodoMarkerSamplingUtility.GetJointNamesForModel(modelName);
            int[] parentIndices = GetParentIndicesForModel(modelName);
            string rootJointName = KimodoMarkerSamplingUtility.GetRootJointNameForModel(modelName);

            GameObject root = new GameObject("__KimodoSplitSamplerRoot")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (modelJointNames == null || modelJointNames.Length == 0 || string.IsNullOrWhiteSpace(rootJointName))
            {
                return root;
            }

            var map = new System.Collections.Generic.Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < modelJointNames.Length; i++)
            {
                string jointName = modelJointNames[i];
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    continue;
                }

                var go = new GameObject(jointName)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                map[jointName] = go.transform;
            }

            for (int i = 0; i < modelJointNames.Length; i++)
            {
                string jointName = modelJointNames[i];
                if (!map.TryGetValue(jointName, out Transform child))
                {
                    continue;
                }

                int parentIndex = (parentIndices != null && i < parentIndices.Length) ? parentIndices[i] : -1;
                Transform parent = root.transform;
                if (parentIndex >= 0 && parentIndex < modelJointNames.Length && map.TryGetValue(modelJointNames[parentIndex], out Transform mappedParent))
                {
                    parent = mappedParent;
                }

                child.SetParent(parent, false);
            }

            if (!map.ContainsKey(rootJointName))
            {
                var fallbackRoot = new GameObject(rootJointName)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                fallbackRoot.transform.SetParent(root.transform, false);
            }

            return root;
        }

        private static bool TrySampleBoundaryPoseFromClip(
            AnimationClip sourceClip,
            GameObject samplerRoot,
            string modelName,
            double sampleTime,
            string markerType,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            sample = null;
            error = string.Empty;
            if (sourceClip == null || samplerRoot == null)
            {
                error = "Invalid clip sampler context.";
                return false;
            }

            double clampedTime = Mathf.Clamp((float)sampleTime, 0f, Mathf.Max(1f / TargetFps, sourceClip.length));
            sourceClip.SampleAnimation(samplerRoot, (float)clampedTime);

            Animator dummyAnimator = samplerRoot.GetComponent<Animator>();
            if (dummyAnimator == null)
            {
                dummyAnimator = samplerRoot.AddComponent<Animator>();
            }

            TimelineClip fakeSourceClip = null;
            if (!KimodoMarkerSamplingUtility.TrySampleMarker(
                    dummyAnimator,
                    samplerRoot.transform,
                    fakeSourceClip,
                    modelName,
                    clampedTime,
                    markerType,
                    out sample,
                    out error))
            {
                return false;
            }

            sample.sampleTime = clampedTime;
            sample.constraintType = markerType ?? "fullbody";
            return true;
        }

        private static int[] GetParentIndicesForModel(string modelName)
        {
            string normalized = string.IsNullOrWhiteSpace(modelName) ? string.Empty : modelName.Trim().ToLowerInvariant();
            if (normalized.Contains("g1"))
            {
                return new[]
                {
                    -1,
                    0, 1, 2, 3, 4, 5, 6,
                    0, 8, 9, 10, 11, 12, 13,
                    0, 15, 16,
                    17, 18, 19, 20, 21, 22, 23, 24,
                    17, 26, 27, 28, 29, 30, 31, 32
                };
            }

            if (normalized.Contains("smplx"))
            {
                return new[]
                {
                    -1,
                    0, 0, 0,
                    1, 2, 3,
                    4, 5, 6,
                    7, 8,
                    9, 9, 9,
                    12, 13, 14,
                    16, 17,
                    18, 19
                };
            }

            return new[]
            {
                -1, 0, 1, 2, 3, 4, 5, 6, 6, 6, 3, 10, 11, 12, 13, 13, 3, 16, 17, 18, 19, 19, 0, 22, 23, 24, 0, 26, 27, 28
            };
        }

        private static AnimatorController ResolveControllerFromObject(UnityEngine.Object obj)
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                AnimatorController controllerByPath = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
                if (controllerByPath != null)
                {
                    return controllerByPath;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimatorController");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null)
                {
                    continue;
                }

                if (ContainsSubAsset(controller, obj))
                {
                    return controller;
                }
            }

            return null;
        }

        private static bool ContainsSubAsset(AnimatorController controller, UnityEngine.Object obj)
        {
            string path = AssetDatabase.GetAssetPath(controller);
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (ReferenceEquals(all[i], obj))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindTransitionOwner(
            AnimatorController controller,
            AnimatorStateTransition targetTransition,
            out AnimatorStateMachine ownerMachine,
            out AnimatorState sourceState,
            out bool isAnyStateTransition)
        {
            ownerMachine = null;
            sourceState = null;
            isAnyStateTransition = false;

            AnimatorControllerLayer[] layers = controller.layers;
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                if (TryFindTransitionOwnerInStateMachine(
                    layers[layerIndex].stateMachine,
                    targetTransition,
                    out ownerMachine,
                    out sourceState,
                    out isAnyStateTransition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindTransitionOwnerInStateMachine(
            AnimatorStateMachine machine,
            AnimatorStateTransition targetTransition,
            out AnimatorStateMachine ownerMachine,
            out AnimatorState sourceState,
            out bool isAnyStateTransition)
        {
            ownerMachine = null;
            sourceState = null;
            isAnyStateTransition = false;

            ChildAnimatorState[] states = machine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                AnimatorStateTransition[] transitions = state.transitions;
                for (int t = 0; t < transitions.Length; t++)
                {
                    if (ReferenceEquals(transitions[t], targetTransition))
                    {
                        ownerMachine = machine;
                        sourceState = state;
                        return true;
                    }
                }
            }

            AnimatorStateTransition[] anyTransitions = machine.anyStateTransitions;
            for (int i = 0; i < anyTransitions.Length; i++)
            {
                if (ReferenceEquals(anyTransitions[i], targetTransition))
                {
                    ownerMachine = machine;
                    isAnyStateTransition = true;
                    return true;
                }
            }

            ChildAnimatorStateMachine[] children = machine.stateMachines;
            for (int i = 0; i < children.Length; i++)
            {
                if (TryFindTransitionOwnerInStateMachine(
                    children[i].stateMachine,
                    targetTransition,
                    out ownerMachine,
                    out sourceState,
                    out isAnyStateTransition))
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetSourceStatePrimaryClipLength(AnimatorState sourceState, out bool usedFallback)
        {
            usedFallback = false;
            if (sourceState == null || sourceState.motion == null)
            {
                usedFallback = true;
                return 1f;
            }

            if (sourceState.motion is AnimationClip clip && clip.length > 0f)
            {
                return clip.length;
            }

            // Motion.averageDuration is available for some motion types; reflection keeps compatibility.
            try
            {
                System.Reflection.PropertyInfo p = sourceState.motion.GetType().GetProperty("averageDuration");
                if (p != null && p.PropertyType == typeof(float))
                {
                    float value = (float)p.GetValue(sourceState.motion, null);
                    if (value > 0f)
                    {
                        return value;
                    }
                }
            }
            catch
            {
                // fallback below
            }

            usedFallback = true;
            return 1f;
        }

        private static void RewireTransitionWithInsertedState(TransitionContext context, string generatedClipPath)
        {
            AnimationClip generatedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(generatedClipPath);
            if (generatedClip == null)
            {
                throw new InvalidOperationException($"Generated clip not found at {generatedClipPath}");
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Split Transition And Insert Kimodo Motion");
            try
            {
                Undo.RegisterCompleteObjectUndo(context.Controller, "Split Transition And Insert Kimodo Motion");
                Undo.RegisterCompleteObjectUndo(context.OwnerStateMachine, "Split Transition And Insert Kimodo Motion");
                Undo.RegisterCompleteObjectUndo(context.SourceState, "Split Transition And Insert Kimodo Motion");
                Undo.RegisterCompleteObjectUndo(context.DestinationState, "Split Transition And Insert Kimodo Motion");

                Vector3 insertPosition = ComputeInsertStatePosition(context.OwnerStateMachine, context.SourceState, context.DestinationState);
                string stateName = "Kimodo_Insert_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                AnimatorState insertedState = context.OwnerStateMachine.AddState(stateName, insertPosition);
                insertedState.motion = generatedClip;
                insertedState.writeDefaultValues = context.SourceState.writeDefaultValues;

                AnimatorStateTransition oldTransition = context.Transition;
                context.SourceState.RemoveTransition(oldTransition);

                AnimatorStateTransition toInserted = context.SourceState.AddTransition(insertedState);
                AnimatorStateTransition toDestination = insertedState.AddTransition(context.DestinationState);

                CopyTransitionCommonSettings(oldTransition, toInserted);
                CopyTransitionCommonSettings(oldTransition, toDestination);

                CopyTransitionConditions(context.Controller, oldTransition, toInserted, includeTriggers: true);
                CopyTransitionConditions(context.Controller, oldTransition, toDestination, includeTriggers: false);

                toDestination.hasExitTime = true;
                toDestination.exitTime = DefaultInsertedExitTime;

                EditorUtility.SetDirty(context.Controller);
                EditorUtility.SetDirty(context.OwnerStateMachine);
                EditorUtility.SetDirty(context.SourceState);
                EditorUtility.SetDirty(insertedState);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static Vector3 ComputeInsertStatePosition(
            AnimatorStateMachine ownerStateMachine,
            AnimatorState sourceState,
            AnimatorState destinationState)
        {
            Vector3 sourcePosition = Vector3.zero;
            Vector3 destinationPosition = sourcePosition + new Vector3(240f, 0f, 0f);
            bool hasSource = false;
            bool hasDestination = false;

            ChildAnimatorState[] states = ownerStateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (ReferenceEquals(states[i].state, sourceState))
                {
                    sourcePosition = states[i].position;
                    hasSource = true;
                }
                else if (ReferenceEquals(states[i].state, destinationState))
                {
                    destinationPosition = states[i].position;
                    hasDestination = true;
                }
            }

            if (hasSource && hasDestination)
            {
                return (sourcePosition + destinationPosition) * 0.5f + new Vector3(0f, 70f, 0f);
            }

            if (hasSource)
            {
                return sourcePosition + new Vector3(220f, 70f, 0f);
            }

            if (hasDestination)
            {
                return destinationPosition + new Vector3(-220f, 70f, 0f);
            }

            return Vector3.zero;
        }

        private static void CopyTransitionCommonSettings(AnimatorStateTransition src, AnimatorStateTransition dst)
        {
            dst.hasExitTime = src.hasExitTime;
            dst.exitTime = src.exitTime;
            dst.hasFixedDuration = src.hasFixedDuration;
            dst.duration = src.duration;
            dst.offset = src.offset;
            dst.interruptionSource = src.interruptionSource;
            dst.orderedInterruption = src.orderedInterruption;
            dst.canTransitionToSelf = src.canTransitionToSelf;
            dst.mute = src.mute;
            dst.solo = src.solo;
        }

        private static void CopyTransitionConditions(
            AnimatorController controller,
            AnimatorStateTransition src,
            AnimatorStateTransition dst,
            bool includeTriggers)
        {
            AnimatorCondition[] conditions = src.conditions;
            for (int i = 0; i < conditions.Length; i++)
            {
                AnimatorCondition c = conditions[i];
                if (!includeTriggers && IsTriggerParameter(controller, c.parameter))
                {
                    continue;
                }

                dst.AddCondition(c.mode, c.threshold, c.parameter);
            }
        }

        private static bool IsTriggerParameter(AnimatorController controller, string parameterName)
        {
            if (controller == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter p = parameters[i];
                if (string.Equals(p.name, parameterName, StringComparison.Ordinal))
                {
                    return p.type == AnimatorControllerParameterType.Trigger;
                }
            }

            return false;
        }

        internal static Func<KimodoTransitionInsertOptions, int, Action<string>, CancellationToken, Task<string>> GenerateMotionJsonOverrideForTests;
    }

    internal readonly struct TransitionContext
    {
        internal readonly AnimatorController Controller;
        internal readonly AnimatorStateMachine OwnerStateMachine;
        internal readonly AnimatorState SourceState;
        internal readonly AnimatorState DestinationState;
        internal readonly AnimatorStateTransition Transition;

        internal TransitionContext(
            AnimatorController controller,
            AnimatorStateMachine ownerStateMachine,
            AnimatorState sourceState,
            AnimatorState destinationState,
            AnimatorStateTransition transition)
        {
            Controller = controller;
            OwnerStateMachine = ownerStateMachine;
            SourceState = sourceState;
            DestinationState = destinationState;
            Transition = transition;
        }
    }

    internal sealed class KimodoTransitionInsertOptions
    {
        internal string Prompt = "a person walks naturally";
        internal int Steps = 100;
        internal bool UseRandomSeed = true;
        internal int Seed = 42;
        internal string ModelName = KimodoAnimatorTransitionSplitInsertTool.DefaultModelName;
        internal KimodoBridgeVramMode VramMode = KimodoBridgeVramMode.Low;
        internal string OutputFolderAssetPath = KimodoAnimatorTransitionSplitInsertTool.DefaultOutputFolder;
        internal KimodoMarkerSampleResult StartPose;
        internal KimodoMarkerSampleResult EndPose;
    }

    internal sealed class SplitInsertResult
    {
        internal string GeneratedClipAssetPath;
        internal int GeneratedFrames;
        internal float GeneratedDurationSeconds;
    }

    internal sealed class KimodoAnimatorTransitionSplitInsertWindow : EditorWindow
    {
        private AnimatorStateTransition transition;
        private readonly KimodoTransitionInsertOptions options = new KimodoTransitionInsertOptions();
        private string status = string.Empty;
        private string error = string.Empty;
        private string durationHint = string.Empty;
        private string previewClipPath = string.Empty;
        private AnimationClip previewClipAsset;
        private bool generating;
        private CancellationTokenSource cts;
        private Guid activeRequestId = Guid.Empty;
        private bool subscribedManager;

        internal static void OpenForTransition(AnimatorStateTransition transition)
        {
            var window = GetWindow<KimodoAnimatorTransitionSplitInsertWindow>(true, "Kimodo Split Transition");
            window.minSize = new Vector2(520f, 430f);
            window.Initialize(transition);
            window.Show();
        }

        private void Initialize(AnimatorStateTransition targetTransition)
        {
            transition = targetTransition;
            options.ModelName = string.IsNullOrWhiteSpace(EditorPrefs.GetString("Kimodo.AnimatorSplit.ModelName", string.Empty))
                ? KimodoAnimatorTransitionSplitInsertTool.DefaultModelName
                : EditorPrefs.GetString("Kimodo.AnimatorSplit.ModelName");
            options.Steps = Mathf.Clamp(EditorPrefs.GetInt("Kimodo.AnimatorSplit.Steps", 100), 1, 1000);
            options.UseRandomSeed = EditorPrefs.GetBool("Kimodo.AnimatorSplit.RandomSeed", true);
            options.Seed = EditorPrefs.GetInt("Kimodo.AnimatorSplit.Seed", 42);
            options.Prompt = EditorPrefs.GetString("Kimodo.AnimatorSplit.Prompt", options.Prompt);
            options.OutputFolderAssetPath = EditorPrefs.GetString("Kimodo.AnimatorSplit.OutputFolder", KimodoAnimatorTransitionSplitInsertTool.DefaultOutputFolder);
            options.VramMode = (KimodoBridgeVramMode)Mathf.Clamp(EditorPrefs.GetInt("Kimodo.AnimatorSplit.Vram", (int)KimodoBridgeVramMode.Low), 0, 1);
            status = string.Empty;
            error = string.Empty;
            previewClipPath = string.Empty;
            previewClipAsset = null;
            UpdateDurationHint();
        }

        private void OnDisable()
        {
            UnsubscribeManagerEvents();
            CancelInternal();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Split Transition And Insert Generated Motion", EditorStyles.boldLabel);
            EditorGUILayout.Space(6f);

            if (!KimodoAnimatorTransitionSplitInsertTool.TryResolveContext(transition, out TransitionContext context, out string contextError))
            {
                EditorGUILayout.HelpBox(contextError, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Source", context.SourceState.name);
            EditorGUILayout.LabelField("Destination", context.DestinationState.name);
            EditorGUILayout.Space(6f);

            options.Prompt = EditorGUILayout.TextArea(options.Prompt ?? string.Empty, GUILayout.Height(72f));
            options.Steps = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Diffusion Steps", "Sampling steps used by the generation model. Higher values are slower but can improve quality."), options.Steps), 1, 1000);
            options.UseRandomSeed = EditorGUILayout.ToggleLeft(new GUIContent("Random Seed", "When enabled, a new random seed is used each run. Disable to make results reproducible with the fixed seed."), options.UseRandomSeed);
            using (new EditorGUI.DisabledScope(options.UseRandomSeed))
            {
                options.Seed = EditorGUILayout.IntField(new GUIContent("Seed", "Fixed random seed used when Random Seed is disabled."), options.Seed);
            }

            options.ModelName = EditorGUILayout.TextField(new GUIContent("Model", "Kimodo model folder/name used for generation, for example Kimodo-SOMA-RP-v1."), options.ModelName);
            options.VramMode = (KimodoBridgeVramMode)EditorGUILayout.EnumPopup(new GUIContent("VRAM Mode", "Low uses less VRAM with quantized encoder. High uses more VRAM with full model stack."), options.VramMode);

            EditorGUILayout.BeginHorizontal();
            options.OutputFolderAssetPath = EditorGUILayout.TextField(new GUIContent("Output Folder", "Project-relative output path under Assets/ for generated .anim clips."), options.OutputFolderAssetPath);
            if (GUILayout.Button(new GUIContent("Browse...", "Pick an output folder inside this project's Assets directory."), GUILayout.Width(84f)))
            {
                string absBase = Path.GetFullPath(Directory.GetCurrentDirectory());
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder (under Assets)", absBase, string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    string normalized = selected.Replace('\\', '/');
                    string projectAssets = Path.GetFullPath(Path.Combine(absBase, "Assets")).Replace('\\', '/');
                    if (normalized.StartsWith(projectAssets, StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = "Assets" + normalized.Substring(projectAssets.Length);
                        options.OutputFolderAssetPath = rel;
                    }
                    else
                    {
                        error = "Selected folder must be inside this project's Assets directory.";
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Boundary poses are auto-sampled from selected transition start/end. Manual boundary input is disabled.", MessageType.Info);

            UpdateDurationHint();
            if (!string.IsNullOrWhiteSpace(durationHint))
            {
                EditorGUILayout.HelpBox(durationHint, MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(generating))
            {
                if (GUILayout.Button(new GUIContent("Generate Preview", "Generate transition motion clip preview only. Does not modify animator state machine."), GUILayout.Height(30f)))
                {
                    _ = RunAsync();
                }
            }

            using (new EditorGUI.DisabledScope(generating || previewClipAsset == null || string.IsNullOrWhiteSpace(previewClipPath)))
            {
                if (GUILayout.Button(new GUIContent("Apply", "Apply current preview clip to animator by splitting transition and inserting generated state."), GUILayout.Height(26f)))
                {
                    ApplyPreview();
                }
            }

            using (new EditorGUI.DisabledScope(!generating))
            {
                if (GUILayout.Button(new GUIContent("Cancel", "Cancel the running split-insert generation request."), GUILayout.Height(24f)))
                {
                    CancelInternal();
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(new GUIContent("Preview Clip"), previewClipAsset, typeof(AnimationClip), false);
            }
            if (!string.IsNullOrWhiteSpace(previewClipPath))
            {
                EditorGUILayout.SelectableLabel(previewClipPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }

        private void UpdateDurationHint()
        {
            if (!KimodoAnimatorTransitionSplitInsertTool.TryResolveContext(transition, out TransitionContext context, out _))
            {
                durationHint = string.Empty;
                return;
            }

            int frames = KimodoAnimatorTransitionSplitInsertTool.CalculateGenerationFrames(context, out float seconds, out string warning);
            durationHint = $"Auto duration from transition: {seconds:F3}s ({frames} frames @ 30fps).";
            if (!string.IsNullOrWhiteSpace(warning))
            {
                durationHint += $" {warning}";
            }
        }

        private async Task RunAsync()
        {
            if (generating)
            {
                return;
            }

            error = string.Empty;
            status = "Starting...";
            SavePrefs();
            var command = new AnimatorSplitInsertCommand(
                transition,
                options.Prompt,
                options.Steps,
                options.UseRandomSeed,
                options.Seed,
                options.ModelName,
                options.VramMode,
                options.OutputFolderAssetPath);

            bool accepted = KimodoEditorCommandManager.Dispatch(command);
            if (!accepted)
            {
                status = "Rejected: target is busy.";
                return;
            }

            activeRequestId = command.RequestId;
            generating = true;
            cts = new CancellationTokenSource();
            Repaint();
            await Task.CompletedTask;
        }

        private void CancelInternal()
        {
            CancellationTokenSource local = cts;
            cts = null;
            if (local == null)
            {
                return;
            }

            try
            {
                if (!local.IsCancellationRequested)
                {
                    local.Cancel();
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                local.Dispose();
            }

            if (activeRequestId != Guid.Empty)
            {
                KimodoEditorCommandManager.Cancel(activeRequestId);
            }
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString("Kimodo.AnimatorSplit.Prompt", options.Prompt ?? string.Empty);
            EditorPrefs.SetInt("Kimodo.AnimatorSplit.Steps", Mathf.Clamp(options.Steps, 1, 1000));
            EditorPrefs.SetBool("Kimodo.AnimatorSplit.RandomSeed", options.UseRandomSeed);
            EditorPrefs.SetInt("Kimodo.AnimatorSplit.Seed", options.Seed);
            EditorPrefs.SetString("Kimodo.AnimatorSplit.ModelName", options.ModelName ?? KimodoAnimatorTransitionSplitInsertTool.DefaultModelName);
            EditorPrefs.SetInt("Kimodo.AnimatorSplit.Vram", (int)options.VramMode);
            EditorPrefs.SetString("Kimodo.AnimatorSplit.OutputFolder", options.OutputFolderAssetPath ?? KimodoAnimatorTransitionSplitInsertTool.DefaultOutputFolder);
        }

        private void OnEnable()
        {
            SubscribeManagerEvents();
        }

        private void SubscribeManagerEvents()
        {
            if (subscribedManager)
            {
                return;
            }

            KimodoEditorCommandManager.CommandProgress += OnCommandProgress;
            KimodoEditorCommandManager.CommandCompleted += OnCommandCompleted;
            KimodoEditorCommandManager.CommandFailed += OnCommandFailed;
            KimodoEditorCommandManager.CommandCanceled += OnCommandCanceled;
            subscribedManager = true;
        }

        private void UnsubscribeManagerEvents()
        {
            if (!subscribedManager)
            {
                return;
            }

            KimodoEditorCommandManager.CommandProgress -= OnCommandProgress;
            KimodoEditorCommandManager.CommandCompleted -= OnCommandCompleted;
            KimodoEditorCommandManager.CommandFailed -= OnCommandFailed;
            KimodoEditorCommandManager.CommandCanceled -= OnCommandCanceled;
            subscribedManager = false;
        }

        private void OnCommandProgress(KimodoEditorCommandProgressEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            status = evt.Message ?? string.Empty;
            EditorApplication.delayCall += Repaint;
        }

        private void OnCommandCompleted(KimodoEditorCommandCompletedEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            if (evt.Payload is KimodoEditorAnimatorSplitInsertResult result)
            {
                previewClipPath = result.GeneratedClipAssetPath ?? string.Empty;
                previewClipAsset = string.IsNullOrWhiteSpace(previewClipPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<AnimationClip>(previewClipPath);
                status = previewClipAsset != null
                    ? $"Preview ready: {previewClipAsset.name}"
                    : "Preview generation completed.";
            }
            else
            {
                status = "Done.";
            }

            error = string.Empty;
            generating = false;
            activeRequestId = Guid.Empty;
            CancelInternal();
            Repaint();
        }

        private void OnCommandFailed(KimodoEditorCommandFailedEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            error = evt.Message;
            status = "Failed.";
            generating = false;
            activeRequestId = Guid.Empty;
            CancelInternal();
            Repaint();
        }

        private void OnCommandCanceled(KimodoEditorCommandCanceledEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            status = "Canceled.";
            generating = false;
            activeRequestId = Guid.Empty;
            CancelInternal();
            Repaint();
        }

        private bool IsActiveCommand(IKimodoEditorCommand command)
        {
            if (command == null || command.Kind != KimodoEditorCommandKind.AnimatorSplitInsert)
            {
                return false;
            }

            if (activeRequestId != Guid.Empty)
            {
                return command.RequestId == activeRequestId;
            }

            return transition != null && string.Equals(command.TargetKey, "animator:" + transition.GetInstanceID(), StringComparison.Ordinal);
        }

        private void ApplyPreview()
        {
            if (transition == null)
            {
                error = "Transition is null.";
                return;
            }

            if (previewClipAsset == null || string.IsNullOrWhiteSpace(previewClipPath))
            {
                error = "No preview clip available to apply.";
                return;
            }

            if (!KimodoAnimatorTransitionSplitInsertTool.ApplyPreviewToTransition(transition, previewClipPath, out string applyError))
            {
                error = applyError;
                return;
            }

            error = string.Empty;
            status = $"Applied preview: {previewClipAsset.name}";
        }
    }
}
