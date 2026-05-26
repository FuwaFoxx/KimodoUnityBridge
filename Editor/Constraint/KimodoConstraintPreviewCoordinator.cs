using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal interface IConstraintPreviewConsumer
    {
        void OnMarkerEnabled(global::UnityEngine.Timeline.KimodoConstraintMarkerBase marker);
        void OnMarkerDisabled(global::UnityEngine.Timeline.KimodoConstraintMarkerBase marker);
        void OnMarkerChanged(global::UnityEngine.Timeline.KimodoConstraintMarkerBase marker, MarkerChangeReason reason);
    }

    internal static class ConstraintPreviewCoordinator
    {
        private static IConstraintPreviewConsumer defaultConsumer;
        private static IConstraintPreviewConsumer activeConsumer;

        internal static void SetDefaultConsumer(IConstraintPreviewConsumer consumer)
        {
            defaultConsumer = consumer;
            if (activeConsumer == null)
            {
                activeConsumer = defaultConsumer;
            }
        }

        internal static void ActivateConsumer(IConstraintPreviewConsumer consumer)
        {
            activeConsumer = consumer ?? defaultConsumer;
        }

        internal static void RestoreDefaultConsumer()
        {
            activeConsumer = defaultConsumer;
        }

        internal static void NotifyMarkerEnabled(global::UnityEngine.Timeline.KimodoConstraintMarkerBase marker)
        {
            (activeConsumer ?? defaultConsumer)?.OnMarkerEnabled(marker);
        }

        internal static void NotifyMarkerDisabled(global::UnityEngine.Timeline.KimodoConstraintMarkerBase marker)
        {
            (activeConsumer ?? defaultConsumer)?.OnMarkerDisabled(marker);
        }

        internal static void NotifyMarkerChanged(global::UnityEngine.Timeline.KimodoConstraintMarkerBase marker, MarkerChangeReason reason)
        {
            (activeConsumer ?? defaultConsumer)?.OnMarkerChanged(marker, reason);
        }
    }
}
