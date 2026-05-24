using KimodoUnityMotionTools.Ai;
using NUnit.Framework;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    public sealed class KimodoAiApiRequestFactoryTests
    {
        [Test]
        public void CreateDefaultRequest_ConvertsFramesToDuration()
        {
            var req = KimodoAiApi.CreateDefaultRequest("hello", frames: 150, steps: 120, seed: 7);
            Assert.AreEqual("hello", req.prompt);
            Assert.AreEqual(5f, req.duration, 1e-6f);
            Assert.AreEqual(120, req.steps);
            Assert.AreEqual(7, req.seed);
            Assert.AreEqual(string.Empty, req.constraints_json);
        }

        [Test]
        public void CreateDefaultRequest_NullSeed_Preserved()
        {
            var req = KimodoAiApi.CreateDefaultRequest("hello", frames: 30, steps: 100, seed: null);
            Assert.IsNull(req.seed);
            Assert.AreEqual(1f, req.duration, 1e-6f);
        }
    }
}
