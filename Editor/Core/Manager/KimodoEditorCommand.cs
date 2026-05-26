using System;

namespace KimodoUnityMotionTools.ProjectEditor.Manager
{
    public enum KimodoEditorCommandKind
    {
        Unknown = 0,
        GeneratePlayableClip = 1,
        CancelPlayableClipGeneration = 2,
        AnimatorSplitInsert = 3,
        BridgeStartServer = 4,
        BridgeStopServer = 5,
        BridgeTryFix = 6,
        BridgeDeleteAllData = 7,
        BridgeRefreshStatus = 8,
        BridgeEnsureRuntimeRoot = 9
    }

    public interface IKimodoEditorCommand
    {
        Guid RequestId { get; }
        string TargetKey { get; }
        KimodoEditorCommandKind Kind { get; }
    }

    public abstract class KimodoEditorCommandBase : IKimodoEditorCommand
    {
        protected KimodoEditorCommandBase(string targetKey, KimodoEditorCommandKind kind)
        {
            RequestId = Guid.NewGuid();
            TargetKey = string.IsNullOrWhiteSpace(targetKey) ? "global" : targetKey;
            Kind = kind;
        }

        public Guid RequestId { get; }

        public string TargetKey { get; }

        public KimodoEditorCommandKind Kind { get; }
    }
}
