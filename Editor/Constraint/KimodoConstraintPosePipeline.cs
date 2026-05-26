using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    internal static class KimodoConstraintPosePipeline
    {
        internal static bool TryBuildUnityPoseFromMarker(
            KimodoConstraintMarkerBase marker,
            out KimodoMarkerSampleResult pose,
            out string error)
        {
            return TryReadUnityPoseFromMarkerData(marker, out pose, out error);
        }

        internal static bool TryReadUnityPoseFromMarkerData(
            KimodoConstraintMarkerBase marker,
            out KimodoMarkerSampleResult pose,
            out string error)
        {
            pose = null;
            error = string.Empty;

            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            pose = new KimodoMarkerSampleResult
            {
                rootPosition = Vector3.zero,
                rootHeading = Vector2.right,
                localAxisAngles = new List<Vector3>()
            };

            if (marker is KimodoRoot2DConstraintMarker root2D)
            {
                pose.rootPosition = new Vector3(root2D.smoothRoot2D.x, 0f, root2D.smoothRoot2D.y);
                if (root2D.includeGlobalHeading)
                {
                    pose.rootHeading = root2D.globalRootHeading;
                }

                return true;
            }

            if (marker is KimodoFullBodyConstraintMarker fullBody)
            {
                pose.rootPosition = fullBody.rootPosition;
                if (fullBody.localJointRots == null || fullBody.localJointRots.Count == 0)
                {
                    error = "marker override has no joint rotations";
                    return false;
                }

                pose.localAxisAngles.AddRange(fullBody.localJointRots);
                return true;
            }

            if (marker is KimodoEndEffectorConstraintMarker endEffector)
            {
                pose.rootPosition = endEffector.rootPosition;
                if (endEffector.localJointRots == null || endEffector.localJointRots.Count == 0)
                {
                    error = "marker override has no joint rotations";
                    return false;
                }

                pose.localAxisAngles.AddRange(endEffector.localJointRots);
                return true;
            }

            error = "unsupported marker type";
            return false;
        }

        internal static bool TryWriteUnityPoseToMarker(
            KimodoConstraintMarkerBase marker,
            KimodoMarkerSampleResult pose,
            out string error)
        {
            error = string.Empty;
            if (!TryWriteUnityPoseToMarkerData(marker, pose, keepOverrideEnabled: true, out error))
            {
                return false;
            }

            EditorUtility.SetDirty(marker);
            return true;
        }

        internal static bool TryWriteUnityPoseToMarkerData(
            KimodoConstraintMarkerBase marker,
            KimodoMarkerSampleResult pose,
            bool keepOverrideEnabled,
            out string error)
        {
            error = string.Empty;

            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (pose == null)
            {
                error = "pose is null";
                return false;
            }

            if (marker is KimodoRoot2DConstraintMarker root2D)
            {
                root2D.smoothRoot2D = new Vector2(pose.rootPosition.x, pose.rootPosition.z);
                if (root2D.includeGlobalHeading)
                {
                    root2D.globalRootHeading = pose.rootHeading;
                }
            }
            else if (marker is KimodoFullBodyConstraintMarker fullBody)
            {
                fullBody.smoothRoot2D = new Vector2(pose.rootPosition.x, pose.rootPosition.z);
                fullBody.rootPosition = pose.rootPosition;
                fullBody.localJointRots = pose.localAxisAngles != null
                    ? new List<Vector3>(pose.localAxisAngles)
                    : new List<Vector3>();
            }
            else if (marker is KimodoEndEffectorConstraintMarker endEffector)
            {
                endEffector.smoothRoot2D = new Vector2(pose.rootPosition.x, pose.rootPosition.z);
                endEffector.rootPosition = pose.rootPosition;
                endEffector.localJointRots = pose.localAxisAngles != null
                    ? new List<Vector3>(pose.localAxisAngles)
                    : new List<Vector3>();
            }
            else
            {
                error = "unsupported marker type";
                return false;
            }

            if (keepOverrideEnabled)
            {
                marker.useOverride = true;
            }

            return true;
        }

        internal static void ApplyPreviewToMarkerData(KimodoConstraintMarkerBase marker, KimodoConstraintJson preview)
        {
            if (marker == null || preview == null)
            {
                return;
            }

            if (preview.frame_indices != null && preview.frame_indices.Count > 0)
            {
                SetFrameIndex(marker, preview.frame_indices[0]);
            }

            if (marker is KimodoRoot2DConstraintMarker root2D)
            {
                if (preview.smooth_root_2d != null && preview.smooth_root_2d.Count > 0)
                {
                    root2D.smoothRoot2D = ToVector2(preview.smooth_root_2d[0]);
                }

                bool hasHeading = preview.global_root_heading != null && preview.global_root_heading.Count > 0;
                root2D.includeGlobalHeading = hasHeading;
                if (hasHeading)
                {
                    root2D.globalRootHeading = ToVector2(preview.global_root_heading[0]);
                }
                return;
            }

            if (marker is KimodoFullBodyConstraintMarker fullBody)
            {
                if (preview.smooth_root_2d != null && preview.smooth_root_2d.Count > 0)
                {
                    fullBody.smoothRoot2D = ToVector2(preview.smooth_root_2d[0]);
                }

                if (preview.root_positions != null && preview.root_positions.Count > 0)
                {
                    fullBody.rootPosition = ToVector3(preview.root_positions[0]);
                }

                if (preview.local_joints_rot != null && preview.local_joints_rot.Count > 0)
                {
                    fullBody.localJointRots = ToVector3List(preview.local_joints_rot[0]);
                }
                return;
            }

            if (marker is KimodoEndEffectorConstraintMarker endEffector)
            {
                if (preview.joint_names != null)
                {
                    endEffector.jointNames = new List<string>(preview.joint_names);
                }

                if (preview.smooth_root_2d != null && preview.smooth_root_2d.Count > 0)
                {
                    endEffector.smoothRoot2D = ToVector2(preview.smooth_root_2d[0]);
                }

                if (preview.root_positions != null && preview.root_positions.Count > 0)
                {
                    endEffector.rootPosition = ToVector3(preview.root_positions[0]);
                }

                if (preview.local_joints_rot != null && preview.local_joints_rot.Count > 0)
                {
                    endEffector.localJointRots = ToVector3List(preview.local_joints_rot[0]);
                }
            }
        }

        internal static KimodoConstraintJson BuildConstraintJsonForExport(KimodoConstraintMarkerBase marker)
        {
            if (marker == null)
            {
                return null;
            }

            if (marker is KimodoRoot2DConstraintMarker root2D)
            {
                Vector2 kimodoRoot2D = KimodoSpaceConversionUtility.ToKimodoHeading(root2D.smoothRoot2D);
                var json = new KimodoConstraintJson
                {
                    type = root2D.ConstraintType,
                    frame_indices = new List<int> { root2D.frameIndex },
                    smooth_root_2d = new List<float[]> { new[] { kimodoRoot2D.x, kimodoRoot2D.y } }
                };

                if (root2D.includeGlobalHeading)
                {
                    Vector2 kimodoHeading = KimodoSpaceConversionUtility.ToKimodoHeading(root2D.globalRootHeading);
                    json.global_root_heading = new List<float[]> { new[] { kimodoHeading.x, kimodoHeading.y } };
                }

                return json;
            }

            if (marker is KimodoFullBodyConstraintMarker fullBody)
            {
                Vector3 kimodoRoot = KimodoSpaceConversionUtility.ToKimodoRootPosition(fullBody.rootPosition);
                List<Vector3> kimodoJoints = ToKimodoAxisAngleList(fullBody.localJointRots);
                Vector2 kimodoRoot2D = new Vector2(kimodoRoot.x, kimodoRoot.z);

                return new KimodoConstraintJson
                {
                    type = fullBody.ConstraintType,
                    frame_indices = new List<int> { fullBody.frameIndex },
                    smooth_root_2d = new List<float[]> { new[] { kimodoRoot2D.x, kimodoRoot2D.y } },
                    root_positions = new List<float[]> { new[] { kimodoRoot.x, kimodoRoot.y, kimodoRoot.z } },
                    local_joints_rot = new List<float[][]> { BuildSingleLocalJointRotFrame(kimodoJoints) }
                };
            }

            if (marker is KimodoEndEffectorConstraintMarker endEffector)
            {
                Vector3 kimodoRoot = KimodoSpaceConversionUtility.ToKimodoRootPosition(endEffector.rootPosition);
                List<Vector3> kimodoJoints = ToKimodoAxisAngleList(endEffector.localJointRots);
                Vector2 kimodoRoot2D = new Vector2(kimodoRoot.x, kimodoRoot.z);

                return new KimodoConstraintJson
                {
                    type = endEffector.ConstraintType,
                    frame_indices = new List<int> { endEffector.frameIndex },
                    joint_names = endEffector.jointNames != null ? new List<string>(endEffector.jointNames) : new List<string>(),
                    smooth_root_2d = new List<float[]> { new[] { kimodoRoot2D.x, kimodoRoot2D.y } },
                    root_positions = new List<float[]> { new[] { kimodoRoot.x, kimodoRoot.y, kimodoRoot.z } },
                    local_joints_rot = new List<float[][]> { BuildSingleLocalJointRotFrame(kimodoJoints) }
                };
            }

            return null;
        }

        private static void SetFrameIndex(KimodoConstraintMarkerBase marker, int value)
        {
            if (marker is KimodoRoot2DConstraintMarker root2D)
            {
                root2D.frameIndex = value;
                return;
            }

            if (marker is KimodoFullBodyConstraintMarker fullBody)
            {
                fullBody.frameIndex = value;
                return;
            }

            if (marker is KimodoEndEffectorConstraintMarker endEffector)
            {
                endEffector.frameIndex = value;
            }
        }

        private static Vector2 ToVector2(float[] value)
        {
            return value != null && value.Length >= 2 ? new Vector2(value[0], value[1]) : Vector2.zero;
        }

        private static Vector3 ToVector3(float[] value)
        {
            return value != null && value.Length >= 3 ? new Vector3(value[0], value[1], value[2]) : Vector3.zero;
        }

        private static List<Vector3> ToVector3List(float[][] values)
        {
            var result = new List<Vector3>();
            if (values == null)
            {
                return result;
            }

            for (int i = 0; i < values.Length; i++)
            {
                result.Add(ToVector3(values[i]));
            }

            return result;
        }

        private static float[][] BuildSingleLocalJointRotFrame(List<Vector3> joints)
        {
            if (joints == null || joints.Count == 0)
            {
                return Array.Empty<float[]>();
            }

            float[][] data = new float[joints.Count][];
            for (int i = 0; i < joints.Count; i++)
            {
                Vector3 v = joints[i];
                data[i] = new[] { v.x, v.y, v.z };
            }

            return data;
        }

        private static List<Vector3> ToKimodoAxisAngleList(List<Vector3> unityAxisAngles)
        {
            var result = new List<Vector3>();
            if (unityAxisAngles == null || unityAxisAngles.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < unityAxisAngles.Count; i++)
            {
                result.Add(KimodoSpaceConversionUtility.ToKimodoAxisAngle(unityAxisAngles[i]));
            }

            return result;
        }
    }
}
