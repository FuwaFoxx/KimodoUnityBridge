using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityEngine.Timeline
{
    public static class KimodoConstraintJsonExporter
    {
        public static string ToConstraintsJson(IReadOnlyList<KimodoMarkerSampleResult> samples)
        {
            List<KimodoConstraintJson> constraints = BuildConstraints(samples, mergeByType: true);
            if (constraints.Count == 0)
            {
                return string.Empty;
            }

            return JsonConvert.SerializeObject(
                constraints,
                Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        public static List<KimodoConstraintJson> BuildConstraints(IReadOnlyList<KimodoMarkerSampleResult> samples)
        {
            var output = new List<KimodoConstraintJson>();
            if (samples == null)
            {
                return output;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                KimodoMarkerSampleResult sample = samples[i];
                KimodoConstraintJson json = BuildConstraint(sample);
                if (json != null)
                {
                    output.Add(json);
                }
            }

            return output;
        }

        public static KimodoConstraintJson BuildConstraint(KimodoMarkerSampleResult sample)
        {
            if (sample == null)
            {
                return null;
            }

            string type = sample.constraintType ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            if (string.Equals(type, "root2d", StringComparison.OrdinalIgnoreCase))
            {
                return BuildRoot2D(sample);
            }

            if (string.Equals(type, "fullbody", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFullBody(sample);
            }

            return BuildEndEffector(sample);
        }

        public static List<KimodoConstraintJson> BuildConstraints(IReadOnlyList<KimodoMarkerSampleResult> samples, bool mergeByType)
        {
            List<KimodoConstraintJson> constraints = BuildConstraints(samples);
            return mergeByType ? MergeConstraintsByType(constraints) : constraints;
        }

        private static KimodoConstraintJson BuildRoot2D(KimodoMarkerSampleResult sample)
        {
            var json = new KimodoConstraintJson
            {
                type = "root2d",
                frame_indices = new List<int> { sample.frameIndex },
                smooth_root_2d = new List<float[]>
                {
                    new[] { -sample.rootPosition.x, sample.rootPosition.z }
                }
            };

            if (sample.hasRootHeading)
            {
                json.global_root_heading = new List<float[]>
                {
                    new[] { -sample.rootHeading.x, sample.rootHeading.y }
                };
            }

            return json;
        }

        private static KimodoConstraintJson BuildFullBody(KimodoMarkerSampleResult sample)
        {
            Vector3 kimodoRoot = new Vector3(-sample.rootPosition.x, sample.rootPosition.y, sample.rootPosition.z);
            var json = new KimodoConstraintJson
            {
                type = "fullbody",
                frame_indices = new List<int> { sample.frameIndex },
                smooth_root_2d = new List<float[]>
                {
                    new[] { kimodoRoot.x, kimodoRoot.z }
                },
                root_positions = new List<float[]>
                {
                    new[] { kimodoRoot.x, kimodoRoot.y, kimodoRoot.z }
                },
                local_joints_rot = new List<float[][]>
                {
                    BuildLocalJointFrame(sample.localAxisAngles)
                }
            };

            return json;
        }

        private static KimodoConstraintJson BuildEndEffector(KimodoMarkerSampleResult sample)
        {
            Vector3 kimodoRoot = new Vector3(-sample.rootPosition.x, sample.rootPosition.y, sample.rootPosition.z);
            var json = new KimodoConstraintJson
            {
                type = sample.constraintType,
                frame_indices = new List<int> { sample.frameIndex },
                joint_names = sample.jointNames != null ? new List<string>(sample.jointNames) : new List<string>(),
                smooth_root_2d = new List<float[]>
                {
                    new[] { kimodoRoot.x, kimodoRoot.z }
                },
                root_positions = new List<float[]>
                {
                    new[] { kimodoRoot.x, kimodoRoot.y, kimodoRoot.z }
                },
                local_joints_rot = new List<float[][]>
                {
                    BuildLocalJointFrame(sample.localAxisAngles)
                }
            };

            return json;
        }

        private static float[][] BuildLocalJointFrame(List<Vector3> joints)
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

        private static List<KimodoConstraintJson> MergeConstraintsByType(List<KimodoConstraintJson> constraints)
        {
            var output = new List<KimodoConstraintJson>();
            if (constraints == null || constraints.Count == 0)
            {
                return output;
            }

            var buckets = new Dictionary<string, List<KimodoConstraintJson>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (KimodoConstraintJson c in constraints)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.type))
                {
                    continue;
                }

                if (!buckets.TryGetValue(c.type, out List<KimodoConstraintJson> list))
                {
                    list = new List<KimodoConstraintJson>();
                    buckets[c.type] = list;
                    order.Add(c.type);
                }
                list.Add(c);
            }

            foreach (string type in order)
            {
                List<KimodoConstraintJson> group = buckets[type];
                if (group == null || group.Count == 0)
                {
                    continue;
                }

                group.Sort((a, b) =>
                {
                    int af = (a.frame_indices != null && a.frame_indices.Count > 0) ? a.frame_indices[0] : int.MaxValue;
                    int bf = (b.frame_indices != null && b.frame_indices.Count > 0) ? b.frame_indices[0] : int.MaxValue;
                    return af.CompareTo(bf);
                });

                output.Add(BuildMergedConstraint(type, group));
            }

            return output;
        }

        private static KimodoConstraintJson BuildMergedConstraint(string type, List<KimodoConstraintJson> group)
        {
            var merged = new KimodoConstraintJson
            {
                type = type,
                frame_indices = new List<int>()
            };

            bool isRoot2D = string.Equals(type, "root2d", StringComparison.OrdinalIgnoreCase);
            bool isFullBody = string.Equals(type, "fullbody", StringComparison.OrdinalIgnoreCase);
            bool isEndEffectorFamily = string.Equals(type, "end-effector", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(type, "left-hand", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(type, "right-hand", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(type, "left-foot", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(type, "right-foot", StringComparison.OrdinalIgnoreCase);

            if (isRoot2D || isFullBody || isEndEffectorFamily)
            {
                merged.smooth_root_2d = new List<float[]>();
            }
            if (isFullBody || isEndEffectorFamily)
            {
                merged.root_positions = new List<float[]>();
                merged.local_joints_rot = new List<float[][]>();
            }
            if (isRoot2D)
            {
                merged.global_root_heading = new List<float[]>();
            }

            if (isEndEffectorFamily && group[0].joint_names != null && group[0].joint_names.Count > 0)
            {
                merged.joint_names = new List<string>(group[0].joint_names);
            }

            for (int i = 0; i < group.Count; i++)
            {
                KimodoConstraintJson c = group[i];
                if (c.frame_indices == null || c.frame_indices.Count == 0)
                {
                    continue;
                }

                merged.frame_indices.AddRange(c.frame_indices);
                if (merged.smooth_root_2d != null && c.smooth_root_2d != null)
                {
                    merged.smooth_root_2d.AddRange(c.smooth_root_2d);
                }
                if (merged.root_positions != null && c.root_positions != null)
                {
                    merged.root_positions.AddRange(c.root_positions);
                }
                if (merged.local_joints_rot != null && c.local_joints_rot != null)
                {
                    merged.local_joints_rot.AddRange(c.local_joints_rot);
                }
                if (merged.global_root_heading != null && c.global_root_heading != null)
                {
                    merged.global_root_heading.AddRange(c.global_root_heading);
                }
            }

            return merged;
        }
    }
}
