using System;
using UnityEngine;

namespace KimodoUnityMotionTools.Bridge
{
    [Serializable]
    public sealed class BridgeRuntimeSettings
    {
        public string runtimeRoot;
        public string launcherPath;
        public string modelName = "Kimodo-SOMA-RP-v1";
        public bool highVram;
        public string modelsRoot;
        public string hostFallback = "127.0.0.1";
        public int startupTimeoutMs = 600000;
        public int pollIntervalMs = 250;
        public int connectTimeoutMs = 3000;
        public int ioTimeoutMs = 120000;
        public bool enableWindows = true;
        public bool enableLinux = true;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                throw new InvalidOperationException("runtimeRoot is empty.");
            }

            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                throw new InvalidOperationException("launcherPath is empty.");
            }

            if (!enableWindows && !enableLinux)
            {
                throw new InvalidOperationException("All runtime platforms are disabled.");
            }

            RuntimePlatform platform = Application.platform;
            if (platform == RuntimePlatform.WindowsEditor || platform == RuntimePlatform.WindowsPlayer)
            {
                if (!enableWindows)
                {
                    throw new PlatformNotSupportedException("Windows runtime is disabled in BridgeRuntimeSettings.");
                }
            }
            else if (platform == RuntimePlatform.LinuxEditor || platform == RuntimePlatform.LinuxPlayer)
            {
                if (!enableLinux)
                {
                    throw new PlatformNotSupportedException("Linux runtime is disabled in BridgeRuntimeSettings.");
                }
            }
            else
            {
                throw new PlatformNotSupportedException($"Unsupported platform for bridge runtime: {platform}");
            }
        }
    }
}
