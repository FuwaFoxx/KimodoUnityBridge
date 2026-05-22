using System;
using System.Threading;
using System.Threading.Tasks;

namespace KimodoUnityMotionTools.Bridge
{
    internal sealed class BridgeStartupWaiter
    {
        internal async Task WaitUntilReadyAsync(
            Func<bool> hasProcessExited,
            Func<int> getExitCode,
            string runtimeRoot,
            string hostFallback,
            BridgeProtocolClient protocolClient,
            int startupTimeoutMs,
            int pollIntervalMs,
            CancellationToken token)
        {
            if (protocolClient == null)
            {
                throw new ArgumentNullException(nameof(protocolClient));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(Math.Max(30000, startupTimeoutMs));
            CancellationToken waitToken = timeoutCts.Token;

            while (true)
            {
                waitToken.ThrowIfCancellationRequested();
                if (BridgeEndpointResolver.TryReadServerEndpoint(runtimeRoot, hostFallback, out string host, out int port, out _))
                {
                    bool ok = await protocolClient.PingAsync(host, port, waitToken, acceptLoading: true);
                    if (ok)
                    {
                        return;
                    }
                }

                if (hasProcessExited != null && hasProcessExited())
                {
                    int exitCode = getExitCode != null ? getExitCode() : -1;
                    throw new Exception($"Bridge exited with code {exitCode}.");
                }

                await Task.Delay(Math.Max(100, pollIntervalMs), waitToken);
            }
        }
    }
}
