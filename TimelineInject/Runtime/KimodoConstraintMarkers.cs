using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Timeline
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

    public interface IKimodoConstraintMarker
    {
        KimodoConstraintJson ToJson();
    }

    [Serializable]
    public sealed class KimodoMarkerSampleRequest
    {
        public Animator animator;
        public Transform skeletonRoot;
        public TimelineClip sourceClip;
        public string modelName;
        public double globalTime;
        public int frameIndex;
        public string markerType;
    }

    [Serializable]
    public sealed class KimodoMarkerSampleResult
    {
        public Vector3 rootPosition;
        public Vector2 rootHeading;
        public List<Vector3> localAxisAngles = new List<Vector3>();
        // Optional: indices of joints that were actually resolved/sampled.
        public List<int> sampledJointIndices = new List<int>();
    }

    public interface IKimodoSampleMarker
    {
        bool TrySampleMarker(KimodoMarkerSampleRequest request, out KimodoMarkerSampleResult result, out string error);
    }

    [Serializable]
    public abstract class KimodoConstraintMarkerBase : Marker, IKimodoConstraintMarker
    {
        [Tooltip("If enabled, use manually edited marker values. If disabled, values are sampled from timeline pose at this marker time.")]
        public bool useOverride;

        public abstract string ConstraintType { get; }

        protected KimodoConstraintJson CreateBase()
        {
            return new KimodoConstraintJson
            {
                type = ConstraintType,
                frame_indices = new List<int>()
            };
        }

        public abstract KimodoConstraintJson ToJson();
    }

    [Serializable]
    public sealed class KimodoRoot2DConstraintMarker : KimodoConstraintMarkerBase
    {
        public override string ConstraintType => "root2d";

        public int frameIndex;
        public Vector2 smoothRoot2D = Vector2.zero;
        public bool includeGlobalHeading;
        public Vector2 globalRootHeading = Vector2.right;

        public override KimodoConstraintJson ToJson()
        {
            KimodoConstraintJson json = CreateBase();
            json.frame_indices.Add(frameIndex);
            Vector2 kimodoRoot2D = new Vector2(-smoothRoot2D.x, smoothRoot2D.y);
            json.smooth_root_2d = new List<float[]> { kimodoRoot2D.ToArray() };

            if (includeGlobalHeading)
            {
                Vector2 kimodoHeading = new Vector2(-globalRootHeading.x, globalRootHeading.y);
                json.global_root_heading = new List<float[]> { kimodoHeading.ToArray() };
            }

            return json;
        }
    }

    [Serializable]
    public sealed class KimodoFullBodyConstraintMarker : KimodoConstraintMarkerBase
    {
        public override string ConstraintType => "fullbody";

        public int frameIndex;
        public Vector2 smoothRoot2D = Vector2.zero;
        public Vector3 rootPosition = new Vector3(0f, 1f, 0f);
        [Tooltip("Single frame local joints axis-angle xyz (radians).")]
        public List<Vector3> localJointRots = new List<Vector3>();

        public override KimodoConstraintJson ToJson()
        {
            KimodoConstraintJson json = CreateBase();
            json.frame_indices.Add(frameIndex);
            Vector3 kimodoRoot = new Vector3(-rootPosition.x, rootPosition.y, rootPosition.z);
            Vector2 kimodoRoot2D = new Vector2(kimodoRoot.x, kimodoRoot.z);
            json.smooth_root_2d = new List<float[]> { kimodoRoot2D.ToArray() };
            json.root_positions = new List<float[]> { kimodoRoot.ToArray() };
            json.local_joints_rot = new List<float[][]> { BuildSingleLocalJointRotFrame(ToKimodoAxisAngles(localJointRots)) };
            return json;
        }

        internal static float[][] BuildSingleLocalJointRotFrame(List<Vector3> joints)
        {
            if (joints == null || joints.Count == 0)
            {
                return Array.Empty<float[]>();
            }

            float[][] data = new float[joints.Count][];
            for (int i = 0; i < joints.Count; i++)
            {
                data[i] = joints[i].ToArray();
            }

            return data;
        }

        internal static List<Vector3> ToKimodoAxisAngles(List<Vector3> unityAxisAngles)
        {
            var result = new List<Vector3>();
            if (unityAxisAngles == null || unityAxisAngles.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < unityAxisAngles.Count; i++)
            {
                Vector3 unityAxisAngle = unityAxisAngles[i];
                float angleRad = unityAxisAngle.magnitude;
                if (angleRad <= 1e-8f)
                {
                    result.Add(Vector3.zero);
                    continue;
                }

                Vector3 axis = unityAxisAngle / angleRad;
                Quaternion unityLocal = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
                Quaternion kimodoLocal = new Quaternion(unityLocal.x, -unityLocal.y, -unityLocal.z, unityLocal.w);
                result.Add(QuaternionToAxisAngleVector(kimodoLocal));
            }

            return result;
        }

        private static Vector3 QuaternionToAxisAngleVector(Quaternion q)
        {
            q.Normalize();
            q.ToAngleAxis(out float degrees, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis == Vector3.zero)
            {
                return Vector3.zero;
            }

            if (degrees > 180f)
            {
                degrees -= 360f;
            }

            return axis.normalized * (degrees * Mathf.Deg2Rad);
        }
    }

    [Serializable]
    public class KimodoEndEffectorConstraintMarker : KimodoConstraintMarkerBase
    {
        public override string ConstraintType => "end-effector";

        public int frameIndex;
        [Tooltip("Allowed values follow Kimodo convention, e.g. LeftHand/RightHand/LeftFoot/RightFoot/Hips.")]
        public List<string> jointNames = new List<string> { "LeftHand" };
        public Vector2 smoothRoot2D = Vector2.zero;
        public Vector3 rootPosition = new Vector3(0f, 1f, 0f);
        [Tooltip("Single frame local joints axis-angle xyz (radians).")]
        public List<Vector3> localJointRots = new List<Vector3>();

        public override KimodoConstraintJson ToJson()
        {
            KimodoConstraintJson json = CreateBase();
            json.frame_indices.Add(frameIndex);
            json.joint_names = new List<string>(jointNames ?? new List<string>());
            Vector3 kimodoRoot = new Vector3(-rootPosition.x, rootPosition.y, rootPosition.z);
            Vector2 kimodoRoot2D = new Vector2(kimodoRoot.x, kimodoRoot.z);
            json.smooth_root_2d = new List<float[]> { kimodoRoot2D.ToArray() };
            json.root_positions = new List<float[]> { kimodoRoot.ToArray() };
            json.local_joints_rot = new List<float[][]> { KimodoFullBodyConstraintMarker.BuildSingleLocalJointRotFrame(KimodoFullBodyConstraintMarker.ToKimodoAxisAngles(localJointRots)) };
            return json;
        }
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
