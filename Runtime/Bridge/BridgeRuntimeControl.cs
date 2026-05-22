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
            int connectTimeoutMs = 1500,
            int ioTimeoutMs = 1200,
            bool acceptLoading = true,
            CancellationToken token = default)
        {
            using var client = new BridgeProtocolClient(connectTimeoutMs, ioTimeoutMs);
            return await client.PingAsync(host, port, token, acceptLoading);
        }

        public static bool IsServerResponsive(
            string host,
            int port,
            int connectTimeoutMs = 1500,
            int ioTimeoutMs = 1200,
            bool acceptLoading = true)
        {
            return IsServerResponsiveAsync(host, port, connectTimeoutMs, ioTimeoutMs, acceptLoading, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        public static async Task<bool> TrySendQuitAsync(
            string host,
            int port,
            int connectTimeoutMs = 1500,
            int ioTimeoutMs = 1200,
            CancellationToken token = default)
        {
            using var client = new BridgeProtocolClient(connectTimeoutMs, ioTimeoutMs);
            return await client.TrySendQuitAsync(host, port, token);
        }

        public static bool TrySendQuit(
            string host,
            int port,
            int connectTimeoutMs = 1500,
            int ioTimeoutMs = 1200)
        {
            return TrySendQuitAsync(host, port, connectTimeoutMs, ioTimeoutMs, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }
}
