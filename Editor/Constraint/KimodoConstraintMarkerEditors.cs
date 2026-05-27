using System;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal abstract class KimodoConstraintStandardMarkerEditorBase : UnityEditor.Editor
    {
        protected abstract string TypeLabel { get; }
        protected abstract string TipText { get; }

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
                if (!KimodoConstraintMarkerEditorUtility.TrySampleMarkerDataFromMarker(markerTarget, out KimodoMarkerSampleResult preview, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
                else
                {
                    KimodoConstraintMarkerPoseMapper.TryWriteSample(markerTarget, preview, keepOverrideEnabled: false, out _);
                }
            }

            if (!windowOpen && markerTarget != null && !KimodoConstraintPoseCache.TryShowOrUpdateFromMarkerData(markerTarget, out string poseError) && !string.IsNullOrWhiteSpace(poseError))
            {
                EditorGUILayout.HelpBox($"Pose cache update failed: {poseError}", MessageType.Warning);
            }

            DrawFields(!useOverride);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                KimodoConstraintMarkerEditorUtility.NotifyInspectorChanged(target as KimodoConstraintMarkerBase);
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
                if (!KimodoConstraintMarkerEditorUtility.TrySampleMarkerDataFromMarker(markerTarget, out KimodoMarkerSampleResult preview, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
                else
                {
                    KimodoConstraintMarkerPoseMapper.TryWriteSample(markerTarget, preview, keepOverrideEnabled: false, out _);
                }
            }

            if (!windowOpen && markerTarget != null && !KimodoConstraintPoseCache.TryShowOrUpdateFromMarkerData(markerTarget, out string poseError) && !string.IsNullOrWhiteSpace(poseError))
            {
                EditorGUILayout.HelpBox($"Pose cache update failed: {poseError}", MessageType.Warning);
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

            foreach (TrackAsset track in TimelineEditor.inspectedAsset.GetOutputTracks())
            {
                if (track != marker.parent)
                {
                    continue;
                }

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.asset is AnimationPlayableAsset && marker.time >= clip.start && marker.time <= clip.end)
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

            double sampleTime = marker.time;

            double originalTime = director.time;
            DirectorWrapMode originalWrap = director.extrapolationMode;
            KimodoMarkerSampleResult sample;
            try
            {
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.time = sampleTime;
                director.Evaluate();

                string modelName = clipRange.asset is KimodoPlayableClip playableClip
                    ? playableClip.bridgeModelName
                    : "Kimodo-SOMA-RP-v1";

                if (!KimodoMarkerSamplingUtility.TrySampleMarker(
                        animator,
                        animator.transform,
                        clipRange,
                        modelName,
                        sampleTime,
                        marker.ConstraintType,
                        out sample,
                        out error))
                {
                    return false;
                }
            }
            finally
            {
                director.time = originalTime;
                director.Evaluate();
                director.extrapolationMode = originalWrap;
            }

            sample.sampleTime = sampleTime;
            sampledData = KimodoConstraintMarkerPoseMapper.NormalizeSample(marker, sample);
            if (sampledData == null)
            {
                error = "failed to build marker sample";
                return false;
            }

            return true;
        }

        public static void MoveMarkerToTime(IMarker marker, double globalTime)
        {
            if (marker == null)
            {
                return;
            }

            Undo.RecordObject(marker as UnityEngine.Object, "Move Kimodo Constraint Marker");
            marker.time = globalTime;
            EditorUtility.SetDirty(marker as UnityEngine.Object);
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

            if (!TryGetClipRangeForMarker(marker, out TimelineClip clipRange))
            {
                EditorGUILayout.HelpBox("Owning AnimationPlayableAsset not found. time keeps current value.", MessageType.Warning);
                EditorGUILayout.PropertyField(timeProp);
                return;
            }

            double localSeconds = GetLocalSecondsInClip(clipRange, marker.time);
            double clipStart = clipRange.start;
            double clipEnd = clipRange.end;
            double clampedCurrent = Math.Max(clipStart, Math.Min(clipEnd, marker.time));
            timeProp.doubleValue = clampedCurrent;
            double displayCurrent = Math.Round(clampedCurrent, 4, MidpointRounding.AwayFromZero);

            double editedTime = EditorGUILayout.DoubleField(
                new GUIContent("Sample Time (seconds)", "Stored in marker data and used by preview/edit. Clamped to clip range."),
                displayCurrent);
            EditorGUILayout.LabelField($"Marker Local Time: {localSeconds:F3}s   Clip Start: {clipRange.start:F3}s", EditorStyles.miniLabel);
            if (Math.Abs(editedTime - clampedCurrent) > 1e-9)
            {
                double clamped = Math.Max(clipStart, Math.Min(clipEnd, editedTime));
                timeProp.doubleValue = clamped;
                MoveMarkerToTime(marker, clamped);
            }
        }

        public static void NotifyInspectorChanged(KimodoConstraintMarkerBase marker)
        {
            if (marker != null)
            {
                EditorUtility.SetDirty(marker);
            }

            SceneView.RepaintAll();
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
                }

                if (marker.useOverride)
                {
                    KimodoConstraintOverrideEditWindow.ShowWindow(marker);
                }
            }
        }
    }
}

