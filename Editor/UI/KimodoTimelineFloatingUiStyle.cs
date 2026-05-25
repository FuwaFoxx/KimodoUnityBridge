using UnityEngine;

namespace KimodoUnityMotionTools.ProjectEditor.UI
{
    [System.Serializable]
    internal sealed class KimodoTimelineFloatingUiStyle
    {
        internal Color panelColor = new Color(0f, 0f, 0f, 0.42f);
        internal Color panelOutlineColor = new Color(1f, 1f, 1f, 0.2f);
        internal Color accentColor = new Color(0.14f, 0.68f, 1f, 1f);
        internal Color textColor = Color.white;
        internal Color sendingTextColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        internal float collapsedBallSize = 28f;
        internal float expandedBallSize = 44f;
        internal float panelWidth = 420f;
        internal float panelHeight = 44f;
        internal float sendButtonSize = 34f;
        internal float bottomMargin = 10f;
    }
}
