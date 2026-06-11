using UnityEngine;

namespace KimodoBridge.Editor
{
    internal static class FootRootMotionSolver
    {
        private enum SupportFoot
        {
            None = 0,
            Left = 1,
            Right = 2
        }

        public static FootRootMotionResult Solve(FootRootMotionFrame[] frames, FootRootMotionSolverSettings settings)
        {
            settings = settings ?? new FootRootMotionSolverSettings();
            if (frames == null || frames.Length == 0)
            {
                return new FootRootMotionResult
                {
                    rootXZ = new Vector2[0],
                    rootYawRadians = new float[0],
                    rootDeltaXZ = new Vector2[0],
                    leftContact = new FootContactState[0],
                    rightContact = new FootContactState[0],
                    debug = new FootRootMotionDebugInfo()
                };
            }

            int count = frames.Length;
            SupportFoot[] supportFeet = new SupportFoot[count];
            float[] conflict = new float[count];
            EvaluateSupportState(frames, settings, supportFeet, conflict);

            Vector2[] rootXZ = new Vector2[count];
            float[] rootYaw = new float[count];
            Vector2[] rootDelta = new Vector2[count];
            Vector2[] leftAnchors = new Vector2[count];
            Vector2[] rightAnchors = new Vector2[count];
            float[] leftConfidence = new float[count];
            float[] rightConfidence = new float[count];
            bool[] usedPrediction = new bool[count];
            bool[] leftPlant = new bool[count];
            bool[] rightPlant = new bool[count];
            FootContactState[] leftStates = new FootContactState[count];
            FootContactState[] rightStates = new FootContactState[count];

            Vector2 sourceRoot0 = ToXZ(frames[0].sampledRootWorld);
            float sourceYaw0 = frames[0].rootYawRadians;
            Vector2 sourceLeftOffset0 = ComputeLocalOffset(frames[0].leftFootWorld, sourceRoot0, 0f);
            Vector2 sourceRightOffset0 = ComputeLocalOffset(frames[0].rightFootWorld, sourceRoot0, 0f);

            Vector2 leftAnchor = sourceLeftOffset0;
            Vector2 rightAnchor = sourceRightOffset0;
            rootXZ[0] = Vector2.zero;
            rootYaw[0] = 0f;
            rootDelta[0] = Vector2.zero;
            Vector2 previousRoot = Vector2.zero;
            float previousYaw = 0f;
            SupportFoot previousSupport = supportFeet[0];
            usedPrediction[0] = previousSupport == SupportFoot.None;

            MarkSupportDebug(0, previousSupport, leftPlant, rightPlant, leftStates, rightStates, leftConfidence, rightConfidence);
            leftAnchors[0] = leftAnchor;
            rightAnchors[0] = rightAnchor;

            for (int i = 1; i < count; i++)
            {
                float dt = Mathf.Max(1e-4f, frames[i].time - frames[i - 1].time);
                Vector2 sourceRoot = ToXZ(frames[i].sampledRootWorld);
                float sourceYaw = ComputeYawDelta(sourceYaw0, frames[i].rootYawRadians);
                Vector2 leftOffset = ComputeLocalOffset(frames[i].leftFootWorld, sourceRoot, sourceYaw);
                Vector2 rightOffset = ComputeLocalOffset(frames[i].rightFootWorld, sourceRoot, sourceYaw);
                SupportFoot support = supportFeet[i];

                Vector2 candidateRoot = previousRoot;
                float candidateYaw = previousYaw;

                if (support == SupportFoot.None)
                {
                    usedPrediction[i] = true;
                    rootXZ[i] = previousRoot;
                    rootYaw[i] = previousYaw;
                    rootDelta[i] = Vector2.zero;
                    leftAnchors[i] = leftAnchor;
                    rightAnchors[i] = rightAnchor;
                    MarkSupportDebug(i, support, leftPlant, rightPlant, leftStates, rightStates, leftConfidence, rightConfidence);

                    previousRoot = rootXZ[i];
                    previousYaw = rootYaw[i];
                    previousSupport = support;
                    continue;
                }

                if (support != previousSupport)
                {
                    if (support == SupportFoot.Left)
                    {
                        leftAnchor = previousRoot + RotateXZ(leftOffset, previousYaw);
                    }
                    else
                    {
                        rightAnchor = previousRoot + RotateXZ(rightOffset, previousYaw);
                    }
                }

                if (support == SupportFoot.Left)
                {
                    candidateYaw = SolveSupportYaw(frames[i], sourceRoot, sourceYaw, SupportFoot.Left, leftOffset, previousYaw);
                    candidateRoot = leftAnchor - RotateXZ(leftOffset, candidateYaw);
                }
                else if (support == SupportFoot.Right)
                {
                    candidateYaw = SolveSupportYaw(frames[i], sourceRoot, sourceYaw, SupportFoot.Right, rightOffset, previousYaw);
                    candidateRoot = rightAnchor - RotateXZ(rightOffset, candidateYaw);
                }

                Vector2 delta = ClampDelta(candidateRoot - previousRoot, settings.MaxDeltaDistance(dt));
                float rawYawDelta = ComputeYawDelta(previousYaw, candidateYaw);
                float yawDelta = ClampYawDelta(rawYawDelta, settings.MaxYawDeltaRadians(dt));

                rootXZ[i] = previousRoot + delta;
                rootYaw[i] = previousYaw + yawDelta;
                rootDelta[i] = delta;

                if (support == SupportFoot.Left)
                {
                    leftAnchor = rootXZ[i] + RotateXZ(leftOffset, rootYaw[i]);
                }
                else if (support == SupportFoot.Right)
                {
                    rightAnchor = rootXZ[i] + RotateXZ(rightOffset, rootYaw[i]);
                }

                leftAnchors[i] = leftAnchor;
                rightAnchors[i] = rightAnchor;
                MarkSupportDebug(i, support, leftPlant, rightPlant, leftStates, rightStates, leftConfidence, rightConfidence);

                previousRoot = rootXZ[i];
                previousYaw = rootYaw[i];
                previousSupport = support;
            }

            return new FootRootMotionResult
            {
                rootXZ = rootXZ,
                rootYawRadians = rootYaw,
                rootDeltaXZ = rootDelta,
                leftContact = leftStates,
                rightContact = rightStates,
                debug = new FootRootMotionDebugInfo
                {
                    leftAnchors = leftAnchors,
                    rightAnchors = rightAnchors,
                    leftConfidence = leftConfidence,
                    rightConfidence = rightConfidence,
                    conflictError = conflict,
                    usedPrediction = usedPrediction,
                    leftPlant = leftPlant,
                    rightPlant = rightPlant
                }
            };
        }

        private static void EvaluateSupportState(
            FootRootMotionFrame[] frames,
            FootRootMotionSolverSettings settings,
            SupportFoot[] supportFeet,
            float[] conflictError)
        {
            DetermineSupportFootSwitchByIkMotion(
                frames,
                settings != null ? settings.SupportSwitchWindowFrameCount : 2,
                supportFeet,
                conflictError);
        }

        private static void DetermineSupportFootSwitchByIkMotion(
            FootRootMotionFrame[] frames,
            int windowFrames,
            SupportFoot[] supportFeet,
            float[] conflictError)
        {
            if (frames == null || supportFeet == null)
            {
                return;
            }

            int count = Mathf.Min(frames.Length, supportFeet.Length);
            if (count == 0)
            {
                return;
            }

            windowFrames = Mathf.Max(2, windowFrames);

            float[] leftMotion = new float[count];
            float[] rightMotion = new float[count];
            float[] leftPrefix = new float[count];
            float[] rightPrefix = new float[count];

            for (int i = 1; i < count; i++)
            {
                leftMotion[i] = ComputeFootMotionScore(
                    frames[i - 1].leftFootWorld,
                    frames[i].leftFootWorld,
                    frames[i - 1].leftFootWorldRotation,
                    frames[i].leftFootWorldRotation);
                rightMotion[i] = ComputeFootMotionScore(
                    frames[i - 1].rightFootWorld,
                    frames[i].rightFootWorld,
                    frames[i - 1].rightFootWorldRotation,
                    frames[i].rightFootWorldRotation);

                leftPrefix[i] = leftPrefix[i - 1] + leftMotion[i];
                rightPrefix[i] = rightPrefix[i - 1] + rightMotion[i];
            }

            supportFeet[0] = SupportFoot.None;
            if (conflictError != null && conflictError.Length > 0)
            {
                conflictError[0] = 0f;
            }
            for (int i = 1; i < count; i++)
            {
                int startIndex = Mathf.Max(1, i - windowFrames + 2);
                float leftSum = leftPrefix[i] - leftPrefix[startIndex - 1];
                float rightSum = rightPrefix[i] - rightPrefix[startIndex - 1];
                if (conflictError != null && i < conflictError.Length)
                {
                    conflictError[i] = Mathf.Abs(leftSum - rightSum);
                }

                if (i < windowFrames - 1)
                {
                    supportFeet[i] = SupportFoot.None;
                    continue;
                }

                supportFeet[i] = DetermineSupportFootFromMotion(leftSum, rightSum);
            }
        }

        private static SupportFoot DetermineSupportFootFromMotion(float leftMotion, float rightMotion)
        {
            const float motionEpsilon = 1e-4f;
            float totalMotion = leftMotion + rightMotion;
            if (totalMotion <= motionEpsilon || Mathf.Abs(leftMotion - rightMotion) <= motionEpsilon)
            {
                return SupportFoot.None;
            }

            return leftMotion > rightMotion ? SupportFoot.Right : SupportFoot.Left;
        }

        private static float ComputeFootMotionScore(
            Vector3 previousPosition,
            Vector3 currentPosition,
            Quaternion previousRotation,
            Quaternion currentRotation)
        {
            float deltaPosition = Vector3.Distance(previousPosition, currentPosition);
            float deltaRotation = Quaternion.Angle(previousRotation, currentRotation) * 0.01f;
            return deltaPosition + deltaRotation;
        }

        private static float SolveSupportYaw(
            FootRootMotionFrame frame,
            Vector2 sourceRoot,
            float sourceYaw,
            SupportFoot support,
            Vector2 supportOffset,
            float fallbackYaw)
        {
            Vector2 hipOffset = ComputeLocalOffset(frame.hipWorld, sourceRoot, sourceYaw);
            Vector2 localHipFromSupport = hipOffset - supportOffset;
            Vector2 supportWorld = support == SupportFoot.Left
                ? ToXZ(frame.leftFootWorld)
                : ToXZ(frame.rightFootWorld);
            Vector2 sampledHipFromSupport = ToXZ(frame.hipWorld) - supportWorld;
            if (localHipFromSupport.sqrMagnitude < 1e-6f || sampledHipFromSupport.sqrMagnitude < 1e-6f)
            {
                return fallbackYaw;
            }

            float localYaw = Mathf.Atan2(localHipFromSupport.x, localHipFromSupport.y);
            float sampledYaw = Mathf.Atan2(sampledHipFromSupport.x, sampledHipFromSupport.y);
            return ComputeYawDelta(localYaw, sampledYaw);
        }

        private static Vector2 ClampDelta(Vector2 delta, float maxDistance)
        {
            if (!IsFinite(maxDistance) || delta.sqrMagnitude <= maxDistance * maxDistance)
            {
                return delta;
            }

            return delta.normalized * maxDistance;
        }

        private static float ClampYawDelta(float yawDelta, float maxRadians)
        {
            yawDelta = Mathf.DeltaAngle(0f, yawDelta * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (!IsFinite(maxRadians))
            {
                return yawDelta;
            }

            return Mathf.Clamp(yawDelta, -maxRadians, maxRadians);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float ComputeYawDelta(float fromRadians, float toRadians)
        {
            return Mathf.DeltaAngle(fromRadians * Mathf.Rad2Deg, toRadians * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        }

        private static Vector2 ComputeLocalOffset(Vector3 worldPoint, Vector2 sourceRoot, float sourceYaw)
        {
            return RotateXZ(ToXZ(worldPoint) - sourceRoot, -sourceYaw);
        }

        private static Vector2 RotateXZ(Vector2 value, float yawRadians)
        {
            float sin = Mathf.Sin(yawRadians);
            float cos = Mathf.Cos(yawRadians);
            return new Vector2(value.x * cos + value.y * sin, -value.x * sin + value.y * cos);
        }

        private static void MarkSupportDebug(
            int index,
            SupportFoot support,
            bool[] leftPlant,
            bool[] rightPlant,
            FootContactState[] leftStates,
            FootContactState[] rightStates,
            float[] leftConfidence,
            float[] rightConfidence)
        {
            bool left = support == SupportFoot.Left;
            bool right = support == SupportFoot.Right;
            leftPlant[index] = left;
            rightPlant[index] = right;
            leftStates[index] = left ? FootContactState.Plant : FootContactState.Air;
            rightStates[index] = right ? FootContactState.Plant : FootContactState.Air;
            leftConfidence[index] = left ? 1f : 0f;
            rightConfidence[index] = right ? 1f : 0f;
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }
}
