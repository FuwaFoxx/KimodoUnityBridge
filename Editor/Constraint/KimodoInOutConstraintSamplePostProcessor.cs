using System;
using System.Collections.Generic;
using TimelineInject;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal static class KimodoInOutConstraintSamplePostProcessor
    {
        private const float InClipClusterThreshold = 0.1f;
        private const int FootSwitchFrameThreshold = 5;

        private enum SupportFootSide
        {
            Unknown = 0,
            Left = 1,
            Right = 2
        }

        internal static bool TryApplyInClipRootMotionCompensation(
            List<KimodoMarkerSampleResult> samples,
            string modelName,
            out string warning,
            out string error)
        {
            warning = string.Empty;
            error = string.Empty;

            if (samples == null || samples.Count == 0)
            {
                return true;
            }

            if (!TryBuildPoseRigRootMotionCompensation(
                    samples,
                    KimodoPlayableClip.NormalizeBridgeModelName(modelName),
                    out Vector3[] compensatedRootPositions,
                    out warning,
                    out error))
            {
                return false;
            }

            if (compensatedRootPositions == null || compensatedRootPositions.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < samples.Count && i < compensatedRootPositions.Length; i++)
            {
                KimodoMarkerSampleResult sample = samples[i];
                if (sample != null)
                {
                    sample.kimodoRootPosition = compensatedRootPositions[i];
                }
            }

            return true;
        }

        internal static void NormalizeConstraintOrigin(List<KimodoMarkerSampleResult> samples)
        {
            if (!TryResolveConstraintOriginAnchorSample(samples, out KimodoMarkerSampleResult anchor))
            {
                return;
            }

            Vector3 anchorRootPosition = anchor.unityRootPos;
            Quaternion inverseAnchorRootRotation = Quaternion.Inverse(anchor.unityRootRot);
            for (int i = 0; i < samples.Count; i++)
            {
                NormalizeConstraintOriginSample(samples[i], anchorRootPosition, inverseAnchorRootRotation);
            }
        }

        internal static void CopyPoseAxes(KimodoMarkerSampleResult sourceSample, KimodoMarkerSampleResult destinationSample)
        {
            if (sourceSample == null || destinationSample == null)
            {
                return;
            }

            destinationSample.localAxisAngles = sourceSample.localAxisAngles != null
                ? new List<Vector3>(sourceSample.localAxisAngles)
                : new List<Vector3>();
            destinationSample.sampledJointIndices = sourceSample.sampledJointIndices != null
                ? new List<int>(sourceSample.sampledJointIndices)
                : new List<int>();
            destinationSample.jointNames = sourceSample.jointNames != null
                ? new List<string>(sourceSample.jointNames)
                : new List<string>();
        }

        private static bool TryBuildPoseRigRootMotionCompensation(
            List<KimodoMarkerSampleResult> samples,
            string modelName,
            out Vector3[] compensatedRootPositions,
            out string warning,
            out string error)
        {
            compensatedRootPositions = null;
            warning = string.Empty;
            error = string.Empty;

            if (samples == null || samples.Count == 0)
            {
                return true;
            }

            int anchorIndex = ResolveConstraintOriginAnchorIndex(samples);
            if (anchorIndex < 0 || anchorIndex >= samples.Count || samples[anchorIndex] == null)
            {
                return true;
            }

            Vector3 anchorRoot = samples[anchorIndex].kimodoRootPosition;
            float maxDistanceSq = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                KimodoMarkerSampleResult sample = samples[i];
                if (sample == null)
                {
                    continue;
                }

                Vector3 delta = sample.kimodoRootPosition - anchorRoot;
                delta.y = 0f;
                maxDistanceSq = Mathf.Max(maxDistanceSq, delta.sqrMagnitude);
            }

            if (maxDistanceSq > InClipClusterThreshold * InClipClusterThreshold)
            {
                return true;
            }

            KimodoConstraintPoseRigFactory.PoseRigInstance rigInstance = null;
            try
            {
                if (!KimodoConstraintPoseRigFactory.TryCreatePoseRig(modelName, 0, 0, out rigInstance, out error))
                {
                    return false;
                }

                compensatedRootPositions = new Vector3[samples.Count];
                Vector3 cumulativeDelta = Vector3.zero;
                SupportFootSide activeFoot = SupportFootSide.Unknown;
                SupportFootSide pendingFoot = SupportFootSide.Unknown;
                int pendingFootFrames = 0;
                Vector3 previousFootWorldPosition = Vector3.zero;
                bool hasPreviousFootWorldPosition = false;

                for (int i = 0; i < samples.Count; i++)
                {
                    KimodoMarkerSampleResult sample = samples[i];
                    if (sample == null)
                    {
                        compensatedRootPositions[i] = Vector3.zero;
                        continue;
                    }

                    if (!KimodoConstraintPoseRigFactory.TryApplySampleToPoseRig(sample, modelName, rigInstance, out error))
                    {
                        return false;
                    }

                    if (!KimodoConstraintPoseRigFootUtility.TryResolveFootWorldPositions(
                            rigInstance,
                            modelName,
                            out Vector3 leftFootPosition,
                            out Vector3 rightFootPosition,
                            out error))
                    {
                        return false;
                    }

                    float leftHeight = leftFootPosition.y;
                    float rightHeight = rightFootPosition.y;
                    SupportFootSide candidateFoot = ResolveHigherFootSide(leftHeight, rightHeight, activeFoot);
                    if (activeFoot == SupportFootSide.Unknown)
                    {
                        activeFoot = candidateFoot;
                        pendingFoot = SupportFootSide.Unknown;
                        pendingFootFrames = 0;
                        hasPreviousFootWorldPosition = false;
                    }
                    else if (candidateFoot != activeFoot)
                    {
                        if (pendingFoot == candidateFoot)
                        {
                            pendingFootFrames++;
                        }
                        else
                        {
                            pendingFoot = candidateFoot;
                            pendingFootFrames = 1;
                        }

                        if (pendingFootFrames >= FootSwitchFrameThreshold)
                        {
                            activeFoot = candidateFoot;
                            pendingFoot = SupportFootSide.Unknown;
                            pendingFootFrames = 0;
                            hasPreviousFootWorldPosition = false;
                            previousFootWorldPosition = Vector3.zero;
                            compensatedRootPositions[i] = sample.kimodoRootPosition + cumulativeDelta;
                            continue;
                        }
                    }
                    else
                    {
                        pendingFoot = SupportFootSide.Unknown;
                        pendingFootFrames = 0;
                    }

                    Vector3 currentFootWorldPosition = activeFoot == SupportFootSide.Left
                        ? leftFootPosition
                        : rightFootPosition;
                    if (currentFootWorldPosition == Vector3.zero)
                    {
                        compensatedRootPositions[i] = sample.kimodoRootPosition + cumulativeDelta;
                        continue;
                    }

                    if (hasPreviousFootWorldPosition)
                    {
                        Vector3 delta = currentFootWorldPosition - previousFootWorldPosition;
                        delta.y = 0f;
                        cumulativeDelta += delta;
                    }

                    previousFootWorldPosition = currentFootWorldPosition;
                    hasPreviousFootWorldPosition = true;
                    compensatedRootPositions[i] = sample.kimodoRootPosition + cumulativeDelta;
                }

                return true;
            }
            finally
            {
                KimodoConstraintPoseRigFactory.DestroyPoseRig(rigInstance);
            }
        }

        private static SupportFootSide ResolveHigherFootSide(float leftHeight, float rightHeight, SupportFootSide activeFoot)
        {
            float epsilon = 1e-4f;
            if (leftHeight > rightHeight + epsilon)
            {
                return SupportFootSide.Left;
            }

            if (rightHeight > leftHeight + epsilon)
            {
                return SupportFootSide.Right;
            }

            return activeFoot != SupportFootSide.Unknown
                ? activeFoot
                : SupportFootSide.Left;
        }

        private static bool TryResolveConstraintOriginAnchorSample(
            List<KimodoMarkerSampleResult> samples,
            out KimodoMarkerSampleResult anchor)
        {
            anchor = null;
            if (samples == null || samples.Count == 0)
            {
                return false;
            }

            int anchorIndex = ResolveConstraintOriginAnchorIndex(samples);
            if (anchorIndex < 0 || anchorIndex >= samples.Count)
            {
                return false;
            }

            anchor = samples[anchorIndex];
            return anchor != null;
        }

        private static int ResolveConstraintOriginAnchorIndex(List<KimodoMarkerSampleResult> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return -1;
            }

            int earliest = -1;
            double earliestTime = double.MaxValue;
            for (int i = 0; i < samples.Count; i++)
            {
                KimodoMarkerSampleResult sample = samples[i];
                if (sample != null && sample.sampleTime < earliestTime)
                {
                    earliestTime = sample.sampleTime;
                    earliest = i;
                }
            }

            return earliest;
        }

        private static void NormalizeConstraintOriginSample(
            KimodoMarkerSampleResult sample,
            Vector3 anchorRootPosition,
            Quaternion inverseAnchorRootRotation)
        {
            if (sample == null)
            {
                return;
            }

            sample.kimodoRootPosition = inverseAnchorRootRotation * (sample.kimodoRootPosition - anchorRootPosition);
            if (sample.localAxisAngles == null || sample.localAxisAngles.Count == 0)
            {
                return;
            }

            Quaternion rootJointRotation = AxisAngleToQuaternion(sample.localAxisAngles[0]);
            Quaternion normalizedRootJointRotation = inverseAnchorRootRotation * rootJointRotation;
            sample.localAxisAngles[0] = KimodoRuntimeUtility.QuaternionToAxisAngleVector(normalizedRootJointRotation);
        }

        private static Quaternion AxisAngleToQuaternion(Vector3 axisAngle)
        {
            float angleRad = axisAngle.magnitude;
            if (angleRad <= 1e-8f)
            {
                return Quaternion.identity;
            }

            Vector3 axis = axisAngle / angleRad;
            return Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
        }
    }
}
