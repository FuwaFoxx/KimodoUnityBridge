using KimodoUnityMotionTools.ProjectEditor.Manager;
using System;
using UnityEditor;
using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.UI
{
    [InitializeOnLoad]
    internal sealed class KimodoTimelineFloatingUiOverlay : EditorWindow
    {
        private static readonly KimodoTimelineFloatingUiStyle Style = new KimodoTimelineFloatingUiStyle();

        private static KimodoTimelineFloatingUiOverlay instance;
        private static EditorWindow hostWindow;

        private static bool hovered;
        private static bool expanded;
        private static bool sending;
        private static bool inputVisible;

        private static int activeClipInstanceId;
        private static Guid activeRequestId = Guid.Empty;
        private static string promptDraft = string.Empty;
        private static double sendAnimStart;

        static KimodoTimelineFloatingUiOverlay()
        {
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CloseInstance;
            EditorApplication.quitting += CloseInstance;

            KimodoEditorCommandManager.CommandProgress += OnCommandProgress;
            KimodoEditorCommandManager.CommandCompleted += OnCommandCompleted;
            KimodoEditorCommandManager.CommandFailed += OnCommandFailed;
            KimodoEditorCommandManager.CommandCanceled += OnCommandCanceled;
        }

        private static void OnEditorUpdate()
        {
            if (!KimodoPlayableClipGenerationSettings.instance.FloatingUiEnabled)
            {
                CloseInstance();
                return;
            }

            ResolveSelectedClip();

            EditorWindow target = ResolveHostWindow();
            if (target == null)
            {
                CloseInstance();
                return;
            }

            hostWindow = target;
            EnsureInstance();
            if (instance != null)
            {
                instance.Reposition();
                instance.Repaint();
            }
        }

        private static void OnSelectionChanged()
        {
            ResolveSelectedClip();
            if (instance != null)
            {
                instance.Repaint();
            }
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            instance = CreateInstance<KimodoTimelineFloatingUiOverlay>();
            instance.titleContent = new GUIContent("KimodoFloatingUI");
            instance.ShowPopup();
        }

        private static void CloseInstance()
        {
            if (instance == null)
            {
                return;
            }

            try
            {
                instance.Close();
            }
            catch
            {
                // ignore
            }
            finally
            {
                instance = null;
                hostWindow = null;
            }
        }

        private static EditorWindow ResolveHostWindow()
        {
            EditorWindow focused = EditorWindow.focusedWindow;
            if (IsTargetWindow(focused))
            {
                return focused;
            }

            EditorWindow mouseOver = EditorWindow.mouseOverWindow;
            if (IsTargetWindow(mouseOver))
            {
                return mouseOver;
            }

            if (IsTargetWindow(hostWindow))
            {
                return hostWindow;
            }

            return null;
        }

        private static bool IsTargetWindow(EditorWindow window)
        {
            if (window == null)
            {
                return false;
            }

            string fullName = window.GetType().FullName ?? string.Empty;
            return string.Equals(fullName, "UnityEditor.Timeline.TimelineWindow", StringComparison.Ordinal)
                || string.Equals(fullName, "UnityEditor.Graphs.AnimatorControllerTool", StringComparison.Ordinal);
        }

        private void Reposition()
        {
            if (hostWindow == null)
            {
                return;
            }

            hovered = EditorWindow.mouseOverWindow == this;
            expanded = (hovered || inputVisible || sending) && activeClipInstanceId != 0;

            float width = expanded ? Style.panelWidth : Style.collapsedBallSize + 2f;
            float height = expanded ? Style.panelHeight + 8f : Style.collapsedBallSize * 0.56f;

            Rect host = hostWindow.position;
            float x = host.x + (host.width - width) * 0.5f;
            float y = expanded
                ? host.yMax - height - Style.bottomMargin
                : host.yMax - height * 0.5f;

            position = new Rect(x, y, width, height);
        }

        private void OnGUI()
        {
            if (activeClipInstanceId == 0)
            {
                inputVisible = false;
                sending = false;
                expanded = false;
            }

            hovered = EditorWindow.mouseOverWindow == this;
            expanded = (hovered || inputVisible || sending) && activeClipInstanceId != 0;

            if (!expanded)
            {
                DrawCollapsed();
                return;
            }

            DrawExpanded();
        }

        private void DrawCollapsed()
        {
            Rect rect = new Rect(1f, 0f, position.width - 2f, position.height * 2f);
            DrawBall(rect, animated: false);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                if (activeClipInstanceId != 0)
                {
                    inputVisible = true;
                    Reposition();
                }
                Event.current.Use();
            }
        }

        private void DrawExpanded()
        {
            Rect panelRect = new Rect(0f, 8f, position.width, Style.panelHeight);
            DrawPanel(panelRect);

            Rect inputRect = new Rect(panelRect.x + 10f, panelRect.y + 8f, panelRect.width - Style.sendButtonSize - 30f, panelRect.height - 16f);
            Rect sendRect = new Rect(panelRect.xMax - Style.sendButtonSize - 8f, panelRect.center.y - Style.sendButtonSize * 0.5f, Style.sendButtonSize, Style.sendButtonSize);

            GUIStyle inputStyle = new GUIStyle(EditorStyles.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12
            };
            inputStyle.normal.textColor = sending ? Style.sendingTextColor : Style.textColor;
            inputStyle.focused.textColor = sending ? Style.sendingTextColor : Style.textColor;
            inputStyle.active.textColor = sending ? Style.sendingTextColor : Style.textColor;

            EditorGUI.BeginDisabledGroup(sending || activeClipInstanceId == 0);
            GUI.SetNextControlName("KimodoFloatingPrompt");
            promptDraft = GUI.TextField(inputRect, promptDraft ?? string.Empty, inputStyle);
            EditorGUI.EndDisabledGroup();

            bool clickSend = GUI.Button(sendRect, "");
            DrawSendButton(sendRect, sending);

            if (!sending && clickSend)
            {
                TrySendCommand();
            }

            if (!sending && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "KimodoFloatingPrompt")
            {
                TrySendCommand();
                Event.current.Use();
            }
        }

        private static void TrySendCommand()
        {
            if (activeClipInstanceId == 0 || string.IsNullOrWhiteSpace(promptDraft))
            {
                return;
            }

            var command = new GenerateFromPromptCommand(activeClipInstanceId, promptDraft);
            bool accepted = KimodoEditorCommandManager.Dispatch(command);
            if (!accepted)
            {
                return;
            }

            activeRequestId = command.RequestId;
            sending = true;
            sendAnimStart = EditorApplication.timeSinceStartup;
        }

        private static void OnCommandProgress(KimodoEditorCommandProgressEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            sending = true;
            instance?.Repaint();
        }

        private static void OnCommandCompleted(KimodoEditorCommandCompletedEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            sending = false;
            activeRequestId = Guid.Empty;
            inputVisible = false;
            ResolveSelectedClip();
            instance?.Repaint();
        }

        private static void OnCommandFailed(KimodoEditorCommandFailedEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            sending = false;
            activeRequestId = Guid.Empty;
            instance?.Repaint();
        }

        private static void OnCommandCanceled(KimodoEditorCommandCanceledEvent evt)
        {
            if (!IsActiveCommand(evt.Command))
            {
                return;
            }

            sending = false;
            activeRequestId = Guid.Empty;
            instance?.Repaint();
        }

        private static bool IsActiveCommand(IKimodoEditorCommand command)
        {
            if (command == null || command.Kind != KimodoEditorCommandKind.GeneratePlayableClip)
            {
                return false;
            }

            if (activeRequestId != Guid.Empty)
            {
                return command.RequestId == activeRequestId;
            }

            return activeClipInstanceId != 0 && string.Equals(command.TargetKey, "clip:" + activeClipInstanceId, StringComparison.Ordinal);
        }

        private static void ResolveSelectedClip()
        {
            if (KimodoEditorSelectionBridge.TryGetSelectedPlayableClip(out KimodoSelectedPlayableClipInfo info))
            {
                if (info.ClipInstanceId != activeClipInstanceId)
                {
                    activeClipInstanceId = info.ClipInstanceId;
                    promptDraft = info.Prompt ?? string.Empty;
                    inputVisible = false;
                    if (!sending)
                    {
                        activeRequestId = Guid.Empty;
                    }
                }
                else if (string.IsNullOrWhiteSpace(promptDraft))
                {
                    promptDraft = info.Prompt ?? string.Empty;
                }

                return;
            }

            if (activeClipInstanceId != 0)
            {
                activeClipInstanceId = 0;
                promptDraft = string.Empty;
                inputVisible = false;
                if (!sending)
                {
                    activeRequestId = Guid.Empty;
                }
            }
        }

        private static void DrawPanel(Rect rect)
        {
            Color border = Style.panelOutlineColor;
            if (sending)
            {
                float t = (float)((EditorApplication.timeSinceStartup - sendAnimStart) * 6f);
                float pulse = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Sin(t));
                border = Color.Lerp(Style.panelOutlineColor, Style.accentColor, pulse);
            }

            EditorGUI.DrawRect(rect, Style.panelColor);
            DrawRectOutline(rect, border, 1f);
        }

        private static void DrawBall(Rect rect, bool animated)
        {
            Color c = Style.accentColor;
            if (animated)
            {
                float t = (float)((EditorApplication.timeSinceStartup - sendAnimStart) * 6f);
                float pulse = 0.72f + 0.28f * (0.5f + 0.5f * Mathf.Sin(t));
                c *= pulse;
            }

            EditorGUI.DrawRect(rect, c);
            DrawRectOutline(rect, new Color(1f, 1f, 1f, 0.55f), 1f);
        }

        private static void DrawSendButton(Rect rect, bool animated)
        {
            DrawBall(rect, animated);
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };
            style.normal.textColor = Color.white;
            GUI.Label(rect, "Send", style);
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
