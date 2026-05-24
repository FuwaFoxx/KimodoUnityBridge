using KimodoUnityMotionTools.Ai;
using KimodoUnityMotionTools.Generation;
using NUnit.Framework;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    public sealed class KimodoAiApiValidationTests
    {
        [Test]
        public void ValidateRequest_NullRequest_Fails()
        {
            bool ok = KimodoAiApi.ValidateRequest(null, out string error);
            Assert.IsFalse(ok);
            StringAssert.Contains("null", error.ToLowerInvariant());
        }

        [Test]
        public void ValidateRequest_EmptyPrompt_Fails()
        {
            var req = new KimodoGenerationRequestDto { prompt = "", duration = 1f, steps = 10 };
            bool ok = KimodoAiApi.ValidateRequest(req, out string error);
            Assert.IsFalse(ok);
            StringAssert.Contains("prompt", error.ToLowerInvariant());
        }

        [Test]
        public void ValidateRequest_NonPositiveDuration_Fails()
        {
            var req = new KimodoGenerationRequestDto { prompt = "walk", duration = 0f, steps = 10 };
            bool ok = KimodoAiApi.ValidateRequest(req, out string error);
            Assert.IsFalse(ok);
            StringAssert.Contains("duration", error.ToLowerInvariant());
        }

        [Test]
        public void ValidateRequest_NonPositiveSteps_Fails()
        {
            var req = new KimodoGenerationRequestDto { prompt = "walk", duration = 1f, steps = 0 };
            bool ok = KimodoAiApi.ValidateRequest(req, out string error);
            Assert.IsFalse(ok);
            StringAssert.Contains("steps", error.ToLowerInvariant());
        }

        [Test]
        public void ValidateRequest_Valid_PassesAndNormalizesConstraints()
        {
            var req = new KimodoGenerationRequestDto
            {
                prompt = "walk",
                duration = 5f,
                steps = 100,
                constraints_json = null
            };
            bool ok = KimodoAiApi.ValidateRequest(req, out string error);
            Assert.IsTrue(ok, error);
            Assert.AreEqual(string.Empty, req.constraints_json);
        }
    }
}
