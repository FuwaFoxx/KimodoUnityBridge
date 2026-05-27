using System;
using System.Collections.Generic;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor.GenerationPipeline
{
    internal sealed class KimodoEditorConstraintProvider
    {
        private static readonly List<KimodoConstraintMarkerBase> LatestMarkerSnapshot = new List<KimodoConstraintMarkerBase>();
        private readonly List<KimodoConstraintMarkerBase> markerBuffer = new List<KimodoConstraintMarkerBase>();

        public static IReadOnlyList<KimodoConstraintMarkerBase> LatestMarkers => LatestMarkerSnapshot;

        public string BuildConstraintsJsonOrThrow(KimodoPlayableClip clip)
        {
            TimelineClip sourceClip = KimodoTimelineClipResolver.FindTimelineClipForAsset(clip);
            if (sourceClip == null)
            {
                UpdateConstraintReferences(null);
                return string.Empty;
            }

            UpdateConstraintReferences(sourceClip);

            bool ok = KimodoInbetweenConstraintUtility.TryBuildConstraintsJson(
                sourceClip,
                clip.enableInbetweenInterpolation,
                clip.generationFrames,
                out string constraintsJson,
                out string error);

            if (!ok)
            {
                throw new InvalidOperationException($"Build constraints failed: {error}");
            }

            return constraintsJson ?? string.Empty;
        }

        public TimelineClip FindTimelineClipForAsset(PlayableAsset asset)
        {
            return KimodoTimelineClipResolver.FindTimelineClipForAsset(asset);
        }

        private void UpdateConstraintReferences(TimelineClip sourceClip)
        {
            markerBuffer.Clear();
            if (sourceClip == null)
            {
                LatestMarkerSnapshot.Clear();
                return;
            }

            TrackAsset track = sourceClip.GetParentTrack();
            if (track == null)
            {
                return;
            }

            double minTime = sourceClip.start;
            double maxTime = sourceClip.end;
            foreach (IMarker marker in track.GetMarkers())
            {
                if (marker is not KimodoConstraintMarkerBase kimodoMarker)
                {
                    continue;
                }

                if (kimodoMarker.time < minTime || kimodoMarker.time > maxTime)
                {
                    continue;
                }

                markerBuffer.Add(kimodoMarker);
            }

            markerBuffer.Sort((a, b) => a.time.CompareTo(b.time));
            LatestMarkerSnapshot.Clear();
            LatestMarkerSnapshot.AddRange(markerBuffer);
        }
    }
}
