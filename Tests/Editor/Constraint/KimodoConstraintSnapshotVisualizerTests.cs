using NUnit.Framework;
using UnityEngine;

namespace KimodoUnityMotionTools.Tests
{
    [TestFixture]
    public sealed class KimodoConstraintSnapshotVisualizerTests
    {
        [Test]
        public void ResolveRigTypeFromModel_SomaDefault()
        {
            var t1 = KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.ResolveRigTypeFromModel("Kimodo-SOMA-RP-v1");
            var t2 = KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.ResolveRigTypeFromModel(string.Empty);
            Assert.AreEqual(KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.SkeletonPreviewRigType.Soma30, t1);
            Assert.AreEqual(KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.SkeletonPreviewRigType.Soma30, t2);
        }

        [Test]
        public void ResolveRigTypeFromModel_SmplxAndG1()
        {
            var smplx = KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.ResolveRigTypeFromModel("Kimodo-SMPLX-RP-v1");
            var g1 = KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.ResolveRigTypeFromModel("Kimodo-G1-RP-v1");
            Assert.AreEqual(KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.SkeletonPreviewRigType.Smplx, smplx);
            Assert.AreEqual(KimodoUnityMotionTools.ProjectEditor.KimodoConstraintSnapshotVisualizer.SkeletonPreviewRigType.G1, g1);
        }

        [Test]
        public void KimodoSpaceConversion_RoundTripRootPositionAndAxisAngle()
        {
            Vector3 unityPos = new Vector3(1.2f, 0.7f, -3.1f);
            Vector3 kimodoPos = KimodoUnityMotionTools.ProjectEditor.KimodoSpaceConversionUtility.ToKimodoRootPosition(unityPos);
            Vector3 unityPosBack = KimodoUnityMotionTools.ProjectEditor.KimodoSpaceConversionUtility.ToUnityRootPosition(kimodoPos);
            Assert.That((unityPos - unityPosBack).magnitude, Is.LessThan(1e-4f));

            Vector3 unityAA = new Vector3(0.2f, -0.5f, 0.3f);
            Vector3 kimodoAA = KimodoUnityMotionTools.ProjectEditor.KimodoSpaceConversionUtility.ToKimodoAxisAngle(unityAA);
            Vector3 unityAABack = KimodoUnityMotionTools.ProjectEditor.KimodoSpaceConversionUtility.ToUnityAxisAngle(kimodoAA);
            Assert.That((unityAA - unityAABack).magnitude, Is.LessThan(1e-3f));
        }
    }
}
