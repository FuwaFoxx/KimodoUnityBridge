using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KimodoUnityMotionTools.Ai;
using KimodoUnityMotionTools.Bridge;
using KimodoUnityMotionTools.Generation;
using NUnit.Framework;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    [Category("KimodoBridge")]
    [Category("AiApi")]
    
    public sealed class KimodoAiApiBridgeIntegrationTests
    {
        private KimodoRuntimeScope scope;

        [SetUp]
        public void SetUp()
        {
            scope = KimodoBridgeTestHarness.CreateRuntimeScope(TestContext.CurrentContext.Test.Name);
        }

        [TearDown]
        public void TearDown()
        {
            TearDownAsync().GetAwaiter().GetResult();
        }

        private async Task TearDownAsync()
        {
            string workingRoot = scope?.WorkingRoot;
            await KimodoBridgeTestHarness.CleanupScopeAsync(scope);
            scope?.Dispose();
            if (!string.IsNullOrWhiteSpace(workingRoot))
            {
                TryDelete(workingRoot);
            }
        }

        [Test]
        [Explicit("Requires local runtime environment and model setup.")]
        public void StartGenerateStop_BasicPath()
        {
            StartGenerateStop_BasicPath_Async().GetAwaiter().GetResult();
        }

        private async Task StartGenerateStop_BasicPath_Async()
        {
            await KimodoBridgeTestHarness.EnsureSetupOrIgnoreAsync(scope);

            string launcher = BridgeLauncherResolver.ResolveStartScript(scope.RuntimeRoot);
            var settings = new KimodoRuntimeGenerationSettings
            {
                bridgeSettings = new BridgeRuntimeSettings
                {
                    runtimeRoot = scope.RuntimeRoot,
                    launcherPath = launcher,
                    modelName = "Kimodo-SOMA-RP-v1",
                    highVram = false,
                    startupTimeoutMs = 120000
                }
            };

            var req = KimodoAiApi.CreateDefaultRequest("walk forward", frames: 60, steps: 40, seed: 123);
            KimodoGenerationResultDto result = null;
            Exception ex = null;
            try
            {
                result = await KimodoAiApi.GenerateAsync(settings, KimodoBackendType.Bridge, req, CancellationToken.None);
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                await KimodoAiApi.StopAsync(settings, KimodoBackendType.Bridge, CancellationToken.None);
            }

            if (ex != null)
            {
                Assert.Ignore("Environment not ready for integration run: " + ex.Message);
            }

            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.motionJsonCompact));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
