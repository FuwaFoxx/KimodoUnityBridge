using System;
using UnityEngine;

namespace KimodoBridge.Editor
{
    internal enum FootContactState
    {
        Air = 0,
        CandidatePlant = 1,
        Plant = 2,
        CandidateRelease = 3
    }

    [Serializable]
    internal sealed class FootRootMotionSolverSettings
    {
        public float sampleRate = 60f;
        public float supportKeepTime = 0f;
        public float airPrediction = 0f;
        public int supportSwitchWindowFrames = 6;
        public float smoothing = 0f;
        public float dampingSpeed = 0f;
        public float dampingAngleSpeed = 0f;
        public bool keepOriginMotion = false;

        public int SupportSwitchWindowFrameCount => Mathf.Max(2, supportSwitchWindowFrames);
        public float SupportKeepTimeSeconds => Mathf.Clamp(supportKeepTime, 0f, 0.3f);
        public float PredictionDecayTime => Mathf.Lerp(0.04f, 0.20f, Mathf.Clamp01(airPrediction));
        public float DeltaSmoothing => Mathf.Clamp01(smoothing);
        public float MaxDeltaDistance(float deltaTime) => dampingSpeed > 0f ? dampingSpeed * Mathf.Max(0f, deltaTime) : float.PositiveInfinity;
        public float MaxYawDeltaRadians(float deltaTime) => dampingAngleSpeed > 0f ? dampingAngleSpeed * Mathf.Deg2Rad * Mathf.Max(0f, deltaTime) : float.PositiveInfinity;
    }

    internal struct FootRootMotionFrame
    {
        public float time;
        public Vector3 leftFootWorld;
        public Quaternion leftFootWorldRotation;
        public Vector3 rightFootWorld;
        public Quaternion rightFootWorldRotation;
        public Vector3 hipWorld;
        public float rootYawRadians;
        public Vector3 sampledRootWorld;
        public Quaternion sampledRootRotation;
    }

    internal sealed class FootRootMotionDebugInfo
    {
        public Vector2[] leftAnchors;
        public Vector2[] rightAnchors;
        public float[] leftConfidence;
        public float[] rightConfidence;
        public float[] conflictError;
        public bool[] usedPrediction;
        public bool[] leftPlant;
        public bool[] rightPlant;
    }

    internal sealed class FootRootMotionResult
    {
        public Vector2[] rootXZ;
        public float[] rootYawRadians;
        public Vector2[] rootDeltaXZ;
        public FootContactState[] leftContact;
        public FootContactState[] rightContact;
        public FootRootMotionDebugInfo debug;
    }
}
