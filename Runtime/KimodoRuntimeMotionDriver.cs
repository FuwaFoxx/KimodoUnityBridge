using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TimelineInject;
using UnityEngine;

namespace KimodoBridge
{
    [AddComponentMenu("Kimodo/Runtime Motion Driver")]
    public sealed class KimodoRuntimeMotionDriver : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Animator targetHumanoidAnimator;

        [Header("Bridge Runtime")]
        [SerializeField] private string modelsRoot = string.Empty;
        [SerializeField] private string modelName = "Kimodo-SOMA-RP-v1";
        [SerializeField] private bool highVram;
        [SerializeField] private bool forceSetup;
        [SerializeField][Min(1f)] private float startupTimeoutMinutes = 30f;

        [Header("Generation")]
        [SerializeField] private string defaultPrompt = IdlePrompt;
        [SerializeField][Min(1)] private int generationFrames = 150;
        [SerializeField][Min(1)] private int diffusionSteps = 100;
        [SerializeField] private bool randomSeed = true;
        [SerializeField] private int fixedSeed = 42;
        [SerializeField][Min(0.1f)] private float segmentIntervalSeconds = 5f;
        [SerializeField] private bool loopHint = true;
        [SerializeField][Min(1)] private int overlapConstraintSamples = 4;
        [SerializeField] private bool allowPartialJoints;
        [SerializeField] private bool trimSegmentTail = true;
        [SerializeField][Range(0f, 0.2f)] private float segmentTailTrimPercent = 0.1f;

        [Header("Foot IK Targets")]
        [SerializeField] private bool driveFootIkTargets = true;
        [SerializeField] private string leftFootIkTargetName = "LeftFootIK";
        [SerializeField] private string rightFootIkTargetName = "RightFootIK";

        [Header("Debug")]
        [SerializeField, Tooltip("Debug only. Draw the internal source skeleton in the scene using Debug.DrawLine.")]
        private bool drawDebugSkeleton;
        [SerializeField] private Color debugSkeletonBoneColor = new Color(0.2f, 0.95f, 1f, 1f);
        [SerializeField] private Color debugSkeletonJointColor = new Color(1f, 0.7f, 0.2f, 1f);
        [SerializeField][Min(0.001f)] private float debugJointMarkerSize = 0.025f;
        [SerializeField] private bool verboseLogging = true;

        private const string FullBodyConstraintType = "fullbody";
        private const string LeftHandConstraintType = "left-hand";
        private const string RightHandConstraintType = "right-hand";
        private const string LeftFootConstraintType = "left-foot";
        private const string RightFootConstraintType = "right-foot";
        private const string Root2DConstraintType = "root2d";
        private const string IdlePrompt = "idle";
        private const string KimodoFolderName = "NvlabKimodoQuickServer~";
        private const float MinGenerationDurationSeconds = 1f;
        private const float MaxGenerationDurationSeconds = 10f;
        private const int MaxOverlapConstraintSamples = 10;

        private KimodoBridgeService bridgeService;
        private CancellationTokenSource lifetimeCts;
        private Task schedulerTask;
        private bool running;
        private bool startRequested;
        private bool generationInFlight;
        private int segmentIndex;
        private int lastGenerationWaitStatusSegment = -1;
        private int generationRequestVersion;
        private string promptDraft;
        private string statusMessage = "Idle.";
        private readonly List<KimodoMarkerSampleResult> nextConstraintPoses = new List<KimodoMarkerSampleResult>();
        private readonly List<KimodoMarkerSampleResult> pendingConstraintSamples = new List<KimodoMarkerSampleResult>();
        private readonly List<KimodoMarkerSampleResult> constraintJsonScratch = new List<KimodoMarkerSampleResult>();
        private SingleTargetMotionPlayer motionPlayer;

        [NonSerialized] private bool promptLocked;

        public string StatusMessage => statusMessage;
        public bool IsRunning => running;
        public bool DrawDebugSkeleton
        {
            get => drawDebugSkeleton;
            set => drawDebugSkeleton = value;
        }

        public bool PromptLocked
        {
            get => promptLocked;
            set
            {
                if (value == promptLocked)
                {
                    return;
                }

                if (value)
                {
                    LockPromptInternal();
                }
                else
                {
                    UnlockPromptInternal();
                }
            }
        }

        private void Reset()
        {
            if (targetHumanoidAnimator == null)
            {
                targetHumanoidAnimator = GetComponent<Animator>();
            }
        }

        private void Awake()
        {
            motionPlayer = new SingleTargetMotionPlayer();
            promptDraft = ResolveInitialPrompt();
            SyncGenerationDurationFromCurrentSettings();
        }

        private void OnEnable()
        {
            EnsurePromptDraftInitialized();
            _ = StartRuntimeAsync();
        }

        private void OnDisable()
        {
            _ = StopRuntimeAsync();
        }

        private void OnDestroy()
        {
            motionPlayer?.Stop();
        }

        private void Update()
        {
            if (motionPlayer == null)
            {
                return;
            }

            motionPlayer.Update(
                Time.deltaTime,
                modelName,
                targetHumanoidAnimator,
                allowPartialJoints,
                driveFootIkTargets,
                leftFootIkTargetName,
                rightFootIkTargetName,
                verboseLogging,
                out GeneratedSegment startedSegment,
                out string playbackError);

            if (!string.IsNullOrWhiteSpace(playbackError))
            {
                UpdateStatus($"Playback failed: {playbackError}");
            }

            if (startedSegment == null)
            {
                if (drawDebugSkeleton)
                {
                    motionPlayer.DrawDebugSkeleton(debugSkeletonBoneColor, debugSkeletonJointColor, debugJointMarkerSize);
                }

                return;
            }

            if (loopHint)
            {
                SetNextConstraintPoses(startedSegment.ConstraintOverlapPoses);
            }
            else
            {
                ClearNextConstraintPoses();
            }

            UpdateStatus($"Playing segment {startedSegment.Index}.");

            if (drawDebugSkeleton)
            {
                motionPlayer.DrawDebugSkeleton(debugSkeletonBoneColor, debugSkeletonJointColor, debugJointMarkerSize);
            }
        }

        public void SetPrompt(string prompt)
        {
            SetPromptInternal(prompt);
        }

        public void SetAnimationPrompt(string prompt)
        {
            SetPromptInternal(prompt);
        }

        public string GetAnimationPrompt(out bool isIdle)
        {
            return GetCurrentPromptInternal(out isIdle);
        }

        public string GetCurrentPrompt(out bool isIdle)
        {
            return GetCurrentPromptInternal(out isIdle);
        }

        public void LockPrompt()
        {
            PromptLocked = true;
        }

        public void UnlockPrompt()
        {
            PromptLocked = false;
        }

        public void SetAnimationDurationSeconds(float seconds)
        {
            ApplyGenerationDurationSeconds(seconds);
        }

        public float GetAnimationDurationSeconds()
        {
            return ResolveGenerationDurationSeconds();
        }

        public void SetLeftHandConstraint(float x, float y, float z, float duration = 1f)
        {
            QueueEndEffectorConstraintInternal("LeftHand constraint", LeftHandConstraintType, "LeftHand", x, y, z, duration);
        }

        public void SetRightHandConstraint(float x, float y, float z, float duration = 1f)
        {
            QueueEndEffectorConstraintInternal("RightHand constraint", RightHandConstraintType, "RightHand", x, y, z, duration);
        }

        public void SetLeftFootConstraint(float x, float y, float z, float duration = 1f)
        {
            QueueEndEffectorConstraintInternal("LeftFoot constraint", LeftFootConstraintType, "LeftFoot", x, y, z, duration);
        }

        public void SetRightFootConstraint(float x, float y, float z, float duration = 1f)
        {
            QueueEndEffectorConstraintInternal("RightFoot constraint", RightFootConstraintType, "RightFoot", x, y, z, duration);
        }

        public void SetRoot2D(float x, float z, float duration = 1f)
        {
            QueueRoot2DConstraintInternal(x, z, duration, null);
        }

        public void SetRoot2D(float x, float z, float headingX, float headingZ, float duration = 1f)
        {
            QueueRoot2DConstraintInternal(x, z, duration, NormalizeHeading(new Vector2(headingX, headingZ)));
        }

        public void SetRoot2DLocal(float x, float z, float duration = 1f)
        {
            if (!TryCreateRoot2DLocalConstraintSample(x, z, duration, null, true, out KimodoMarkerSampleResult sample, out string error))
            {
                UpdateStatus(error);
                return;
            }

            pendingConstraintSamples.Add(sample);
            _ = RefreshUpcomingGenerationAsync(
                $"Root2D local queued at ({x:0.###}, {z:0.###}).",
                "Root2D local queued. Waiting for current generation to finish.",
                "Root2D local queued. Generating constrained segment.");
        }

        public void SetRoot2DLocal(float x, float z, float headingX, float headingZ, float duration = 1f)
        {
            if (!TryCreateRoot2DLocalConstraintSample(
                    x,
                    z,
                    duration,
                    new Vector2(headingX, headingZ),
                    false,
                    out KimodoMarkerSampleResult sample,
                    out string error))
            {
                UpdateStatus(error);
                return;
            }

            pendingConstraintSamples.Add(sample);
            _ = RefreshUpcomingGenerationAsync(
                $"Root2D local queued at ({x:0.###}, {z:0.###}).",
                "Root2D local queued. Waiting for current generation to finish.",
                "Root2D local queued. Generating constrained segment.");
        }

        public Vector3 GetPosition()
        {
            return GetCurrentPositionInternal();
        }

        public async Task ResetMotionAsync()
        {
            promptLocked = false;
            promptDraft = ResolveInitialPrompt();
            pendingConstraintSamples.Clear();
            ClearNextConstraintPoses();
            generationRequestVersion++;
            lastGenerationWaitStatusSegment = -1;
            RewindSegmentIndexAfterQueueInvalidation(motionPlayer.QueuedSegmentCount);
            motionPlayer.ClearQueue();

            if (!running || bridgeService == null || lifetimeCts == null || lifetimeCts.IsCancellationRequested)
            {
                UpdateStatus("Prompt reset.");
                return;
            }

            if (generationInFlight)
            {
                UpdateStatus("Prompt reset. Waiting for current generation to finish.");
                return;
            }

            UpdateStatus("Prompt reset. Generating fresh segment.");
            await GenerateNextSegmentAsync(lifetimeCts.Token);
        }

        private async Task StartRuntimeAsync()
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
                    Debug.LogError($"[KimodoRuntimeMotionDriver] {error}", this);
                    return;
                }

                lifetimeCts?.Cancel();
                lifetimeCts?.Dispose();
                lifetimeCts = new CancellationTokenSource();

                segmentIndex = 0;
                generationInFlight = false;
                generationRequestVersion = 0;
                lastGenerationWaitStatusSegment = -1;
                pendingConstraintSamples.Clear();
                ClearNextConstraintPoses();
                motionPlayer.Stop();
                motionPlayer.ResetCompletionState();
                motionPlayer.ClearQueue();

                bridgeService?.Dispose();
                bridgeService = new KimodoBridgeService(BuildBridgeRuntimeSettings());

                UpdateStatus("Starting Kimodo bridge...");
                await bridgeService.StartAsync(OnProgress, lifetimeCts.Token);

                running = true;
                schedulerTask = RunSchedulerLoopAsync(lifetimeCts.Token);
                UpdateStatus("Bridge ready.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                UpdateStatus($"Start failed: {ex.Message}");
                await StopRuntimeAsync();
            }
            finally
            {
                startRequested = false;
            }
        }

        private async Task StopRuntimeAsync()
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
                    Debug.LogWarning($"[KimodoRuntimeMotionDriver] Scheduler stop observed exception: {ex.Message}", this);
                }
            }

            if (bridgeService != null)
            {
                try
                {
                    await bridgeService.DetachAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[KimodoRuntimeMotionDriver] Detach bridge failed: {ex.Message}", this);
                }

                bridgeService.Dispose();
                bridgeService = null;
            }

            cts?.Dispose();
            generationInFlight = false;
            lastGenerationWaitStatusSegment = -1;
            pendingConstraintSamples.Clear();
            ClearNextConstraintPoses();
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
                Debug.LogException(ex, this);
                UpdateStatus($"Scheduler failed: {ex.Message}");
                running = false;
            }
        }

        private void MaybeQueueNextGeneration(CancellationToken token)
        {
            if (!running || generationInFlight || bridgeService == null)
            {
                return;
            }

            if (motionPlayer.QueuedSegmentCount > 0)
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
            if (generationInFlight || bridgeService == null)
            {
                return;
            }

            generationInFlight = true;
            int requestVersion = generationRequestVersion;
            int requestSegmentIndex = segmentIndex;
            try
            {
                string prompt = ResolvePrompt();
                string constraintsJson = BuildNextConstraintsJson();
                var request = new KimodoGenerationRequestDto
                {
                    prompt = prompt,
                    duration = ResolveGenerationDurationSeconds(),
                    seed = randomSeed ? (int?)null : fixedSeed,
                    steps = Mathf.Max(1, diffusionSteps),
                    constraints_json = constraintsJson,
                    boundary_pose_json = string.Empty,
                    loop_hint = loopHint,
                    segment_index = requestSegmentIndex,
                    transition_duration = 0f
                };

                OnProgress($"Generating segment {requestSegmentIndex}...");
                KimodoBridgeGenerationResult bridgeResult = await bridgeService.GenerateAsync(request, OnProgress, token);

                KimodoRawMotionMetadata metadata = await Task.Run(() =>
                {
                    var generationResult = new KimodoGenerationResultDto
                    {
                        motionJsonCompact = bridgeResult?.MotionJsonCompact,
                        motionData = bridgeResult?.MotionData,
                        motionFormat = bridgeResult?.MotionFormat,
                        rawStatus = bridgeResult?.RawStatus,
                        message = bridgeResult?.Message
                    };

                    if (!KimodoRawMotionUtility.TryAnalyzeGenerationResult(
                            generationResult,
                            modelName,
                            out KimodoRawMotionMetadata parsedMetadata,
                            out string parseError,
                            FullBodyConstraintType,
                            0.0,
                            allowPartialJoints))
                    {
                        throw new InvalidOperationException(parseError);
                    }

                    return parsedMetadata;
                }, token);

                int effectiveLastFrameIndex = ResolveEffectiveLastFrameIndex(metadata.Motion);
                if (!metadata.Motion.TryReadUnityRootPosition(effectiveLastFrameIndex, out Vector3 effectiveLastRootPosition))
                {
                    throw new InvalidOperationException(
                        $"Failed to read effective tail root position for frame {effectiveLastFrameIndex}.");
                }

                if (!KimodoRawMotionUtility.TryExtractMarkerSample(
                        metadata.Motion,
                        modelName,
                        effectiveLastFrameIndex,
                        out KimodoMarkerSampleResult effectiveTailPose,
                        out string tailError,
                        FullBodyConstraintType,
                        0.0,
                        allowPartialJoints))
                {
                    throw new InvalidOperationException(tailError);
                }

                if (requestVersion != generationRequestVersion || token.IsCancellationRequested)
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"[KimodoRuntimeMotionDriver] Discard stale segment {requestSegmentIndex} generation result.", this);
                    }

                    return;
                }

                List<KimodoMarkerSampleResult> constraintOverlapPoses =
                    BuildConstraintOverlapPoses(metadata.Motion, effectiveLastFrameIndex);
                if (constraintOverlapPoses.Count == 0)
                {
                    KimodoMarkerSampleResult fallbackPose = effectiveTailPose.Clone();
                    fallbackPose.sampleTime = 0.0;
                    constraintOverlapPoses.Add(fallbackPose);
                }

                motionPlayer.Enqueue(new GeneratedSegment
                {
                    Index = requestSegmentIndex,
                    PromptText = prompt,
                    Motion = metadata.Motion,
                    ConstraintOverlapPoses = constraintOverlapPoses,
                    FirstRootPosition = metadata.FirstRootPosition,
                    LastRootPosition = effectiveLastRootPosition,
                    WorldAccumulatedOffset = Vector3.zero,
                    EffectiveLastFrameIndex = effectiveLastFrameIndex,
                    EffectiveLastFrameTimeSeconds = metadata.Motion.FrameRate > 0f
                        ? effectiveLastFrameIndex / metadata.Motion.FrameRate
                        : metadata.Motion.LastFrameTimeSeconds
                }, verboseLogging);

                pendingConstraintSamples.Clear();
                if (!promptLocked)
                {
                    promptDraft = ResolveInitialPrompt();
                }

                segmentIndex = requestSegmentIndex + 1;
                UpdateStatus($"Segment {requestSegmentIndex} ready.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                UpdateStatus($"Generate failed: {ex.Message}");
            }
            finally
            {
                generationInFlight = false;
            }
        }

        private List<KimodoMarkerSampleResult> BuildActiveGenerationConstraints()
        {
            var samples = new List<KimodoMarkerSampleResult>();

            if (loopHint && nextConstraintPoses.Count > 0)
            {
                for (int i = 0; i < nextConstraintPoses.Count; i++)
                {
                    KimodoMarkerSampleResult source = nextConstraintPoses[i];
                    if (source == null)
                    {
                        continue;
                    }

                    KimodoMarkerSampleResult sample = source.Clone();
                    sample.constraintType = FullBodyConstraintType;
                    sample.sampleTime = source.sampleTime;
                    sample.kimodoRootPosition = new Vector3(0f, sample.kimodoRootPosition.y, 0f);
                    sample.unityRootPos = sample.kimodoRootPosition;
                    samples.Add(sample);
                }
            }

            for (int i = 0; i < pendingConstraintSamples.Count; i++)
            {
                KimodoMarkerSampleResult pending = pendingConstraintSamples[i];
                if (pending == null)
                {
                    continue;
                }

                KimodoMarkerSampleResult clone = pending.Clone();
                clone.sampleTime = ClampConstraintTime((float)clone.sampleTime);
                samples.Add(clone);
            }

            samples.Sort((a, b) => a.sampleTime.CompareTo(b.sampleTime));
            return samples;
        }

        private string BuildNextConstraintsJson()
        {
            List<KimodoMarkerSampleResult> activeConstraints = BuildActiveGenerationConstraints();
            if (activeConstraints.Count == 0)
            {
                return string.Empty;
            }

            constraintJsonScratch.Clear();
            constraintJsonScratch.AddRange(activeConstraints);
            return KimodoConstraintJsonExporter.ToConstraintsJson(
                constraintJsonScratch,
                0.0,
                ResolveGenerationDurationSeconds());
        }

        private async Task RefreshUpcomingGenerationAsync(
            string inactiveStatus,
            string waitingStatus,
            string generatingStatus)
        {
            generationRequestVersion++;
            lastGenerationWaitStatusSegment = -1;
            int clearedQueuedSegmentCount = motionPlayer.QueuedSegmentCount;
            motionPlayer.ClearQueue();
            RewindSegmentIndexAfterQueueInvalidation(clearedQueuedSegmentCount);

            if (!running || bridgeService == null || lifetimeCts == null || lifetimeCts.IsCancellationRequested)
            {
                UpdateStatus(inactiveStatus);
                return;
            }

            if (generationInFlight)
            {
                UpdateStatus(waitingStatus);
                return;
            }

            UpdateStatus(generatingStatus);
            await GenerateNextSegmentAsync(lifetimeCts.Token);
        }

        private void RewindSegmentIndexAfterQueueInvalidation(int clearedQueuedSegmentCount)
        {
            if (clearedQueuedSegmentCount <= 0)
            {
                return;
            }

            int minSegmentIndex = Mathf.Max(0, motionPlayer.LastCompletedSegmentIndex + 1);
            segmentIndex = Mathf.Max(minSegmentIndex, segmentIndex - clearedQueuedSegmentCount);
        }

        private string SetPromptInternal(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return GetCurrentPromptInternal(out bool _);
            }

            promptDraft = prompt.Trim();
            _ = RefreshUpcomingGenerationAsync(
                $"Prompt updated: {promptDraft}",
                $"Prompt updated: {promptDraft}. Waiting for current generation to finish.",
                $"Prompt updated: {promptDraft}. Generating fresh segment.");
            return promptDraft;
        }

        private string LockPromptInternal()
        {
            string promptToLock = motionPlayer.CurrentPromptText;
            if (string.IsNullOrWhiteSpace(promptToLock))
            {
                promptToLock = ResolvePrompt();
            }

            promptDraft = string.IsNullOrWhiteSpace(promptToLock) ? ResolveInitialPrompt() : promptToLock.Trim();
            promptLocked = true;
            _ = RefreshUpcomingGenerationAsync(
                $"Prompt locked: {promptDraft}",
                $"Prompt locked: {promptDraft}. Waiting for current generation to finish.",
                $"Prompt locked: {promptDraft}. Generating fresh segment.");
            return promptDraft;
        }

        private string UnlockPromptInternal()
        {
            promptLocked = false;
            promptDraft = ResolveInitialPrompt();
            _ = RefreshUpcomingGenerationAsync(
                "Prompt unlocked. Using idle.",
                "Prompt unlocked. Waiting for current generation to finish.",
                "Prompt unlocked. Generating idle segment.");
            return promptDraft;
        }

        private string QueueEndEffectorConstraintInternal(
            string label,
            string constraintType,
            string jointName,
            float x,
            float y,
            float z,
            float durationSeconds)
        {
            if (!TryCreateShiftedConstraintSample(
                    constraintType,
                    jointName,
                    new Vector3(x, y, z),
                    durationSeconds,
                    out KimodoMarkerSampleResult sample,
                    out string error))
            {
                UpdateStatus(error);
                return error;
            }

            pendingConstraintSamples.Add(sample);
            _ = RefreshUpcomingGenerationAsync(
                $"{label} queued at {FormatVector3(new Vector3(x, y, z))}.",
                $"{label} queued. Waiting for current generation to finish.",
                $"{label} queued. Generating constrained segment.");
            return $"{label} queued at {FormatVector3(new Vector3(x, y, z))}.";
        }

        private string QueueRoot2DConstraintInternal(float x, float z, float durationSeconds, Vector2? heading)
        {
            if (!TryCreateRoot2DConstraintSample(x, z, durationSeconds, heading, out KimodoMarkerSampleResult sample, out string error))
            {
                UpdateStatus(error);
                return error;
            }

            pendingConstraintSamples.Add(sample);
            _ = RefreshUpcomingGenerationAsync(
                $"Root2D queued at ({x:0.###}, {z:0.###}).",
                "Root2D queued. Waiting for current generation to finish.",
                "Root2D queued. Generating constrained segment.");
            return $"Root2D queued at ({x:0.###}, {z:0.###}).";
        }

        private string GetCurrentPromptInternal(out bool isIdle)
        {
            string currentPrompt = motionPlayer.CurrentPromptText;
            string resolved = string.IsNullOrWhiteSpace(currentPrompt)
                ? ResolvePrompt()
                : currentPrompt.Trim();
            isIdle = string.Equals(resolved, ResolveInitialPrompt(), StringComparison.OrdinalIgnoreCase);
            return resolved;
        }

        private Vector3 GetCurrentPositionInternal()
        {
            Transform hips = targetHumanoidAnimator != null
                ? targetHumanoidAnimator.GetBoneTransform(HumanBodyBones.Hips)
                : null;
            if (hips != null)
            {
                return hips.position;
            }

            if (motionPlayer.HasCurrentSegment)
            {
                return motionPlayer.CurrentRootPosition;
            }

            return targetHumanoidAnimator != null ? targetHumanoidAnimator.transform.position : transform.position;
        }

        private float ClampConstraintTime(float durationSeconds)
        {
            return Mathf.Clamp(durationSeconds, 0f, ResolveGenerationDurationSeconds());
        }

        private bool TryCreateShiftedConstraintSample(
            string constraintType,
            string jointName,
            Vector3 targetWorldPosition,
            float durationSeconds,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            sample = null;
            if (!TryCaptureCurrentPoseConstraint(constraintType, durationSeconds, out sample, out error))
            {
                return false;
            }

            Transform constraintRoot = motionPlayer.ConstraintSkeletonRoot;
            Transform targetJoint = KimodoRetargetAvatarUtility.FindTransformByName(constraintRoot, jointName);
            if (targetJoint == null)
            {
                error = $"Cannot find joint '{jointName}' under constraint skeleton root.";
                sample = null;
                return false;
            }

            Vector3 offset = targetWorldPosition - targetJoint.position;
            sample.kimodoRootPosition += offset;
            sample.unityRootPos += offset;
            sample.constraintType = constraintType;
            return true;
        }

        private bool TryCreateRoot2DConstraintSample(
            float x,
            float z,
            float durationSeconds,
            Vector2? heading,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            if (!TryCaptureCurrentPoseConstraint(Root2DConstraintType, durationSeconds, out sample, out error))
            {
                return false;
            }

            Vector3 offset = new Vector3(x - sample.kimodoRootPosition.x, 0f, z - sample.kimodoRootPosition.z);
            sample.kimodoRootPosition += offset;
            sample.unityRootPos += offset;
            sample.constraintType = Root2DConstraintType;
            sample.localAxisAngles = new List<Vector3>();
            sample.sampledJointIndices = new List<int>();
            if (heading.HasValue)
            {
                sample.hasRootHeading = true;
                sample.rootHeading = NormalizeHeading(heading.Value);
            }

            return true;
        }

        private bool TryCreateRoot2DLocalConstraintSample(
            float localX,
            float localZ,
            float durationSeconds,
            Vector2? localHeading,
            bool autoFaceTarget,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            if (!TryCaptureCurrentPoseConstraint(Root2DConstraintType, durationSeconds, out sample, out error))
            {
                return false;
            }

            Vector2 origin2D = new Vector2(sample.kimodoRootPosition.x, sample.kimodoRootPosition.z);
            Vector2 basisForward = sample.hasRootHeading
                ? NormalizeHeading(sample.rootHeading)
                : Vector2.up;
            Vector2 basisRight = new Vector2(basisForward.y, -basisForward.x);

            Vector2 worldOffset2D = basisRight * localX + basisForward * localZ;
            Vector2 target2D = origin2D + worldOffset2D;

            Vector3 offset = new Vector3(target2D.x - sample.kimodoRootPosition.x, 0f, target2D.y - sample.kimodoRootPosition.z);
            sample.kimodoRootPosition += offset;
            sample.unityRootPos += offset;
            sample.constraintType = Root2DConstraintType;
            sample.localAxisAngles = new List<Vector3>();
            sample.sampledJointIndices = new List<int>();
            sample.hasRootHeading = true;

            if (autoFaceTarget)
            {
                sample.rootHeading = worldOffset2D.sqrMagnitude > 1e-8f
                    ? NormalizeHeading(worldOffset2D)
                    : basisForward;
            }
            else if (localHeading.HasValue)
            {
                Vector2 normalizedLocalHeading = NormalizeHeading(localHeading.Value);
                Vector2 worldHeading = basisRight * normalizedLocalHeading.x + basisForward * normalizedLocalHeading.y;
                sample.rootHeading = NormalizeHeading(worldHeading);
            }
            else
            {
                sample.rootHeading = basisForward;
            }

            return true;
        }

        private bool TryCaptureCurrentPoseConstraint(
            string constraintType,
            float durationSeconds,
            out KimodoMarkerSampleResult sample,
            out string error)
        {
            if (!motionPlayer.EnsureConstraintSkeletonReady(modelName, out error))
            {
                sample = null;
                return false;
            }

            return KimodoMarkerSamplingUtility.TrySampleMarkerFromProfileSkeletonRaw(
                null,
                motionPlayer.ConstraintSkeletonRoot,
                modelName,
                ClampConstraintTime(durationSeconds),
                constraintType,
                null,
                null,
                null,
                out sample,
                out error);
        }

        private static Vector2 NormalizeHeading(Vector2 heading)
        {
            if (heading.sqrMagnitude <= 1e-8f)
            {
                return Vector2.right;
            }

            heading.Normalize();
            return heading;
        }

        private void OnProgress(string message)
        {
            if (verboseLogging && !string.IsNullOrWhiteSpace(message))
            {
                Debug.Log($"[KimodoRuntimeMotionDriver] {message}", this);
            }

            UpdateStatus(message);
        }

        private void UpdateStatus(string message)
        {
            statusMessage = string.IsNullOrWhiteSpace(message) ? " " : message;
        }

        private string ResolvePrompt()
        {
            string prompt = promptDraft;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = defaultPrompt;
            }

            return string.IsNullOrWhiteSpace(prompt) ? IdlePrompt : prompt.Trim();
        }

        private string ResolveInitialPrompt()
        {
            string prompt = defaultPrompt;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = IdlePrompt;
            }

            return string.IsNullOrWhiteSpace(prompt) ? IdlePrompt : prompt.Trim();
        }

        private void EnsurePromptDraftInitialized()
        {
            if (string.IsNullOrWhiteSpace(promptDraft))
            {
                promptDraft = ResolveInitialPrompt();
            }
        }

        private void SyncGenerationDurationFromCurrentSettings()
        {
            ApplyGenerationDurationSeconds(ResolveGenerationDurationSeconds());
        }

        private float ResolveGenerationDurationSeconds()
        {
            float frameDuration = generationFrames / KimodoPlayableClip.FIXED_FRAME_RATE;
            return Mathf.Clamp(
                Mathf.Max(segmentIntervalSeconds, frameDuration),
                MinGenerationDurationSeconds,
                MaxGenerationDurationSeconds);
        }

        private void ApplyGenerationDurationSeconds(float durationSeconds)
        {
            float clamped = Mathf.Clamp(durationSeconds, MinGenerationDurationSeconds, MaxGenerationDurationSeconds);
            segmentIntervalSeconds = clamped;
            generationFrames = Mathf.Max(1, Mathf.RoundToInt(clamped * KimodoPlayableClip.FIXED_FRAME_RATE));
        }

        private int ResolveEffectiveLastFrameIndex(KimodoRawMotionData motion)
        {
            if (motion == null || motion.FrameCount <= 1)
            {
                return 0;
            }

            int lastFrameIndex = motion.FrameCount - 1;
            if (!trimSegmentTail)
            {
                return lastFrameIndex;
            }

            float trimPercent = Mathf.Clamp(segmentTailTrimPercent, 0.05f, 0.2f);
            int trimmedFrameCount = Mathf.FloorToInt(lastFrameIndex * trimPercent);
            return Mathf.Clamp(lastFrameIndex - trimmedFrameCount, 1, lastFrameIndex);
        }

        private List<KimodoMarkerSampleResult> BuildConstraintOverlapPoses(
            KimodoRawMotionData motion,
            int effectiveLastFrameIndex)
        {
            int sampleCount = Mathf.Clamp(overlapConstraintSamples, 1, MaxOverlapConstraintSamples);
            var samples = new List<KimodoMarkerSampleResult>(sampleCount);
            if (motion == null)
            {
                return samples;
            }

            float frameRate = motion.FrameRate > 1e-6f ? motion.FrameRate : KimodoPlayableClip.FIXED_FRAME_RATE;
            float constraintFrameRate = KimodoPlayableClip.FIXED_FRAME_RATE > 1e-6f
                ? KimodoPlayableClip.FIXED_FRAME_RATE
                : frameRate;
            int lastSourceFrameIndex = int.MinValue;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                int reverseOrdinal = 1 << sampleIndex;
                int sourceFrameIndex = Mathf.Clamp(
                    effectiveLastFrameIndex - (reverseOrdinal - 1),
                    0,
                    effectiveLastFrameIndex);
                if (sourceFrameIndex == lastSourceFrameIndex)
                {
                    continue;
                }

                double sampleTime = (reverseOrdinal - 1) / Mathf.Max(1e-6f, constraintFrameRate);
                if (!KimodoRawMotionUtility.TryExtractMarkerSample(
                        motion,
                        modelName,
                        sourceFrameIndex,
                        out KimodoMarkerSampleResult sample,
                        out string sampleError,
                        FullBodyConstraintType,
                        sampleTime,
                        allowPartialJoints))
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning(
                            $"[KimodoRuntimeMotionDriver] Failed to extract overlap sample {sampleIndex} at frame {sourceFrameIndex}: {sampleError}",
                            this);
                    }

                    continue;
                }

                lastSourceFrameIndex = sourceFrameIndex;
                samples.Add(sample);
            }

            return samples;
        }

        private void ClearNextConstraintPoses()
        {
            nextConstraintPoses.Clear();
            constraintJsonScratch.Clear();
        }

        private void SetNextConstraintPoses(IReadOnlyList<KimodoMarkerSampleResult> poses)
        {
            nextConstraintPoses.Clear();
            if (poses == null)
            {
                return;
            }

            for (int i = 0; i < poses.Count; i++)
            {
                KimodoMarkerSampleResult pose = poses[i];
                if (pose != null)
                {
                    nextConstraintPoses.Add(pose);
                }
            }
        }

        private BridgeRuntimeSettings BuildBridgeRuntimeSettings()
        {
            string resolvedRuntimeRoot = EnsureRuntimeRootReady();
            string launcherPath = BridgeLauncherResolver.ResolveStartScript(resolvedRuntimeRoot);
            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                throw new FileNotFoundException($"Cannot resolve bridge launcher under '{resolvedRuntimeRoot}'.");
            }

            return BridgeRuntimeSettingsFactory.Create(
                runtimeRoot: resolvedRuntimeRoot,
                launcherPath: launcherPath,
                modelName: modelName,
                highVram: highVram,
                forceSetup: forceSetup,
                modelsRoot: string.IsNullOrWhiteSpace(modelsRoot) ? null : Path.GetFullPath(modelsRoot),
                startupTimeoutMs: Mathf.Max(
                    BridgeRuntimeSettings.DefaultStartupTimeoutMs,
                    Mathf.RoundToInt(Mathf.Max(1f, startupTimeoutMinutes) * 60f * 1000f)));
        }

        private bool ValidateConfiguration(out string error)
        {
            if (targetHumanoidAnimator == null)
            {
                error = "Target humanoid animator is not assigned.";
                return false;
            }

            string resolvedRuntimeRoot = EnsureRuntimeRootReady();
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

        private string EnsureRuntimeRootReady()
        {
            return KimodoRuntimeBootstrapUtility.EnsureRuntimeRootForCurrentMode(ResolveRuntimeRoot());
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
        }

        private sealed class GeneratedSegment
        {
            public int Index;
            public string PromptText;
            public KimodoRawMotionData Motion;
            public List<KimodoMarkerSampleResult> ConstraintOverlapPoses;
            public Vector3 FirstRootPosition;
            public Vector3 LastRootPosition;
            public Vector3 WorldAccumulatedOffset;
            public int EffectiveLastFrameIndex;
            public float EffectiveLastFrameTimeSeconds;
        }

        private sealed class SingleTargetMotionPlayer
        {
            private readonly Queue<GeneratedSegment> queuedSegments = new Queue<GeneratedSegment>();
            private readonly object queueGate = new object();

            private KimodoRawMotionPlaybackBinding sourceBinding;
            private SkeletonCache sourceCache;
            private string sourceCacheModelName;
            private Transform sourceRootJoint;
            private Vector3 currentSegmentRootBaseline;
            private Vector3 lastCompletedWorldOffset;
            private GeneratedSegment currentSegment;
            private TargetRetargetState targetState;
            private float timeSeconds;
            private bool playing;

            private sealed class TargetRetargetState : IDisposable
            {
                public Animator Animator;
                public Avatar Avatar;
                public HumanPoseHandler PoseHandler;
                public Transform LeftFootBone;
                public Transform RightFootBone;
                public Transform LeftFootIkTarget;
                public Transform RightFootIkTarget;
                public Vector3 LeftFootTargetBaselinePosition;
                public Quaternion LeftFootTargetBaselineRotation;
                public Vector3 RightFootTargetBaselinePosition;
                public Quaternion RightFootTargetBaselineRotation;
                public Vector3 SourceLeftFootBaselineWorldPosition;
                public Quaternion SourceLeftFootBaselineWorldRotation;
                public Vector3 SourceRightFootBaselineWorldPosition;
                public Quaternion SourceRightFootBaselineWorldRotation;
                public bool LeftFootIkInitialized;
                public bool RightFootIkInitialized;
                public bool AnimatorWasEnabled;
                public bool AnimatorDisabledForRetarget;

                public void Dispose()
                {
                    if (Animator != null && AnimatorDisabledForRetarget)
                    {
                        Animator.enabled = AnimatorWasEnabled;
                    }

                    Animator = null;
                    Avatar = null;
                    PoseHandler = null;
                    AnimatorDisabledForRetarget = false;
                    AnimatorWasEnabled = false;
                }
            }

            public bool HasCurrentSegment => currentSegment != null;
            public string CurrentPromptText => currentSegment != null ? currentSegment.PromptText : string.Empty;
            public Vector3 CurrentRootPosition => sourceRootJoint != null ? sourceRootJoint.position : Vector3.zero;
            public Transform ConstraintSkeletonRoot => sourceCache != null ? sourceCache.skeletonRoot : null;
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

            public void Enqueue(GeneratedSegment segment, bool verboseLogging)
            {
                if (segment == null)
                {
                    return;
                }

                lock (queueGate)
                {
                    queuedSegments.Enqueue(segment);
                    if (verboseLogging)
                    {
                        Debug.Log($"[KimodoRuntimeMotionDriver] Enqueue segment {segment.Index}. queueCount={queuedSegments.Count}");
                    }
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
                lastCompletedWorldOffset = Vector3.zero;
            }

            public void Update(
                float deltaTime,
                string modelName,
                Animator targetAnimator,
                bool allowPartialJoints,
                bool driveFootIkTargets,
                string leftFootIkTargetName,
                string rightFootIkTargetName,
                bool verboseLogging,
                out GeneratedSegment startedSegment,
                out string error)
            {
                startedSegment = null;
                error = string.Empty;

                if (playing && sourceBinding != null)
                {
                    AdvanceCurrentMotion(deltaTime, out error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return;
                    }
                }

                if (!playing && TryDequeue(out GeneratedSegment next))
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"[KimodoRuntimeMotionDriver] Attempting to play dequeued segment {next.Index}.");
                    }

                    if (!Play(
                            next,
                            modelName,
                            targetAnimator,
                            allowPartialJoints,
                            driveFootIkTargets,
                            leftFootIkTargetName,
                            rightFootIkTargetName,
                            out error,
                            verboseLogging))
                    {
                        return;
                    }

                    startedSegment = next;
                }
            }

            public void Stop()
            {
                StopActiveMotion();
                DisposeRetargetCache();
            }

            public void DrawDebugSkeleton(Color boneColor, Color jointColor, float jointMarkerSize)
            {
                Transform[] joints = sourceBinding != null ? sourceBinding.joints : null;
                KimodoRawMotionData motion = sourceBinding != null ? sourceBinding.motion : null;
                if (joints == null || motion == null)
                {
                    return;
                }

                int count = Mathf.Min(joints.Length, motion.JointCount);
                int[] parents = motion.jointParents;
                for (int i = 0; i < count; i++)
                {
                    Transform joint = joints[i];
                    if (joint == null)
                    {
                        continue;
                    }

                    Vector3 position = joint.position;
                    DrawJointMarker(position, jointMarkerSize, jointColor);

                    if (parents == null || i >= parents.Length)
                    {
                        continue;
                    }

                    int parentIndex = parents[i];
                    if (parentIndex < 0 || parentIndex >= count)
                    {
                        continue;
                    }

                    Transform parent = joints[parentIndex];
                    if (parent == null)
                    {
                        continue;
                    }

                    Debug.DrawLine(parent.position, position, boneColor, 0f, false);
                }
            }

            public bool EnsureConstraintSkeletonReady(string modelName, out string error)
            {
                error = string.Empty;
                if (sourceCache != null && string.Equals(sourceCacheModelName, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out Avatar sourceAvatar, out error))
                {
                    return false;
                }

                DisposeSourceRetargetCache();
                if (!KimodoRetargetAvatarUtility.TryBuildSkeletonCache(
                        sourceAvatar,
                        "KimodoRuntimeMotionDriver_SourceConstraint",
                        out sourceCache,
                        out error))
                {
                    return false;
                }

                sourceCacheModelName = modelName;
                return true;
            }

            private bool Play(
                GeneratedSegment segment,
                string modelName,
                Animator targetAnimator,
                bool allowPartialJoints,
                bool driveFootIkTargets,
                string leftFootIkTargetName,
                string rightFootIkTargetName,
                out string error,
                bool verboseLogging)
            {
                StopActiveMotion();
                if (!TryCreateDirectRetargetBinding(
                        segment.Motion,
                        modelName,
                        targetAnimator,
                        allowPartialJoints,
                        driveFootIkTargets,
                        leftFootIkTargetName,
                        rightFootIkTargetName,
                        out error))
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning($"[KimodoRuntimeMotionDriver] Play segment {segment?.Index ?? -1} failed while creating retarget binding: {error}");
                    }

                    StopActiveMotion();
                    return false;
                }

                currentSegment = segment;
                currentSegment.WorldAccumulatedOffset = ResolveNextWorldOffset(segment.FirstRootPosition);
                currentSegmentRootBaseline = segment.FirstRootPosition;
                ResetTargetFootIkBaselines();
                timeSeconds = 0f;
                if (!TryApplyFrame(0, out error))
                {
                    if (verboseLogging)
                    {
                        Debug.LogWarning($"[KimodoRuntimeMotionDriver] Play segment {segment?.Index ?? -1} failed while applying frame 0: {error}");
                    }

                    StopActiveMotion();
                    return false;
                }

                playing = true;
                return true;
            }

            private void AdvanceCurrentMotion(float deltaTime, out string error)
            {
                error = string.Empty;
                if (!playing || sourceBinding == null)
                {
                    return;
                }

                timeSeconds += Mathf.Max(0f, deltaTime);
                bool reachedEnd = false;
                float segmentEndTime = currentSegment != null
                    ? Mathf.Max(0f, currentSegment.EffectiveLastFrameTimeSeconds)
                    : (sourceBinding.motion != null ? sourceBinding.motion.LastFrameTimeSeconds : 0f);
                if (sourceBinding.motion != null && timeSeconds >= segmentEndTime)
                {
                    timeSeconds = segmentEndTime;
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
                    Vector3 completedDelta = currentSegment.LastRootPosition - currentSegment.FirstRootPosition;
                    lastCompletedWorldOffset = currentSegment.WorldAccumulatedOffset + new Vector3(
                        completedDelta.x,
                        0f,
                        completedDelta.z);
                }
            }

            private void StopActiveMotion()
            {
                sourceBinding = null;
                sourceRootJoint = null;
                currentSegment = null;
                currentSegmentRootBaseline = Vector3.zero;
                timeSeconds = 0f;
                playing = false;
            }

            private void DisposeRetargetCache()
            {
                DisposeSourceRetargetCache();
                DisposeTargetState();
            }

            private void DisposeSourceRetargetCache()
            {
                sourceBinding = null;
                sourceCache?.Dispose();
                sourceCache = null;
                sourceCacheModelName = null;
            }

            private void DisposeTargetState()
            {
                targetState?.Dispose();
                targetState = null;
            }

            private Vector3 ResolveNextWorldOffset(Vector3 nextSegmentFirstRootPosition)
            {
                return lastCompletedWorldOffset;
            }

            private bool TryCreateDirectRetargetBinding(
                KimodoRawMotionData motion,
                string modelName,
                Animator targetAnimator,
                bool allowPartialJoints,
                bool driveFootIkTargets,
                string leftFootIkTargetName,
                string rightFootIkTargetName,
                out string error)
            {
                error = string.Empty;
                if (!TrySyncTargetState(
                        targetAnimator,
                        driveFootIkTargets,
                        leftFootIkTargetName,
                        rightFootIkTargetName,
                        out bool hasTarget,
                        out error))
                {
                    return false;
                }

                if (!hasTarget)
                {
                    sourceBinding = null;
                    return true;
                }

                if (!KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out Avatar sourceAvatar, out error))
                {
                    return false;
                }

                if (sourceCache == null || !string.Equals(sourceCacheModelName, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    DisposeSourceRetargetCache();
                    if (!KimodoRetargetAvatarUtility.TryBuildSkeletonCache(
                            sourceAvatar,
                            "KimodoRuntimeMotionDriver_SourceRetarget",
                            out sourceCache,
                            out error))
                    {
                        return false;
                    }

                    sourceCacheModelName = modelName;
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

                sourceRootJoint = sourceBinding.joints != null && sourceBinding.joints.Length > 0
                    ? sourceBinding.joints[0]
                    : null;

                return true;
            }

            private bool TryApplyFrame(int frameIndex, out string error)
            {
                if (sourceBinding != null && !KimodoRawMotionUtility.TryApplyFrame(sourceBinding, frameIndex, out error, applyRootPosition: false))
                {
                    return false;
                }

                if (!TryApplySourceDeltaRoot(frameIndex, out error))
                {
                    return false;
                }

                return TryApplyHumanoidPose(out error);
            }

            private bool TryApplyTime(float sampleTimeSeconds, out string error)
            {
                if (sourceBinding != null &&
                    !KimodoRawMotionUtility.TryApplyTime(sourceBinding, sampleTimeSeconds, out error, loop: false, applyRootPosition: false))
                {
                    return false;
                }

                if (!TryApplySourceDeltaRoot(sampleTimeSeconds, out error))
                {
                    return false;
                }

                return TryApplyHumanoidPose(out error);
            }

            private bool TryApplySourceDeltaRoot(int frameIndex, out string error)
            {
                error = string.Empty;
                if (sourceBinding?.joints == null || sourceBinding.joints.Length == 0 || currentSegment == null)
                {
                    return true;
                }

                if (!currentSegment.Motion.TryReadUnityRootPosition(frameIndex, out Vector3 rootPosition))
                {
                    error = $"Failed to read source root position for frame {frameIndex}.";
                    return false;
                }

                Vector3 delta = rootPosition - currentSegmentRootBaseline;
                sourceBinding.joints[0].localPosition = new Vector3(
                    currentSegment.WorldAccumulatedOffset.x + delta.x,
                    rootPosition.y,
                    currentSegment.WorldAccumulatedOffset.z + delta.z);
                return true;
            }

            private bool TryApplySourceDeltaRoot(float sampleTimeSeconds, out string error)
            {
                error = string.Empty;
                if (sourceBinding?.joints == null || sourceBinding.joints.Length == 0 || currentSegment == null)
                {
                    return true;
                }

                if (!KimodoRawMotionUtility.ResolveInterpolatedRootPosition(currentSegment.Motion, sampleTimeSeconds, false, out Vector3 rootPosition))
                {
                    error = $"Failed to sample source root position at time {sampleTimeSeconds:0.###}.";
                    return false;
                }

                Vector3 delta = rootPosition - currentSegmentRootBaseline;
                sourceBinding.joints[0].localPosition = new Vector3(
                    currentSegment.WorldAccumulatedOffset.x + delta.x,
                    rootPosition.y,
                    currentSegment.WorldAccumulatedOffset.z + delta.z);
                return true;
            }

            private bool TryApplyHumanoidPose(out string error)
            {
                error = string.Empty;
                if (sourceCache == null || targetState?.PoseHandler == null)
                {
                    return true;
                }

                if (!KimodoRetargetSamplingUtility.TryCaptureMuscleSample(sourceCache, out MuscleSample sample, out error))
                {
                    return false;
                }

                HumanPose pose = sample.pose;
                BuildFootWorldPose(
                    sample,
                    out Vector3 leftFootWorldPosition,
                    out Quaternion leftFootWorldRotation,
                    out Vector3 rightFootWorldPosition,
                    out Quaternion rightFootWorldRotation);

                targetState.PoseHandler.SetHumanPose(ref pose);
                ApplyFootIkTargets(
                    targetState,
                    leftFootWorldPosition,
                    leftFootWorldRotation,
                    rightFootWorldPosition,
                    rightFootWorldRotation);
                return true;
            }

            private bool TrySyncTargetState(
                Animator animator,
                bool driveFootIkTargets,
                string leftFootIkTargetName,
                string rightFootIkTargetName,
                out bool hasTarget,
                out string error)
            {
                error = string.Empty;
                hasTarget = animator != null;

                if (animator == null)
                {
                    DisposeTargetState();
                    return true;
                }

                Avatar avatar = animator.avatar;
                if (!KimodoRetargetCoreUtility.IsValidHumanoid(avatar))
                {
                    error = "Humanoid retarget animator avatar is null, invalid, or not humanoid.";
                    return false;
                }

                bool needsNewState = targetState == null || !ReferenceEquals(targetState.Animator, animator);
                bool needsNewPoseHandler = needsNewState || targetState.PoseHandler == null || !ReferenceEquals(targetState.Avatar, avatar);
                if (needsNewState)
                {
                    DisposeTargetState();
                    targetState = new TargetRetargetState
                    {
                        Animator = animator
                    };
                }

                if (needsNewPoseHandler)
                {
                    targetState.Avatar = avatar;
                    targetState.PoseHandler = new HumanPoseHandler(avatar, animator.transform);
                }

                targetState.LeftFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                targetState.RightFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                targetState.LeftFootIkTarget = driveFootIkTargets
                    ? FindChildByNameRecursive(animator.transform, leftFootIkTargetName)
                    : null;
                targetState.RightFootIkTarget = driveFootIkTargets
                    ? FindChildByNameRecursive(animator.transform, rightFootIkTargetName)
                    : null;

                if (!targetState.AnimatorDisabledForRetarget)
                {
                    targetState.AnimatorWasEnabled = animator.enabled;
                    targetState.AnimatorDisabledForRetarget = true;
                }

                animator.enabled = false;
                return true;
            }

            private void ResetTargetFootIkBaselines()
            {
                if (targetState == null)
                {
                    return;
                }

                targetState.LeftFootIkInitialized = false;
                targetState.RightFootIkInitialized = false;
            }

            private static void BuildFootWorldPose(
                MuscleSample sample,
                out Vector3 leftFootWorldPosition,
                out Quaternion leftFootWorldRotation,
                out Vector3 rightFootWorldPosition,
                out Quaternion rightFootWorldRotation)
            {
                HumanPose pose = sample != null ? sample.pose : default;
                Vector3 rootPosition = pose.bodyPosition;
                Quaternion rootRotation = pose.bodyRotation;
                leftFootWorldPosition = rootPosition + rootRotation * (sample != null ? sample.leftFootPosition : Vector3.zero);
                leftFootWorldRotation = rootRotation * (sample != null ? sample.leftFootRotation : Quaternion.identity);
                rightFootWorldPosition = rootPosition + rootRotation * (sample != null ? sample.rightFootPosition : Vector3.zero);
                rightFootWorldRotation = rootRotation * (sample != null ? sample.rightFootRotation : Quaternion.identity);
            }

            private static void ApplyFootIkTargets(
                TargetRetargetState state,
                Vector3 leftFootWorldPosition,
                Quaternion leftFootWorldRotation,
                Vector3 rightFootWorldPosition,
                Quaternion rightFootWorldRotation)
            {
                if (state == null)
                {
                    return;
                }

                ApplyFootIkTarget(
                    state.LeftFootBone,
                    state.LeftFootIkTarget,
                    ref state.LeftFootIkInitialized,
                    ref state.LeftFootTargetBaselinePosition,
                    ref state.LeftFootTargetBaselineRotation,
                    ref state.SourceLeftFootBaselineWorldPosition,
                    ref state.SourceLeftFootBaselineWorldRotation,
                    leftFootWorldPosition,
                    leftFootWorldRotation);

                ApplyFootIkTarget(
                    state.RightFootBone,
                    state.RightFootIkTarget,
                    ref state.RightFootIkInitialized,
                    ref state.RightFootTargetBaselinePosition,
                    ref state.RightFootTargetBaselineRotation,
                    ref state.SourceRightFootBaselineWorldPosition,
                    ref state.SourceRightFootBaselineWorldRotation,
                    rightFootWorldPosition,
                    rightFootWorldRotation);
            }

            private static void ApplyFootIkTarget(
                Transform footBone,
                Transform ikTarget,
                ref bool initialized,
                ref Vector3 targetBaselinePosition,
                ref Quaternion targetBaselineRotation,
                ref Vector3 sourceBaselineWorldPosition,
                ref Quaternion sourceBaselineWorldRotation,
                Vector3 sourceCurrentWorldPosition,
                Quaternion sourceCurrentWorldRotation)
            {
                if (ikTarget == null)
                {
                    return;
                }

                if (!initialized)
                {
                    Vector3 alignedPosition = footBone != null ? footBone.position : ikTarget.position;
                    Quaternion alignedRotation = footBone != null ? footBone.rotation : ikTarget.rotation;
                    ikTarget.SetPositionAndRotation(alignedPosition, alignedRotation);
                    targetBaselinePosition = alignedPosition;
                    targetBaselineRotation = alignedRotation;
                    sourceBaselineWorldPosition = sourceCurrentWorldPosition;
                    sourceBaselineWorldRotation = sourceCurrentWorldRotation;
                    initialized = true;
                    return;
                }

                Vector3 deltaPosition = sourceCurrentWorldPosition - sourceBaselineWorldPosition;
                Quaternion deltaRotation = sourceCurrentWorldRotation * Quaternion.Inverse(sourceBaselineWorldRotation);
                ikTarget.SetPositionAndRotation(
                    targetBaselinePosition + deltaPosition,
                    deltaRotation * targetBaselineRotation);
            }

            private static Transform FindChildByNameRecursive(Transform root, string childName)
            {
                if (root == null || string.IsNullOrWhiteSpace(childName))
                {
                    return null;
                }

                if (string.Equals(root.name, childName, StringComparison.Ordinal))
                {
                    return root;
                }

                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    Transform found = FindChildByNameRecursive(child, childName);
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            }

            private static void DrawJointMarker(Vector3 position, float size, Color color)
            {
                float markerSize = Mathf.Max(0.001f, size);
                Debug.DrawLine(position + Vector3.left * markerSize, position + Vector3.right * markerSize, color, 0f, false);
                Debug.DrawLine(position + Vector3.up * markerSize, position + Vector3.down * markerSize, color, 0f, false);
                Debug.DrawLine(position + Vector3.forward * markerSize, position + Vector3.back * markerSize, color, 0f, false);
            }
        }
    }
}
