using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoBridge.Editor
{
    internal sealed class KimodoTimelineInOutConstraintContext
    {
        public TimelineClip SourceClip;
        public TrackAsset Track;
        public PlayableDirector Director;
        public Animator Animator;
        public Avatar SourceAvatar;
        public string ModelName = KimodoPlayableClip.DefaultBridgeModelName;
        public AnimationClip CurrentClip;
        public TimelineClip PreviousTimelineClip;
        public TimelineClip NextTimelineClip;
        public AnimationClip PreviousClip;
        public AnimationClip NextClip;
        public string PreviousClipWarning = string.Empty;
        public string NextClipWarning = string.Empty;
    }

    internal static class KimodoTimelineInOutConstraintContextUtility
    {
        internal static bool TryResolve(
            TimelineClip sourceClip,
            out KimodoTimelineInOutConstraintContext context,
            out string error)
        {
            context = null;
            error = string.Empty;

            if (sourceClip == null)
            {
                error = "No selected timeline clip for constraint export.";
                return false;
            }

            TrackAsset track = sourceClip.GetParentTrack();
            if (track == null)
            {
                error = "Cannot resolve parent animation track.";
                return false;
            }

            if (!TryResolveDirector(sourceClip, track, out PlayableDirector director, out error))
            {
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null)
            {
                error = "Animation track has no Animator binding.";
                return false;
            }

            KimodoLocalAvatarUtility.AvatarResolveResult avatarResult = KimodoLocalAvatarUtility.ResolveAvatarFromGameObject(animator.gameObject);
            Avatar sourceAvatar = avatarResult.Avatar;
            if (!KimodoRetargetCoreUtility.IsValidHumanoid(sourceAvatar))
            {
                error = $"Resolve source avatar failed: {avatarResult.Error}";
                return false;
            }

            TryResolveNeighborTimelineClips(sourceClip, out TimelineClip previousTimelineClip, out TimelineClip nextTimelineClip);
            TryResolveAnimationClip(previousTimelineClip, out AnimationClip previousClip, out string previousWarning);
            TryResolveAnimationClip(sourceClip, out AnimationClip currentClip, out _);
            TryResolveAnimationClip(nextTimelineClip, out AnimationClip nextClip, out string nextWarning);

            context = new KimodoTimelineInOutConstraintContext
            {
                SourceClip = sourceClip,
                Track = track,
                Director = director,
                Animator = animator,
                SourceAvatar = sourceAvatar,
                ModelName = KimodoPlayableClip.NormalizeBridgeModelName(((KimodoPlayableClip)sourceClip.asset)?.bridgeModelName),
                CurrentClip = currentClip,
                PreviousTimelineClip = previousTimelineClip,
                NextTimelineClip = nextTimelineClip,
                PreviousClip = previousClip,
                NextClip = nextClip,
                PreviousClipWarning = previousWarning,
                NextClipWarning = nextWarning
            };
            return true;
        }

        internal static bool TryResolveDirector(
            TimelineClip sourceClip,
            TrackAsset track,
            out PlayableDirector director,
            out string error)
        {
            director = null;
            error = string.Empty;

            PlayableDirector inspectedDirector = TimelineEditor.inspectedDirector;
            if (inspectedDirector != null)
            {
                director = inspectedDirector;
                return true;
            }

            TimelineAsset timelineAsset = ResolveTimelineAsset(sourceClip, track);
            if (timelineAsset == null)
            {
                error = "Timeline inspected director is null and TimelineAsset cannot be resolved.";
                return false;
            }

            var candidates = new List<PlayableDirector>();
            PlayableDirector[] directors = Resources.FindObjectsOfTypeAll<PlayableDirector>();
            for (int i = 0; i < directors.Length; i++)
            {
                PlayableDirector candidate = directors[i];
                if (candidate == null ||
                    candidate.playableAsset != timelineAsset ||
                    EditorUtility.IsPersistent(candidate) ||
                    candidate.gameObject == null ||
                    !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                error = "Timeline inspected director is null and no scene PlayableDirector references this TimelineAsset.";
                return false;
            }

            PlayableDirector selectedDirector = TryResolveSelectedDirector(candidates);
            if (selectedDirector != null)
            {
                director = selectedDirector;
                return true;
            }

            if (candidates.Count == 1)
            {
                director = candidates[0];
                return true;
            }

            error = "Timeline inspected director is null and multiple scene PlayableDirectors reference this TimelineAsset. Select/open the correct PlayableDirector in Timeline.";
            return false;
        }

        private static TimelineAsset ResolveTimelineAsset(TimelineClip sourceClip, TrackAsset track)
        {
            TrackAsset resolvedTrack = track != null ? track : sourceClip?.GetParentTrack();
            if (resolvedTrack != null && resolvedTrack.timelineAsset != null)
            {
                return resolvedTrack.timelineAsset;
            }

            return TimelineEditor.inspectedAsset;
        }

        private static PlayableDirector TryResolveSelectedDirector(List<PlayableDirector> candidates)
        {
            GameObject selectedGameObject = Selection.activeGameObject;
            if (selectedGameObject == null)
            {
                return null;
            }

            PlayableDirector selectedDirector = selectedGameObject.GetComponent<PlayableDirector>();
            if (selectedDirector != null && candidates.Contains(selectedDirector))
            {
                return selectedDirector;
            }

            selectedDirector = selectedGameObject.GetComponentInParent<PlayableDirector>();
            return selectedDirector != null && candidates.Contains(selectedDirector)
                ? selectedDirector
                : null;
        }

        internal static void TryResolveNeighborTimelineClips(
            TimelineClip sourceClip,
            out TimelineClip previousClip,
            out TimelineClip nextClip)
        {
            previousClip = null;
            nextClip = null;

            TrackAsset track = sourceClip != null ? sourceClip.GetParentTrack() : null;
            if (track == null)
            {
                return;
            }

            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip == null || clip == sourceClip)
                {
                    continue;
                }

                if (clip.end <= sourceClip.start)
                {
                    if (previousClip == null || clip.end > previousClip.end)
                    {
                        previousClip = clip;
                    }
                }

                if (clip.start >= sourceClip.end)
                {
                    if (nextClip == null || clip.start < nextClip.start)
                    {
                        nextClip = clip;
                    }
                }
            }
        }

        internal static bool HasPreviousNeighbor(TimelineClip clip)
        {
            TryResolveNeighborTimelineClips(clip, out TimelineClip previousClip, out _);
            return previousClip != null;
        }

        internal static bool TryResolveAnimationClip(TimelineClip timelineClip, out AnimationClip clip, out string warning)
        {
            clip = null;
            warning = string.Empty;

            if (timelineClip == null)
            {
                return false;
            }

            if (!KimodoMarkerSamplingUtility.TryResolveAnimationClipFromTimelineClip(timelineClip, out clip, out string error))
            {
                warning = string.IsNullOrWhiteSpace(error)
                    ? "Timeline clip does not contain a usable AnimationClip."
                    : error;
                return false;
            }

            return clip != null;
        }

        internal static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
        }
    }
}
