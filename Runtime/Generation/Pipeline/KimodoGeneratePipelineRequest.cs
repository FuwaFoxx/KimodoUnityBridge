using KimodoUnityMotionTools.Generation;

namespace KimodoUnityMotionTools.Generation.Pipeline
{
    public sealed class KimodoGeneratePipelineRequest
    {
        public KimodoBackendType BackendType;
        public KimodoRuntimeGenerationSettings RuntimeSettings;
        public KimodoGenerationRequestDto GenerationRequest;
    }
}
