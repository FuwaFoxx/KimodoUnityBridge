using System.Diagnostics;

namespace KimodoUnityMotionTools.Bridge
{
    internal sealed class BridgeProcessTerminator
    {
        private readonly IBridgePlatformProcess platformProcess;

        internal BridgeProcessTerminator(IBridgePlatformProcess platformProcess)
        {
            this.platformProcess = platformProcess ?? throw new System.ArgumentNullException(nameof(platformProcess));
        }

        internal void KillProcessTree(ref Process process, ref int processId)
        {
            Process proc = process;
            int pid = processId;
            process = null;
            processId = -1;

            if (pid <= 0 && proc != null)
            {
                try
                {
                    pid = proc.Id;
                }
                catch
                {
                    pid = -1;
                }
            }

            if (pid > 0)
            {
                platformProcess.KillProcessTreeByPid(pid);
            }

            if (proc != null)
            {
                try { proc.Dispose(); } catch { }
            }
        }
    }
}
