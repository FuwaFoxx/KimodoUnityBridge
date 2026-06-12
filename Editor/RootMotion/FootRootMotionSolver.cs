using UnityEngine;

namespace KimodoBridge.Editor
{
    internal static class FootRootMotionSolver
    {
        private struct FrameSolveState
        {
            public FootRootMotionSupportState supportState;
            public FootRootMotionSupportState drivenSupportState;
            public Vector2 rootXZ;
            public float rootYawRadians;
            public Vector2 rootDeltaXZ;
            public float rootYawDeltaRadians;
            public Vector3 leftAnchor;
            public Vector3 rightAnchor;
            public float leftAnchorYawRadians;
            public float rightAnchorYawRadians;
            public bool hasLeftAnchor;
            public bool hasRightAnchor;
            public float leftConfidence;
            public float rightConfidence;
            public bool usedPrediction;
            public bool leftPlant;
            public bool rightPlant;
            public FootContactState leftContact;
            public FootContactState rightContact;
            public float leftCost;
            public float rightCost;
            public float leftPhaseDrive;
            public float rightPhaseDrive;
        }

        public static FootRootMotionResult Solve(FootRootMotionFrame[] frames, FootRootMotionSolverSettings settings)
        {
            return Solve(frames, settings, null);
        }

        public static FootRootMotionResult Solve(
            FootRootMotionFrame[] frames,
            FootRootMotionSolverSettings settings,
            IFootRootMotionCostEvaluator costEvaluator)
        {
            settings = settings ?? new FootRootMotionSolverSettings();
            if (frames == null || frames.Length == 0)
            {
                return CreateEmptyResult();
            }

            FootRootMotionSolveFrame[] solveFrames = BuildSolveFrames(frames);
            FootRootMotionPhaseFrame[] phaseFrames = FootRootMotionPhaseUtility.BuildPhaseFrames(frames, settings);
            int count = solveFrames.Length;

            var states = new FrameSolveState[count];
            FootRootMotionPhaseFrame firstPhaseFrame = phaseFrames.Length > 0
                ? phaseFrames[0]
                : default(FootRootMotionPhaseFrame);
            FootRootMotionSupportState initialSupportState = ResolveInitialSupportState(phaseFrames, settings);
            FootRootMotionSupportState initialDrivenSupportState = ResolveDrivenSupportState(
                initialSupportState,
                FootRootMotionSupportState.Unknown,
                settings);

            states[0] = CreateFirstFrameState(
                solveFrames[0],
                firstPhaseFrame,
                initialSupportState,
                initialDrivenSupportState);

            for (int i = 1; i < count; i++)
            {
                FootRootMotionPhaseFrame phaseFrame = i < phaseFrames.Length
                    ? phaseFrames[i]
                    : default(FootRootMotionPhaseFrame);
                FrameSolveState previousState = states[i - 1];
                FootRootMotionSupportState supportState = DetermineOutputSupportState(
                    phaseFrame,
                    previousState.supportState,
                    settings);
                FootRootMotionSupportState drivenSupportState = ResolveDrivenSupportState(
                    supportState,
                    previousState.drivenSupportState,
                    settings);

                states[i] = SolveFrame(
                    solveFrames[i],
                    solveFrames[i - 1],
                    phaseFrame,
                    previousState,
                    supportState,
                    drivenSupportState,
                    settings);
            }

            return BuildResult(states);
        }

        private static FootRootMotionResult CreateEmptyResult()
        {
            return new FootRootMotionResult
            {
                rootXZ = new Vector2[0],
                rootYawRadians = new float[0],
                rootDeltaXZ = new Vector2[0],
                rootYawDeltaRadians = new float[0],
                supportStates = new FootRootMotionSupportState[0],
                leftContact = new FootContactState[0],
                rightContact = new FootContactState[0],
                debug = new FootRootMotionDebugInfo
                {
                    leftAnchors = new Vector2[0],
                    rightAnchors = new Vector2[0],
                    leftConfidence = new float[0],
                    rightConfidence = new float[0],
                    conflictError = new float[0],
                    usedPrediction = new bool[0],
                    leftPlant = new bool[0],
                    rightPlant = new bool[0],
                    supportStates = new FootRootMotionSupportState[0],
                    leftTotalCost = new float[0],
                    rightTotalCost = new float[0],
                    leftPhaseDrive = new float[0],
                    rightPhaseDrive = new float[0]
                }
            };
        }

        private static FootRootMotionSolveFrame[] BuildSolveFrames(FootRootMotionFrame[] frames)
        {
            var solveFrames = new FootRootMotionSolveFrame[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                FootRootMotionFrame frame = frames[i];
                solveFrames[i] = new FootRootMotionSolveFrame
                {
                    time = frame.time,
                    sampledRootXZ = FootRootMotionMathUtility.ToXZ(frame.sampledRootWorld),
                    sampledRootYawRadians = FootRootMotionMathUtility.ExtractYawRadians(frame.sampledRootRotation),
                    leftFootWorld = frame.leftFootWorld,
                    rightFootWorld = frame.rightFootWorld,
                    leftFootLocal = frame.leftFootLocal,
                    leftFootLocalYawRadians = frame.leftFootLocalYawRadians,
                    rightFootLocal = frame.rightFootLocal,
                    rightFootLocalYawRadians = frame.rightFootLocalYawRadians
                };
            }

            return solveFrames;
        }

        private static FootRootMotionSupportState ResolveInitialSupportState(
            FootRootMotionPhaseFrame[] phaseFrames,
            FootRootMotionSolverSettings settings)
        {
            if (phaseFrames != null && phaseFrames.Length > 0)
            {
                FootRootMotionSupportState hint = phaseFrames[0].supportStateHint;
                if (hint == FootRootMotionSupportState.LeftPlant || hint == FootRootMotionSupportState.RightPlant)
                {
                    return hint;
                }
            }

            switch (settings.initialSupportMode)
            {
                case FootRootMotionInitialSupportMode.Right:
                    return FootRootMotionSupportState.RightPlant;
                case FootRootMotionInitialSupportMode.Left:
                    return FootRootMotionSupportState.LeftPlant;
                default:
                    return FootRootMotionSupportState.LeftPlant;
            }
        }

        private static FootRootMotionSupportState DetermineOutputSupportState(
            FootRootMotionPhaseFrame phaseFrame,
            FootRootMotionSupportState previousState,
            FootRootMotionSolverSettings settings)
        {
            switch (phaseFrame.supportStateHint)
            {
                case FootRootMotionSupportState.LeftPlant:
                case FootRootMotionSupportState.RightPlant:
                case FootRootMotionSupportState.DoubleSupport:
                case FootRootMotionSupportState.Air:
                    return phaseFrame.supportStateHint;
            }

            if (previousState != FootRootMotionSupportState.Unknown)
            {
                return previousState;
            }

            return ResolveInitialSupportState(null, settings);
        }

        private static FootRootMotionSupportState ResolveDrivenSupportState(
            FootRootMotionSupportState supportState,
            FootRootMotionSupportState previousDrivenSupportState,
            FootRootMotionSolverSettings settings)
        {
            if (supportState == FootRootMotionSupportState.LeftPlant || supportState == FootRootMotionSupportState.RightPlant)
            {
                return supportState;
            }

            if (previousDrivenSupportState == FootRootMotionSupportState.LeftPlant ||
                previousDrivenSupportState == FootRootMotionSupportState.RightPlant)
            {
                return previousDrivenSupportState;
            }

            switch (settings.initialSupportMode)
            {
                case FootRootMotionInitialSupportMode.Right:
                    return FootRootMotionSupportState.RightPlant;
                case FootRootMotionInitialSupportMode.Left:
                case FootRootMotionInitialSupportMode.Auto:
                default:
                    return FootRootMotionSupportState.LeftPlant;
            }
        }

        private static FrameSolveState CreateFirstFrameState(
            FootRootMotionSolveFrame frame,
            FootRootMotionPhaseFrame phaseFrame,
            FootRootMotionSupportState supportState,
            FootRootMotionSupportState drivenSupportState)
        {
            FrameSolveState state = new FrameSolveState
            {
                supportState = supportState,
                drivenSupportState = drivenSupportState,
                rootXZ = Vector2.zero,
                rootYawRadians = 0f,
                rootDeltaXZ = Vector2.zero,
                rootYawDeltaRadians = 0f,
                leftCost = phaseFrame.leftWindowDrive,
                rightCost = phaseFrame.rightWindowDrive,
                leftPhaseDrive = phaseFrame.leftWindowDrive,
                rightPhaseDrive = phaseFrame.rightWindowDrive
            };

            CaptureAnchorFromSolvedRoot(ref state, frame, FootRootMotionSupportState.LeftPlant, state.rootXZ, state.rootYawRadians);
            CaptureAnchorFromSolvedRoot(ref state, frame, FootRootMotionSupportState.RightPlant, state.rootXZ, state.rootYawRadians);
            MarkSupportState(ref state, supportState, 1f);
            return state;
        }

        private static FrameSolveState SolveFrame(
            FootRootMotionSolveFrame frame,
            FootRootMotionSolveFrame previousFrame,
            FootRootMotionPhaseFrame phaseFrame,
            FrameSolveState previousState,
            FootRootMotionSupportState supportState,
            FootRootMotionSupportState drivenSupportState,
            FootRootMotionSolverSettings settings)
        {
            FrameSolveState state = previousState;
            state.supportState = supportState;
            state.drivenSupportState = drivenSupportState;
            state.leftCost = phaseFrame.leftWindowDrive;
            state.rightCost = phaseFrame.rightWindowDrive;
            state.leftPhaseDrive = phaseFrame.leftWindowDrive;
            state.rightPhaseDrive = phaseFrame.rightWindowDrive;
            state.usedPrediction = false;

            float deltaTime = Mathf.Max(1e-4f, frame.time - previousFrame.time);
            switch (supportState)
            {
                case FootRootMotionSupportState.LeftPlant:
                case FootRootMotionSupportState.RightPlant:
                    SolvePlantFrame(
                        ref state,
                        frame,
                        previousState,
                        supportState,
                        drivenSupportState,
                        settings,
                        deltaTime);
                    state.usedPrediction = false;
                    MarkSupportState(ref state, supportState, 1f);
                    return state;
                case FootRootMotionSupportState.DoubleSupport:
                    ApplyZeroDelta(ref state, previousState);
                    state.usedPrediction = false;
                    MarkSupportState(ref state, supportState, 1f);
                    return state;
                case FootRootMotionSupportState.Air:
                    ApplySampledSpeedDelta(ref state, frame, previousFrame, previousState, settings, deltaTime);
                    state.usedPrediction = true;
                    MarkSupportState(ref state, supportState, 0f);
                    return state;
                case FootRootMotionSupportState.Unknown:
                default:
                    ApplyZeroDelta(ref state, previousState);
                    state.usedPrediction = true;
                    MarkSupportState(ref state, supportState, 0f);
                    return state;
            }
        }

        private static void SolvePlantFrame(
            ref FrameSolveState state,
            FootRootMotionSolveFrame frame,
            FrameSolveState previousState,
            FootRootMotionSupportState supportState,
            FootRootMotionSupportState drivenSupportState,
            FootRootMotionSolverSettings settings,
            float deltaTime)
        {
            bool supportChanged =
                previousState.drivenSupportState != drivenSupportState ||
                previousState.supportState == FootRootMotionSupportState.DoubleSupport ||
                previousState.supportState == FootRootMotionSupportState.Air ||
                previousState.supportState == FootRootMotionSupportState.Unknown;
            if (supportChanged || !HasAnchor(previousState, drivenSupportState))
            {
                CaptureAnchorFromSolvedRoot(
                    ref state,
                    frame,
                    drivenSupportState,
                    previousState.rootXZ,
                    previousState.rootYawRadians);
            }

            SolveAgainstSupportAnchor(
                ref state,
                frame,
                previousState,
                supportState,
                settings,
                deltaTime);
        }

        private static void CaptureAnchorFromSolvedRoot(
            ref FrameSolveState state,
            FootRootMotionSolveFrame frame,
            FootRootMotionSupportState supportState,
            Vector2 rootXZ,
            float rootYawRadians)
        {
            Vector3 supportLocal = GetSupportLocalPosition(frame, supportState);
            Vector2 supportWorldXZ = rootXZ +
                FootRootMotionMathUtility.RotateXZ(
                    new Vector2(supportLocal.x, supportLocal.z),
                    rootYawRadians);
            float supportWorldYawRadians = rootYawRadians + GetSupportLocalYaw(frame, supportState);
            Vector3 supportWorld = GetSupportWorld(frame, supportState);
            Vector3 anchor = new Vector3(supportWorldXZ.x, supportWorld.y, supportWorldXZ.y);

            if (supportState == FootRootMotionSupportState.LeftPlant)
            {
                state.leftAnchor = anchor;
                state.leftAnchorYawRadians = supportWorldYawRadians;
                state.hasLeftAnchor = true;
                return;
            }

            state.rightAnchor = anchor;
            state.rightAnchorYawRadians = supportWorldYawRadians;
            state.hasRightAnchor = true;
        }

        private static void SolveAgainstSupportAnchor(
            ref FrameSolveState state,
            FootRootMotionSolveFrame frame,
            FrameSolveState previousState,
            FootRootMotionSupportState supportState,
            FootRootMotionSolverSettings settings,
            float deltaTime)
        {
            Vector3 supportLocal = GetSupportLocalPosition(frame, supportState);
            float supportLocalYawRadians = GetSupportLocalYaw(frame, supportState);
            Vector3 anchor = GetAnchor(state, supportState);
            float anchorYawRadians = GetAnchorYaw(state, supportState);

            float targetRootYawRadians = anchorYawRadians - supportLocalYawRadians;
            Vector2 targetRootXZ = new Vector2(anchor.x, anchor.z) -
                FootRootMotionMathUtility.RotateXZ(
                    new Vector2(supportLocal.x, supportLocal.z),
                    targetRootYawRadians);

            Vector2 deltaXZ = FootRootMotionMathUtility.ClampMagnitude(
                targetRootXZ - previousState.rootXZ,
                settings.MaxDeltaDistance(deltaTime));
            float yawDeltaRadians = FootRootMotionMathUtility.ClampYawDelta(
                FootRootMotionMathUtility.DeltaYawRadians(previousState.rootYawRadians, targetRootYawRadians),
                settings.MaxYawDeltaRadians(deltaTime));

            state.rootXZ = previousState.rootXZ + deltaXZ;
            state.rootYawRadians = previousState.rootYawRadians + yawDeltaRadians;
            state.rootDeltaXZ = deltaXZ;
            state.rootYawDeltaRadians = yawDeltaRadians;
        }

        private static void ApplyZeroDelta(ref FrameSolveState state, FrameSolveState previousState)
        {
            state.rootXZ = previousState.rootXZ;
            state.rootYawRadians = previousState.rootYawRadians;
            state.rootDeltaXZ = Vector2.zero;
            state.rootYawDeltaRadians = 0f;
        }

        private static void ApplySampledSpeedDelta(
            ref FrameSolveState state,
            FootRootMotionSolveFrame frame,
            FootRootMotionSolveFrame previousFrame,
            FrameSolveState previousState,
            FootRootMotionSolverSettings settings,
            float deltaTime)
        {
            Vector2 sampledDeltaXZ = frame.sampledRootXZ - previousFrame.sampledRootXZ;
            float sampledYawDeltaRadians = FootRootMotionMathUtility.DeltaYawRadians(
                previousFrame.sampledRootYawRadians,
                frame.sampledRootYawRadians);

            Vector2 deltaXZ = FootRootMotionMathUtility.ClampMagnitude(
                sampledDeltaXZ,
                settings.MaxDeltaDistance(deltaTime));
            float yawDeltaRadians = FootRootMotionMathUtility.ClampYawDelta(
                sampledYawDeltaRadians,
                settings.MaxYawDeltaRadians(deltaTime));

            state.rootXZ = previousState.rootXZ + deltaXZ;
            state.rootYawRadians = previousState.rootYawRadians + yawDeltaRadians;
            state.rootDeltaXZ = deltaXZ;
            state.rootYawDeltaRadians = yawDeltaRadians;
        }

        private static bool HasAnchor(FrameSolveState state, FootRootMotionSupportState supportState)
        {
            return supportState == FootRootMotionSupportState.LeftPlant
                ? state.hasLeftAnchor
                : state.hasRightAnchor;
        }

        private static Vector3 GetAnchor(FrameSolveState state, FootRootMotionSupportState supportState)
        {
            return supportState == FootRootMotionSupportState.LeftPlant
                ? state.leftAnchor
                : state.rightAnchor;
        }

        private static float GetAnchorYaw(FrameSolveState state, FootRootMotionSupportState supportState)
        {
            return supportState == FootRootMotionSupportState.LeftPlant
                ? state.leftAnchorYawRadians
                : state.rightAnchorYawRadians;
        }

        private static Vector3 GetSupportWorld(FootRootMotionSolveFrame frame, FootRootMotionSupportState supportState)
        {
            return supportState == FootRootMotionSupportState.LeftPlant
                ? frame.leftFootWorld
                : frame.rightFootWorld;
        }

        private static Vector3 GetSupportLocalPosition(FootRootMotionSolveFrame frame, FootRootMotionSupportState supportState)
        {
            return supportState == FootRootMotionSupportState.LeftPlant
                ? frame.leftFootLocal
                : frame.rightFootLocal;
        }

        private static float GetSupportLocalYaw(FootRootMotionSolveFrame frame, FootRootMotionSupportState supportState)
        {
            return supportState == FootRootMotionSupportState.LeftPlant
                ? frame.leftFootLocalYawRadians
                : frame.rightFootLocalYawRadians;
        }

        private static FootRootMotionResult BuildResult(FrameSolveState[] states)
        {
            int count = states.Length;
            Vector2[] rootXZ = new Vector2[count];
            float[] rootYawRadians = new float[count];
            Vector2[] rootDeltaXZ = new Vector2[count];
            float[] rootYawDeltaRadians = new float[count];
            Vector2[] leftAnchors = new Vector2[count];
            Vector2[] rightAnchors = new Vector2[count];
            float[] leftConfidence = new float[count];
            float[] rightConfidence = new float[count];
            float[] leftTotalCost = new float[count];
            float[] rightTotalCost = new float[count];
            float[] leftPhaseDrive = new float[count];
            float[] rightPhaseDrive = new float[count];
            float[] conflictError = new float[count];
            bool[] usedPrediction = new bool[count];
            bool[] leftPlant = new bool[count];
            bool[] rightPlant = new bool[count];
            FootRootMotionSupportState[] supportStates = new FootRootMotionSupportState[count];
            FootContactState[] leftContact = new FootContactState[count];
            FootContactState[] rightContact = new FootContactState[count];

            for (int i = 0; i < count; i++)
            {
                FrameSolveState state = states[i];
                rootXZ[i] = state.rootXZ;
                rootYawRadians[i] = state.rootYawRadians;
                rootDeltaXZ[i] = state.rootDeltaXZ;
                rootYawDeltaRadians[i] = state.rootYawDeltaRadians;
                leftAnchors[i] = new Vector2(state.leftAnchor.x, state.leftAnchor.z);
                rightAnchors[i] = new Vector2(state.rightAnchor.x, state.rightAnchor.z);
                leftConfidence[i] = state.leftConfidence;
                rightConfidence[i] = state.rightConfidence;
                leftTotalCost[i] = state.leftCost;
                rightTotalCost[i] = state.rightCost;
                leftPhaseDrive[i] = state.leftPhaseDrive;
                rightPhaseDrive[i] = state.rightPhaseDrive;
                conflictError[i] = Mathf.Abs(state.leftCost - state.rightCost);
                usedPrediction[i] = state.usedPrediction;
                leftPlant[i] = state.leftPlant;
                rightPlant[i] = state.rightPlant;
                supportStates[i] = state.supportState;
                leftContact[i] = state.leftContact;
                rightContact[i] = state.rightContact;
            }

            return new FootRootMotionResult
            {
                rootXZ = rootXZ,
                rootYawRadians = rootYawRadians,
                rootDeltaXZ = rootDeltaXZ,
                rootYawDeltaRadians = rootYawDeltaRadians,
                supportStates = supportStates,
                leftContact = leftContact,
                rightContact = rightContact,
                debug = new FootRootMotionDebugInfo
                {
                    leftAnchors = leftAnchors,
                    rightAnchors = rightAnchors,
                    leftConfidence = leftConfidence,
                    rightConfidence = rightConfidence,
                    conflictError = conflictError,
                    usedPrediction = usedPrediction,
                    leftPlant = leftPlant,
                    rightPlant = rightPlant,
                    supportStates = supportStates,
                    leftTotalCost = leftTotalCost,
                    rightTotalCost = rightTotalCost,
                    leftPhaseDrive = leftPhaseDrive,
                    rightPhaseDrive = rightPhaseDrive
                }
            };
        }

        private static void MarkSupportState(
            ref FrameSolveState state,
            FootRootMotionSupportState supportState,
            float confidence)
        {
            state.leftPlant = false;
            state.rightPlant = false;
            state.leftConfidence = 0f;
            state.rightConfidence = 0f;
            state.leftContact = FootContactState.Air;
            state.rightContact = FootContactState.Air;

            switch (supportState)
            {
                case FootRootMotionSupportState.LeftPlant:
                    state.leftPlant = true;
                    state.leftConfidence = confidence;
                    state.leftContact = FootContactState.Plant;
                    break;
                case FootRootMotionSupportState.RightPlant:
                    state.rightPlant = true;
                    state.rightConfidence = confidence;
                    state.rightContact = FootContactState.Plant;
                    break;
                case FootRootMotionSupportState.DoubleSupport:
                    state.leftPlant = true;
                    state.rightPlant = true;
                    state.leftConfidence = confidence;
                    state.rightConfidence = confidence;
                    state.leftContact = FootContactState.Plant;
                    state.rightContact = FootContactState.Plant;
                    break;
                case FootRootMotionSupportState.Air:
                case FootRootMotionSupportState.Unknown:
                default:
                    break;
            }
        }
    }
}
