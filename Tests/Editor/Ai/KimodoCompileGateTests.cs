using KimodoBridge.Editor;
using NUnit.Framework;
using System.Reflection;

namespace KimodoBridge.Editor
{
    [TestFixture]
    public sealed class KimodoCompileGateTests
    {
        [Test]
        public void CompileGate_StateProperty_IsReachable()
        {
            // Accessing static gate should be safe and non-throwing.
            bool _ = EditorCompilationStateGate.IsCompilingOrReloading;
            Assert.Pass();
        }

        [Test]
        public void ServerStateCache_PauseResume_MethodsExistAndCallable()
        {
            var cache = new KimodoBridgeServerStateCache();
            MethodInfo pause = typeof(KimodoBridgeServerStateCache).GetMethod(
                "Pause",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo resume = typeof(KimodoBridgeServerStateCache).GetMethod(
                "Resume",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.NotNull(pause);
            Assert.NotNull(resume);
            pause.Invoke(cache, null);
            resume.Invoke(cache, null);
            Assert.Pass();
        }
    }
}
