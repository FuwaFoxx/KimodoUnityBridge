using KimodoUnityMotionTools.Ai;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    public sealed class KimodoAiApiBakeTests
    {
        private const string MinimalValidMotionJson = @"
{
  ""num_frames"": 2,
  ""num_joints"": 1,
  ""fps"": 30,
  ""joint_names"": [""Hips""],
  ""joint_parents"": [-1],
  ""positions"": [
    [[0.0, 1.0, 0.0]],
    [[0.1, 1.0, 0.0]]
  ],
  ""local_rot_quats"": [
    1.0, 0.0, 0.0, 0.0,
    1.0, 0.0, 0.0, 0.0
  ]
}";

        [Test]
        public void BakeMotionJsonToClip_ValidJson_SucceedsAndProducesExpectedClipMode()
        {
            var clip = new AnimationClip();
            bool ok = KimodoAiApi.BakeMotionJsonToClip(clip, MinimalValidMotionJson, out string error);
            Assert.IsTrue(ok, error);
            Assert.IsTrue(clip.legacy);

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.IsNotEmpty(bindings);
            Assert.IsTrue(HasBinding(bindings, "Hips", "m_LocalPosition.x"));
            Assert.IsTrue(HasBinding(bindings, "Hips", "m_LocalRotation.x"));
        }

        [Test]
        public void BakeMotionJsonToClip_InvalidJson_FailsWithReadableError()
        {
            var clip = new AnimationClip();
            bool ok = KimodoAiApi.BakeMotionJsonToClip(clip, "{ invalid", out string error);
            Assert.IsFalse(ok);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }

        [Test]
        public void BakeMotionJsonToClip_MissingFields_FailsWithReadableError()
        {
            var clip = new AnimationClip();
            bool ok = KimodoAiApi.BakeMotionJsonToClip(clip, @"{ ""num_frames"":2 }", out string error);
            Assert.IsFalse(ok);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }

        private static bool HasBinding(EditorCurveBinding[] bindings, string path, string propertyName)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].path == path && bindings[i].propertyName == propertyName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
