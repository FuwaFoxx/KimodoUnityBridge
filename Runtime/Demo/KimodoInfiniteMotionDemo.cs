using KimodoBridge;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace KimodoBridge
{
    public sealed class KimodoInfiniteMotionDemo : MonoBehaviour
    {
        [Header("Target")]
        public Animator targetAnimator;
        public string prompt = "a person walks forward.";
        public KimodoBackendType backendType = KimodoBackendType.Bridge;
        public KimodoRuntimeGenerationSettings runtimeSettings;

        [Header("Loop")]
        [Range(1f, 10f)]
        public float segmentDurationSeconds = 4f;
        [Range(0.25f, 2f)]
        public float prefetchLeadSeconds = 1f;
        [Range(1, 4)]
        public int maxQueueSize = 2;
        [Range(1, 300)]
        public int diffusionSteps = 100;
        public bool autoStart = true;
        public bool loopHint = true;

        [Header("UI")]
        public bool showOverlay = true;

        private readonly Queue<MotionSegment> queuedSegments = new Queue<MotionSegment>();
        private CancellationTokenSource lifetimeCts;
        private KimodoGeneratePipeline pipeline;
        private MotionSegment currentSegment;
        private Avatar sourceAvatar;
        private string targetRootBoneName;
        private Transform targetSkeletonRoot;
        private Transform[] targetBoneTransforms;
        private bool started;
        private bool generationInFlight;
        private string status = "idle";
        private string error = string.Empty;
        private float currentSegmentTime;

        private void Awake()
        {
            pipeline = new KimodoGeneratePipeline();
            lifetimeCts = new CancellationTokenSource();
        }

        private void Start()
        {
            if (autoStart)
            {
                _ = StartDemoAsync();
            }
        }

        private void OnDisable()
        {
            StopDemo();
        }

        private void OnDestroy()
        {
            StopDemo();
        }

        public async Task StartDemoAsync()
        {
            if (started)
            {
                return;
            }

            if (targetAnimator == null)
            {
                error = "Target Animator is not assigned.";
                status = "failed";
                return;
            }

            if (!TryInitializeRuntimeRetarget(out error))
            {
                status = "failed";
                return;
            }

            started = true;
            status = "starting";
            error = string.Empty;

            if (currentSegment == null)
            {
                currentSegment = await GenerateSegmentAsync(0, null, lifetimeCts.Token);
                if (currentSegment == null)
                {
                    started = false;
                    status = "failed";
                    return;
                }
            }

            currentSegmentTime = 0f;
            _ = EnsureNextSegmentAsync();
            status = "running";
        }

        public void StopDemo()
        {
            started = false;
            status = "stopped";

            lifetimeCts?.Cancel();
            lifetimeCts?.Dispose();
            lifetimeCts = new CancellationTokenSource();

            queuedSegments.Clear();
            DestroySegmentClips();
            currentSegment = null;
            currentSegmentTime = 0f;
            targetBoneTransforms = null;
            generationInFlight = false;
        }

        private async Task EnsureNextSegmentAsync()
        {
            if (!started || generationInFlight)
            {
                return;
            }

            if (queuedSegments.Count >= maxQueueSize)
            {
                return;
            }

            generationInFlight = true;
            try
            {
                int index = (currentSegment?.Index ?? -1) + 1 + queuedSegments.Count;
                var segment = await GenerateSegmentAsync(index, CaptureBoundaryPose(), lifetimeCts.Token);
                if (segment != null && started)
                {
                    queuedSegments.Enqueue(segment);
                }
            }
            finally
            {
                generationInFlight = false;
            }
        }

        private async Task<MotionSegment> GenerateSegmentAsync(int index, string boundaryPoseJson, CancellationToken token)
        {
            if (pipeline == null)
            {
                pipeline = new KimodoGeneratePipeline();
            }

            var request = new KimodoGeneratePipelineRequest
            {
                BackendType = backendType,
                RuntimeSettings = runtimeSettings,
                GenerationRequest = new KimodoGenerationRequestDto
                {
                    prompt = prompt ?? string.Empty,
                    duration = Mathf.Max(0.25f, segmentDurationSeconds),
                    seed = null,
                    steps = Mathf.Max(1, diffusionSteps),
                    constraints_json = string.Empty,
                    boundary_pose_json = boundaryPoseJson ?? string.Empty,
                    loop_hint = loopHint,
                    segment_index = index,
                    transition_duration = Mathf.Min(0.5f, prefetchLeadSeconds)
                }
            };

            try
            {
                status = $"generating #{index}";
                var result = await pipeline.ExecuteAsync(
                    request,
                    (stage, message) => status = $"{stage}: {message}",
                    token);

                if (result == null || string.IsNullOrWhiteSpace(result.MotionJsonCompact))
                {
                    error = "Generation returned empty motion json.";
                    status = "failed";
                    return null;
                }

                if (!started)
                {
                    return null;
                }

                var clip = new AnimationClip
                {
                    name = $"KimodoSegment_{index}",
                    legacy = true
                };
                if (!KimodoBridge.KimodoRuntimeClipBaker.TryBake(clip, result.MotionJsonCompact, out string bakeError))
                {
                    error = bakeError;
                    status = "failed";
                    return null;
                }

                return new MotionSegment(index, clip);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                status = "failed";
                return null;
            }
        }

        private bool TryInitializeRuntimeRetarget(out string initError)
        {
            initError = string.Empty;
            targetBoneTransforms = null;

            Avatar targetAvatar = targetAnimator != null ? targetAnimator.avatar : null;
            if (!KimodoRetargetTools.IsValidHumanoid(targetAvatar))
            {
                initError = "Target Animator avatar is null, invalid, or non-humanoid.";
                return false;
            }

            string sourceModelName = runtimeSettings?.bridgeSettings != null
                ? runtimeSettings.bridgeSettings.modelName
                : string.Empty;
            if (!KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(sourceModelName, out sourceAvatar, out string sourceAvatarError))
            {
                initError = $"Resolve source avatar failed: {sourceAvatarError}";
                return false;
            }

            targetRootBoneName = KimodoRetargetAvatarUtility.ResolveSkeletonRootBoneName(targetAvatar);
            targetSkeletonRoot = KimodoRetargetAvatarUtility.FindTransformByName(targetAnimator.transform, targetRootBoneName);
            if (targetSkeletonRoot == null)
            {
                initError = $"Target skeleton root '{targetRootBoneName}' was not found under target Animator.";
                return false;
            }

            return true;
        }

        private void Update()
        {
            if (!started || currentSegment == null || targetAnimator == null)
            {
                return;
            }

            if (!TrySampleAndApplyCurrentSegment(currentSegmentTime, out string sampleError))
            {
                error = sampleError;
                status = "failed";
                StopDemo();
                return;
            }

            float currentLength = Mathf.Max(0.01f, currentSegment.Clip != null ? currentSegment.Clip.length : segmentDurationSeconds);
            float remaining = currentLength - currentSegmentTime;

            if (remaining <= prefetchLeadSeconds)
            {
                _ = EnsureNextSegmentAsync();
            }

            currentSegmentTime += Time.deltaTime;
            if (currentSegmentTime < currentLength)
            {
                return;
            }

            if (queuedSegments.Count == 0)
            {
                currentSegmentTime = Mathf.Repeat(currentSegmentTime, currentLength);
                return;
            }

            MotionSegment previous = currentSegment;
            currentSegment = queuedSegments.Dequeue();
            currentSegmentTime = 0f;
            if (previous?.Clip != null)
            {
                Destroy(previous.Clip);
            }
        }

        private bool TrySampleAndApplyCurrentSegment(float sampleTime, out string sampleError)
        {
            sampleError = string.Empty;

            if (currentSegment?.Clip == null)
            {
                sampleError = "Current segment clip is missing.";
                return false;
            }

            if (!KimodoRetargetTools.TryRetargetNew(currentSegment.Clip, sourceAvatar, targetAnimator.avatar, sampleTime, out BoneSample targetSample, out sampleError))
            {
                return false;
            }

            return TryApplyBoneSampleToTarget(targetSample, out sampleError);
        }

        private bool TryApplyBoneSampleToTarget(BoneSample sample, out string applyError)
        {
            applyError = string.Empty;

            if (targetSkeletonRoot == null)
            {
                applyError = "Target skeleton root is missing.";
                return false;
            }

            return KimodoRetargetAvatarUtility.TryApplyBoneSample(
                sample,
                targetSkeletonRoot,
                targetRootBoneName,
                ref targetBoneTransforms,
                out applyError);
        }

        private string CaptureBoundaryPose()
        {
            if (targetAnimator == null)
            {
                return string.Empty;
            }

            var root = targetAnimator.transform;
            var hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips) ?? root;
            var data = new KimodoBoundaryPoseDto
            {
                rootPosition = hips.position,
                rootRotation = hips.rotation
            };
            return JsonUtility.ToJson(data);
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 360, 220), GUI.skin.box);
            GUILayout.Label($"Status: {status}");
            GUILayout.Label($"Error: {error}");
            GUILayout.Label($"Queue: {queuedSegments.Count}");
            GUILayout.Label($"Current: {(currentSegment != null ? currentSegment.Index.ToString() : "-")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start"))
            {
                _ = StartDemoAsync();
            }
            if (GUILayout.Button("Stop"))
            {
                StopDemo();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.Label("Prompt");
            prompt = GUILayout.TextField(prompt ?? string.Empty);
            GUILayout.EndArea();
        }

        private sealed class MotionSegment
        {
            public MotionSegment(int index, AnimationClip clip)
            {
                Index = index;
                Clip = clip;
                Duration = clip != null ? clip.length : 0f;
            }

            public int Index { get; }
            public AnimationClip Clip { get; }
            public float Duration { get; }
        }

        private void DestroySegmentClips()
        {
            if (currentSegment?.Clip != null)
            {
                Destroy(currentSegment.Clip);
            }

            while (queuedSegments.Count > 0)
            {
                MotionSegment segment = queuedSegments.Dequeue();
                if (segment?.Clip != null)
                {
                    Destroy(segment.Clip);
                }
            }
        }
    }
}
