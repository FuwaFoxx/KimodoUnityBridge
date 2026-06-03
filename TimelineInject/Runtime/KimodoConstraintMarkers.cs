using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace TimelineInject
{
    public static class KimodoVectorExtensions
    {
        public static float[] ToArray(this Vector2 value)
        {
            return new[] { value.x, value.y };
        }

        public static float[] ToArray(this Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }
    }

    [Serializable]
    public class KimodoConstraintJson
    {
        public string type;
        public List<int> frame_indices = new List<int>();
        public List<float[]> smooth_root_2d;
        public List<float[]> global_root_heading;
        public List<float[][]> local_joints_rot;
        public List<float[]> root_positions;
        public List<string> joint_names;
    }

    public enum KimodoConstraintRigType
    {
        Soma30 = 0,
        G1 = 1,
        Smplx = 2,
        Unknown = 3
    }

    [Serializable]
    public sealed class KimodoMarkerSampleResult
    {
        public string constraintType = string.Empty;
        public double sampleTime;
        public KimodoConstraintRigType rigType = KimodoConstraintRigType.Soma30;
        public bool hasRootHeading = true;
        public Vector3 rootPosition;
        public Vector2 rootHeading = Vector2.right;
        public List<string> jointNames = new List<string>();
        public List<Vector3> localAxisAngles = new List<Vector3>();
        public List<int> sampledJointIndices = new List<int>();

        public KimodoMarkerSampleResult Clone()
        {
            return new KimodoMarkerSampleResult
            {
                constraintType = constraintType ?? string.Empty,
                sampleTime = sampleTime,
                rigType = rigType,
                hasRootHeading = hasRootHeading,
                rootPosition = rootPosition,
                rootHeading = rootHeading,
                jointNames = jointNames != null ? new List<string>(jointNames) : new List<string>(),
                localAxisAngles = localAxisAngles != null ? new List<Vector3>(localAxisAngles) : new List<Vector3>(),
                sampledJointIndices = sampledJointIndices != null ? new List<int>(sampledJointIndices) : new List<int>()
            };
        }
    }

    public interface IKimodoSampleMarker
    {
        bool TrySampleMarker(
            Animator animator,
            Transform skeletonRoot,
            TimelineClip sourceClip,
            string modelName,
            double globalTime,
            string markerType,
            out KimodoMarkerSampleResult result,
            out string error);
    }

    [Serializable]
    public abstract class KimodoConstraintMarkerBase : Marker
    {
        [Tooltip("If enabled, use manually edited marker values. If disabled, values are sampled from timeline pose at this marker time.")]
        public bool useOverride;
        [SerializeField]
        private KimodoMarkerSampleResult sampleData = new KimodoMarkerSampleResult();

        public abstract string ConstraintType { get; }

        public KimodoMarkerSampleResult SampleData
        {
            get
            {
                EnsureSampleData();
                return sampleData;
            }
            set
            {
                sampleData = value ?? new KimodoMarkerSampleResult();
                SyncConstraintType();
            }
        }

        protected void EnsureSampleData()
        {
            if (sampleData == null)
            {
                sampleData = new KimodoMarkerSampleResult();
            }

            SyncConstraintType();
        }

        private void SyncConstraintType()
        {
            if (sampleData != null)
            {
                sampleData.constraintType = ConstraintType;
            }
        }

        public Vector3 rootPosition
        {
            get => SampleData.rootPosition;
            set => SampleData.rootPosition = value;
        }

        public Vector2 smoothRoot2D
        {
            get => new Vector2(rootPosition.x, rootPosition.z);
            set => rootPosition = new Vector3(value.x, rootPosition.y, value.y);
        }

        public bool includeGlobalHeading
        {
            get => SampleData.hasRootHeading;
            set => SampleData.hasRootHeading = value;
        }

        public Vector2 globalRootHeading
        {
            get => SampleData.rootHeading;
            set => SampleData.rootHeading = value;
        }

        public List<string> jointNames
        {
            get => SampleData.jointNames;
            set => SampleData.jointNames = value ?? new List<string>();
        }

        public List<Vector3> localJointRots
        {
            get => SampleData.localAxisAngles;
            set => SampleData.localAxisAngles = value ?? new List<Vector3>();
        }

        protected virtual void OnEnable()
        {
            EnsureSampleData();
        }

        protected virtual void OnDisable()
        {
            // no-op: marker lifecycle event hub has been removed.
        }
    }

    [Serializable]
    public sealed class KimodoRoot2DConstraintMarker : KimodoConstraintMarkerBase
    {
        public override string ConstraintType => "root2d";
    }

    [Serializable]
    public sealed class KimodoFullBodyConstraintMarker : KimodoConstraintMarkerBase
    {
        public override string ConstraintType => "fullbody";
    }

    [Serializable]
    public class KimodoEndEffectorConstraintMarker : KimodoConstraintMarkerBase
    {
        public override string ConstraintType => "end-effector";
    }

    [Serializable]
    public sealed class KimodoLeftHandConstraintMarker : KimodoEndEffectorConstraintMarker
    {
        public override string ConstraintType => "left-hand";
    }

    [Serializable]
    public sealed class KimodoRightHandConstraintMarker : KimodoEndEffectorConstraintMarker
    {
        public override string ConstraintType => "right-hand";
    }

    [Serializable]
    public sealed class KimodoLeftFootConstraintMarker : KimodoEndEffectorConstraintMarker
    {
        public override string ConstraintType => "left-foot";
    }

    [Serializable]
    public sealed class KimodoRightFootConstraintMarker : KimodoEndEffectorConstraintMarker
    {
        public override string ConstraintType => "right-foot";
    }
}
