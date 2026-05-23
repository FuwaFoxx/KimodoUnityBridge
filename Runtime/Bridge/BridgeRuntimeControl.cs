using System.Threading;
using System.Threading.Tasks;

namespace KimodoUnityMotionTools.Bridge
{
    public static class BridgeRuntimeControl
    {
        public static bool TryReadServerEndpoint(string runtimeRoot, out string host, out int port)
        {
            return BridgeEndpointResolver.TryReadServerEndpoint(runtimeRoot, "127.0.0.1", out host, out port, out _);
        }

        public static async Task<bool> IsServerResponsiveAsync(
            string host,
            int port,
            int connectTimeoutMs = BridgeRuntimeSettings.DefaultStatusConnectTimeoutMs,
            int ioTimeoutMs = BridgeRuntimeSettings.DefaultStatusIoTimeoutMs,
            bool acceptLoading = true,
            CancellationToken token = default)
        {
            using var client = new BridgeProtocolClient(connectTimeoutMs, ioTimeoutMs);
            return await client.PingAsync(host, port, token, acceptLoading);
        }

        public static async Task<bool> TrySendQuitAsync(
            string host,
            int port,
            int connectTimeoutMs = BridgeRuntimeSettings.DefaultStatusConnectTimeoutMs,
            int ioTimeoutMs = BridgeRuntimeSettings.DefaultStatusIoTimeoutMs,
            CancellationToken token = default)
        {
            using var client = new BridgeProtocolClient(connectTimeoutMs, ioTimeoutMs);
            return await client.TrySendQuitAsync(host, port, token);
        }
    }
}
