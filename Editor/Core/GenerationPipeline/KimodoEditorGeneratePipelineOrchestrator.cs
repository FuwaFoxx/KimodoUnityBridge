using KimodoBridge;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal sealed class KimodoEditorGeneratePipelineOrchestrator
    {
        private const string DefaultModelName = "Kimodo-SOMA-RP-v1";
        private readonly KimodoEditorClipWritebackService clipWritebackService = new KimodoEditorClipWritebackService();

        public async Task<KimodoEditorGenerateResult> ExecuteAsync(KimodoEditorGenerateRequest request)
        {
            if (request == null)
            {
                throw new InvalidOperationException("Generate request is null.");
            }

            string prompt = request.Prompt?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new InvalidOperationException("Prompt is empty.");
            }

            string modelName = NormalizeModelName(request.ModelName);
            request.Progress?.Invoke(KimodoGeneratePipelineStage.InvokeBackend, "Generating motion...");
            string motionJson = await GenerateMotionJsonAsync(request, prompt, modelName);
            if (string.IsNullOrWhiteSpace(motionJson))
            {
                throw new InvalidOperationException("No motion json found in workflow outputs.");
            }

            if (request.TargetClip == null)
            {
                throw new InvalidOperationException("Target clip is null.");
            }

            request.Progress?.Invoke(KimodoGeneratePipelineStage.Bake, "Baking animation...");
            if (!clipWritebackService.BakeMotionJsonToClip(request.TargetClip, motionJson, modelName, out string bakeError))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(bakeError) ? "Bake failed." : bakeError);
            }

            if (request.CanSkipRetarget != null && request.CanSkipRetarget(request.TargetClip))
            {
                request.Progress?.Invoke(KimodoGeneratePipelineStage.Retarget, "Skipping retarget: binding hierarchy already matches clip bindings.");
                return Complete(request, prompt, motionJson, request.TargetClip);
            }

            if (!IsValidHumanoid(request.OriginRetargetAvatar) || !IsValidHumanoid(request.TargetRetargetAvatar))
            {
                throw new InvalidOperationException("Retarget requires valid humanoid originAvatar and targetAvatar.");
            }

            request.Progress?.Invoke(KimodoGeneratePipelineStage.Retarget, "Retargeting...");
            if (!clipWritebackService.TryGetOrCreateMuscleClipCache(
                    request.TargetClip,
                    request.OriginRetargetAvatar,
                    out AnimationClip cachedMuscleClip,
                    out string muscleCacheError))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(muscleCacheError)
                    ? "Build muscle clip cache failed."
                    : muscleCacheError);
            }

            if (request.ExportMuscleClip)
            {
                if (!clipWritebackService.WriteMuscleClipCacheToClip(request.TargetClip, cachedMuscleClip, out string writeCacheError))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(writeCacheError)
                        ? "Write cached muscle clip failed."
                        : writeCacheError);
                }

                return Complete(request, prompt, motionJson, request.TargetClip);
            }

            if (!KimodoRetargetTools.TryRetargetNew(
                    request.TargetClip,
                    request.OriginRetargetAvatar,
                    request.TargetRetargetAvatar,
                    request.ExportMuscleClip,
                    cachedMuscleClip,
                    out AnimationClip retargetClip,
                    out string retargetError))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(retargetError)
                    ? "Retarget failed."
                    : retargetError);
            }

            if (retargetClip != null)
            {
                request.TargetClip = retargetClip;
                EditorUtility.SetDirty(retargetClip);
            }

            return Complete(request, prompt, motionJson, request.TargetClip);
        }

        private KimodoEditorGenerateResult Complete(KimodoEditorGenerateRequest request, string prompt, string motionJson, AnimationClip generatedClip)
        {
            request.Progress?.Invoke(KimodoGeneratePipelineStage.Finalize, "Finalizing generated assets...");
            request.Progress?.Invoke(KimodoGeneratePipelineStage.Completed, "Generation complete.");

            return new KimodoEditorGenerateResult
            {
                ConstraintsPath = string.Empty,
                Prompt = prompt,
                Seed = request.EffectiveSeed,
                MotionJsonCompact = motionJson,
                GeneratedClip = generatedClip
            };
        }

        private static async Task<string> GenerateMotionJsonAsync(KimodoEditorGenerateRequest request, string prompt, string modelName)
        {
            string kimodoRootPath = KimodoBridgeController.ResolveRuntimeRootOrThrow();
            string launcherPath = KimodoBridgeController.ResolveStartScriptOrThrow(kimodoRootPath);
            bool highVram = request.BridgeVramMode == KimodoBridgeVramMode.High;
            string modelsRoot = string.IsNullOrWhiteSpace(request.ModelsRoot) ? string.Empty : Path.GetFullPath(request.ModelsRoot.Trim());

            var generationRequest = new KimodoGenerationRequestDto
            {
                prompt = prompt,
                duration = request.DurationSeconds,
                seed = request.EffectiveSeed,
                steps = request.DiffusionSteps,
                constraints_json = request.ConstraintsJson ?? string.Empty
            };

            KimodoBackendType backendType = request.GenerationBackend == KimodoGenerationBackend.KimodoBridge
                ? KimodoBackendType.Bridge
                : KimodoBackendType.ComfyUi;

            if (backendType == KimodoBackendType.Bridge)
            {
                KimodoGenerationResultDto bridgeResult = await KimodoBridgeController.GenerateBridgeAsync(
                    launcherPath,
                    modelName,
                    highVram,
                    kimodoRootPath,
                    modelsRoot,
                    generationRequest,
                    msg => request.Progress?.Invoke(KimodoGeneratePipelineStage.InvokeBackend, msg ?? string.Empty),
                    request.Token);

                KimodoBridgeController.RequestServerStateRefresh(force: true);
                return bridgeResult.motionJsonCompact;
            }

            var settings = new KimodoRuntimeGenerationSettings
            {
                bridgeSettings = new BridgeRuntimeSettings
                {
                    runtimeRoot = kimodoRootPath,
                    launcherPath = launcherPath,
                    modelName = modelName,
                    highVram = highVram,
                    modelsRoot = modelsRoot,
                    startupTimeoutMs = ComputeBridgeStartupTimeoutMs(kimodoRootPath, highVram, modelName, request.GenerationTimeoutSeconds)
                },
                comfyHost = string.IsNullOrWhiteSpace(request.ComfyHost) ? "127.0.0.1" : request.ComfyHost.Trim(),
                comfyPort = request.ComfyPort,
                comfyTimeoutSeconds = request.GenerationTimeoutSeconds,
                comfyWorkflowResourceName = "kimodo-unity-workflow"
            };

            var pipelineRequest = new KimodoGeneratePipelineRequest
            {
                BackendType = backendType,
                RuntimeSettings = settings,
                GenerationRequest = generationRequest
            };

            IKimodoGeneratePipeline pipeline = new KimodoGeneratePipeline();
            KimodoGeneratePipelineResult pipelineResult = await pipeline.ExecuteAsync(
                pipelineRequest,
                (stage, message) => request.Progress?.Invoke(stage, message),
                request.Token);
            return pipelineResult.MotionJsonCompact;
        }

        private static int ComputeBridgeStartupTimeoutMs(string runtimeRoot, bool highVram, string modelName, float generationTimeoutSeconds)
        {
            int requestedMs = Math.Max(30000, Mathf.RoundToInt(generationTimeoutSeconds * 1000f));
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

        private static string NormalizeModelName(string modelName)
        {
            return string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName.Trim();
        }

        private static bool IsValidHumanoid(Avatar avatar)
        {
            return avatar != null && avatar.isValid && avatar.isHuman;
        }
    }
}
