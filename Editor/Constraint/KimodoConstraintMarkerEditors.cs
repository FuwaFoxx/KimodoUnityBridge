using System;
using KimodoUnityMotionTools.ProjectEditor.Manager;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    [CustomEditor(typeof(KimodoRoot2DConstraintMarker))]
    internal sealed class KimodoRoot2DConstraintMarkerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Purpose: constrain the character root trajectory on the ground plane (X/Z) at a key frame. Optional heading constraint is supported.\n" +
                "Recommended for path following, locomotion route control, and turn direction control.",
                MessageType.Info);
            EditorGUILayout.Space(4f);

            DrawCommonHeader("Root2D");
            DrawAutoFrameIndex();

            KimodoConstraintMarkerBase markerTarget = target as KimodoConstraintMarkerBase;
            SerializedProperty overrideProp = serializedObject.FindProperty("useOverride");
            bool useOverride = overrideProp != null && overrideProp.boolValue;
            bool activeEditSession = KimodoConstraintOverrideEditSession.HasActiveSession(markerTarget);
            if (!useOverride && !activeEditSession)
            {
                if (!KimodoConstraintExportUtility.TryBuildAutoConstraintPreview(markerTarget, out KimodoConstraintJson preview, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
                else
                {
                    ApplyPreviewToTarget(preview);
                    serializedObject.Update();
                }
            }

            DrawRoot2DFields(!useOverride);

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

        private void DrawAutoFrameIndex()
        {
            SerializedProperty frameProp = serializedObject.FindProperty("frameIndex");
            if (frameProp == null)
            {
                return;
            }

            if (!KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(target as IMarker, out TimelineClip clipRange))
            {
                EditorGUILayout.HelpBox("Owning AnimationPlayableAsset not found. frameIndex keeps current value.", MessageType.Warning);
                EditorGUILayout.PropertyField(frameProp);
                return;
            }

            double localSeconds = KimodoConstraintMarkerEditorUtility.GetLocalSecondsInClip(clipRange, ((IMarker)target).time);
            int maxFrameIndex = KimodoConstraintMarkerEditorUtility.GetMaxKimodoFrameIndex(clipRange);
            int autoFrame = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, ((IMarker)target).time);

            KimodoConstraintMarkerEditorUtility.EnsureFrameIndex(frameProp, autoFrame);
            frameProp.intValue = autoFrame;

            int currentFrame = frameProp.intValue;
            int editedFrame = EditorGUILayout.IntField(new GUIContent("Frame Index (Auto from Marker)", "Auto-synced from marker time in the owning clip. Editing this value also moves marker time to the selected frame."), currentFrame);
            EditorGUILayout.LabelField($"Marker Local Time: {localSeconds:F3}s   Clip Start: {clipRange.start:F3}s", EditorStyles.miniLabel);
            if (editedFrame != currentFrame)
            {
                int clampedFrame = Mathf.Clamp(editedFrame, 0, maxFrameIndex);
                frameProp.intValue = clampedFrame;
                KimodoConstraintMarkerEditorUtility.MoveMarkerToFrame(target as IMarker, clipRange, clampedFrame);
            }
        }

        private void DrawRoot2DFields(bool readOnly)
        {
            if (readOnly)
            {
                EditorGUILayout.HelpBox("Override disabled. Showing sampled result (read-only).", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(readOnly);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("smoothRoot2D"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeGlobalHeading"));

            SerializedProperty includeGlobalHeadingProp = serializedObject.FindProperty("includeGlobalHeading");
            if (includeGlobalHeadingProp != null && includeGlobalHeadingProp.boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("globalRootHeading"));
            }

            EditorGUI.EndDisabledGroup();
        }

        private void ApplyPreviewToTarget(KimodoConstraintJson preview)
        {
            if (target is KimodoConstraintMarkerBase marker)
            {
                KimodoConstraintPosePipeline.ApplyPreviewToMarkerData(marker, preview);
                EditorUtility.SetDirty(marker);
            }
        }
    }

    [CustomEditor(typeof(KimodoFullBodyConstraintMarker))]
    internal sealed class KimodoFullBodyConstraintMarkerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Purpose: apply a strong full-body pose constraint at a key frame (root position + local joint rotations).\n" +
                "Recommended when you need the generated motion to match a specific target pose at that frame.",
                MessageType.Info);
            EditorGUILayout.Space(4f);

            DrawCommonHeader("FullBody");
            DrawAutoFrameIndex();

            KimodoConstraintMarkerBase markerTarget = target as KimodoConstraintMarkerBase;
            SerializedProperty overrideProp = serializedObject.FindProperty("useOverride");
            bool useOverride = overrideProp != null && overrideProp.boolValue;
            bool activeEditSession = KimodoConstraintOverrideEditSession.HasActiveSession(markerTarget);
            if (!useOverride && !activeEditSession)
            {
                if (!KimodoConstraintExportUtility.TryBuildAutoConstraintPreview(markerTarget, out KimodoConstraintJson preview, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
                else
                {
                    ApplyPreviewToTarget(preview);
                    serializedObject.Update();
                }
            }

            DrawFullBodyFields(!useOverride);

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

        private void DrawAutoFrameIndex()
        {
            SerializedProperty frameProp = serializedObject.FindProperty("frameIndex");
            if (frameProp == null)
            {
                return;
            }

            if (!KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(target as IMarker, out TimelineClip clipRange))
            {
                EditorGUILayout.HelpBox("Owning AnimationPlayableAsset not found. frameIndex keeps current value.", MessageType.Warning);
                EditorGUILayout.PropertyField(frameProp);
                return;
            }

            double localSeconds = KimodoConstraintMarkerEditorUtility.GetLocalSecondsInClip(clipRange, ((IMarker)target).time);
            int maxFrameIndex = KimodoConstraintMarkerEditorUtility.GetMaxKimodoFrameIndex(clipRange);
            int autoFrame = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, ((IMarker)target).time);

            KimodoConstraintMarkerEditorUtility.EnsureFrameIndex(frameProp, autoFrame);
            frameProp.intValue = autoFrame;

            int currentFrame = frameProp.intValue;
            int editedFrame = EditorGUILayout.IntField(new GUIContent("Frame Index (Auto from Marker)", "Auto-synced from marker time in the owning clip. Editing this value also moves marker time to the selected frame."), currentFrame);
            EditorGUILayout.LabelField($"Marker Local Time: {localSeconds:F3}s   Clip Start: {clipRange.start:F3}s", EditorStyles.miniLabel);
            if (editedFrame != currentFrame)
            {
                int clampedFrame = Mathf.Clamp(editedFrame, 0, maxFrameIndex);
                frameProp.intValue = clampedFrame;
                KimodoConstraintMarkerEditorUtility.MoveMarkerToFrame(target as IMarker, clipRange, clampedFrame);
            }
        }

        private void DrawFullBodyFields(bool readOnly)
        {
            if (readOnly)
            {
                EditorGUILayout.HelpBox("Override disabled. Showing sampled result (read-only).", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(readOnly);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("smoothRoot2D"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rootPosition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localJointRots"), true);
            EditorGUI.EndDisabledGroup();
        }

        private void ApplyPreviewToTarget(KimodoConstraintJson preview)
        {
            if (target is KimodoConstraintMarkerBase marker)
            {
                KimodoConstraintPosePipeline.ApplyPreviewToMarkerData(marker, preview);
                EditorUtility.SetDirty(marker);
            }
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

            DrawAutoFrameIndex();
            bool useOverride = !isCustomEndEffector && overrideProp != null && overrideProp.boolValue;
            KimodoConstraintMarkerBase markerTarget = target as KimodoConstraintMarkerBase;
            bool activeEditSession = KimodoConstraintOverrideEditSession.HasActiveSession(markerTarget);

            if (!useOverride && !activeEditSession)
            {
                if (!KimodoConstraintExportUtility.TryBuildAutoConstraintPreview(markerTarget, out KimodoConstraintJson preview, out string error))
                {
                    EditorGUILayout.HelpBox($"Auto preview unavailable: {error}", MessageType.Warning);
                }
                else
                {
                    ApplyPreviewToTarget(preview);
                    serializedObject.Update();
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
        }

        private void DrawAutoFrameIndex()
        {
            SerializedProperty frameProp = serializedObject.FindProperty("frameIndex");
            if (frameProp == null)
            {
                return;
            }

            if (!KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(target as IMarker, out TimelineClip clipRange))
            {
                EditorGUILayout.HelpBox("Owning AnimationPlayableAsset not found. frameIndex keeps current value.", MessageType.Warning);
                EditorGUILayout.PropertyField(frameProp);
                return;
            }

            double localSeconds = KimodoConstraintMarkerEditorUtility.GetLocalSecondsInClip(clipRange, ((IMarker)target).time);
            int maxFrameIndex = KimodoConstraintMarkerEditorUtility.GetMaxKimodoFrameIndex(clipRange);
            int autoFrame = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, ((IMarker)target).time);

            KimodoConstraintMarkerEditorUtility.EnsureFrameIndex(frameProp, autoFrame);
            frameProp.intValue = autoFrame;

            int currentFrame = frameProp.intValue;
            int editedFrame = EditorGUILayout.IntField(new GUIContent("Frame Index (Auto from Marker)", "Auto-synced from marker time in the owning clip. Editing this value also moves marker time to the selected frame."), currentFrame);
            EditorGUILayout.LabelField($"Marker Local Time: {localSeconds:F3}s   Clip Start: {clipRange.start:F3}s", EditorStyles.miniLabel);
            if (editedFrame != currentFrame)
            {
                int clampedFrame = Mathf.Clamp(editedFrame, 0, maxFrameIndex);
                frameProp.intValue = clampedFrame;
                KimodoConstraintMarkerEditorUtility.MoveMarkerToFrame(target as IMarker, clipRange, clampedFrame);
            }
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
            SerializedProperty jointNamesProp = serializedObject.FindProperty("jointNames");
            if (jointNamesProp != null && typeName == "end-effector")
            {
                EditorGUILayout.PropertyField(jointNamesProp, true);
            }
            else if (typeName != "end-effector")
            {
                EditorGUILayout.HelpBox("Fixed joint group marker type; joint_names is determined by marker class.", MessageType.None);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("smoothRoot2D"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rootPosition"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localJointRots"), true);
            EditorGUI.EndDisabledGroup();
        }

        private void ApplyPreviewToTarget(KimodoConstraintJson preview)
        {
            if (target is KimodoConstraintMarkerBase marker)
            {
                KimodoConstraintPosePipeline.ApplyPreviewToMarkerData(marker, preview);
                EditorUtility.SetDirty(marker);
            }
        }
    }

    internal static class KimodoConstraintMarkerEditorUtility
    {
        public const double KimodoFps = 30.0;
        private const double TimeEpsilon = 1e-14;

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

        public static int GetMaxKimodoFrameIndex(TimelineClip clipRange)
        {
            if (clipRange == null)
            {
                return 0;
            }

            int frameCount = ToFramesTimelineStyle(clipRange.duration, KimodoFps);
            return Math.Max(0, frameCount - 1);
        }

        public static int TimeToKimodoFrameIndex(TimelineClip clipRange, double globalTime)
        {
            if (clipRange == null)
            {
                return 0;
            }

            double localSeconds = GetLocalSecondsInClip(clipRange, globalTime);
            int frame = ToFramesTimelineStyle(localSeconds, KimodoFps);
            int maxIndex = GetMaxKimodoFrameIndex(clipRange);
            return Mathf.Clamp(frame, 0, maxIndex);
        }

        private static int ToFramesTimelineStyle(double timeSeconds, double fps)
        {
            // Use nearest-frame rounding to avoid systematic "one-frame earlier" bias from floor.
            return (int)Math.Round(timeSeconds * fps, MidpointRounding.AwayFromZero);
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

        public static void EnsureFrameIndex(SerializedProperty frameProp, int fallbackValue)
        {
            if (frameProp == null)
            {
                return;
            }

            if (frameProp.intValue < 0)
            {
                frameProp.intValue = fallbackValue;
            }
        }

        public static void MoveMarkerToFrame(IMarker marker, TimelineClip clipRange, int frameIndex)
        {
            if (marker == null || clipRange == null)
            {
                return;
            }

            int clampedFrame = Mathf.Clamp(frameIndex, 0, GetMaxKimodoFrameIndex(clipRange));
            double localSeconds = clampedFrame / KimodoFps;
            double absTime = clipRange.start + localSeconds;
            absTime = Math.Min(clipRange.end, absTime);

            Undo.RecordObject(marker as UnityEngine.Object, "Move Kimodo Constraint Marker");
            marker.time = absTime;
            EditorUtility.SetDirty(marker as UnityEngine.Object);
            KimodoEditorCommandManager.Dispatch(
                new ConstraintSnapshotRefreshCommand());
        }

        public static void NotifyInspectorChanged(KimodoConstraintMarkerBase marker)
        {
            if (marker != null)
            {
                EditorUtility.SetDirty(marker);
            }

            KimodoEditorCommandManager.Dispatch(new ConstraintSnapshotRefreshCommand());
            SceneView.RepaintAll();
        }

        public static void DrawOverrideEditButton(SerializedObject so, KimodoConstraintMarkerBase marker)
        {
            if (so == null || marker == null)
            {
                return;
            }

            bool activeSession = KimodoConstraintOverrideEditSession.HasActiveSession(marker);
            bool windowOpen = KimodoConstraintOverrideEditWindow.IsOpenForMarker(marker);
            string label = activeSession ? (windowOpen ? "Editing..." : "Reopen Edit") : "Edit";
            using (new EditorGUI.DisabledScope(activeSession && windowOpen))
            {
                if (!GUILayout.Button(new GUIContent(label, "Open scene-based override edit session. This enables useOverride automatically if needed."), GUILayout.Height(22f)))
                {
                    return;
                }

                if (activeSession)
                {
                    KimodoConstraintOverrideEditWindow.ShowWindow(marker);
                    return;
                }

                SerializedProperty overrideProp = so.FindProperty("useOverride");
                if (overrideProp != null && !overrideProp.boolValue)
                {
                    overrideProp.boolValue = true;
                    so.ApplyModifiedProperties();
                }

                if (!KimodoConstraintOverrideEditSession.TryBegin(marker, out string error))
                {
                    Debug.LogWarning($"[Kimodo][ConstraintOverrideEdit] begin failed: {error}");
                }
            }
        }
    }
}
