using System;
using KimodoUnityMotionTools.Bridge;

namespace KimodoUnityMotionTools.Generation
{
    public enum KimodoBackendType
    {
        Bridge = 0,
        ComfyUi = 1
    }

    [Serializable]
    public sealed class KimodoGenerationRequestDto
    {
        public string prompt;
        public float duration;
        public int? seed;
        public int steps;
        public string constraints_json;
    }

    [Serializable]
    public sealed class KimodoGenerationResultDto
    {
        public string motionJsonCompact;
        public KimodoBackendType backendType;
        public string rawStatus;
        public string message;
    }

    [Serializable]
    public sealed class KimodoRuntimeGenerationSettings
    {
        public BridgeRuntimeSettings bridgeSettings;
        public string comfyHost = "127.0.0.1";
        public int comfyPort = 8188;
        public float comfyTimeoutSeconds = 120f;
        public string comfyWorkflowResourceName = "kimodo-unity-workflow";
    }
}
