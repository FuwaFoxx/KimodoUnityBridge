using System.Diagnostics;

namespace KimodoBridge
{
    internal interface IBridgePlatformProcess
    {
        bool SupportsCurrentPlatform();
        ProcessStartInfo BuildLauncherStartInfo(string launcherPath, string modelName, bool highVram, bool forceSetup, string modelsRoot, int idleTimeoutSeconds, int ownerProcessId);
    }
}
