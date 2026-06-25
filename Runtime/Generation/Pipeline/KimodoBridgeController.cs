using System;
using System.Threading;
using System.Threading.Tasks;

namespace KimodoBridge
{
    public sealed class KimodoBridgeController : IKimodoGeneratePipeline
    {
        public async Task<KimodoBridgeControllerResult> ExecuteAsync(
            KimodoBridgeControllerRequest request,
            Action<KimodoBridgeControllerStage, string> progress,
            CancellationToken token)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            progress?.Invoke(KimodoBridgeControllerStage.Validate, "Validating generation request...");

            if (request.RuntimeSettings == null)
            {
                throw new InvalidOperationException("Runtime settings are required.");
            }

            if (request.GenerationRequest == null)
            {
                throw new InvalidOperationException("Generation request is required.");
            }

            token.ThrowIfCancellationRequested();
            KimodoGenerationResultDto result = await ExecuteBridgeAsync(request, progress, token);

            if (result == null)
            {
                throw new InvalidOperationException("Runtime generation returned null result.");
            }

            if (string.IsNullOrWhiteSpace(result.motionJsonCompact))
            {
                throw new InvalidOperationException(result.message ?? "No motion json found in runtime generation result.");
            }

            progress?.Invoke(KimodoBridgeControllerStage.Completed, "Generation backend completed.");

            return new KimodoBridgeControllerResult
            {
                MotionJsonCompact = result.motionJsonCompact,
                Message = result.message ?? string.Empty,
                RawStatus = result.rawStatus ?? string.Empty
            };
        }

        private static async Task<KimodoGenerationResultDto> ExecuteBridgeAsync(
            KimodoBridgeControllerRequest request,
            Action<KimodoBridgeControllerStage, string> progress,
            CancellationToken token)
        {
            if (request.RuntimeSettings.bridgeSettings == null)
            {
                throw new InvalidOperationException("Bridge runtime settings are required.");
            }

            progress?.Invoke(KimodoBridgeControllerStage.InvokeBackend, "Starting generation backend...");

            using var bridgeService = new KimodoBridgeService(request.RuntimeSettings.bridgeSettings);
            _ = await bridgeService.StartAsync(
                message => progress?.Invoke(KimodoBridgeControllerStage.InvokeBackend, message ?? string.Empty),
                token);

            token.ThrowIfCancellationRequested();
            progress?.Invoke(KimodoBridgeControllerStage.InvokeBackend, "Invoking generation backend...");

            string motionJson = await bridgeService.GenerateAsync(
                request.GenerationRequest,
                message => progress?.Invoke(KimodoBridgeControllerStage.InvokeBackend, message ?? string.Empty),
                token);

            return new KimodoGenerationResultDto
            {
                rawStatus = "done",
                message = "Bridge generation complete.",
                motionJsonCompact = motionJson
            };
        }
    }

    public sealed class KimodoBridgeControllerResult
    {
        public string MotionJsonCompact;
        public string Message;
        public string RawStatus;
    }

    public enum KimodoBridgeControllerStage
    {
        None = 0,
        Validate = 1,
        Constraint = 2,
        InvokeBackend = 3,
        AssetWrite = 4,
        Bake = 5,
        Retarget = 6,
        Finalize = 7,
        Completed = 8
    }

    public sealed class KimodoBridgeControllerRequest
    {
        public KimodoRuntimeGenerationSettings RuntimeSettings;
        public KimodoGenerationRequestDto GenerationRequest;
    }
}
