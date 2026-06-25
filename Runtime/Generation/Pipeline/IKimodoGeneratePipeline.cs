using System;
using System.Threading;
using System.Threading.Tasks;

namespace KimodoBridge
{
    public interface IKimodoGeneratePipeline
    {
        Task<KimodoBridgeControllerResult> ExecuteAsync(
            KimodoBridgeControllerRequest request,
            Action<KimodoBridgeControllerStage, string> progress,
            CancellationToken token);
    }
}
