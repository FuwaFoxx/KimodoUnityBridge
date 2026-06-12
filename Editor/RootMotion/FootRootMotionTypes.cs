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

    internal enum FootRootMotionSupportState
    {
        Unknown = 0,
        LeftPlant = 1,
        RightPlant = 2,
        DoubleSupport = 3,
        Air = 4
    }

    internal enum FootRootMotionInitialSupportMode
    {
        Auto = 0,
        Left = 1,
        Right = 2
    }

    internal enum FootRootMotionPhaseHint
    {
        Unknown = 0,
        LeftSwing = 1,
        RightSwing = 2,
        Balanced = 3
    }

    internal enum FootRootMotionCostEvaluatorKind
    {
        Default = 0
    }

    internal enum FootRootMotionLegBendChannel
    {
        LowerLegStretch = 0,
        UpperLegInOut = 1,
        LowerLegTwistInOut = 2
    }

    [Serializable]
    internal sealed class FootRootMotionSolverSettings
    {
        public float sampleRate = 60f;
        public FootRootMotionInitialSupportMode initialSupportMode = FootRootMotionInitialSupportMode.Auto;
        public FootRootMotionCostEvaluatorKind costEvaluator = FootRootMotionCostEvaluatorKind.Default;
        public FootRootMotionLegBendChannel legBendChannel = FootRootMotionLegBendChannel.LowerLegStretch;
        public int initialLookaheadFrames = 8;
        public int supportHoldFrames = 4;
        public int supportSwitchWindowFrames = 6;
        public int phaseWindowFrames = 6;
        public float supportSwitchMargin = 0.35f;
        public float doubleSupportCostThreshold = 1.5f;
        public float doubleSupportCostDifference = 0.3f;
        public float airCostThreshold = 3.5f;
        public float phaseHintThreshold = 0.1f;
        public float supportSlipXZReference = 0.02f;
        public float supportSlipYReference = 0.015f;
        public float hipResidualReference = 0.03f;
        public float rootDeltaReference = 0.04f;
        public float rootYawReferenceDegrees = 5f;
        public float supportSlipXZWeight = 4f;
        public float supportSlipYWeight = 3f;
        public float hipResidualWeight = 2f;
        public float rootDeltaWeight = 1.5f;
        public float rootYawWeight = 1f;
        public float phaseWeight = 0.5f;
        public float dampingSpeed = 0f;
        public float dampingAngleSpeed = 0f;
        public bool keepOriginMotion = false;

        public int InitialLookaheadFrameCount => Mathf.Max(1, initialLookaheadFrames);
        public int SupportHoldFrameCount => Mathf.Max(0, supportHoldFrames);
        public int SupportSwitchWindowFrameCount => Mathf.Max(2, supportSwitchWindowFrames);
        public int PhaseWindowFrameCount => Mathf.Max(2, phaseWindowFrames);
        public float SupportSlipXZReference => Mathf.Max(1e-4f, supportSlipXZReference);
        public float SupportSlipYReference => Mathf.Max(1e-4f, supportSlipYReference);
        public float HipResidualReference => Mathf.Max(1e-4f, hipResidualReference);
        public float RootDeltaReference => Mathf.Max(1e-4f, rootDeltaReference);
        public float RootYawReferenceRadians => Mathf.Max(0.5f, rootYawReferenceDegrees) * Mathf.Deg2Rad;
        public float SupportSwitchMargin => Mathf.Max(0f, supportSwitchMargin);
        public float DoubleSupportCostThreshold => Mathf.Max(0f, doubleSupportCostThreshold);
        public float DoubleSupportCostDifference => Mathf.Max(0f, doubleSupportCostDifference);
        public float AirCostThreshold => Mathf.Max(DoubleSupportCostThreshold + 1e-4f, Mathf.Max(0f, airCostThreshold));
        public float PhaseHintThreshold => Mathf.Clamp(Mathf.Max(0f, phaseHintThreshold), 0.01f, 1f);
        public float MaxDeltaDistance(float deltaTime) => dampingSpeed > 0f ? dampingSpeed * Mathf.Max(0f, deltaTime) : float.PositiveInfinity;
        public float MaxYawDeltaRadians(float deltaTime) => dampingAngleSpeed > 0f ? dampingAngleSpeed * Mathf.Deg2Rad * Mathf.Max(0f, deltaTime) : float.PositiveInfinity;
    }

    internal struct FootRootMotionFrame
    {
        public float time;
        public MuscleSample muscleSample;
        public Vector3 sampledRootWorld;
        public Quaternion sampledRootRotation;
        public Vector3 hipWorld;
        public Vector3 leftFootWorld;
        public Quaternion leftFootWorldRotation;
        public Vector3 rightFootWorld;
        public Quaternion rightFootWorldRotation;
        public Vector3 hipLocal;
        public float hipLocalYawRadians;
        public Vector3 leftFootLocal;
        public float leftFootLocalYawRadians;
        public Vector3 rightFootLocal;
        public float rightFootLocalYawRadians;
    }

    internal struct FootRootMotionSolveFrame
    {
        public float time;
        public Vector2 sampledRootXZ;
        public float sampledRootYawRadians;
        public Vector3 leftFootWorld;
        public Vector3 rightFootWorld;
        public Vector3 leftFootLocal;
        public float leftFootLocalYawRadians;
        public Vector3 rightFootLocal;
        public float rightFootLocalYawRadians;
    }

    internal struct FootRootMotionPhaseFrame
    {
        public float leftDrive;
        public float rightDrive;
        public float leftWindowDrive;
        public float rightWindowDrive;
        public float leftWindowDelta;
        public float rightWindowDelta;
        public bool leftJumped;
        public bool rightJumped;
        public float confidence;
        public FootRootMotionPhaseHint hint;
        public FootRootMotionSupportState supportStateHint;
    }

    internal struct FootRootMotionCostContext
    {
        public int frameIndex;
        public FootRootMotionSupportState supportState;
        public FootRootMotionPhaseHint phaseHint;
        public float phaseConfidence;
        public float supportSlipXZ;
        public float supportSlipY;
        public float hipResidual;
        public float rootDeltaDeviation;
        public float rootYawDeviationRadians;
        public FootRootMotionSolverSettings settings;
    }

    internal struct FootRootMotionCostBreakdown
    {
        public float supportSlipXZCost;
        public float supportSlipYCost;
        public float hipResidualCost;
        public float rootDeltaCost;
        public float rootYawCost;
        public float phaseCost;
        public float totalCost;
    }

    internal struct FootRootMotionSupportHypothesis
    {
        public FootRootMotionSupportState supportState;
        public Vector2 rootXZ;
        public float rootYawRadians;
        public Vector2 rootDeltaXZ;
        public float rootYawDeltaRadians;
        public Vector3 leftAnchor;
        public Vector3 rightAnchor;
        public bool hasLeftAnchor;
        public bool hasRightAnchor;
        public float supportSlipXZ;
        public float supportSlipY;
        public float hipResidual;
        public float rootDeltaDeviation;
        public float rootYawDeviationRadians;
        public float phaseBias;
        public FootRootMotionCostBreakdown cost;
        public bool isValid;
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
        public FootRootMotionSupportState[] supportStates;
        public float[] leftTotalCost;
        public float[] rightTotalCost;
        public float[] leftPhaseDrive;
        public float[] rightPhaseDrive;
    }

    internal sealed class FootRootMotionResult
    {
        public Vector2[] rootXZ;
        public float[] rootYawRadians;
        public Vector2[] rootDeltaXZ;
        public float[] rootYawDeltaRadians;
        public FootRootMotionSupportState[] supportStates;
        public FootContactState[] leftContact;
        public FootContactState[] rightContact;
        public FootRootMotionDebugInfo debug;
    }
}
