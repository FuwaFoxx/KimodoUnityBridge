using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal sealed class KimodoConstraintOverrideEditWindow : EditorWindow
    {
        private KimodoConstraintMarkerBase marker;
        private Vector2 scroll;
        private string lastError;

        internal static void ShowWindow(KimodoConstraintMarkerBase marker)
        {
            var window = GetWindow<KimodoConstraintOverrideEditWindow>(true, "Kimodo Constraint Override Edit");
            window.minSize = new Vector2(420f, 260f);
            window.marker = marker;
            window.lastError = string.Empty;
            window.Show();
            window.Focus();
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

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (marker == null || !KimodoConstraintOverrideEditSession.HasActiveSession(marker))
            {
                Close();
                return;
            }

            KimodoConstraintOverrideEditSession.PingSession(marker);
            Repaint();
        }

        private void OnGUI()
        {
            if (marker == null)
            {
                EditorGUILayout.HelpBox("Marker is null.", MessageType.Error);
                return;
            }

            if (!KimodoConstraintOverrideEditSession.HasActiveSession(marker))
            {
                EditorGUILayout.HelpBox("Edit session is not active.", MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Close", "Close this window. No changes are committed when session is inactive."), GUILayout.Height(28f)))
                {
                    Close();
                }
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
            EditorGUILayout.LabelField("Constraint Override Edit Session", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edit the preview rig pose directly in Scene view. Marker override data updates in real time.",
                MessageType.Info);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Marker", KimodoConstraintOverrideEditSession.DescribeMarker(marker));
            EditorGUILayout.LabelField("Override", marker.useOverride ? "Enabled" : "Disabled");
            EditorGUILayout.Space(6f);
        }

        private void DrawMarkerPayload()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                var so = new SerializedObject(marker);
                so.Update();
                DrawPropertyIfExists(so, "frameIndex");
                DrawPropertyIfExists(so, "smoothRoot2D");
                DrawPropertyIfExists(so, "rootPosition");
                DrawPropertyIfExists(so, "localJointRots");
                SerializedProperty includeHeadingProp = so.FindProperty("includeGlobalHeading");
                if (includeHeadingProp != null)
                {
                    EditorGUILayout.PropertyField(includeHeadingProp);
                    if (includeHeadingProp.boolValue)
                    {
                        EditorGUILayout.PropertyField(so.FindProperty("globalRootHeading"), true);
                    }
                }
            }
        }

        private void DrawFooter()
        {
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                EditorGUILayout.HelpBox(lastError, MessageType.Error);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Cancel", "Discard scene edit session changes and exit override edit mode."), GUILayout.Height(30f)))
            {
                KimodoConstraintOverrideEditSession.Cancel(marker);
                Close();
            }

            if (GUILayout.Button(new GUIContent("End Edit", "Commit edited override values from preview rig back to marker data."), GUILayout.Height(30f)))
            {
                if (!KimodoConstraintOverrideEditSession.TryCommit(marker, out string error))
                {
                    lastError = string.IsNullOrWhiteSpace(error) ? "Commit failed." : error;
                }
                else
                {
                    Close();
                }
            }
            EditorGUILayout.EndHorizontal();
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
