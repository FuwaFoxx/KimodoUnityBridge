using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoConstraintMarkerPoseMapper
    {
        internal static bool TryWriteSample(
            KimodoConstraintMarkerBase marker,
            KimodoMarkerSampleResult sample,
            bool keepOverrideEnabled,
            out string error)
        {
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (sample == null)
            {
                error = "sample is null";
                return false;
            }

            KimodoMarkerSampleResult normalized = NormalizeSample(marker, sample);
            if (normalized == null)
            {
                error = "failed to normalize sample";
                return false;
            }
            marker.SampleData = normalized;
            marker.time = normalized.sampleTime;

            if (keepOverrideEnabled)
            {
                marker.useOverride = true;
            }

            EditorUtility.SetDirty(marker);
            return true;
        }

        internal static KimodoMarkerSampleResult NormalizeSample(
            KimodoConstraintMarkerBase marker,
            KimodoMarkerSampleResult sample)
        {
            if (marker == null || sample == null)
            {
                return null;
            }

            KimodoMarkerSampleResult cloned = sample.Clone();
            cloned.constraintType = marker.ConstraintType;
            cloned.sampleTime = marker.time;
            if (cloned.jointNames == null)
            {
                cloned.jointNames = new List<string>();
            }

            if (marker is KimodoRoot2DConstraintMarker)
            {
                bool hasHeading = marker.SampleData != null && marker.SampleData.hasRootHeading;
                cloned.hasRootHeading = hasHeading;
                if (!hasHeading)
                {
                    cloned.rootHeading = Vector2.right;
                }

                cloned.localAxisAngles = new List<Vector3>();
                cloned.sampledJointIndices = new List<int>();
            }
            else if (marker is KimodoEndEffectorConstraintMarker)
            {
                List<string> configured = marker.SampleData != null && marker.SampleData.jointNames != null
                    ? marker.SampleData.jointNames
                    : null;
                if (configured == null || configured.Count == 0)
                {
                    configured = new List<string> { "LeftHand" };
                }

                cloned.jointNames = new List<string>(configured);
            }

            cloned.constraintType = marker.ConstraintType;
            cloned.hasRootHeading = marker is KimodoRoot2DConstraintMarker ? cloned.hasRootHeading : false;
            cloned.localAxisAngles ??= new List<Vector3>();
            cloned.sampledJointIndices ??= new List<int>();
            cloned.jointNames ??= new List<string>();
            return cloned;
        }
    }
}
