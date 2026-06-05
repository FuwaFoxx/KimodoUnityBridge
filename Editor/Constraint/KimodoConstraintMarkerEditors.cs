using KimodoBridge;
using KimodoBridge.Editor;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using TimelineInject;

namespace KimodoBridge.Editor
{
    internal abstract class KimodoConstraintStandardMarkerEditorBase : UnityEditor.Editor
    {
        protected abstract string TypeLabel { get; }
        protected abstract string TipText { get; }

        private void OnDisable()
        {
            KimodoConstraintMarkerEditorUtility.ClearMarkerPoseCachePreview(target as KimodoConstraintMarkerBase, keepIfOverrideWindowOpen: true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(TipText, MessageType.Info);
            EditorGUILayout.Space(4f);

            DrawCommonHeader(TypeLabel);
            DrawMarkerTime();

            KimodoConstraintMarkerBase markerTarget = target as KimodoConstraintMarkerBase;
            SerializedProperty overrideProp = serializedObject.FindProperty("useOverride");
            bool useOverride = overrideProp != null && overrideProp.boolValue;
            bool windowOpen = KimodoConstraintOverrideEditWindow.IsOpenForMarker(markerTarget);
            if (!useOverride && windowOpen)
            {
                KimodoConstraintOverrideEditWindow openWindow = KimodoConstraintOverrideEditWindow.GetOpenWindow();
                if (openWindow != null && openWindow.TargetMarker == markerTarget)
                {
                    openWindow.Close();
                }
                windowOpen = false;
            }

            if (!useOverride && !windowOpen)
            {
                if (!KimodoConstraintMarkerEditorUtility.TryUpdateAutoSampleMarkerData(markerTarget, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
            }

            DrawFields(!useOverride);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                KimodoConstraintMarkerEditorUtility.NotifyInspectorChanged(target as KimodoConstraintMarkerBase);
            }

            if (!windowOpen && markerTarget != null && !KimodoConstraintMarkerEditorUtility.TryRenderMarkerToPoseCacheIfNeeded(markerTarget, out string poseError) && !string.IsNullOrWhiteSpace(poseError))
            {
                EditorGUILayout.HelpBox($"Pose cache update failed: {poseError}", MessageType.Warning);
            }
        }

        private void DrawCommonHeader(string type)
        {
            EditorGUILayout.LabelField($"Kimodo Constraint Marker ({type})", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useOverride"));
            KimodoConstraintMarkerEditorUtility.DrawOverrideEditButton(serializedObject, target as KimodoConstraintMarkerBase);
            EditorGUILayout.Space(4f);
        }

        private void DrawMarkerTime()
        {
            KimodoConstraintMarkerEditorUtility.DrawSampleTimeField(serializedObject, target as IMarker);
        }

        protected abstract void DrawFields(bool readOnly);
    }

    [CustomEditor(typeof(KimodoFullBodyConstraintMarker))]
    internal sealed class KimodoFullBodyConstraintMarkerEditor : KimodoConstraintStandardMarkerEditorBase
    {
        protected override string TypeLabel => "FullBody";
        protected override string TipText =>
            "Purpose: apply a strong full-body pose constraint at a key frame (root position + local joint rotations).\n" +
            "Recommended when you need the generated motion to match a specific target pose at that frame.";

        protected override void DrawFields(bool readOnly)
        {
            if (readOnly)
            {
                EditorGUILayout.HelpBox("Override disabled. Showing sampled result (read-only).", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(readOnly);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.rootPosition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.localAxisAngles"), true);
            EditorGUI.EndDisabledGroup();
        }
    }

    [CustomEditor(typeof(KimodoRoot2DConstraintMarker))]
    internal sealed class KimodoRoot2DConstraintMarkerEditor : KimodoConstraintStandardMarkerEditorBase
    {
        protected override string TypeLabel => "Root2D";
        protected override string TipText =>
            "Purpose: constrain the character root trajectory on the ground plane (X/Z) at a key frame. Optional heading constraint is supported.\n" +
            "Recommended for path following, locomotion route control, and turn direction control.";

        protected override void DrawFields(bool readOnly)
        {
            if (readOnly)
            {
                EditorGUILayout.HelpBox("Override disabled. Showing sampled result (read-only).", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(readOnly);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.rootPosition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.hasRootHeading"));
            SerializedProperty includeGlobalHeadingProp = serializedObject.FindProperty("sampleData.hasRootHeading");
            if (includeGlobalHeadingProp != null && includeGlobalHeadingProp.boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.rootHeading"));
            }
            EditorGUI.EndDisabledGroup();
        }
    }

    [CustomEditor(typeof(KimodoEndEffectorConstraintMarker), true)]
    internal sealed class KimodoEndEffectorConstraintMarkerEditor : UnityEditor.Editor
    {
        private void OnDisable()
        {
            KimodoConstraintMarkerEditorUtility.ClearMarkerPoseCachePreview(target as KimodoConstraintMarkerBase, keepIfOverrideWindowOpen: true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            string typeName = (target as KimodoEndEffectorConstraintMarker)?.ConstraintType ?? "end-effector";
            bool isCustomEndEffector = string.Equals(typeName, "end-effector", StringComparison.OrdinalIgnoreCase);
            EditorGUILayout.HelpBox(GetTipByType(typeName), MessageType.Info);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Kimodo Constraint Marker ({typeName})", EditorStyles.boldLabel);

            SerializedProperty overrideProp = serializedObject.FindProperty("useOverride");
            if (isCustomEndEffector)
            {
                overrideProp.boolValue = false;
                EditorGUILayout.Toggle(new GUIContent("useOverride", "Disabled for custom end-effector marker; values are sampled from timeline pose."), false);
            }
            else
            {
                EditorGUILayout.PropertyField(overrideProp);
                KimodoConstraintMarkerEditorUtility.DrawOverrideEditButton(serializedObject, target as KimodoConstraintMarkerBase);
            }

            DrawMarkerTime();
            bool useOverride = !isCustomEndEffector && overrideProp != null && overrideProp.boolValue;
            KimodoConstraintMarkerBase markerTarget = target as KimodoConstraintMarkerBase;
            bool windowOpen = KimodoConstraintOverrideEditWindow.IsOpenForMarker(markerTarget);
            if (!useOverride && windowOpen)
            {
                KimodoConstraintOverrideEditWindow openWindow = KimodoConstraintOverrideEditWindow.GetOpenWindow();
                if (openWindow != null && openWindow.TargetMarker == markerTarget)
                {
                    openWindow.Close();
                }
                windowOpen = false;
            }

            if (!useOverride && !windowOpen)
            {
                if (!KimodoConstraintMarkerEditorUtility.TryUpdateAutoSampleMarkerData(markerTarget, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
            }

            if (isCustomEndEffector)
            {
                EditorGUILayout.HelpBox("end-effector has no override mode; sampling from timeline pose.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(useOverride
                    ? "Override enabled. Editing marker values."
                    : "Override disabled. Showing sampled result (read-only).", MessageType.Info);
            }
            DrawEEFields(typeName, !useOverride);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                KimodoConstraintMarkerEditorUtility.NotifyInspectorChanged(target as KimodoConstraintMarkerBase);
            }

            if (!windowOpen && markerTarget != null && !KimodoConstraintMarkerEditorUtility.TryRenderMarkerToPoseCacheIfNeeded(markerTarget, out string poseError) && !string.IsNullOrWhiteSpace(poseError))
            {
                EditorGUILayout.HelpBox($"Pose cache update failed: {poseError}", MessageType.Warning);
            }
        }

        private void DrawMarkerTime()
        {
            KimodoConstraintMarkerEditorUtility.DrawSampleTimeField(serializedObject, target as IMarker);
        }

        private static string GetTipByType(string typeName)
        {
            switch (typeName)
            {
                case "left-hand":
                    return "Purpose: constrain the left-hand end-effector chain position/orientation at a key frame.\nRecommended for grab, wave, and pointing control.";
                case "right-hand":
                    return "Purpose: constrain the right-hand end-effector chain position/orientation at a key frame.\nRecommended for grab, wave, and pointing control.";
                case "left-foot":
                    return "Purpose: constrain the left-foot end-effector chain position/orientation at a key frame.\nRecommended for foot placement, stepping targets, and anti-sliding control.";
                case "right-foot":
                    return "Purpose: constrain the right-foot end-effector chain position/orientation at a key frame.\nRecommended for foot placement, stepping targets, and anti-sliding control.";
                default:
                    return "Purpose: custom end-effector constraint (joint_names can include LeftHand/RightHand/LeftFoot/RightFoot/Hips).\n" +
                           "Recommended for mixed multi-target constraints (for example, hand and foot targets at the same time).";
            }
        }

        private void DrawEEFields(string typeName, bool readOnly)
        {
            EditorGUI.BeginDisabledGroup(readOnly);
            SerializedProperty jointNamesProp = serializedObject.FindProperty("sampleData.jointNames");
            if (jointNamesProp != null && typeName == "end-effector")
            {
                EditorGUILayout.PropertyField(jointNamesProp, true);
            }
            else if (typeName != "end-effector")
            {
                EditorGUILayout.HelpBox("Fixed joint group marker type; joint_names is determined by marker class.", MessageType.None);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.rootPosition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleData.localAxisAngles"), true);
            EditorGUI.EndDisabledGroup();
        }

    }

    internal static class KimodoConstraintMarkerEditorUtility
    {
        public const double KimodoFps = 30.0;
        private static readonly Dictionary<int, AutoSampleCacheEntry> AutoSampleCache = new Dictionary<int, AutoSampleCacheEntry>();
        private static readonly Dictionary<int, string> PoseRenderSignatures = new Dictionary<int, string>();

        private sealed class AutoSampleCacheEntry
        {
            public string Signature;
            public bool Success;
            public string Error;
        }

        private struct MarkerSamplingContext
        {
            public TimelineClip ClipRange;
            public TrackAsset Track;
            public Animator Animator;
            public AnimationClip SourceClip;
            public Avatar SourceAvatar;
            public Avatar TargetAvatar;
            public string ModelName;
        }

        public static double GetLocalSecondsInClip(TimelineClip clipRange, double globalTime)
        {
            if (clipRange == null)
            {
                return 0.0;
            }

            double local = clipRange.ToLocalTime(globalTime);
            if (local < 0.0)
            {
                return 0.0;
            }
            if (local > clipRange.duration)
            {
                return clipRange.duration;
            }
            return local;
        }

        public static bool TryGetClipRangeForMarker(IMarker marker, out TimelineClip clipRange)
        {
            clipRange = null;
            if (marker == null || marker.parent == null || TimelineEditor.inspectedAsset == null)
            {
                return false;
            }

            // Prefer current marker timeline position. If the marker is temporarily outside any clip,
            // fall back to the last sampled time so preview/edit can still recover its previous owner clip.
            if (TryFindClipRangeByTime(marker, marker.time, out clipRange))
            {
                return true;
            }

            if (marker is KimodoConstraintMarkerBase kimodoMarker)
            {
                double hintedTime = kimodoMarker.SampleData != null ? kimodoMarker.SampleData.sampleTime : marker.time;
                if (Math.Abs(hintedTime - marker.time) > 1e-9 && TryFindClipRangeByTime(marker, hintedTime, out clipRange))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindClipRangeByTime(IMarker marker, double time, out TimelineClip clipRange)
        {
            clipRange = null;
            if (marker == null || marker.parent == null || TimelineEditor.inspectedAsset == null)
            {
                return false;
            }

            foreach (TrackAsset track in TimelineEditor.inspectedAsset.GetOutputTracks())
            {
                if (track != marker.parent)
                {
                    continue;
                }

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.asset is AnimationPlayableAsset && time >= clip.start && time <= clip.end)
                    {
                        clipRange = clip;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TrySampleMarkerDataFromMarker(
            KimodoConstraintMarkerBase marker,
            out KimodoMarkerSampleResult sampledData,
            out string error)
        {
            sampledData = null;
            error = string.Empty;

            if (!TryBuildMarkerSamplingContext(marker, out MarkerSamplingContext context, out error))
            {
                return false;
            }

            double sampleTime = marker.time;
            double localSampleTime = GetLocalSecondsInClip(context.ClipRange, sampleTime);
            if (!KimodoMarkerSamplingUtility.TrySampleMarkerFromClipWithRetargetCore(
                    context.SourceClip,
                    marker.ConstraintType,
                    localSampleTime,
                    context.SourceAvatar,
                    context.TargetAvatar,
                    context.ModelName,
                    out KimodoMarkerSampleResult sample,
                    out error))
            {
                return false;
            }

            sample.sampleTime = sampleTime;
            sampledData = KimodoMarkerSamplingUtility.NormalizeConstraintMarkerSample(marker, sample);
            if (sampledData == null)
            {
                error = "failed to build marker sample";
                return false;
            }

            return true;
        }

        public static bool TryUpdateAutoSampleMarkerData(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryBuildAutoSampleSignature(marker, out string signature, out error))
            {
                return false;
            }

            int id = marker.GetInstanceID();
            if (AutoSampleCache.TryGetValue(id, out AutoSampleCacheEntry cached) &&
                string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            {
                error = cached.Error ?? string.Empty;
                return cached.Success;
            }

            if (!TrySampleMarkerDataFromMarker(marker, out KimodoMarkerSampleResult preview, out error))
            {
                AutoSampleCache[id] = new AutoSampleCacheEntry
                {
                    Signature = signature,
                    Success = false,
                    Error = error ?? string.Empty
                };
                return false;
            }

            if (!KimodoMarkerSamplingEditorUtility.TryWriteConstraintMarkerSample(marker, preview, keepOverrideEnabled: false, out error))
            {
                AutoSampleCache[id] = new AutoSampleCacheEntry
                {
                    Signature = signature,
                    Success = false,
                    Error = error ?? string.Empty
                };
                return false;
            }

            AutoSampleCache[id] = new AutoSampleCacheEntry
            {
                Signature = signature,
                Success = true,
                Error = string.Empty
            };
            PoseRenderSignatures.Remove(id);
            return true;
        }

        private static bool TryBuildMarkerSamplingContext(
            KimodoConstraintMarkerBase marker,
            out MarkerSamplingContext context,
            out string error)
        {
            context = default;
            error = string.Empty;

            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryGetClipRangeForMarker(marker, out TimelineClip clipRange) || clipRange == null)
            {
                error = "clip range not found";
                return false;
            }

            TrackAsset track = clipRange.GetParentTrack();
            if (track == null)
            {
                error = "parent track not found";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null || animator.transform == null)
            {
                error = "Animation track has no Animator binding.";
                return false;
            }

            AnimationClip sourceClip = clipRange.asset is AnimationPlayableAsset playableAsset ? playableAsset.clip : null;
            if (sourceClip == null)
            {
                error = "Animation clip is missing.";
                return false;
            }

            string modelName = ResolveModelName(clipRange);
            KimodoLocalAvatarUtility.AvatarResolveResult sourceAvatarResult = KimodoLocalAvatarUtility.ResolveAvatarFromGameObject(animator.gameObject);
            Avatar sourceAvatar = sourceAvatarResult.Avatar;
            if (sourceAvatar == null || !sourceAvatar.isValid || !sourceAvatar.isHuman)
            {
                error = $"Resolve source avatar failed: {sourceAvatarResult.Error}";
                return false;
            }

            if (!KimodoRuntimeAvatarSkeletonBuilder.TryLoadAvatarByModelName(modelName, out Avatar targetAvatar, out string targetError))
            {
                error = $"Resolve target avatar failed: {targetError}";
                return false;
            }

            context = new MarkerSamplingContext
            {
                ClipRange = clipRange,
                Track = track,
                Animator = animator,
                SourceClip = sourceClip,
                SourceAvatar = sourceAvatar,
                TargetAvatar = targetAvatar,
                ModelName = modelName
            };
            return true;
        }

        private static bool TryBuildAutoSampleSignature(KimodoConstraintMarkerBase marker, out string signature, out string error)
        {
            signature = string.Empty;
            if (!TryBuildMarkerSamplingContext(marker, out MarkerSamplingContext context, out error))
            {
                return false;
            }

            double globalTime = Math.Max(0.0, marker.time);
            double localTime = GetLocalSecondsInClip(context.ClipRange, globalTime);
            KimodoMarkerSampleResult sampleData = marker.SampleData;
            int clipAssetId = context.ClipRange.asset is UnityEngine.Object clipAsset ? clipAsset.GetInstanceID() : 0;
            signature = string.Join("|",
                marker.GetInstanceID().ToString(CultureInfo.InvariantCulture),
                marker.ConstraintType ?? string.Empty,
                FormatDouble(globalTime),
                FormatDouble(localTime),
                context.ModelName ?? string.Empty,
                context.Track != null ? context.Track.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0",
                context.Animator != null ? context.Animator.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0",
                context.SourceClip != null ? context.SourceClip.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0",
                clipAssetId.ToString(CultureInfo.InvariantCulture),
                context.SourceAvatar != null ? context.SourceAvatar.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0",
                context.TargetAvatar != null ? context.TargetAvatar.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0",
                FormatDouble(context.ClipRange.start),
                FormatDouble(context.ClipRange.duration),
                FormatDouble(context.ClipRange.clipIn),
                FormatDouble(context.ClipRange.timeScale),
                context.SourceClip != null ? FormatFloat(context.SourceClip.length) : "0",
                context.SourceClip != null ? FormatFloat(context.SourceClip.frameRate) : "0",
                sampleData != null && sampleData.hasRootHeading ? "1" : "0",
                BuildStringListSignature(sampleData != null ? sampleData.jointNames : null));
            return true;
        }

        public static void MoveMarkerToTime(IMarker marker, double globalTime)
        {
            if (marker == null)
            {
                return;
            }

            if (marker is KimodoConstraintMarkerBase kimodoMarker)
            {
                ClearMarkerEditorCaches(kimodoMarker);
                KimodoConstraintPoseCache.DestroyEntriesForItemId(kimodoMarker.GetInstanceID().ToString());
                kimodoMarker.time = globalTime;
                kimodoMarker.SampleData.sampleTime = Math.Max(0.0, globalTime);
            }

            UnityEngine.Object markerObject = marker as UnityEngine.Object;
            UnityEngine.Object parentTrackObject = marker.parent as UnityEngine.Object;

            if (markerObject != null)
            {
                Undo.RecordObject(markerObject, "Move Kimodo Constraint Marker");
            }
            if (parentTrackObject != null)
            {
                Undo.RecordObject(parentTrackObject, "Move Kimodo Constraint Marker");
            }


            if (markerObject != null)
            {
                EditorUtility.SetDirty(markerObject);
            }
            if (parentTrackObject != null)
            {
                EditorUtility.SetDirty(parentTrackObject);
            }

            if (TimelineEditor.inspectedAsset != null)
            {
                EditorUtility.SetDirty(TimelineEditor.inspectedAsset);
            }

            TimelineEditor.Refresh(RefreshReason.ContentsModified);
            SceneView.RepaintAll();
        }

        public static void DrawSampleTimeField(SerializedObject so, IMarker marker)
        {
            if (so == null || marker == null)
            {
                return;
            }

            SerializedProperty timeProp = so.FindProperty("sampleData.sampleTime");
            if (timeProp == null)
            {
                return;
            }

            // Keep stored sample time aligned with marker timeline position.
            double markerTime = Math.Max(0.0, marker.time);
            if (Math.Abs(timeProp.doubleValue - markerTime) > 1e-9)
            {
                timeProp.doubleValue = markerTime;
            }

            double sourceTime = Math.Max(0.0, timeProp.doubleValue);
            if (Math.Abs(timeProp.doubleValue - sourceTime) > 1e-9)
            {
                timeProp.doubleValue = sourceTime;
            }

            double displayCurrent = Math.Round(sourceTime, 4, MidpointRounding.AwayFromZero);
            double displaySampleTime = Math.Max(0.0, marker.time);
            if (TryGetClipRangeForMarker(marker, out TimelineClip clipRange) && clipRange != null)
            {
                displaySampleTime = GetLocalSecondsInClip(clipRange, marker.time);
            }
            displaySampleTime = Math.Round(displaySampleTime, 4, MidpointRounding.AwayFromZero);

            double editedTime = EditorGUILayout.DoubleField(
                new GUIContent("Marker Time (seconds)", "Absolute timeline time stored in marker data and used by preview/edit. Allowed range: [0, +inf)."),
                displayCurrent);
            double normalizedEdited = Math.Max(0.0, editedTime);
            EditorGUILayout.LabelField($"Sample Time: {displaySampleTime:F4}s", EditorStyles.miniLabel);
            if (Math.Abs(normalizedEdited - sourceTime) > 1e-9)
            {
                MoveMarkerToTime(marker, normalizedEdited);

                // Refresh SerializedObject cache after direct marker.time mutation to avoid stale writeback.
                so.UpdateIfRequiredOrScript();
                SerializedProperty refreshedTimeProp = so.FindProperty("sampleData.sampleTime");
                if (refreshedTimeProp != null)
                {
                    refreshedTimeProp.doubleValue = normalizedEdited;
                }
            }
        }

        public static void NotifyInspectorChanged(KimodoConstraintMarkerBase marker)
        {
            if (marker != null)
            {
                ClearMarkerEditorCaches(marker);
                EditorUtility.SetDirty(marker);
            }

            SceneView.RepaintAll();
        }

        public static void ClearMarkerPoseCachePreview(KimodoConstraintMarkerBase marker, bool keepIfOverrideWindowOpen)
        {
            if (marker == null)
            {
                return;
            }

            ClearMarkerEditorCaches(marker);

            if (keepIfOverrideWindowOpen && KimodoConstraintOverrideEditWindow.IsOpenForMarker(marker))
            {
                return;
            }

            KimodoConstraintPoseCache.DestroyEntriesForItemId(marker.GetInstanceID().ToString());
            SceneView.RepaintAll();
        }

        public static bool TryBuildRenderContextForMarker(KimodoConstraintMarkerBase marker, out PoseCacheRenderContext context, out string error)
        {
            context = default;
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryGetClipRangeForMarker(marker, out TimelineClip clipRange) || clipRange == null)
            {
                error = "clip range not found";
                return false;
            }

            TrackAsset track = clipRange.GetParentTrack();
            if (track == null)
            {
                error = "parent track not found";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null)
            {
                error = "animation track has no animator binding";
                return false;
            }

            KimodoPlayableClip playableClip = clipRange.asset as KimodoPlayableClip;
            string modelName = ResolveModelName(clipRange);
            KimodoConstraintRigType rigType = KimodoRigProfileDatabase.ResolveRigTypeFromModelName(modelName);
            int clipContextId = playableClip != null
                ? playableClip.GetInstanceID()
                : ((clipRange.asset as UnityEngine.Object) != null
                    ? (clipRange.asset as UnityEngine.Object).GetInstanceID()
                    : track.GetInstanceID());
            context = new PoseCacheRenderContext(clipContextId, animator.GetInstanceID(), modelName, rigType);
            return true;
        }

        public static bool TryRenderMarkerToPoseCacheIfNeeded(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (!TryBuildMarkerRenderSignature(marker, out string signature, out error))
            {
                return false;
            }

            int id = marker.GetInstanceID();
            if (PoseRenderSignatures.TryGetValue(id, out string cached) &&
                string.Equals(cached, signature, StringComparison.Ordinal))
            {
                return true;
            }

            if (!TryRenderMarkerToPoseCache(marker, out error))
            {
                return false;
            }

            PoseRenderSignatures[id] = signature;
            return true;
        }

        private static bool TryBuildMarkerRenderSignature(KimodoConstraintMarkerBase marker, out string signature, out string error)
        {
            signature = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryBuildRenderContextForMarker(marker, out PoseCacheRenderContext context, out error))
            {
                return false;
            }

            if (!KimodoMarkerSamplingUtility.TryNormalizeConstraintMarkerSample(marker, marker.SampleData, out KimodoMarkerSampleResult sample, out error))
            {
                return false;
            }

            signature = string.Join("|",
                marker.GetInstanceID().ToString(CultureInfo.InvariantCulture),
                context.ClipId.ToString(CultureInfo.InvariantCulture),
                context.AnimatorId.ToString(CultureInfo.InvariantCulture),
                context.ModelName ?? string.Empty,
                context.RigType.ToString(),
                BuildSampleSignature(sample));
            return true;
        }

        public static bool TryBuildRenderContextForPlayableClip(
            KimodoPlayableClip playableClip,
            out PoseCacheRenderContext context,
            out TimelineClip timelineClip,
            out string error)
        {
            context = default;
            timelineClip = null;
            error = string.Empty;
            if (playableClip == null)
            {
                error = "playable clip is null";
                return false;
            }

            timelineClip = KimodoTimelineClipResolver.FindTimelineClipForAsset(playableClip);
            if (timelineClip == null)
            {
                error = "timeline clip not found for playable clip";
                return false;
            }

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
            {
                error = "parent track not found";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            Animator animator = director.GetGenericBinding(track) as Animator;
            if (animator == null)
            {
                error = "animation track has no animator binding";
                return false;
            }

            string modelName = string.IsNullOrWhiteSpace(playableClip.bridgeModelName)
                ? "Kimodo-SOMA-RP-v1"
                : playableClip.bridgeModelName.Trim();
            KimodoConstraintRigType rigType = KimodoRigProfileDatabase.ResolveRigTypeFromModelName(modelName);
            context = new PoseCacheRenderContext(playableClip.GetInstanceID(), animator.GetInstanceID(), modelName, rigType);
            return true;
        }

        public static bool TryRenderMarkerToPoseCache(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryBuildRenderContextForMarker(marker, out PoseCacheRenderContext context, out error))
            {
                return false;
            }

            KimodoConstraintPoseCache.DestroyEntriesForItemId(marker.GetInstanceID().ToString(), context);

            if (!KimodoMarkerSamplingUtility.TryNormalizeConstraintMarkerSample(marker, marker.SampleData, out KimodoMarkerSampleResult sample, out error))
            {
                return false;
            }

            var item = new PoseCacheRenderItem
            {
                EntryId = marker.GetInstanceID().ToString(),
                SampleData = sample,
                ConstraintType = marker.ConstraintType,
                HighlightJoints = KimodoMarkerSamplingUtility.BuildHighlightJointsForMarker(marker, context.ModelName),
                Visible = true
            };
            var batch = new List<PoseCacheRenderItem>(1) { item };
            return KimodoConstraintPoseCache.RenderBatch(context, batch, out error);
        }

        public static bool TryRenderMarkersBatchToPoseCache(
            PoseCacheRenderContext context,
            IReadOnlyList<KimodoConstraintMarkerBase> markers,
            out string error)
        {
            error = string.Empty;
            if (markers == null || markers.Count == 0)
            {
                KimodoConstraintPoseCache.SetGroupState(context, visible: false, selectable: false);
                return true;
            }

            var items = new List<PoseCacheRenderItem>(markers.Count);
            for (int i = 0; i < markers.Count; i++)
            {
                KimodoConstraintMarkerBase marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                KimodoConstraintPoseCache.DestroyEntriesForItemId(marker.GetInstanceID().ToString(), context);

                if (!KimodoMarkerSamplingUtility.TryNormalizeConstraintMarkerSample(marker, marker.SampleData, out KimodoMarkerSampleResult sample, out string normalizeError))
                {
                    error = normalizeError;
                    return false;
                }

                items.Add(new PoseCacheRenderItem
                {
                    EntryId = marker.GetInstanceID().ToString(),
                    SampleData = sample,
                    ConstraintType = marker.ConstraintType,
                    HighlightJoints = KimodoMarkerSamplingUtility.BuildHighlightJointsForMarker(marker, context.ModelName),
                    Visible = true
                });
            }

            return KimodoConstraintPoseCache.RenderBatch(context, items, out error);
        }

        public static void DrawOverrideEditButton(SerializedObject so, KimodoConstraintMarkerBase marker)
        {
            if (so == null || marker == null)
            {
                return;
            }

            bool windowOpen = KimodoConstraintOverrideEditWindow.IsOpenForMarker(marker);
            string label = windowOpen ? "Reopen Edit" : "Edit";
            if (GUILayout.Button(new GUIContent(label, "Open pose edit window. This enables useOverride automatically if needed."), GUILayout.Height(22f)))
            {
                SerializedProperty overrideProp = so.FindProperty("useOverride");
                if (overrideProp != null && !overrideProp.boolValue)
                {
                    overrideProp.boolValue = true;
                    so.ApplyModifiedProperties();
                    ClearMarkerEditorCaches(marker);
                }

                if (marker.useOverride)
                {
                    KimodoConstraintOverrideEditWindow.ShowWindow(marker);
                }
            }
        }

        private static string ResolveModelName(TimelineClip clipRange)
        {
            KimodoPlayableClip playableClip = clipRange != null ? clipRange.asset as KimodoPlayableClip : null;
            return playableClip != null && !string.IsNullOrWhiteSpace(playableClip.bridgeModelName)
                ? playableClip.bridgeModelName.Trim()
                : "Kimodo-SOMA-RP-v1";
        }

        private static void ClearMarkerEditorCaches(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return;
            }

            int id = marker.GetInstanceID();
            AutoSampleCache.Remove(id);
            PoseRenderSignatures.Remove(id);
        }

        private static string BuildSampleSignature(KimodoMarkerSampleResult sample)
        {
            if (sample == null)
            {
                return string.Empty;
            }

            return string.Join("|",
                sample.constraintType ?? string.Empty,
                FormatDouble(sample.sampleTime),
                sample.rigType.ToString(),
                sample.hasRootHeading ? "1" : "0",
                FormatVector3(sample.rootPosition),
                FormatVector2(sample.rootHeading),
                BuildStringListSignature(sample.jointNames),
                BuildVector3ListSignature(sample.localAxisAngles),
                BuildIntListSignature(sample.sampledJointIndices));
        }

        private static string BuildStringListSignature(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", values);
        }

        private static string BuildVector3ListSignature(IReadOnlyList<Vector3> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                parts[i] = FormatVector3(values[i]);
            }

            return string.Join(",", parts);
        }

        private static string BuildIntListSignature(IReadOnlyList<int> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(",", parts);
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"{FormatFloat(value.x)},{FormatFloat(value.y)}";
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"{FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)}";
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}

