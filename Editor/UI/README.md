# Kimodo Timeline Floating UI (Editor/UI)

This folder is isolated as a dedicated Editor UI assembly.

## Scope
- UI rendering and interaction only.
- Command DTO creation and dispatch via `KimodoEditorCommandManager`.
- No direct bridge/generation/bake/retarget business invocation.

## Included
- `KimodoTool.Editor.UI.asmdef`
- `KimodoTimelineFloatingUiOverlay.cs`
- `KimodoTimelineFloatingUiStyle.cs`

## Runtime Behavior
- Shows only when focused/mouse-over window is:
  - `UnityEditor.Timeline.TimelineWindow`
  - `UnityEditor.Graphs.AnimatorControllerTool`
- Anchored to bottom-center of target host window.
- Collapsed half-ball by default.
- Expands on hover/click.
- Sends `GenerateFromPromptCommand` only.
- Sending state is driven by manager command events.
