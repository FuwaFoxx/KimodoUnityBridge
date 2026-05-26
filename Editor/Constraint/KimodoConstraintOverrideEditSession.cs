using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    [InitializeOnLoad]
    internal static class KimodoConstraintOverrideEditSession
    {
        private static readonly Dictionary<int, SessionData> Sessions = new Dictionary<int, SessionData>();

        static KimodoConstraintOverrideEditSession()
        {
            AssemblyReloadEvents.beforeAssemblyReload += EndAllWithoutCommit;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += EndAllWithoutCommit;
            EditorApplication.update += OnEditorUpdate;
        }

        internal static bool TryBegin(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            int key = marker.GetInstanceID();
            if (Sessions.ContainsKey(key))
            {
                KimodoConstraintOverrideEditWindow.ShowWindow(marker);
                return true;
            }

            if (!TryBuildSession(marker, out SessionData session, out error))
            {
                return false;
            }

            Sessions[key] = session;
            KimodoConstraintOverrideEditWindow.ShowWindow(marker);
            KimodoConstraintMarkerEventHub.RaiseMarkerChanged(marker, MarkerChangeReason.SessionStateChanged);
            KimodoConstraintMarkerEditorUtility.NotifyInspectorChanged(marker);
            return true;
        }

        internal static bool HasActiveSession(KimodoConstraintMarkerBase marker)
        {
            return marker != null && Sessions.ContainsKey(marker.GetInstanceID());
        }

        internal static bool HasAnyActiveSession()
        {
            return Sessions.Count > 0;
        }

        internal static void AppendActiveMarkers(List<KimodoConstraintMarkerBase> output)
        {
            if (output == null || Sessions.Count == 0)
            {
                return;
            }

            var existing = new HashSet<int>();
            for (int i = 0; i < output.Count; i++)
            {
                if (output[i] != null)
                {
                    existing.Add(output[i].GetInstanceID());
                }
            }

            foreach (KeyValuePair<int, SessionData> kv in Sessions)
            {
                SessionData session = kv.Value;
                if (session?.Marker == null)
                {
                    continue;
                }

                int id = session.Marker.GetInstanceID();
                if (existing.Add(id))
                {
                    output.Add(session.Marker);
                }
            }
        }

        internal static bool TryGetSession(KimodoConstraintMarkerBase marker, out SessionData session)
        {
            session = null;
            return marker != null && Sessions.TryGetValue(marker.GetInstanceID(), out session) && session != null;
        }

        internal static bool TryCommit(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (!TryGetSession(marker, out SessionData session))
            {
                error = "session not found";
                return false;
            }

            if (!CapturePoseAndWriteBack(session, out error))
            {
                return false;
            }

            session.Marker.useOverride = true;
            EditorUtility.SetDirty(session.Marker);
            AssetDatabase.SaveAssets();
            EndSession(session, keepMarkerChanges: true);
            return true;
        }

        internal static void Cancel(KimodoConstraintMarkerBase marker)
        {
            if (!TryGetSession(marker, out SessionData session))
            {
                return;
            }

            EndSession(session, keepMarkerChanges: false);
        }

        internal static void PingSession(KimodoConstraintMarkerBase marker)
        {
            if (!TryGetSession(marker, out SessionData session))
            {
                return;
            }

            session.LastPingTime = EditorApplication.timeSinceStartup;
        }

        internal static string DescribeMarker(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return "(null)";
            }

            return $"{marker.name} ({marker.ConstraintType}) @ {marker.time:F3}s";
        }

        private static bool TryBuildSession(KimodoConstraintMarkerBase marker, out SessionData session, out string error)
        {
            session = null;
            error = string.Empty;

            if (!KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(marker, out TimelineClip clipRange) || clipRange == null)
            {
                error = "clip range not found";
                return false;
            }

            if (!(clipRange.asset is KimodoPlayableClip playableClip))
            {
                error = "clip asset is not KimodoPlayableClip";
                return false;
            }

            if (!TryResolveSessionBinding(marker, clipRange, out PlayableDirector director, out Animator boundAnimator, out error))
            {
                return false;
            }

            if (!TrySampleCurrentPreviewPose(marker, clipRange, director, boundAnimator, out KimodoMarkerSampleResult initialPose, out error))
            {
                return false;
            }

            // Seed marker override data at session start so strict preview path always has complete pose data.
            if (!KimodoConstraintMarkerPoseMapper.TryWriteSample(marker, initialPose, keepOverrideEnabled: true, out error))
            {
                return false;
            }

            string serializedMarkerSnapshot = EditorJsonUtility.ToJson(marker);
            if (string.IsNullOrWhiteSpace(serializedMarkerSnapshot))
            {
                error = "failed to snapshot marker";
                return false;
            }

            marker.useOverride = true;
            EditorUtility.SetDirty(marker);

            EnsureAnimationModeOn();
            SceneView.RepaintAll();

            session = new SessionData
            {
                Marker = marker,
                ClipRange = clipRange,
                PlayableClip = playableClip,
                Director = director,
                BoundAnimator = boundAnimator,
                SerializedMarkerSnapshot = serializedMarkerSnapshot,
                LastPoseSignature = ComputePoseSignature(initialPose),
                LastPingTime = EditorApplication.timeSinceStartup,
                FixedMarkerTime = marker.time
            };
            return true;
        }

        private static void OnEditorUpdate()
        {
            if (Sessions.Count == 0)
            {
                return;
            }

            var removed = new List<int>();
            foreach (KeyValuePair<int, SessionData> kv in Sessions)
            {
                SessionData session = kv.Value;
                if (session == null || session.Marker == null || session.BoundAnimator == null)
                {
                    removed.Add(kv.Key);
                    continue;
                }

                if (!CapturePoseAndWriteBack(session, out _))
                {
                    continue;
                }
            }

            for (int i = 0; i < removed.Count; i++)
            {
                Sessions.Remove(removed[i]);
            }
        }

        private static bool CapturePoseAndWriteBack(SessionData session, out string error)
        {
            error = string.Empty;
            if (session?.Marker == null || session.BoundAnimator == null)
            {
                error = "session is invalid";
                return false;
            }

            if (System.Math.Abs(session.Marker.time - session.FixedMarkerTime) > 1e-6)
            {
                session.Marker.time = session.FixedMarkerTime;
            }

            if (!TrySampleCurrentPreviewPose(session.Marker, session.ClipRange, session.Director, session.BoundAnimator, out KimodoMarkerSampleResult pose, out error, session.FixedMarkerTime))
            {
                return false;
            }

            int currentSig = ComputePoseSignature(pose);
            if (currentSig == session.LastPoseSignature)
            {
                return true;
            }

            session.LastPoseSignature = currentSig;

            if (!KimodoConstraintMarkerPoseMapper.TryWriteSample(session.Marker, pose, keepOverrideEnabled: true, out error))
            {
                return false;
            }

            KimodoConstraintMarkerEventHub.RaiseMarkerChanged(session.Marker, MarkerChangeReason.DataChanged);
            SceneView.RepaintAll();
            return true;
        }

        private static bool TryResolveSessionBinding(
            KimodoConstraintMarkerBase marker,
            TimelineClip clipRange,
            out PlayableDirector director,
            out Animator boundAnimator,
            out string error)
        {
            director = null;
            boundAnimator = null;
            error = string.Empty;

            if (marker == null || clipRange == null)
            {
                error = "invalid marker or clip";
                return false;
            }

            TrackAsset track = clipRange.GetParentTrack();
            if (track == null && marker.parent is TrackAsset markerTrack)
            {
                track = markerTrack;
            }
            if (track == null)
            {
                error = "parent track not found";
                return false;
            }

            director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            boundAnimator = director.GetGenericBinding(track) as Animator;
            if (boundAnimator != null && boundAnimator.transform != null)
            {
                return true;
            }

            TrackAsset current = track.parent as TrackAsset;
            while (current != null)
            {
                boundAnimator = director.GetGenericBinding(current) as Animator;
                if (boundAnimator != null && boundAnimator.transform != null)
                {
                    return true;
                }

                current = current.parent as TrackAsset;
            }

            error = "track has no animator binding on self track or parent tracks";
            return false;
        }

        private static bool TrySampleCurrentPreviewPose(
            KimodoConstraintMarkerBase marker,
            TimelineClip clipRange,
            PlayableDirector director,
            Animator boundAnimator,
            out KimodoMarkerSampleResult pose,
            out string error,
            double? fixedTime = null)
        {
            pose = null;
            error = string.Empty;

            if (marker == null || clipRange == null || boundAnimator == null || boundAnimator.transform == null)
            {
                error = "invalid marker/clip/animator";
                return false;
            }

            double sampleTime = fixedTime ?? marker.time;
            int frameIndex = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, sampleTime);
            sampleTime = KimodoConstraintPosePipeline.ResolveSampleTimeFromFrameIndex(clipRange, frameIndex);
            if (director != null)
            {
                director.time = sampleTime;
                director.Evaluate();
            }

            if (!KimodoConstraintPosePipeline.TrySampleUnityPoseForMarkerContext(
                    clipRange,
                    boundAnimator,
                    boundAnimator.transform,
                    sampleTime,
                    marker.ConstraintType,
                    out pose,
                    out error))
            {
                return false;
            }

            pose.frameIndex = frameIndex;
            pose.constraintType = marker.ConstraintType;

            return pose != null;
        }

        private static int ComputePoseSignature(KimodoMarkerSampleResult pose)
        {
            unchecked
            {
                int hash = 486187739;
                if (pose == null)
                {
                    return hash;
                }

                Vector3 p = pose.rootPosition;
                Vector2 h = pose.rootHeading;
                hash = hash * 31 + Quantize(p.x);
                hash = hash * 31 + Quantize(p.y);
                hash = hash * 31 + Quantize(p.z);
                hash = hash * 31 + Quantize(h.x);
                hash = hash * 31 + Quantize(h.y);

                int count = pose.localAxisAngles != null ? pose.localAxisAngles.Count : 0;
                hash = hash * 31 + count;
                int maxSample = Mathf.Min(count, 40);
                for (int i = 0; i < maxSample; i++)
                {
                    Vector3 aa = pose.localAxisAngles[i];
                    hash = hash * 31 + Quantize(aa.x);
                    hash = hash * 31 + Quantize(aa.y);
                    hash = hash * 31 + Quantize(aa.z);
                }

                return hash;
            }
        }

        private static int Quantize(float v)
        {
            return Mathf.RoundToInt(v * 100000f);
        }

        private static void EndAllWithoutCommit()
        {
            if (Sessions.Count == 0)
            {
                return;
            }

            var sessions = new List<SessionData>(Sessions.Values);
            Sessions.Clear();
            for (int i = 0; i < sessions.Count; i++)
            {
                EndSession(sessions[i], keepMarkerChanges: false, removeFromMap: false);
            }
        }

        private static void EndSession(SessionData session, bool keepMarkerChanges, bool removeFromMap = true)
        {
            if (session == null)
            {
                return;
            }

            if (session.Marker != null && !keepMarkerChanges && !string.IsNullOrWhiteSpace(session.SerializedMarkerSnapshot))
            {
                EditorJsonUtility.FromJsonOverwrite(session.SerializedMarkerSnapshot, session.Marker);
                EditorUtility.SetDirty(session.Marker);
            }

            if (removeFromMap && session.Marker != null)
            {
                Sessions.Remove(session.Marker.GetInstanceID());
            }

            KimodoConstraintMarkerEventHub.RaiseMarkerChanged(session.Marker, MarkerChangeReason.SessionStateChanged);
            EnsureAnimationModeOffWhenNoSessions();
            KimodoConstraintMarkerEditorUtility.NotifyInspectorChanged(session.Marker);
            SceneView.RepaintAll();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange _)
        {
            EndAllWithoutCommit();
        }

        private static void EnsureAnimationModeOn()
        {
            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }
        }

        private static void EnsureAnimationModeOffWhenNoSessions()
        {
            if (Sessions.Count == 0 && AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
        }

        internal sealed class SessionData
        {
            public KimodoConstraintMarkerBase Marker;
            public TimelineClip ClipRange;
            public KimodoPlayableClip PlayableClip;
            public PlayableDirector Director;
            public Animator BoundAnimator;
            public string SerializedMarkerSnapshot;
            public int LastPoseSignature;
            public double LastPingTime;
            public double FixedMarkerTime;
        }
    }
}
