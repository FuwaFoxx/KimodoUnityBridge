using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal sealed class KimodoConstraintOverrideEditWindow : EditorWindow
    {
        private static KimodoConstraintOverrideEditWindow currentWindow;
        private static KimodoConstraintMarkerBase lastKnownMarker;
        private KimodoConstraintMarkerBase marker;
        private Vector2 scroll;
        private string lastError;

        internal KimodoConstraintMarkerBase TargetMarker => marker;

        internal static void ShowWindow(KimodoConstraintMarkerBase marker)
        {
            var window = GetWindow<KimodoConstraintOverrideEditWindow>(true, "Kimodo Constraint Override Edit");
            window.minSize = new Vector2(420f, 260f);
            window.marker = marker;
            if (marker != null)
            {
                lastKnownMarker = marker;
            }
            window.lastError = string.Empty;
            window.Show();
            window.Focus();
        }

        internal static KimodoConstraintOverrideEditWindow GetOpenWindow()
        {
            if (currentWindow != null)
            {
                return currentWindow;
            }

            KimodoConstraintOverrideEditWindow[] windows = Resources.FindObjectsOfTypeAll<KimodoConstraintOverrideEditWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                {
                    currentWindow = windows[i];
                    return currentWindow;
                }
            }

            return null;
        }

        internal static bool IsOpenForMarker(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return false;
            }

            KimodoConstraintOverrideEditWindow[] windows = Resources.FindObjectsOfTypeAll<KimodoConstraintOverrideEditWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null && windows[i].marker == marker)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasAnyOpenWindow()
        {
            return Resources.FindObjectsOfTypeAll<KimodoConstraintOverrideEditWindow>().Length > 0;
        }

        private void OnEnable()
        {
            currentWindow = this;
            if (marker != null)
            {
                lastKnownMarker = marker;
            }
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            KimodoConstraintMarkerBase restoreMarker = marker != null ? marker : lastKnownMarker;

            if (marker != null && marker.useOverride)
            {
                if (!KimodoConstraintPoseCache.TryCaptureToMarkerData(marker, out string captureError) && !string.IsNullOrWhiteSpace(captureError))
                {
                    Debug.LogWarning($"[Kimodo][ConstraintOverride] capture on close failed: {captureError}");
                }
                else
                {
                    EditorUtility.SetDirty(marker);
                    AssetDatabase.SaveAssets();
                }
            }

            if (currentWindow == this)
            {
                currentWindow = null;
            }
            EditorApplication.update -= OnEditorUpdate;
            KimodoConstraintPoseCache.Hide();
            SceneView.RepaintAll();

            if (restoreMarker != null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (restoreMarker != null)
                    {
                        Selection.activeObject = restoreMarker;
                        EditorApplication.delayCall += () =>
                        {
                            if (restoreMarker != null)
                            {
                                Selection.activeObject = restoreMarker;
                            }
                        };
                    }
                };
            }
        }

        private void OnEditorUpdate()
        {
            if (marker == null)
            {
                Close();
                return;
            }

            if (!marker.useOverride)
            {
                lastError = "override is disabled.";
            }

            Repaint();
        }

        private void OnGUI()
        {
            if (marker == null)
            {
                EditorGUILayout.HelpBox("Marker is null.", MessageType.Error);
                return;
            }

            DrawHeader();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawMarkerPayload();
            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Constraint Override Edit", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Edit the pose cache directly. Marker data updates immediately.", MessageType.Info);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Marker", marker != null ? marker.name : "(null)");
            EditorGUILayout.LabelField("Override", marker.useOverride ? "Enabled" : "Disabled");
            EditorGUILayout.Space(6f);
        }

        private void DrawMarkerPayload()
        {
            if (!marker.useOverride)
            {
                EditorGUILayout.HelpBox("Override is disabled. Enable it to edit cached pose values.", MessageType.Warning);
            }

            var so = new SerializedObject(marker);
            so.Update();

            using (new EditorGUI.DisabledScope(!marker.useOverride))
            {
                DrawPropertyIfExists(so, "sampleData.sampleTime");
                DrawPropertyIfExists(so, "sampleData.rootPosition");
                DrawPropertyIfExists(so, "sampleData.localAxisAngles");
                SerializedProperty includeHeadingProp = so.FindProperty("sampleData.hasRootHeading");
                if (includeHeadingProp != null)
                {
                    EditorGUILayout.PropertyField(includeHeadingProp);
                    if (includeHeadingProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(so.FindProperty("sampleData.rootHeading"), true);
                    }
                }
            }

            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(marker);
                if (KimodoConstraintPoseCache.TryShowOrUpdateFromMarkerData(marker, out string poseError))
                {
                    lastError = string.Empty;
                }
                else
                {
                    lastError = string.IsNullOrWhiteSpace(poseError) ? "pose cache update failed." : poseError;
                }
            }

            EditorGUILayout.HelpBox("Pose writes back continuously while this window is open.", MessageType.None);
        }

        private void DrawFooter()
        {
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                EditorGUILayout.HelpBox(lastError, MessageType.Error);
            }

            EditorGUILayout.Space(6f);
            if (GUILayout.Button(new GUIContent("Close", "Close the edit window and keep current marker data."), GUILayout.Height(30f)))
            {
                EditorUtility.SetDirty(marker);
                AssetDatabase.SaveAssets();
                Close();
            }
        }

        private static void DrawPropertyIfExists(SerializedObject so, string name)
        {
            if (so == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop, true);
            }
        }
    }
}
