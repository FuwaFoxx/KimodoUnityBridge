using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using KimodoUnityMotionTools;
using KimodoUnityMotionTools.ProjectEditor;
using KimodoUnityMotionTools.Bridge;
using KimodoUnityMotionTools.Generation;

namespace KimodoUnityMotionTools.ProjectEditor
{
    [CustomEditor(typeof(KimodoPlayableClip))]
    public class KimodoPlayableClipEditor : UnityEditor.Editor
    {
        private const float TargetFps = 30f;
        private const string GeneratedClipFolder = "Assets/KimodoGeneratedClips";
        private const string GeneratedClipNamePrefix = "Kimodo_";

        private SerializedProperty generationBackend;
        private SerializedProperty comfyuiIP;
        private SerializedProperty comfyuiPort;
        private SerializedProperty bridgeModelName;
        private SerializedProperty bridgeVramMode;
        private SerializedProperty motionPrompt;
        private SerializedProperty generationFrames;
        private SerializedProperty diffusionSteps;
        private SerializedProperty randomProp;
        private SerializedProperty seed;
        private SerializedProperty enableInbetweenInterpolation;
        private SerializedProperty workflowJsonAsset;

        private SerializedProperty animationClipProp;
        private SerializedProperty footIKProp;
        private SerializedProperty loopProp;
        private SerializedProperty autoRetargetOnBindingProp;
        private SerializedProperty customRetargetAvatarProp;
        private SerializedProperty curveFilterOptionsProp;

        private KimodoPlayableClip clip;
        private bool isGenerating;
        private string lastStatus;
        private string lastError;
        private CancellationTokenSource generationCts;
        private int lastSubmittedSeed = int.MinValue;
        private string lastConstraintsPath = string.Empty;
        private readonly List<KimodoConstraintMarkerBase> lastConstraintMarkers = new List<KimodoConstraintMarkerBase>();
        private bool lastIncludesAutoInbetweenConstraint;
        private bool bridgeRunningCached;
        private bool bridgePortDiscoveredCached;
        private bool bridgeStatusReady;
        private bool showAdvancedFoldout = true;

        private void OnEnable()
        {
            InitializeSerializedBindings();
            showAdvancedFoldout = KimodoPlayableClipGenerationSettings.instance.AdvancedCurveFilterFoldout;
            PullBridgeStatusSnapshot(forceRefresh: true);
        }

        private void InitializeSerializedBindings()
        {
            clip = (KimodoPlayableClip)target;
            generationBackend = serializedObject.FindProperty("generationBackend");
            comfyuiIP = serializedObject.FindProperty("comfyuiIP");
            comfyuiPort = serializedObject.FindProperty("comfyuiPort");
            bridgeModelName = serializedObject.FindProperty("bridgeModelName");
            bridgeVramMode = serializedObject.FindProperty("bridgeVramMode");
            motionPrompt = serializedObject.FindProperty("motionPrompt");
            generationFrames = serializedObject.FindProperty("generationFrames");
            diffusionSteps = serializedObject.FindProperty("diffusionSteps");
            randomProp = serializedObject.FindProperty("randomSeed");
            seed = serializedObject.FindProperty("seed");
            enableInbetweenInterpolation = serializedObject.FindProperty("enableInbetweenInterpolation");
            workflowJsonAsset = serializedObject.FindProperty("workflowJsonAsset");

            animationClipProp = serializedObject.FindProperty("m_Clip");
            footIKProp = serializedObject.FindProperty("m_ApplyFootIK");
            loopProp = serializedObject.FindProperty("m_Loop");
            autoRetargetOnBindingProp = serializedObject.FindProperty("autoRetargetOnBinding");
            customRetargetAvatarProp = serializedObject.FindProperty("customRetargetAvatar");
            curveFilterOptionsProp = serializedObject.FindProperty("curveFilterOptions");
        }

        internal Task GenerateForTestsAsync()
        {
            InitializeSerializedBindings();
            return GenerateAsync();
        }

        internal void CancelGenerationForTests()
        {
            CancelGenerationInternal();
        }

        internal bool IsGeneratingForTests => isGenerating;

        internal string LastStatusForTests => lastStatus;

        internal string LastErrorForTests => lastError;

        internal void SetBridgeGenerationInputsForTests(
            string prompt,
            int generationFramesValue,
            int diffusionStepsValue,
            bool randomSeedEnabled,
            int seedValue)
        {
            InitializeSerializedBindings();
            serializedObject.UpdateIfRequiredOrScript();
            generationBackend.intValue = (int)KimodoGenerationBackend.KimodoBridge;
            motionPrompt.stringValue = prompt ?? string.Empty;
            generationFrames.intValue = Mathf.Clamp(generationFramesValue, KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES);
            diffusionSteps.intValue = Mathf.Clamp(diffusionStepsValue, 1, 1000);
            randomProp.boolValue = randomSeedEnabled;
            seed.intValue = seedValue;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void OnDisable()
        {
            CancelGenerationInternal();
            EditorUtility.ClearProgressBar();
        }

        public override void OnInspectorGUI()
        {
            if (clip == null)
            {
                EditorGUILayout.HelpBox("Target clip is null.", MessageType.Error);
                return;
            }

            PullBridgeStatusSnapshot(forceRefresh: false);
            serializedObject.UpdateIfRequiredOrScript();
            DrawGenerationSection();
            DrawBakeSection();
            DrawErrorSection();
            DrawGeneratedInfo();
            DrawAnimationClipSection();
            if (serializedObject.hasModifiedProperties)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawGenerationSection()
        {
            EditorGUILayout.LabelField("Generate Motion", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            if (generationBackend != null)
            {
                EditorGUILayout.PropertyField(generationBackend, new GUIContent("Backend"));
            }

            bool useBridge = clip.generationBackend == KimodoGenerationBackend.KimodoBridge;
            if (useBridge)
            {
                if (bridgeModelName != null)
                {
                    DrawBridgeModelSelector();
                }
                if (bridgeVramMode != null)
                {
                    EditorGUILayout.PropertyField(
                        bridgeVramMode,
                        new GUIContent("VRAM Mode", "Low: quantized text encoder (~4G). High: full Llama+LLM2Vec (~16G)."));
                }

                int encoderVramGb = clip.bridgeVramMode == KimodoBridgeVramMode.High ? 16 : 4;
                int totalVramGb = 2 + encoderVramGb;
                EditorGUILayout.HelpBox(
                    $"Estimated VRAM for selected mode: ~{totalVramGb} GB (core 2 GB + encoder {encoderVramGb} GB).",
                    MessageType.Info);
            }
            else
            {
                comfyuiIP.stringValue = EditorGUILayout.TextField("ComfyUI IP", comfyuiIP.stringValue);
                comfyuiPort.intValue = EditorGUILayout.IntField("ComfyUI Port", comfyuiPort.intValue);
                EditorGUILayout.HelpBox("Workflow source is fixed to Runtime/Resources/kimodo-unity-workflow.json.", MessageType.Info);
            }

            motionPrompt.stringValue = EditorGUILayout.TextArea(motionPrompt.stringValue, GUILayout.Height(60));

            int oldFrames = generationFrames.intValue;
            int newFrames = EditorGUILayout.IntSlider("Duration (frames)", oldFrames, KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES);
            if (newFrames != oldFrames)
            {
                generationFrames.intValue = newFrames;
                TrySyncTimelineDuration(newFrames);
            }

            diffusionSteps.intValue = Mathf.Clamp(EditorGUILayout.IntField("Diffusion Steps", diffusionSteps.intValue), 1, 1000);

            EditorGUILayout.BeginHorizontal();
            randomProp.boolValue = EditorGUILayout.ToggleLeft("Random", randomProp.boolValue, GUILayout.Width(110f));
            EditorGUI.BeginDisabledGroup(randomProp.boolValue);
            seed.intValue = EditorGUILayout.IntField("Seed", seed.intValue);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (enableInbetweenInterpolation != null)
            {
                EditorGUILayout.PropertyField(
                    enableInbetweenInterpolation,
                    new GUIContent("In-between Interpolation", "Use neighboring clip boundary poses as constraints to generate in-between motion."));
            }

            float seconds = generationFrames.intValue / TargetFps;
            EditorGUILayout.LabelField($"Duration: {seconds:F2}s", EditorStyles.miniLabel);
            DrawConstraintReferenceList();

            bool disableGenerate = isGenerating || KimodoBridgeController.IsRuntimeMaintenanceInProgress;
            GUI.enabled = !disableGenerate;
            if (GUILayout.Button("Generate & Bake", GUILayout.Height(32)))
            {
                _ = GenerateAsync();
            }
            GUI.enabled = isGenerating;
            if (GUILayout.Button("Cancel", GUILayout.Height(24)))
            {
                CancelGenerationInternal();
                lastStatus = "Generation canceled.";
            }
            GUI.enabled = true;

            if (useBridge)
            {
                DrawEstimatedSetupTimeHint();
            }

            if (useBridge)
            {
                if (!bridgeStatusReady)
                {
                    EditorGUILayout.LabelField("Bridge status: checking...", EditorStyles.miniLabel);
                }

                bool closeAllowed = bridgeRunningCached || bridgePortDiscoveredCached || isGenerating;
                EditorGUI.BeginDisabledGroup(!closeAllowed);
                if (GUILayout.Button("Close Bridge Server", GUILayout.Height(22)))
                {
                    _ = CloseBridgeServerAndRefreshStatusAsync();
                }
                EditorGUI.EndDisabledGroup();
            }

            if (!string.IsNullOrWhiteSpace(lastStatus))
            {
                EditorGUILayout.LabelField(lastStatus, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawConstraintReferenceList()
        {
            EditorGUILayout.LabelField("Constraint References", EditorStyles.miniBoldLabel);
            if (lastConstraintMarkers.Count == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < lastConstraintMarkers.Count; i++)
                {
                    KimodoConstraintMarkerBase marker = lastConstraintMarkers[i];
                    if (marker == null)
                    {
                        continue;
                    }

                    EditorGUILayout.ObjectField(
                        new GUIContent($"{marker.ConstraintType} @ {marker.time:F3}s"),
                        marker,
                        typeof(KimodoConstraintMarkerBase),
                        true);
                }
            }

            if (lastIncludesAutoInbetweenConstraint)
            {
                EditorGUILayout.LabelField("- Auto in-between fullbody constraint (generated)", EditorStyles.miniLabel);
            }
        }

        private void PullBridgeStatusSnapshot(bool forceRefresh)
        {
            if (clip == null || clip.generationBackend != KimodoGenerationBackend.KimodoBridge)
            {
                return;
            }

            KimodoBridgeController.RequestServerStateRefresh(forceRefresh);
            KimodoBridgeController.ServerStatusSnapshot snapshot = KimodoBridgeController.GetServerStatusSnapshot();
            bridgeStatusReady = snapshot.Ready;
            bridgeRunningCached = snapshot.Running;
            bridgePortDiscoveredCached = snapshot.HasPort;
        }

        private void DrawAnimationClipSection()
        {
            EditorGUILayout.LabelField("Animation Clip", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            if (animationClipProp != null)
            {
                EditorGUILayout.PropertyField(animationClipProp, new GUIContent("Clip"));
            }
            else
            {
                EditorGUILayout.HelpBox("Clip property not found.", MessageType.Warning);
            }

            if (footIKProp != null)
            {
                EditorGUILayout.PropertyField(footIKProp, new GUIContent("Foot IK"));
            }

            if (loopProp != null)
            {
                EditorGUILayout.PropertyField(loopProp, new GUIContent("Loop"));
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawBakeSection()
        {
            EditorGUILayout.LabelField("Animation Bake", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            if (autoRetargetOnBindingProp != null)
            {
                EditorGUILayout.PropertyField(autoRetargetOnBindingProp, new GUIContent("Auto Retarget On Binding"));
            }
            if (autoRetargetOnBindingProp != null && !autoRetargetOnBindingProp.boolValue && customRetargetAvatarProp != null)
            {
                EditorGUILayout.PropertyField(customRetargetAvatarProp, new GUIContent("Custom Avatar"));
                Avatar customAvatar = clip != null ? clip.CustomRetargetAvatar : null;
                if (customAvatar == null)
                {
                    EditorGUILayout.HelpBox("Custom Avatar is required when Auto Retarget On Binding is disabled.", MessageType.Warning);
                }
                else if (!customAvatar.isValid || !customAvatar.isHuman)
                {
                    EditorGUILayout.HelpBox("Custom Avatar must be a valid Humanoid Avatar.", MessageType.Error);
                }
            }
            DrawAdvancedCurveFilterSection();

            EditorGUILayout.EndVertical();
        }

        private async Task GenerateAsync()
        {
            if (isGenerating)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(motionPrompt.stringValue))
            {
                lastError = "Prompt is empty.";
                Repaint();
                return;
            }

            isGenerating = true;
            lastError = string.Empty;
            lastStatus = $"Generating: {motionPrompt.stringValue}";
            generationCts = new CancellationTokenSource();
            Repaint();
            try
            {
                string constraintsJson = BuildConstraintsJsonOrThrow();
                int effectiveSeed = ResolveEffectiveSeed();
                lastConstraintsPath = string.IsNullOrWhiteSpace(constraintsJson) ? "(none)" : "(inline-json)";
                string motionJson = await GenerateMotionJsonViaRuntimeServiceAsync(constraintsJson, effectiveSeed, generationCts.Token);
                if (string.IsNullOrWhiteSpace(motionJson))
                {
                    throw new Exception("No motion json found in workflow outputs.");
                }

                CreateAndAssignNewAnimationClip();
                ApplyMotionJsonToClip(motionJson);
                BakeCurrentMotionData();
                if (string.IsNullOrEmpty(lastError))
                {
                    TrimGeneratedClipsToLimit();
                }
                lastStatus = "Generation complete.";
            }
            catch (OperationCanceledException)
            {
                lastStatus = "Generation canceled.";
            }
            catch (Exception e)
            {
                string backendName = clip != null ? clip.generationBackend.ToString() : "Unknown";
                string modelName = clip != null ? clip.bridgeModelName : string.Empty;
                string prompt = motionPrompt != null ? motionPrompt.stringValue : string.Empty;
                int frames = generationFrames != null ? generationFrames.intValue : -1;
                int steps = diffusionSteps != null ? diffusionSteps.intValue : -1;
                int currentSeed = seed != null ? seed.intValue : 0;
                string context = $"backend={backendName}, model={modelName}, frames={frames}, steps={steps}, seed={currentSeed}, constraints={lastConstraintsPath}, status={lastStatus}, prompt={prompt}";
                lastError = $"{e.Message}\n{context}";
                lastStatus = "Generation failed.";
                Debug.LogError($"[Kimodo] Generate failed.\nContext: {context}\nException: {e}");
            }
            finally
            {
                isGenerating = false;
                CancelGenerationInternal();
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private int ResolveEffectiveSeed()
        {
            int effectiveSeed = randomProp.boolValue
                ? Guid.NewGuid().GetHashCode() & int.MaxValue
                : seed.intValue;

            if (randomProp.boolValue)
            {
                seed.intValue = effectiveSeed;
            }

            if (effectiveSeed == lastSubmittedSeed)
            {
                unchecked { effectiveSeed = effectiveSeed + 1; }
                seed.intValue = effectiveSeed;
                lastStatus = $"Seed auto-incremented to {effectiveSeed} to avoid cache hit.";
            }

            lastSubmittedSeed = effectiveSeed;
            return effectiveSeed;
        }

        private async Task<string> GenerateMotionJsonViaRuntimeServiceAsync(string constraintsJson, int effectiveSeed, CancellationToken token)
        {
            string expectedRuntimeRoot = KimodoBridgeController.GetRuntimeRootPath();
            if (!Directory.Exists(expectedRuntimeRoot))
            {
                lastStatus = "First setup: preparing NvlabKimodoQuickServer...";
                Repaint();
                Debug.Log("[Kimodo] First setup: runtime root missing, bootstrapping from package template.");
            }

            string kimodoRootPath = KimodoBridgeController.ResolveRuntimeRootOrThrow();
            string launcherPath = KimodoBridgeController.ResolveStartScriptOrThrow(kimodoRootPath);
            string modelName = string.IsNullOrWhiteSpace(clip.bridgeModelName) ? "Kimodo-SOMA-RP-v1" : clip.bridgeModelName.Trim();
            bool highVram = clip.bridgeVramMode == KimodoBridgeVramMode.High;
            float durationSeconds = generationFrames.intValue / TargetFps;
            string modelsRoot = KimodoPlayableClipGenerationSettings.instance.LocalModelsPath?.Trim();
            if (!string.IsNullOrWhiteSpace(modelsRoot))
            {
                modelsRoot = Path.GetFullPath(modelsRoot);
            }

            Debug.Log($"[Kimodo] Prompt: {motionPrompt.stringValue}");
            if (!string.IsNullOrWhiteSpace(modelsRoot))
            {
                Debug.Log($"[Kimodo] Using custom models root: {modelsRoot}");
            }

            KimodoBackendType backendType = clip.generationBackend == KimodoGenerationBackend.KimodoBridge
                ? KimodoBackendType.Bridge
                : KimodoBackendType.ComfyUi;

            lastStatus = $"Generating: {motionPrompt.stringValue}";
            Repaint();

            var request = new KimodoGenerationRequestDto
            {
                prompt = motionPrompt.stringValue,
                duration = durationSeconds,
                seed = effectiveSeed,
                steps = diffusionSteps.intValue,
                constraints_json = constraintsJson ?? string.Empty
            };

            KimodoGenerationResultDto result;
            if (backendType == KimodoBackendType.Bridge)
            {
                result = await KimodoBridgeController.GenerateBridgeAsync(
                    launcherPath,
                    modelName,
                    highVram,
                    kimodoRootPath,
                    modelsRoot,
                    request,
                    progress =>
                    {
                        lastStatus = progress;
                        Repaint();
                    },
                    token);

                PullBridgeStatusSnapshot(forceRefresh: true);
            }
            else
            {
                var settings = new KimodoRuntimeGenerationSettings
                {
                    bridgeSettings = new BridgeRuntimeSettings
                    {
                        runtimeRoot = kimodoRootPath,
                        launcherPath = launcherPath,
                        modelName = modelName,
                        highVram = highVram,
                        modelsRoot = modelsRoot,
                        startupTimeoutMs = ComputeBridgeStartupTimeoutMs(kimodoRootPath, highVram, modelName)
                    },
                    comfyHost = comfyuiIP.stringValue,
                    comfyPort = comfyuiPort.intValue,
                    comfyTimeoutSeconds = KimodoPlayableClipGenerationSettings.instance.GenerationTimeoutSeconds,
                    comfyWorkflowResourceName = "kimodo-unity-workflow"
                };

                using var runtimeService = new KimodoRuntimeGenerationService(settings);
                result = await runtimeService.GenerateAsync(
                    request,
                    backendType,
                    progress =>
                    {
                        lastStatus = progress;
                        Repaint();
                    },
                    token);
            }

            if (result == null || string.IsNullOrWhiteSpace(result.motionJsonCompact))
            {
                throw new Exception(result?.message ?? "No motion json found in runtime generation result.");
            }

            return result.motionJsonCompact;
        }

        private string BuildConstraintsJsonOrThrow()
        {
            TimelineClip sourceClip = FindTimelineClipForAsset(clip);
            if (sourceClip == null)
            {
                UpdateConstraintReferences(null);
                return string.Empty;
            }

            UpdateConstraintReferences(sourceClip);

            bool ok = KimodoInbetweenConstraintUtility.TryBuildConstraintsJson(
                sourceClip,
                enableInbetweenInterpolation != null && enableInbetweenInterpolation.boolValue,
                generationFrames != null ? generationFrames.intValue : KimodoPlayableClip.MIN_FRAMES,
                out string constraintsJson,
                out string error);

            if (!ok)
            {
                throw new InvalidOperationException($"Build constraints failed: {error}");
            }

            return constraintsJson ?? string.Empty;
        }

        private void UpdateConstraintReferences(TimelineClip sourceClip)
        {
            lastConstraintMarkers.Clear();
            lastIncludesAutoInbetweenConstraint = false;
            if (sourceClip == null)
            {
                return;
            }

            TrackAsset track = sourceClip.GetParentTrack();
            if (track == null)
            {
                return;
            }

            double minTime = sourceClip.start;
            double maxTime = sourceClip.end;
            foreach (IMarker marker in track.GetMarkers())
            {
                if (marker is not KimodoConstraintMarkerBase kimodoMarker)
                {
                    continue;
                }

                if (kimodoMarker.time < minTime || kimodoMarker.time > maxTime)
                {
                    continue;
                }

                lastConstraintMarkers.Add(kimodoMarker);
            }

            lastConstraintMarkers.Sort((a, b) => a.time.CompareTo(b.time));
            lastIncludesAutoInbetweenConstraint =
                enableInbetweenInterpolation != null && enableInbetweenInterpolation.boolValue;
        }

        private async Task CloseBridgeServerAndRefreshStatusAsync()
        {
            await KimodoBridgeController.CloseServerAsync();
            bridgeRunningCached = false;
            bridgePortDiscoveredCached = false;
            bridgeStatusReady = true;
            PullBridgeStatusSnapshot(forceRefresh: true);
            Repaint();
        }

        private int ComputeBridgeStartupTimeoutMs(string runtimeRoot, bool highVram, string modelName)
        {
            float timeoutSeconds = KimodoPlayableClipGenerationSettings.instance.GenerationTimeoutSeconds;
            int requestedMs = Math.Max(30000, Mathf.RoundToInt(timeoutSeconds * 1000f));
            int timeoutMs = requestedMs;

            KimodoBridgeController.ModelSetupStatus modelStatus =
                KimodoBridgeController.EvaluateModelSetupStatus(runtimeRoot, highVram, modelName, modelsRootOverride: null);
            if (modelStatus.Missing)
            {
                int minutes = modelStatus.EstimatedMinutes;
                int dynamicMs = (int)Math.Round(Math.Max(600f, minutes * 60f) * 1000f);
                timeoutMs = Math.Max(timeoutMs, dynamicMs);
            }

            return timeoutMs;
        }

        private void DrawBridgeModelSelector()
        {
            string current = string.IsNullOrWhiteSpace(bridgeModelName.stringValue) ? "Kimodo-SOMA-RP-v1" : bridgeModelName.stringValue.Trim();
            string[] options = KimodoBridgeController.SupportedModelNames;
            int idx = Array.IndexOf(options, current);
            if (idx < 0)
            {
                idx = 0;
            }

            int newIdx = EditorGUILayout.Popup(new GUIContent("Bridge Model"), idx, options);
            bridgeModelName.stringValue = options[Mathf.Clamp(newIdx, 0, options.Length - 1)];
        }

        private void DrawEstimatedSetupTimeHint()
        {
            string runtimeRoot = KimodoBridgeController.GetRuntimeRootPath();
            bool highVram = clip != null && clip.bridgeVramMode == KimodoBridgeVramMode.High;
            string modelName = clip == null || string.IsNullOrWhiteSpace(clip.bridgeModelName) ? "Kimodo-SOMA-RP-v1" : clip.bridgeModelName.Trim();
            string modelsRootOverride = KimodoPlayableClipGenerationSettings.instance.LocalModelsPath?.Trim();
            if (!KimodoBridgeController.TryGetModelMissingSetupMinutes(runtimeRoot, highVram, modelName, modelsRootOverride, out int minutes))
            {
                return;
            }
            EditorGUILayout.HelpBox($"Model missing detected, update required, approximately {minutes} minutes.", MessageType.None);
        }

        private void CancelGenerationInternal()
        {
            CancellationTokenSource cts = Interlocked.Exchange(ref generationCts, null);
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
            catch (ObjectDisposedException)
            {
                // Already disposed by another path.
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void ApplyMotionJsonToClip(string motionJson)
        {
            JObject obj = JObject.Parse(motionJson);

            Undo.RecordObject(clip, "Apply Kimodo Motion");
            clip.motionData = motionJson;
            clip.lastGeneratedPrompt = motionPrompt.stringValue;
            clip.isGenerated = true;

            clip.frameCount = obj.Value<int?>("num_frames") ?? 0;
            clip.jointCount = obj.Value<int?>("num_joints") ?? 0;
            clip.fps = obj.Value<int?>("fps") ?? 30;

            if (obj["joint_names"] is JArray names)
            {
                string[] arr = new string[names.Count];
                for (int i = 0; i < names.Count; i++)
                {
                    arr[i] = names[i]?.ToString();
                }
                clip.jointNames = arr;
            }
            else
            {
                clip.jointNames = null;
            }

            if (obj["joints"] is JArray joints)
            {
                float[] arr = new float[joints.Count];
                for (int i = 0; i < joints.Count; i++)
                {
                    arr[i] = joints[i] != null ? joints[i].Value<float>() : 0f;
                }
                clip.motionPositions = arr;
            }
            else
            {
                clip.motionPositions = null;
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
        }

        private void BakeCurrentMotionData()
        {
            if (clip == null || clip.clip == null || string.IsNullOrWhiteSpace(clip.motionData))
            {
                lastError = "Clip / motionData is missing.";
                return;
            }

            Undo.RecordObject(clip, "Bake Kimodo Motion");
            string error;
            bool willRetargetPipeline = clip.autoRetargetOnBinding || (!clip.autoRetargetOnBinding && clip.CustomRetargetAvatar != null);
            KimodoCurveFilterOptions bakeFilterOptions = clip.curveFilterOptions;
            if (willRetargetPipeline && clip.curveFilterOptions != null)
            {
                bakeFilterOptions = new KimodoCurveFilterOptions
                {
                    enabled = false,
                    positionError = clip.curveFilterOptions.positionError,
                    rotationError = clip.curveFilterOptions.rotationError,
                    floatError = clip.curveFilterOptions.floatError,
                    ensureQuaternionContinuity = false
                };
            }
            bool ok = KimodoUnityMotionTools.KimodoAnimationBaker.BakeIntoClip(
                targetClip: clip.clip,
                motionJson: clip.motionData,
                skeletonType: clip.InferredSkeletonType,
                curveFilterOptions: bakeFilterOptions,
                out error
            );

            if (!ok)
            {
                lastError = error;
                lastStatus = string.Empty;
                Debug.LogWarning($"[Kimodo] Bake failed: {error}");
                return;
            }

            clip.isGenerated = true;
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(clip.clip);
            AssetDatabase.SaveAssets();

            bool didRetarget = false;
            if (clip.autoRetargetOnBinding)
            {
                TimelineClip timelineClip = FindTimelineClipForAsset(clip);
                if (CanDirectOutputByJointNameMatch(clip, timelineClip))
                {
                    Debug.Log("[Kimodo] Retarget skipped: all source joints are present on bound skeleton by name.");
                }
                else
                {
                bool retargetOk = KimodoRetargetPipeline.TryRetargetBakedClip(
                    clip,
                    timelineClip,
                    out KimodoRetargetResultMode retargetMode,
                    out string retargetDetails);

                if (retargetOk)
                    {
                        Debug.Log($"[Kimodo] Retarget success. {retargetDetails}");
                        EditorUtility.SetDirty(clip.clip);
                        AssetDatabase.SaveAssets();
                        didRetarget = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[Kimodo] Retarget fallback to SOMA. {retargetDetails}");
                    }
                }
            }
            else if (clip.CustomRetargetAvatar != null)
            {
                if (KimodoRetargetPipeline.TryRetargetClipToAvatar(
                        clip.clip,
                        clip.CustomRetargetAvatar,
                        out AnimationClip customRetargetClip,
                        out string customRetargetDetails))
                {
                    if (customRetargetClip != null)
                    {
                        AnimationUtility.SetAnimationClipSettings(
                            customRetargetClip,
                            new AnimationClipSettings
                            {
                                loopTime = false,
                                keepOriginalPositionY = true
                            });

                        string clipPath = AssetDatabase.GetAssetPath(clip.clip);
                        if (!string.IsNullOrWhiteSpace(clipPath))
                        {
                            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(customRetargetClip);
                            clip.clip.ClearCurves();
                            for (int i = 0; i < bindings.Length; i++)
                            {
                                EditorCurveBinding b = bindings[i];
                                AnimationCurve c = AnimationUtility.GetEditorCurve(customRetargetClip, b);
                                clip.clip.SetCurve(b.path, b.type, b.propertyName, c);
                            }
                            clip.clip.frameRate = customRetargetClip.frameRate;
                            EditorUtility.SetDirty(clip.clip);
                            AssetDatabase.SaveAssets();
                            didRetarget = true;
                        }
                    }

                    Debug.Log($"[Kimodo] Custom avatar retarget success. {customRetargetDetails}");
                }
                else
                {
                    Debug.LogWarning($"[Kimodo] Custom avatar retarget failed. {customRetargetDetails}");
                }
            }

            if (didRetarget)
            {
                ApplyCurveFilterAfterRetarget(clip.clip);
            }

            RefreshTimelinePreviewGraph();

            lastError = string.Empty;
            lastStatus = "Bake complete.";
            Debug.Log("[Kimodo] Bake complete (SOMA).");
        }

        private void ApplyCurveFilterAfterRetarget(AnimationClip targetClip)
        {
            if (targetClip == null || clip == null || clip.curveFilterOptions == null)
            {
                return;
            }

            var options = clip.curveFilterOptions;
            if (!options.enabled)
            {
                if (options.ensureQuaternionContinuity)
                {
                    targetClip.EnsureQuaternionContinuity();
                }
                return;
            }

            GameObject tempRoot = BuildHierarchyFromClipBindings(targetClip, "KimodoPostRetargetFilterRoot");
            tempRoot.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var recorder = new UnityEditor.Animations.GameObjectRecorder(tempRoot);
                recorder.BindComponentsOfType<Transform>(tempRoot, true);
                float fps = targetClip.frameRate > 0f ? targetClip.frameRate : 30f;
                int frameCount = Mathf.Max(2, Mathf.RoundToInt(targetClip.length * fps));
                float dt = 1f / fps;
                for (int f = 0; f < frameCount; f++)
                {
                    float t = f / fps;
                    targetClip.SampleAnimation(tempRoot, t);
                    recorder.TakeSnapshot(dt);
                }

                var filter = new UnityEditor.Animations.CurveFilterOptions
                {
                    keyframeReduction = true,
                    positionError = Mathf.Clamp01(options.positionError),
                    rotationError = Mathf.Clamp01(options.rotationError),
                    scaleError = Mathf.Clamp01(options.positionError),
                    floatError = Mathf.Clamp01(options.floatError),
                    unrollRotation = true
                };
                targetClip.ClearCurves();
                recorder.SaveToClip(targetClip, fps, filter);
                if (options.ensureQuaternionContinuity)
                {
                    targetClip.EnsureQuaternionContinuity();
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
            }
        }

        private static GameObject BuildHierarchyFromClipBindings(AnimationClip clipAsset, string rootName)
        {
            var root = new GameObject(rootName);
            var created = new Dictionary<string, Transform>(StringComparer.Ordinal);
            created[string.Empty] = root.transform;

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clipAsset);
            for (int i = 0; i < bindings.Length; i++)
            {
                string path = bindings[i].path ?? string.Empty;
                if (created.ContainsKey(path))
                {
                    continue;
                }

                EnsurePath(path, root.transform, created);
            }

            return root;
        }

        private static Transform EnsurePath(string path, Transform root, Dictionary<string, Transform> cache)
        {
            if (cache.TryGetValue(path, out Transform existing))
            {
                return existing;
            }

            if (string.IsNullOrEmpty(path))
            {
                cache[string.Empty] = root;
                return root;
            }

            int split = path.LastIndexOf('/');
            string parentPath = split > 0 ? path.Substring(0, split) : string.Empty;
            string selfName = split >= 0 ? path.Substring(split + 1) : path;
            Transform parent = EnsurePath(parentPath, root, cache);

            var go = new GameObject(string.IsNullOrWhiteSpace(selfName) ? "Bone" : selfName);
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            cache[path] = t;
            return t;
        }

        private void DrawAdvancedCurveFilterSection()
        {
            if (curveFilterOptionsProp == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            bool newFoldout = EditorGUILayout.Foldout(showAdvancedFoldout, "Advanced", true);
            if (newFoldout != showAdvancedFoldout)
            {
                showAdvancedFoldout = newFoldout;
                KimodoPlayableClipGenerationSettings.instance.AdvancedCurveFilterFoldout = showAdvancedFoldout;
                KimodoPlayableClipGenerationSettings.instance.SaveSettings();
            }
            if (!showAdvancedFoldout)
            {
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Curve Filter Options", EditorStyles.boldLabel);

            SerializedProperty enabledProp = curveFilterOptionsProp.FindPropertyRelative("enabled");
            SerializedProperty positionErrorProp = curveFilterOptionsProp.FindPropertyRelative("positionError");
            SerializedProperty rotationErrorProp = curveFilterOptionsProp.FindPropertyRelative("rotationError");
            SerializedProperty floatErrorProp = curveFilterOptionsProp.FindPropertyRelative("floatError");
            SerializedProperty ensureQuatProp = curveFilterOptionsProp.FindPropertyRelative("ensureQuaternionContinuity");

            if (enabledProp != null)
            {
                EditorGUILayout.PropertyField(enabledProp, new GUIContent("Reduce Keyframes"));
            }

            if (enabledProp != null && enabledProp.boolValue)
            {
                if (positionErrorProp != null)
                {
                    positionErrorProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent("Position Error"),
                        positionErrorProp.floatValue,
                        0f,
                        1f);
                }

                if (rotationErrorProp != null)
                {
                    rotationErrorProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent("Rotation Error"),
                        rotationErrorProp.floatValue,
                        0f,
                        1f);
                }

                if (floatErrorProp != null)
                {
                    floatErrorProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent("Float Error"),
                        floatErrorProp.floatValue,
                        0f,
                        1f);
                }
            }

            if (ensureQuatProp != null)
            {
                EditorGUILayout.PropertyField(ensureQuatProp, new GUIContent("Ensure Quaternion Continuity"));
            }

            EditorGUI.indentLevel--;
        }

        private static bool CanDirectOutputByJointNameMatch(KimodoPlayableClip playableClip, TimelineClip timelineClip)
        {
            if (playableClip == null || playableClip.jointNames == null || playableClip.jointNames.Length == 0)
            {
                return false;
            }

            if (!TryResolveBoundAnimatorForTimelineClip(timelineClip, out Animator animator))
            {
                return false;
            }

            Transform skeletonRoot = animator != null ? animator.transform : null;
            if (skeletonRoot == null)
            {
                return false;
            }

            var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<Transform>();
            stack.Push(skeletonRoot);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(current.name))
                {
                    nameSet.Add(current.name);
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            for (int i = 0; i < playableClip.jointNames.Length; i++)
            {
                string jointName = playableClip.jointNames[i];
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    continue;
                }

                if (!nameSet.Contains(jointName))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveBoundAnimatorForTimelineClip(TimelineClip timelineClip, out Animator animator)
        {
            animator = null;
            if (timelineClip == null)
            {
                return false;
            }

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
            {
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                return false;
            }

            TrackAsset currentTrack = track;
            while (currentTrack != null)
            {
                animator = director.GetGenericBinding(currentTrack) as Animator;
                if (animator != null)
                {
                    return true;
                }

                currentTrack = currentTrack.parent as TrackAsset;
            }

            return false;
        }

        private void DrawErrorSection()
        {
            if (!string.IsNullOrEmpty(lastError))
            {
                EditorGUILayout.HelpBox(lastError, MessageType.Error);
            }
        }

        private void DrawGeneratedInfo()
        {
            if (!clip.isGenerated)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            if (!string.IsNullOrWhiteSpace(clip.lastGeneratedPrompt))
            {
                EditorGUILayout.LabelField($"Prompt: {clip.lastGeneratedPrompt}", EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField($"Frames: {clip.frameCount}, Joints: {clip.jointCount}, FPS: {clip.fps}", EditorStyles.miniLabel);
            if (!string.IsNullOrWhiteSpace(lastConstraintsPath))
            {
                EditorGUILayout.LabelField($"Constraints: {lastConstraintsPath}", EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Reset", GUILayout.Width(100)))
            {
                Undo.RecordObject(clip, "Reset Kimodo Clip");
                clip.ResetGeneration();
                EditorUtility.SetDirty(clip);
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateAndAssignNewAnimationClip()
        {
            var newAnimationClip = new AnimationClip
            {
                name = $"{GeneratedClipNamePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}"
            };

            if (!AssetDatabase.IsValidFolder(GeneratedClipFolder))
            {
                AssetDatabase.CreateFolder("Assets", "KimodoGeneratedClips");
            }

            string fileName = $"{newAnimationClip.name}.anim";
            string savePath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedClipFolder}/{fileName}");
            AssetDatabase.CreateAsset(newAnimationClip, savePath);

            clip.clip = newAnimationClip;
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(clip.clip);
            AssetDatabase.SaveAssets();
            RefreshTimelinePreviewGraph();
        }

        private void TrimGeneratedClipsToLimit()
        {
            int maxCount = Mathf.Clamp(
                KimodoPlayableClipGenerationSettings.instance.MaxGeneratedClips,
                KimodoPlayableClipGenerationSettings.MinGeneratedClipsLimit,
                KimodoPlayableClipGenerationSettings.MaxGeneratedClipsLimit);

            if (!AssetDatabase.IsValidFolder(GeneratedClipFolder))
            {
                return;
            }

            string[] clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { GeneratedClipFolder });
            if (clipGuids == null || clipGuids.Length <= maxCount)
            {
                return;
            }

            var clipPaths = new List<string>(clipGuids.Length);
            foreach (string guid in clipGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                if (!name.StartsWith(GeneratedClipNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                clipPaths.Add(path);
            }

            if (clipPaths.Count <= maxCount)
            {
                return;
            }

            clipPaths.Sort(CompareGeneratedClipPathsByAgeOldestFirst);
            string activeClipPath = clip.clip != null ? AssetDatabase.GetAssetPath(clip.clip) : string.Empty;

            foreach (string candidatePath in clipPaths)
            {
                if (!string.IsNullOrWhiteSpace(activeClipPath) &&
                    string.Equals(candidatePath, activeClipPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsAssetReferencedByOtherAssets(candidatePath))
                {
                    Debug.Log($"[Kimodo] Generated clip cleanup skipped referenced clip: {candidatePath}");
                    return;
                }

                if (AssetDatabase.DeleteAsset(candidatePath))
                {
                    AssetDatabase.SaveAssets();
                }
                return;
            }
        }

        private static int CompareGeneratedClipPathsByAgeOldestFirst(string leftPath, string rightPath)
        {
            string leftName = Path.GetFileNameWithoutExtension(leftPath) ?? string.Empty;
            string rightName = Path.GetFileNameWithoutExtension(rightPath) ?? string.Empty;
            string leftStamp = leftName.StartsWith(GeneratedClipNamePrefix, StringComparison.Ordinal)
                ? leftName.Substring(GeneratedClipNamePrefix.Length)
                : leftName;
            string rightStamp = rightName.StartsWith(GeneratedClipNamePrefix, StringComparison.Ordinal)
                ? rightName.Substring(GeneratedClipNamePrefix.Length)
                : rightName;
            return string.Compare(leftStamp, rightStamp, StringComparison.Ordinal);
        }

        private static bool IsAssetReferencedByOtherAssets(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            foreach (string path in allAssets)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    string.Equals(path, assetPath, StringComparison.OrdinalIgnoreCase) ||
                    !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] dependencies;
                try
                {
                    dependencies = AssetDatabase.GetDependencies(path, false);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < dependencies.Length; i++)
                {
                    if (string.Equals(dependencies[i], assetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RefreshTimelinePreviewGraph()
        {
            if (clip == null || TimelineEditor.inspectedAsset == null)
            {
                return;
            }

            // Refresh only when Timeline preview is currently enabled.
            if (!TryGetTimelinePreviewMode(out bool isPreviewMode) || !isPreviewMode)
            {
                return;
            }

            // Refresh only when this playable clip is selected in Timeline.
            if (FindTimelineClipForAsset(clip) == null)
            {
                return;
            }

            if (!TrySetTimelinePreviewMode(false))
            {
                return;
            }

            TrySetTimelinePreviewMode(true);
            TimelineEditor.Refresh(RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate | RefreshReason.WindowNeedsRedraw);
        }

        private static bool TryGetTimelinePreviewMode(out bool previewMode)
        {
            previewMode = false;
            object timelineState = GetTimelineEditorState();
            if (timelineState == null)
            {
                return false;
            }

            Type stateType = timelineState.GetType();
            PropertyInfo previewModeProperty = stateType.GetProperty("previewMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (previewModeProperty != null && previewModeProperty.PropertyType == typeof(bool))
            {
                previewMode = (bool)previewModeProperty.GetValue(timelineState, null);
                return true;
            }

            FieldInfo previewModeField = stateType.GetField("previewMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (previewModeField != null && previewModeField.FieldType == typeof(bool))
            {
                previewMode = (bool)previewModeField.GetValue(timelineState);
                return true;
            }

            return false;
        }

        private static bool TrySetTimelinePreviewMode(bool value)
        {
            object timelineState = GetTimelineEditorState();
            if (timelineState == null)
            {
                return false;
            }

            Type stateType = timelineState.GetType();
            PropertyInfo previewModeProperty = stateType.GetProperty("previewMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (previewModeProperty != null && previewModeProperty.PropertyType == typeof(bool) && previewModeProperty.CanWrite)
            {
                previewModeProperty.SetValue(timelineState, value, null);
                return true;
            }

            FieldInfo previewModeField = stateType.GetField("previewMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (previewModeField != null && previewModeField.FieldType == typeof(bool))
            {
                previewModeField.SetValue(timelineState, value);
                return true;
            }

            return false;
        }

        private static object GetTimelineEditorState()
        {
            Type timelineEditorType = typeof(TimelineEditor);
            const BindingFlags StaticMemberFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo stateProperty = timelineEditorType.GetProperty("state", StaticMemberFlags);
            if (stateProperty != null)
            {
                return stateProperty.GetValue(null, null);
            }

            FieldInfo stateField = timelineEditorType.GetField("state", StaticMemberFlags);
            if (stateField != null)
            {
                return stateField.GetValue(null);
            }

            return null;
        }

        private void TrySyncTimelineDuration(int frames)
        {
            UnityEngine.Timeline.TimelineClip timelineClip = FindTimelineClipForAsset(clip);
            if (timelineClip == null)
            {
                return;
            }

            float newDuration = frames / TargetFps;
            UndoExtensions.RegisterClip(timelineClip, L10n.Tr("Modify Clip Duration"));
            timelineClip.duration = newDuration;
        }

        private UnityEngine.Timeline.TimelineClip FindTimelineClipForAsset(PlayableAsset asset)
        {
            if (TimelineEditor.inspectedAsset == null)
            {
                return null;
            }

            foreach (UnityEngine.Timeline.TimelineClip selectedClip in TimelineEditor.selectedClips)
            {
                if (selectedClip.asset == asset)
                {
                    return selectedClip;
                }
            }

            foreach (UnityEngine.Timeline.TrackAsset track in TimelineEditor.inspectedAsset.GetOutputTracks())
            {
                foreach (UnityEngine.Timeline.TimelineClip timelineClip in track.GetClips())
                {
                    if (timelineClip.asset == asset)
                    {
                        return timelineClip;
                    }
                }
            }

            return null;
        }

    }
}


