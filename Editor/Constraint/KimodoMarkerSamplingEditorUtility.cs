using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using TimelineInject;

namespace KimodoBridge.Editor
{
    internal static class KimodoMarkerSamplingEditorUtility
    {
        public static bool TryWriteConstraintMarkerSample(
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

            KimodoMarkerSampleResult normalized = KimodoMarkerSamplingUtility.NormalizeConstraintMarkerSample(marker, sample);
            if (normalized == null)
            {
                error = "failed to normalize sample";
                return false;
            }

            bool changed = !AreSamplesEquivalent(marker.SampleData, normalized) ||
                System.Math.Abs(marker.time - normalized.sampleTime) > 1e-9 ||
                (keepOverrideEnabled && !marker.useOverride);
            if (!changed)
            {
                return true;
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

        private static bool AreSamplesEquivalent(KimodoMarkerSampleResult left, KimodoMarkerSampleResult right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.constraintType ?? string.Empty, right.constraintType ?? string.Empty, System.StringComparison.Ordinal) &&
                System.Math.Abs(left.sampleTime - right.sampleTime) <= 1e-9 &&
                left.rigType == right.rigType &&
                left.hasRootHeading == right.hasRootHeading &&
                Approximately(left.rootPosition, right.rootPosition) &&
                Approximately(left.rootHeading, right.rootHeading) &&
                StringListsEqual(left.jointNames, right.jointNames) &&
                Vector3ListsEqual(left.localAxisAngles, right.localAxisAngles) &&
                IntListsEqual(left.sampledJointIndices, right.sampledJointIndices);
        }

        private static bool StringListsEqual(System.Collections.Generic.IReadOnlyList<string> left, System.Collections.Generic.IReadOnlyList<string> right)
        {
            int leftCount = left != null ? left.Count : 0;
            int rightCount = right != null ? right.Count : 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; i++)
            {
                if (!string.Equals(left[i] ?? string.Empty, right[i] ?? string.Empty, System.StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Vector3ListsEqual(System.Collections.Generic.IReadOnlyList<Vector3> left, System.Collections.Generic.IReadOnlyList<Vector3> right)
        {
            int leftCount = left != null ? left.Count : 0;
            int rightCount = right != null ? right.Count : 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; i++)
            {
                if (!Approximately(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IntListsEqual(System.Collections.Generic.IReadOnlyList<int> left, System.Collections.Generic.IReadOnlyList<int> right)
        {
            int leftCount = left != null ? left.Count : 0;
            int rightCount = right != null ? right.Count : 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return (left - right).sqrMagnitude <= 1e-10f;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 1e-10f;
        }
    }
}
