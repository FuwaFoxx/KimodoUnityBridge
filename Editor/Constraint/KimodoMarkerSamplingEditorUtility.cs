using UnityEditor;
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

            marker.SampleData = normalized;
            marker.time = normalized.sampleTime;
            if (keepOverrideEnabled)
            {
                marker.useOverride = true;
            }

            EditorUtility.SetDirty(marker);
            return true;
        }
    }
}
