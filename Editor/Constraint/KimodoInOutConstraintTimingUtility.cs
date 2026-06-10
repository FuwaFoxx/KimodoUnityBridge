using System;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal static class KimodoInOutConstraintTimingUtility
    {
        internal static int ClampFrameCount(int generationFrames)
        {
            return Mathf.Clamp(generationFrames, KimodoPlayableClip.MIN_FRAMES, KimodoPlayableClip.MAX_FRAMES);
        }

        internal static int DurationSecondsToFrameCount(float durationSeconds)
        {
            float minDurationSeconds = FrameCountToDurationSeconds(KimodoPlayableClip.MIN_FRAMES);
            float maxDurationSeconds = FrameCountToDurationSeconds(KimodoPlayableClip.MAX_FRAMES);
            return Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(durationSeconds, minDurationSeconds, maxDurationSeconds) * KimodoPlayableClip.FIXED_FRAME_RATE),
                KimodoPlayableClip.MIN_FRAMES,
                KimodoPlayableClip.MAX_FRAMES);
        }

        internal static float FrameCountToDurationSeconds(int frameCount)
        {
            return Mathf.Max(0, frameCount) / KimodoPlayableClip.FIXED_FRAME_RATE;
        }

        internal static double ResolveConstraintClipDurationSeconds(int frameCount)
        {
            int safeFrameCount = Mathf.Max(1, frameCount);
            return safeFrameCount / KimodoPlayableClip.FIXED_FRAME_RATE;
        }

        internal static double ResolveConstraintEndSampleTimeSeconds(int frameCount)
        {
            int safeFrameCount = Mathf.Max(1, frameCount);
            return Math.Max(0.0, (safeFrameCount - 1) / KimodoPlayableClip.FIXED_FRAME_RATE);
        }

        internal static double ResolveClipSampleTime(AnimationClip clip, double normalizedTime)
        {
            if (clip == null)
            {
                return 0.0;
            }

            double duration = Math.Max(0.0, clip.length);
            if (duration <= 0.0)
            {
                return 0.0;
            }

            double clampedNormalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
            if (clampedNormalizedTime <= 0.0)
            {
                return 0.0;
            }

            if (clampedNormalizedTime >= 1.0)
            {
                double epsilon = Math.Min(1e-3, duration * 0.5);
                return Math.Max(0.0, duration - epsilon);
            }

            return clampedNormalizedTime * duration;
        }
    }
}
