using System.Diagnostics;
using KimodoUnityMotionTools.Bridge;
using NUnit.Framework;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    public sealed class KimodoBridgeLogPumpStopTests
    {
        [Test]
        public void Stop_WithoutStart_ReturnsQuickly()
        {
            using var pump = new BridgeLogPump();
            var sw = Stopwatch.StartNew();
            pump.Stop();
            sw.Stop();
            Assert.Less(sw.ElapsedMilliseconds, 50);
        }
    }
}
