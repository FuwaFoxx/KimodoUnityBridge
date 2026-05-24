using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KimodoUnityMotionTools.Bridge;
using KimodoUnityMotionTools.Generation;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal sealed class KimodoBridgeGenerationFacade : IDisposable
    {
        internal enum ShutdownMode
        {
            DetachOnly = 0,
            KillAndDispose = 1
        }

        private KimodoRuntimeGenerationService sharedRuntimeGenerationService;
        private string currentServiceRuntimeRoot = string.Empty;
        private string currentServiceLauncherPath = string.Empty;
        private string currentServiceModelName = string.Empty;
        private string currentServiceModelsRoot = string.Empty;
        private bool currentServiceHighVram;
        private bool currentServiceForceSetup;
        private bool isClosing;
        private int shutdownTicket;

        internal async Task<string> StartServerAsync(
            string launcherPath,
            string modelName,
            bool highVram,
            string kimodoRootPath,
            string modelsRoot,
            bool forceSetup,
            Action<string> progress,
            CancellationToken token)
        {
            float startupTimeoutSeconds = BridgeRuntimeSettings.DefaultStartupTimeoutMs / 1000f;
            int points = KimodoServerRuntimeUtil.EstimateMissingConfigPoints(kimodoRootPath, highVram, modelName, modelsRoot);
            if (points > 0)
            {
                int minutes = Math.Max(3, points * 3);
                startupTimeoutSeconds = Math.Max(BridgeRuntimeSettings.DefaultStartupTimeoutMs / 1000f, minutes * 60f);
            }

            KimodoRuntimeGenerationService runtimeService = GetOrCreateRuntimeGenerationService(
                kimodoRootPath,
                launcherPath,
                modelName,
                highVram,
                modelsRoot,
                forceSetup,
                startupTimeoutMs: (int)Math.Round(startupTimeoutSeconds * 1000f));
            return await runtimeService.StartAsync(KimodoBackendType.Bridge, progress, token);
        }

        internal async Task<KimodoGenerationResultDto> GenerateBridgeAsync(
            string launcherPath,
            string modelName,
            bool highVram,
            string kimodoRootPath,
            string modelsRoot,
            KimodoGenerationRequestDto request,
            Action<string> progress,
            CancellationToken token)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            int startupTimeoutMs = BridgeRuntimeSettings.DefaultStartupTimeoutMs;
            int points = KimodoServerRuntimeUtil.EstimateMissingConfigPoints(kimodoRootPath, highVram, modelName, modelsRoot);
            if (points > 0)
            {
                int minutes = Math.Max(3, points * 3);
                startupTimeoutMs = Math.Max(startupTimeoutMs, (int)Math.Round(Math.Max(BridgeRuntimeSettings.DefaultStartupTimeoutMs / 1000f, minutes * 60f) * 1000f));
            }

            KimodoRuntimeGenerationService runtimeService = GetOrCreateRuntimeGenerationService(
                kimodoRootPath,
                launcherPath,
                modelName,
                highVram,
                modelsRoot,
                forceSetup: false,
                startupTimeoutMs: startupTimeoutMs);

            await runtimeService.StartAsync(KimodoBackendType.Bridge, progress, token);
            return await runtimeService.GenerateAsync(request, KimodoBackendType.Bridge, progress, token);
        }

        internal async Task CloseServerAsync(Func<Task<(bool hasEndpoint, string host, int port)>> tryGetEndpointAsync)
        {
            await ShutdownAsync(ShutdownMode.KillAndDispose, tryGetEndpointAsync, CancellationToken.None);
        }

        internal void DetachSharedRuntimeGenerationService()
        {
            _ = ShutdownAsync(ShutdownMode.DetachOnly, null, CancellationToken.None);
        }

        internal void DisposeSharedRuntimeGenerationService()
        {
            _ = ShutdownAsync(ShutdownMode.KillAndDispose, null, CancellationToken.None);
        }

        internal bool TryGetAttachedServiceRuntimeRoot(out string runtimeRoot)
        {
            runtimeRoot = currentServiceRuntimeRoot;
            return !string.IsNullOrWhiteSpace(runtimeRoot);
        }

        private KimodoRuntimeGenerationService GetOrCreateRuntimeGenerationService(
            string runtimeRoot,
            string launcherPath,
            string modelName,
            bool highVram,
            string modelsRoot,
            bool forceSetup = false,
            int startupTimeoutMs = BridgeRuntimeSettings.DefaultStartupTimeoutMs)
        {
            string resolvedRuntimeRoot = Path.GetFullPath(runtimeRoot ?? string.Empty);
            string resolvedLauncherPath = Path.GetFullPath(launcherPath ?? string.Empty);
            string resolvedModelName = string.IsNullOrWhiteSpace(modelName) ? "Kimodo-SOMA-RP-v1" : modelName.Trim();
            string resolvedModelsRoot = string.IsNullOrWhiteSpace(modelsRoot) ? string.Empty : Path.GetFullPath(modelsRoot.Trim());

            bool reusable =
                sharedRuntimeGenerationService != null &&
                string.Equals(currentServiceRuntimeRoot, resolvedRuntimeRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentServiceLauncherPath, resolvedLauncherPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentServiceModelName, resolvedModelName, StringComparison.Ordinal) &&
                currentServiceHighVram == highVram &&
                currentServiceForceSetup == forceSetup &&
                string.Equals(currentServiceModelsRoot, resolvedModelsRoot, StringComparison.OrdinalIgnoreCase);

            if (reusable)
            {
                return sharedRuntimeGenerationService;
            }

            try
            {
                sharedRuntimeGenerationService?.Dispose();
            }
            catch
            {
                // ignore disposal failure
            }

            var settings = new KimodoRuntimeGenerationSettings
            {
                bridgeSettings = CreateBridgeSettings(
                    runtimeRoot: resolvedRuntimeRoot,
                    launcherPath: resolvedLauncherPath,
                    modelName: resolvedModelName,
                    highVram: highVram,
                    forceSetup: forceSetup,
                    modelsRoot: resolvedModelsRoot,
                    startupTimeoutMs: Math.Max(30000, startupTimeoutMs)),
                comfyWorkflowResourceName = "kimodo-unity-workflow"
            };

            sharedRuntimeGenerationService = new KimodoRuntimeGenerationService(settings);
            currentServiceRuntimeRoot = resolvedRuntimeRoot;
            currentServiceLauncherPath = resolvedLauncherPath;
            currentServiceModelName = resolvedModelName;
            currentServiceHighVram = highVram;
            currentServiceForceSetup = forceSetup;
            currentServiceModelsRoot = resolvedModelsRoot;
            return sharedRuntimeGenerationService;
        }

        private static BridgeRuntimeSettings CreateBridgeSettings(
            string runtimeRoot,
            string launcherPath,
            string modelName,
            bool highVram,
            bool forceSetup,
            string modelsRoot,
            int startupTimeoutMs)
        {
            return new BridgeRuntimeSettings
            {
                runtimeRoot = runtimeRoot,
                launcherPath = launcherPath,
                modelName = modelName,
                highVram = highVram,
                forceSetup = forceSetup,
                modelsRoot = modelsRoot,
                startupTimeoutMs = startupTimeoutMs,
                connectTimeoutMs = BridgeRuntimeSettings.DefaultConnectTimeoutMs,
                ioTimeoutMs = BridgeRuntimeSettings.DefaultIoTimeoutMs,
                modelLoadingTimeoutMs = BridgeRuntimeSettings.DefaultModelLoadingTimeoutMs,
                modelLoadingPollIntervalMs = BridgeRuntimeSettings.DefaultModelLoadingPollIntervalMs,
                statusConnectTimeoutMs = BridgeRuntimeSettings.DefaultStatusConnectTimeoutMs,
                statusIoTimeoutMs = BridgeRuntimeSettings.DefaultStatusIoTimeoutMs
            };
        }

        private void ResetSharedServiceState()
        {
            currentServiceRuntimeRoot = string.Empty;
            currentServiceLauncherPath = string.Empty;
            currentServiceModelName = string.Empty;
            currentServiceModelsRoot = string.Empty;
            currentServiceHighVram = false;
            currentServiceForceSetup = false;
        }

        internal async Task ShutdownAsync(
            ShutdownMode mode,
            Func<Task<(bool hasEndpoint, string host, int port)>> tryGetEndpointAsync,
            CancellationToken token)
        {
            int ticket = Interlocked.Increment(ref shutdownTicket);
            if (isClosing)
            {
                UnityEngine.Debug.Log($"[Kimodo][BridgeShutdown] skip duplicate shutdown, mode={mode}, ticket={ticket}");
                return;
            }

            isClosing = true;
            UnityEngine.Debug.Log($"[Kimodo][BridgeShutdown] begin mode={mode}, ticket={ticket}");
            try
            {
                KimodoRuntimeGenerationService runtimeService = sharedRuntimeGenerationService;
                sharedRuntimeGenerationService = null;
                ResetSharedServiceState();

                if (runtimeService != null)
                {
                    try
                    {
                        if (mode == ShutdownMode.DetachOnly)
                        {
                            await runtimeService.DetachAsync(KimodoBackendType.Bridge, token);
                            UnityEngine.Debug.Log("[Kimodo][BridgeShutdown] detached shared runtime service.");
                        }
                        else
                        {
                            await runtimeService.KillAsync(KimodoBackendType.Bridge, token);
                            UnityEngine.Debug.Log("[Kimodo][BridgeShutdown] killed shared runtime service.");
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                    finally
                    {
                        try { runtimeService.Dispose(); } catch { }
                    }
                }
                else if (mode == ShutdownMode.KillAndDispose && tryGetEndpointAsync != null)
                {
                    try
                    {
                        (bool hasEndpoint, string host, int port) = await tryGetEndpointAsync();
                        if (hasEndpoint)
                        {
                            await BridgeRuntimeControl.TrySendQuitAsync(
                                host,
                                port,
                                BridgeRuntimeSettings.DefaultStatusConnectTimeoutMs,
                                BridgeRuntimeSettings.DefaultStatusIoTimeoutMs,
                                token);
                            UnityEngine.Debug.Log($"[Kimodo][BridgeShutdown] sent quit to {host}:{port}.");
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            finally
            {
                if (ticket == shutdownTicket)
                {
                    isClosing = false;
                }

                UnityEngine.Debug.Log($"[Kimodo][BridgeShutdown] end mode={mode}, ticket={ticket}");
            }
        }

        public void Dispose()
        {
            _ = ShutdownAsync(ShutdownMode.KillAndDispose, null, CancellationToken.None);
        }
    }
}
