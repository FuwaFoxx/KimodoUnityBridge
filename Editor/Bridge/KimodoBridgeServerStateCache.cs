using KimodoUnityMotionTools.Bridge;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal sealed class KimodoBridgeServerStateCache
    {
        private readonly object gate = new object();
        private bool runningCached;
        private bool hasPortCached;
        private string hostCached = "127.0.0.1";
        private int portCached = -1;
        private bool ready;
        private bool queryInFlight;
        private int queryVersion;
        private double nextQueryAt;
        private bool paused;

        private const double QueryCooldownSeconds = 2.0;

        internal bool IsServerRunning
        {
            get
            {
                RequestRefresh(force: false);
                lock (gate)
                {
                    return ready && runningCached;
                }
            }
        }

        internal KimodoBridgeController.ServerStatusSnapshot GetSnapshot()
        {
            lock (gate)
            {
                return new KimodoBridgeController.ServerStatusSnapshot(
                    ready: ready,
                    running: runningCached,
                    hasPort: hasPortCached,
                    queryInFlight: queryInFlight,
                    host: hostCached,
                    port: portCached);
            }
        }

        internal void RequestRefresh(bool force)
        {
            if (EditorCompilationStateGate.IsCompilingOrReloading)
            {
                return;
            }

            ScheduleRefresh(force);
        }

        internal void Pause()
        {
            lock (gate)
            {
                paused = true;
                queryVersion++;
                queryInFlight = false;
                nextQueryAt = EditorApplication.timeSinceStartup + QueryCooldownSeconds;
            }
            UnityEngine.Debug.Log("[Kimodo][ServerStateCache] paused.");
        }

        internal void Resume()
        {
            lock (gate)
            {
                paused = false;
                queryVersion++;
                queryInFlight = false;
                nextQueryAt = EditorApplication.timeSinceStartup + QueryCooldownSeconds;
            }
            UnityEngine.Debug.Log("[Kimodo][ServerStateCache] resumed.");
        }

        internal void Invalidate(System.Func<string> getRuntimeRoot)
        {
            lock (gate)
            {
                runningCached = false;
                hasPortCached = false;
                hostCached = "127.0.0.1";
                portCached = -1;
                ready = false;
                queryInFlight = false;
                queryVersion++;
                nextQueryAt = 0.0;
            }

            ScheduleRefresh(force: true, getRuntimeRoot);
        }

        private void ScheduleRefresh(bool force, System.Func<string> getRuntimeRoot = null)
        {
            double now = EditorApplication.timeSinceStartup;
            string runtimeRoot = getRuntimeRoot != null ? getRuntimeRoot() : KimodoBridgeRuntimeInstallFacade.GetRuntimeRootPath();

            lock (gate)
            {
                if (paused || EditorCompilationStateGate.IsCompilingOrReloading)
                {
                    return;
                }

                if (!force)
                {
                    if (queryInFlight || now < nextQueryAt)
                    {
                        return;
                    }
                }

                queryInFlight = true;
                nextQueryAt = now + QueryCooldownSeconds;
                int version = ++queryVersion;
                _ = RefreshAsync(runtimeRoot, version);
            }
        }

        private async Task RefreshAsync(string runtimeRoot, int version)
        {
            bool running = false;
            bool hasPort = false;
            string host = "127.0.0.1";
            int port = -1;
            try
            {
                if (!string.IsNullOrWhiteSpace(runtimeRoot) &&
                    Directory.Exists(runtimeRoot) &&
                    BridgeRuntimeControl.TryReadServerEndpoint(runtimeRoot, out string readHost, out int readPort))
                {
                    hasPort = true;
                    host = readHost;
                    port = readPort;
                    running = await BridgeRuntimeControl.IsServerResponsiveAsync(
                        readHost,
                        readPort,
                        BridgeRuntimeSettings.DefaultStatusConnectTimeoutMs,
                        BridgeRuntimeSettings.DefaultStatusIoTimeoutMs,
                        acceptLoading: true,
                        CancellationToken.None);
                }
            }
            catch
            {
                running = false;
            }

            lock (gate)
            {
                if (version != queryVersion)
                {
                    return;
                }

                if (paused || EditorCompilationStateGate.IsCompilingOrReloading)
                {
                    queryInFlight = false;
                    return;
                }

                runningCached = running;
                hasPortCached = hasPort;
                hostCached = host;
                portCached = port;
                ready = true;
                queryInFlight = false;
            }
        }
    }
}
