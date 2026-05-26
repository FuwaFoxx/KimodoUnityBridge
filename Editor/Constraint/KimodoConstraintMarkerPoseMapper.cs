using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoConstraintMarkerPoseMapper
    {
        internal static bool TryReadSample(KimodoConstraintMarkerBase marker, out KimodoMarkerSampleResult sample, out string error)
        {
            sample = null;
            error = string.Empty;

            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            sample = marker.SampleData != null ? marker.SampleData.Clone() : new KimodoMarkerSampleResult();
            sample.constraintType = marker.ConstraintType;
            EnsureMarkerShape(marker, sample);
            return true;
        }

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

            KimodoMarkerSampleResult cloned = sample.Clone();
            cloned.constraintType = marker.ConstraintType;
            EnsureMarkerShape(marker, cloned);
            marker.SampleData = cloned;

            if (keepOverrideEnabled)
            {
                marker.useOverride = true;
            }

            EditorUtility.SetDirty(marker);
            return true;
        }

        internal static KimodoMarkerSampleResult BuildSampleFromCapture(
            KimodoConstraintMarkerBase marker,
            int frameIndex,
            KimodoMarkerSampleResult captured)
        {
            if (marker == null || captured == null)
            {
                return null;
            }

            KimodoMarkerSampleResult sample = captured.Clone();
            sample.constraintType = marker.ConstraintType;
            sample.frameIndex = frameIndex;
            if (sample.jointNames == null)
            {
                sample.jointNames = new List<string>();
            }

            if (marker is KimodoRoot2DConstraintMarker)
            {
                bool hasHeading = marker.SampleData != null && marker.SampleData.hasRootHeading;
                sample.hasRootHeading = hasHeading;
                if (!hasHeading)
                {
                    sample.rootHeading = Vector2.right;
                }

                sample.localAxisAngles = new List<Vector3>();
                sample.sampledJointIndices = new List<int>();
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

                sample.jointNames = new List<string>(configured);
            }

            EnsureMarkerShape(marker, sample);
            return sample;
        }

        internal static void EnsureMarkerDefaults(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return;
            }

            KimodoMarkerSampleResult sample = marker.SampleData ?? new KimodoMarkerSampleResult();
            EnsureMarkerShape(marker, sample);
            marker.SampleData = sample;
        }

        private static void EnsureMarkerShape(KimodoConstraintMarkerBase marker, KimodoMarkerSampleResult sample)
        {
            if (sample == null)
            {
                return;
            }

            sample.constraintType = marker != null ? marker.ConstraintType : sample.constraintType;
            sample.hasRootHeading = marker is KimodoRoot2DConstraintMarker ? sample.hasRootHeading : false;

            sample.localAxisAngles ??= new List<Vector3>();
            sample.sampledJointIndices ??= new List<int>();
            sample.jointNames ??= new List<string>();

            if (marker is KimodoRoot2DConstraintMarker)
            {
                sample.localAxisAngles.Clear();
                sample.sampledJointIndices.Clear();
                sample.jointNames.Clear();
            }
            else if (marker is KimodoFullBodyConstraintMarker)
            {
                sample.hasRootHeading = false;
                sample.jointNames.Clear();
            }
            else if (marker is KimodoEndEffectorConstraintMarker)
            {
                sample.hasRootHeading = false;
                if (sample.jointNames.Count == 0)
                {
                    sample.jointNames.Add("LeftHand");
                }
            }
        }
    }
}
