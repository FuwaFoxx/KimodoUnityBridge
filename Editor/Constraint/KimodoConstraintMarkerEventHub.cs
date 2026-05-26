using System;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal enum MarkerChangeReason
    {
        TimeChanged = 0,
        OverrideChanged = 1,
        DataChanged = 2,
        SelectionContextChanged = 3,
        SessionStateChanged = 4
    }

    internal static class KimodoConstraintMarkerEventHub
    {
        internal static event Action<KimodoConstraintMarkerBase> MarkerEnabled;
        internal static event Action<KimodoConstraintMarkerBase> MarkerDisabled;
        internal static event Action<KimodoConstraintMarkerBase, MarkerChangeReason> MarkerChanged;

        internal static void RaiseMarkerEnabled(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return;
            }

            MarkerEnabled?.Invoke(marker);
        }

        internal static void RaiseMarkerDisabled(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return;
            }

            MarkerDisabled?.Invoke(marker);
        }

        internal static void RaiseMarkerChanged(KimodoConstraintMarkerBase marker, MarkerChangeReason reason)
        {
            MarkerChanged?.Invoke(marker, reason);
        }
    }
}
