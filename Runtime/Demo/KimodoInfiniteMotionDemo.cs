using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TimelineInject;
using UnityEngine;

namespace KimodoBridge
{
    public sealed class KimodoInfiniteMotionDemo : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform profileSkeletonRoot;
        [SerializeField] private Animator humanoidRetargetAnimator;

        [Header("Bridge Runtime")]
        [SerializeField] private string modelsRoot = string.Empty;
        [SerializeField] private string modelName = "Kimodo-SOMA-RP-v1";
        [SerializeField] private bool highVram;
        [SerializeField] private bool forceSetup;

        [Header("Generation")]
        [SerializeField] private string defaultPrompt = "A person dancing with energetic rhythm.";
        [SerializeField][Min(1)] private int generationFrames = 150;
        [SerializeField][Min(1)] private int diffusionSteps = 100;
        [SerializeField] private bool randomSeed = true;
        [SerializeField] private int fixedSeed = 42;
        [SerializeField][Min(0.1f)] private float segmentIntervalSeconds = 5f;
        [SerializeField] private bool loopHint = true;
        [SerializeField] private bool allowPartialJoints;

        [Header("Debug")]
        [SerializeField] private bool autoStartOnEnable;
        [SerializeField] private bool verboseLogging = true;

        private const string FullBodyConstraintType = "fullbody";
        private const string KimodoFolderName = "NvlabKimodoQuickServer";

        private KimodoRuntimeGenerationService generationService;
        private CancellationTokenSource lifetimeCts;
        private Task schedulerTask;
        private bool running;
        private bool startRequested;

        private RawMotionPlayer motionPlayer;

        private bool generationInFlight;
        private int segmentIndex;
        private int lastGenerationWaitStatusSegment = -1;
        private KimodoMarkerSampleResult nextConstraintPose;
        private bool manualSendRequested;
        private string promptDraft;
        private string statusMessage = "Idle.";

        private void Awake()
        {
            motionPlayer = new RawMotionPlayer();
            promptDraft = ResolveInitialPrompt();
        }

        private void OnEnable()
        {
            if (ValidateConfiguration(out _))
            {
                try
                {
                    EnsurePromptDraftInitialized();
                    UpdateStatus("Idle.");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Config warning: {ex.Message}");
                }
            }
            else
            {
                EnsurePromptDraftInitialized();
                UpdateStatus("Idle.");
            }

            if (autoStartOnEnable)
            {
                _ = StartDemoAsync();
            }
        }

        private void OnDisable()
        {
            _ = StopDemoAsync();
        }

        private void Update()
        {
            motionPlayer.Update(
                Time.deltaTime,
                modelName,
                profileSkeletonRoot,
                humanoidRetargetAnimator,
                allowPartialJoints,
                out GeneratedSegment startedSegment,
                out string playbackError);
            if (!string.IsNullOrWhiteSpace(playbackError))
            {
                UpdateStatus($"Playback failed: {playbackError}");
            }

            if (startedSegment != null)
            {
                nextConstraintPose = startedSegment.TailPose;
                UpdateStatus($"Playing segment {startedSegment.Index}.");
            }
        }

        private void OnGUI()
        {
            DrawPromptBar();
        }

        private void OnDestroy()
        {
            motionPlayer.Stop();
        }

        public async Task StartDemoAsync()
        {
            if (running || startRequested)
            {
                return;
            }

            startRequested = true;
            try
            {
                if (!ValidateConfiguration(out string error))
                {
                    UpdateStatus(error);
                    Debug.LogError($"[KimodoInfiniteMotionDemo] {error}");
                    return;
                }

                lifetimeCts?.Cancel();
                lifetimeCts?.Dispose();
                lifetimeCts = new CancellationTokenSource();

                segmentIndex = 0;
                lastGenerationWaitStatusSegment = -1;
                nextConstraintPose = null;
                generationInFlight = false;
                manualSendRequested = false;
                motionPlayer.ResetCompletionState();
                motionPlayer.ClearQueue();

                generationService?.Dispose();
                generationService = new KimodoRuntimeGenerationService(BuildRuntimeGenerationSettings());

                UpdateStatus("Starting Kimodo bridge...");
                await generationService.StartAsync(KimodoBackendType.Bridge, OnProgress, lifetimeCts.Token);

                running = true;
                schedulerTask = RunSchedulerLoopAsync(lifetimeCts.Token);
                UpdateStatus("Bridge ready.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                UpdateStatus($"Start failed: {ex.Message}");
                await StopDemoAsync();
            }
            finally
            {
                startRequested = false;
            }
        }

        public async Task StopDemoAsync()
        {
            running = false;

            CancellationTokenSource cts = lifetimeCts;
            lifetimeCts = null;
            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                }
            }

            Task task = schedulerTask;
            schedulerTask = null;
            if (task != null)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[KimodoInfiniteMotionDemo] Scheduler stop observed exception: {ex.Message}");
                }
            }

            if (generationService != null)
            {
                try
                {
                    await generationService.StopAsync(KimodoBackendType.Bridge, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[KimodoInfiniteMotionDemo] Stop bridge failed: {ex.Message}");
                }

                generationService.Dispose();
                generationService = null;
            }

            if (cts != null)
            {
                cts.Dispose();
            }

            generationInFlight = false;
            lastGenerationWaitStatusSegment = -1;
            nextConstraintPose = null;
            motionPlayer.Stop();
            motionPlayer.ResetCompletionState();
            motionPlayer.ClearQueue();
            UpdateStatus("Stopped.");
        }

        private async Task RunSchedulerLoopAsync(CancellationToken token)
        {
            try
            {
                await GenerateNextSegmentAsync(token);

                while (!token.IsCancellationRequested)
                {
                    MaybeQueueNextGeneration(token);
                    await Task.Delay(100, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                UpdateStatus($"Scheduler failed: {ex.Message}");
                running = false;
            }
        }

        private void MaybeQueueNextGeneration(CancellationToken token)
        {
            if (!running || generationInFlight || generationService == null)
            {
                return;
            }

            bool manualTrigger = manualSendRequested;
            if (!manualTrigger && motionPlayer.QueuedSegmentCount > 0)
            {
                return;
            }

            if (!CanStartGenerationForCurrentSegment(out int waitingForSegment))
            {
                if (lastGenerationWaitStatusSegment != segmentIndex)
                {
                    UpdateStatus($"Waiting for segment {waitingForSegment} to finish before generating segment {segmentIndex}.");
                    lastGenerationWaitStatusSegment = segmentIndex;
                }

                return;
            }

            bool shouldGenerate;
            if (manualTrigger)
            {
                shouldGenerate = true;
            }
            else
            {
                shouldGenerate = motionPlayer.QueuedSegmentCount == 0;
            }

            if (!shouldGenerate)
            {
                return;
            }

            manualSendRequested = false;
            lastGenerationWaitStatusSegment = -1;
            _ = GenerateNextSegmentAsync(token);
        }

        private bool CanStartGenerationForCurrentSegment(out int waitingForSegment)
        {
            int requiredCompletedSegment = segmentIndex - 2;
            waitingForSegment = requiredCompletedSegment;
            if (requiredCompletedSegment < 0)
            {
                return true;
            }

            return motionPlayer.LastCompletedSegmentIndex >= requiredCompletedSegment;
        }

        private async Task GenerateNextSegmentAsync(CancellationToken token)
        {
            if (generationInFlight || generationService == null)
            {
                return;
            }

            generationInFlight = true;
            try
            {
                string prompt = ResolvePrompt();
                string constraintsJson = BuildNextConstraintsJson();
                var request = new KimodoGenerationRequestDto
                {
                    prompt = prompt,
                    duration = Mathf.Max(segmentIntervalSeconds, generationFrames / KimodoPlayableClip.FIXED_FRAME_RATE),
                    seed = randomSeed ? (int?)null : fixedSeed,
                    steps = Mathf.Max(1, diffusionSteps),
                    constraints_json = constraintsJson,
                    boundary_pose_json = string.Empty,
                    loop_hint = loopHint,
                    segment_index = segmentIndex,
                    transition_duration = 0f
                };

                OnProgress($"Generating segment {segmentIndex}...");
                KimodoGenerationResultDto result = await generationService.GenerateAsync(
                    request,
                    KimodoBackendType.Bridge,
                    OnProgress,
                    token);

                if (!KimodoRawMotionUtility.TryParse(result.motionJsonCompact, out KimodoRawMotionData motion, out string parseError))
                {
                    throw new InvalidOperationException(parseError);
                }

                if (!KimodoRawMotionUtility.TryExtractTailMarkerSample(
                        motion,
                        modelName,
                        out KimodoMarkerSampleResult tailPose,
                        out string tailError,
                        FullBodyConstraintType,
                        0.0,
                        allowPartialJoints))
                {
                    throw new InvalidOperationException(tailError);
                }

                motionPlayer.Enqueue(new GeneratedSegment
                {
                    Index = segmentIndex,
                    Motion = motion,
                    TailPose = tailPose
                });

                segmentIndex++;
                UpdateStatus($"Segment {segmentIndex - 1} ready.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                UpdateStatus($"Generate failed: {ex.Message}");
            }
            finally
            {
                generationInFlight = false;
            }
        }

        private string BuildNextConstraintsJson()
        {
            if (nextConstraintPose == null)
            {
                return string.Empty;
            }

            KimodoMarkerSampleResult sample = nextConstraintPose.Clone();
            sample.constraintType = FullBodyConstraintType;
            sample.sampleTime = 0.0;
            return KimodoConstraintJsonExporter.ToConstraintsJson(
                new List<KimodoMarkerSampleResult> { sample },
                0.0,
                Mathf.Max(segmentIntervalSeconds, generationFrames / KimodoPlayableClip.FIXED_FRAME_RATE));
        }

        private KimodoRuntimeGenerationSettings BuildRuntimeGenerationSettings()
        {
            string resolvedRuntimeRoot = ResolveRuntimeRoot();
            string launcherPath = BridgeLauncherResolver.ResolveStartScript(resolvedRuntimeRoot);
            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                throw new FileNotFoundException($"Cannot resolve bridge launcher under '{resolvedRuntimeRoot}'.");
            }

            return new KimodoRuntimeGenerationSettings
            {
                bridgeSettings = new BridgeRuntimeSettings
                {
                    runtimeRoot = resolvedRuntimeRoot,
                    launcherPath = launcherPath,
                    modelName = modelName,
                    highVram = highVram,
                    forceSetup = forceSetup,
                    modelsRoot = string.IsNullOrWhiteSpace(modelsRoot) ? null : Path.GetFullPath(modelsRoot)
                }
            };
        }

        private bool ValidateConfiguration(out string error)
        {
            if (profileSkeletonRoot == null)
            {
                error = "Profile skeleton root is not assigned.";
                return false;
            }

            string resolvedRuntimeRoot = ResolveRuntimeRoot();
            if (string.IsNullOrWhiteSpace(resolvedRuntimeRoot))
            {
                error = "Runtime root is empty.";
                return false;
            }

            if (!Directory.Exists(resolvedRuntimeRoot))
            {
                error = $"Runtime root does not exist: {resolvedRuntimeRoot}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private string ResolveRuntimeRoot()
        {
            if (Application.isEditor)
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", KimodoFolderName));
            }

            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, KimodoFolderName));
        }

        private string ResolvePrompt()
        {
            string prompt = promptDraft;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = defaultPrompt;
            }

            return string.IsNullOrWhiteSpace(prompt) ? "A person dancing." : prompt.Trim();
        }

        public void StartDemo()
        {
            _ = StartDemoAsync();
        }

        public void StopDemo()
        {
            _ = StopDemoAsync();
        }

        private void DrawPromptBar()
        {
            const float margin = 12f;
            const float panelHeight = 74f;
            const float buttonWidth = 110f;
            const float fieldHeight = 28f;

            DrawStatusPanel(margin);

            Rect panelRect = new Rect(
                margin,
                Mathf.Max(margin, Screen.height - panelHeight - margin),
                Mathf.Max(0f, Screen.width - margin * 2f),
                panelHeight);

            GUI.Box(panelRect, GUIContent.none);

            Rect fieldRect = new Rect(
                panelRect.x + 12f,
                panelRect.y + 14f,
                Mathf.Max(0f, panelRect.width - buttonWidth - 32f),
                fieldHeight);

            Rect buttonRect = new Rect(
                panelRect.xMax - buttonWidth - 12f,
                fieldRect.y,
                buttonWidth,
                fieldHeight);

            GUI.SetNextControlName("KimodoPromptInput");
            promptDraft = GUI.TextField(fieldRect, promptDraft ?? string.Empty);

            if (Event.current.type == EventType.KeyDown &&
                GUI.GetNameOfFocusedControl() == "KimodoPromptInput" &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Event.current.Use();
                RequestManualSend();
            }

            if (GUI.Button(buttonRect, "Send"))
            {
                RequestManualSend();
            }
        }

        private void DrawStatusPanel(float margin)
        {
            const float panelHeight = 42f;
            Rect panelRect = new Rect(
                margin,
                margin,
                Mathf.Max(0f, Screen.width - margin * 2f),
                panelHeight);

            GUI.Box(panelRect, GUIContent.none);

            Rect labelRect = new Rect(
                panelRect.x + 12f,
                panelRect.y + 10f,
                Mathf.Max(0f, panelRect.width - 24f),
                22f);

            GUI.Label(labelRect, string.IsNullOrWhiteSpace(statusMessage) ? " " : statusMessage);
        }

        private void RequestManualSend()
        {
            manualSendRequested = true;
            if (!running || generationService == null || generationInFlight)
            {
                return;
            }

            MaybeQueueNextGeneration(lifetimeCts != null ? lifetimeCts.Token : CancellationToken.None);
        }

        private void OnProgress(string message)
        {
            if (verboseLogging && !string.IsNullOrWhiteSpace(message))
            {
                Debug.Log($"[KimodoInfiniteMotionDemo] {message}");
            }

            UpdateStatus(message);
        }

        private void UpdateStatus(string message)
        {
            statusMessage = string.IsNullOrWhiteSpace(message) ? " " : message;
        }

        private string ResolveInitialPrompt()
        {
            string prompt = defaultPrompt;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = defaultPrompt;
            }

            return string.IsNullOrWhiteSpace(prompt) ? "A person dancing." : prompt.Trim();
        }

        private void EnsurePromptDraftInitialized()
        {
            if (string.IsNullOrWhiteSpace(promptDraft))
            {
                promptDraft = ResolveInitialPrompt();
            }
        }

        private sealed class GeneratedSegment
        {
            public int Index;
            public KimodoRawMotionData Motion;
            public KimodoMarkerSampleResult TailPose;
        }

        private sealed class RawMotionPlayer
        {
            private readonly Queue<GeneratedSegment> queuedSegments = new Queue<GeneratedSegment>();
            private readonly object queueGate = new object();

            private KimodoRawMotionPlaybackBinding profileBinding;
            private KimodoRawMotionPlaybackBinding sourceBinding;
            private SkeletonCache sourceCache;
            private HumanPoseHandler targetPoseHandler;
            private Animator targetAnimator;
            private bool restoreTargetAnimatorEnabled;
            private bool targetAnimatorWasEnabled;
            private GeneratedSegment currentSegment;
            private float timeSeconds;
            private bool playing;

            public bool IsPlaying => playing;
            public int LastCompletedSegmentIndex { get; private set; } = -1;

            public int QueuedSegmentCount
            {
                get
                {
                    lock (queueGate)
                    {
                        return queuedSegments.Count;
                    }
                }
            }

            public void Enqueue(GeneratedSegment segment)
            {
                if (segment == null)
                {
                    return;
                }

                lock (queueGate)
                {
                    queuedSegments.Enqueue(segment);
                }
            }

            public void ClearQueue()
            {
                lock (queueGate)
                {
                    queuedSegments.Clear();
                }
            }

            public void ResetCompletionState()
            {
                LastCompletedSegmentIndex = -1;
            }

            public void Update(
                float deltaTime,
                string modelName,
                Transform profileSkeletonRoot,
                Animator humanoidRetargetAnimator,
                bool allowPartialJoints,
                out GeneratedSegment startedSegment,
                out string error)
            {
                startedSegment = null;
                error = string.Empty;

                if (playing && profileBinding != null)
                {
                    AdvanceCurrentMotion(deltaTime, out error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return;
                    }
                }

                if (!playing && TryDequeue(out GeneratedSegment next))
                {
                    if (!Play(
                            next,
                            modelName,
                            profileSkeletonRoot,
                            humanoidRetargetAnimator,
                            allowPartialJoints,
                            out error))
                    {
                        return;
                    }

                    startedSegment = next;
                }
            }

            public void Stop()
            {
                StopActiveMotion();
            }

            private bool Play(
                GeneratedSegment segment,
                string modelName,
                Transform profileSkeletonRoot,
                Animator humanoidRetargetAnimator,
                bool allowPartialJoints,
                out string error)
            {
                StopActiveMotion();
                if (!KimodoRawMotionUtility.TryCreatePlaybackBinding(
                        segment.Motion,
                        modelName,
                        profileSkeletonRoot,
                        out profileBinding,
                        out error,
                        allowPartialJoints))
                {
                    return false;
                }

                if (humanoidRetargetAnimator != null &&
                    !TryCreateDirectRetargetBinding(segment.Motion, modelName, humanoidRetargetAnimator, allowPartialJoints, out error))
                {
                    StopActiveMotion();
                    return false;
                }

                currentSegment = segment;
                timeSeconds = 0f;
                playing = true;
                return TryApplyFrame(0, out error);
            }

            private void AdvanceCurrentMotion(float deltaTime, out string error)
            {
                error = string.Empty;
                if (!playing || profileBinding == null)
                {
                    return;
                }

                timeSeconds += Mathf.Max(0f, deltaTime);
                bool reachedEnd = false;
                if (profileBinding.Motion != null && timeSeconds >= profileBinding.Motion.LastFrameTimeSeconds)
                {
                    timeSeconds = profileBinding.Motion.LastFrameTimeSeconds;
                    reachedEnd = true;
                }

                if (!TryApplyTime(timeSeconds, out error))
                {
                    StopActiveMotion();
                    return;
                }

                if (reachedEnd)
                {
                    MarkCurrentSegmentCompleted();
                    StopActiveMotion();
                }
            }

            private bool TryDequeue(out GeneratedSegment segment)
            {
                lock (queueGate)
                {
                    if (queuedSegments.Count == 0)
                    {
                        segment = null;
                        return false;
                    }

                    segment = queuedSegments.Dequeue();
                    return true;
                }
            }

            private void MarkCurrentSegmentCompleted()
            {
                if (currentSegment != null && currentSegment.Index > LastCompletedSegmentIndex)
                {
                    LastCompletedSegmentIndex = currentSegment.Index;
                }
            }

            private void StopActiveMotion()
            {
                profileBinding = null;
                sourceBinding = null;
                sourceCache?.Dispose();
                sourceCache = null;
                targetPoseHandler = null;
                if (targetAnimator != null && restoreTargetAnimatorEnabled)
                {
                    targetAnimator.enabled = targetAnimatorWasEnabled;
                }

                targetAnimator = null;
                restoreTargetAnimatorEnabled = false;
                targetAnimatorWasEnabled = false;
                currentSegment = null;
                timeSeconds = 0f;
                playing = false;
            }

            private bool TryCreateDirectRetargetBinding(
                KimodoRawMotionData motion,
                string modelName,
                Animator humanoidAnimator,
                bool allowPartialJoints,
                out string error)
            {
                error = string.Empty;
                if (humanoidAnimator == null)
                {
                    return true;
                }

                Avatar targetAvatar = humanoidAnimator.avatar;
                if (!KimodoRetargetCoreUtility.IsValidHumanoid(targetAvatar))
                {
                    error = "Humanoid retarget animator avatar is null, invalid, or not humanoid.";
                    return false;
                }

                if (!KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out Avatar sourceAvatar, out error))
                {
                    return false;
                }

                if (!KimodoRetargetAvatarUtility.TryBuildSkeletonCache(
                        sourceAvatar,
                        "KimodoInfiniteMotionDemo_SourceRetarget",
                        out sourceCache,
                        out error))
                {
                    return false;
                }

                if (!KimodoRawMotionUtility.TryCreatePlaybackBinding(
                        motion,
                        modelName,
                        sourceCache.skeletonRoot,
                        out sourceBinding,
                        out error,
                        allowPartialJoints))
                {
                    return false;
                }

                targetAnimator = humanoidAnimator;
                targetPoseHandler = new HumanPoseHandler(targetAvatar, humanoidAnimator.transform);
                targetAnimatorWasEnabled = humanoidAnimator.enabled;
                restoreTargetAnimatorEnabled = true;
                humanoidAnimator.enabled = false;
                return true;
            }

            private bool TryApplyFrame(int frameIndex, out string error)
            {
                if (!KimodoRawMotionUtility.TryApplyFrame(profileBinding, frameIndex, out error))
                {
                    return false;
                }

                if (sourceBinding != null && !KimodoRawMotionUtility.TryApplyFrame(sourceBinding, frameIndex, out error))
                {
                    return false;
                }

                return TryApplyHumanoidPose(out error);
            }

            private bool TryApplyTime(float sampleTimeSeconds, out string error)
            {
                if (!KimodoRawMotionUtility.TryApplyTime(profileBinding, sampleTimeSeconds, out error))
                {
                    return false;
                }

                if (sourceBinding != null && !KimodoRawMotionUtility.TryApplyTime(sourceBinding, sampleTimeSeconds, out error))
                {
                    return false;
                }

                return TryApplyHumanoidPose(out error);
            }

            private bool TryApplyHumanoidPose(out string error)
            {
                error = string.Empty;
                if (sourceCache == null || targetPoseHandler == null)
                {
                    return true;
                }

                if (!KimodoRetargetSamplingUtility.TryCaptureMuscleSample(sourceCache, out MuscleSample sample, out error))
                {
                    return false;
                }

                HumanPose pose = sample.pose;
                targetPoseHandler.SetHumanPose(ref pose);
                return true;
            }
        }
    }
}
