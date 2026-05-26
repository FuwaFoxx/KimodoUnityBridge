using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Timeline;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace KimodoUnityMotionTools.ProjectEditor
{
    [InitializeOnLoad]
    internal static class KimodoConstraintPoseCache
    {
        private sealed class PoseRigEntry
        {
            public Transform Root;
            public Dictionary<string, Transform> NameMap;
            public KimodoConstraintRigType RigType;
            public List<Material> GeneratedMaterials;
        }

        private static readonly Dictionary<KimodoConstraintRigType, PoseRigEntry> Rigs = new Dictionary<KimodoConstraintRigType, PoseRigEntry>();
        private static PoseRigEntry activeRig;

        static KimodoConstraintPoseCache()
        {
            Selection.selectionChanged += OnSelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += DestroyAll;
            SceneView.duringSceneGui += OnSceneGui;
        }

        public static bool ShowOrUpdateFromMarkerData(KimodoConstraintMarkerBase marker)
        {
            return TryShowOrUpdateFromMarkerData(marker, out _);
        }

        public static bool TryShowOrUpdateFromMarkerData(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryResolveModelName(marker, out string modelName, out error))
            {
                return false;
            }

            if (!KimodoConstraintMarkerPoseMapper.TryReadSample(marker, out KimodoMarkerSampleResult sample, out error))
            {
                return false;
            }

            if (sample == null)
            {
                error = "marker sample is null";
                return false;
            }

            if (!TryGetOrCreateRig(modelName, sample.rigType, out PoseRigEntry rig, out error))
            {
                return false;
            }

            if (!ApplySampleToRig(sample, modelName, rig, out error))
            {
                return false;
            }

            ShowOnlyRig(rig);
            SceneView.RepaintAll();
            return true;
        }

        public static bool TryCaptureToMarkerData(KimodoConstraintMarkerBase marker, out string error)
        {
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!TryResolveModelName(marker, out string modelName, out error))
            {
                return false;
            }

            if (!TryGetOrCreateRig(modelName, marker.SampleData != null ? marker.SampleData.rigType : KimodoConstraintRigType.Soma30, out PoseRigEntry rig, out error))
            {
                return false;
            }

            if (rig == null || rig.Root == null || rig.NameMap == null)
            {
                error = "pose rig is invalid";
                return false;
            }

            string[] modelJointNames = KimodoMarkerSamplingUtility.GetJointNamesForModel(modelName);
            if (modelJointNames == null || modelJointNames.Length == 0)
            {
                error = $"model joint layout not found for '{modelName}'";
                return false;
            }

            KimodoMarkerSampleResult target = marker.SampleData != null ? marker.SampleData.Clone() : new KimodoMarkerSampleResult();
            target.constraintType = marker.ConstraintType;
            target.sampleTime = marker.time;
            target.rigType = ResolveRigTypeFromModelName(modelName);

            string rootJoint = KimodoMarkerSamplingUtility.GetRootJointNameForModel(modelName);
            if (string.IsNullOrWhiteSpace(rootJoint) || !rig.NameMap.TryGetValue(rootJoint, out Transform rootTransform) || rootTransform == null)
            {
                error = $"root joint '{rootJoint}' not found on pose rig";
                return false;
            }

            target.rootPosition = rootTransform.position;
            Vector3 forward = rootTransform.forward;
            Vector2 heading = new Vector2(forward.x, forward.z);
            if (heading.sqrMagnitude <= 1e-8f)
            {
                heading = Vector2.right;
            }
            else
            {
                heading.Normalize();
            }
            target.rootHeading = heading;

            if (marker is KimodoRoot2DConstraintMarker)
            {
                target.localAxisAngles = new List<Vector3>();
                target.sampledJointIndices = new List<int>();
            }
            else
            {
                var localAxisAngles = new List<Vector3>(modelJointNames.Length);
                var sampledIndices = new List<int>(modelJointNames.Length);
                for (int i = 0; i < modelJointNames.Length; i++)
                {
                    string jointName = modelJointNames[i];
                    if (!rig.NameMap.TryGetValue(jointName, out Transform t) || t == null)
                    {
                        error = $"joint '{jointName}' missing on pose rig";
                        return false;
                    }

                    localAxisAngles.Add(KimodoRuntimeUtility.QuaternionToAxisAngleVector(t.localRotation));
                    sampledIndices.Add(i);
                }

                target.localAxisAngles = localAxisAngles;
                target.sampledJointIndices = sampledIndices;
            }

            if (!KimodoConstraintMarkerPoseMapper.TryWriteSample(marker, target, keepOverrideEnabled: marker.useOverride, out error))
            {
                return false;
            }

            return true;
        }

        public static void Hide()
        {
            foreach (KeyValuePair<KimodoConstraintRigType, PoseRigEntry> kv in Rigs)
            {
                PoseRigEntry entry = kv.Value;
                if (entry?.Root != null && entry.Root.gameObject != null && entry.Root.gameObject.activeSelf)
                {
                    entry.Root.gameObject.SetActive(false);
                }
            }
            activeRig = null;
            SceneView.RepaintAll();
        }

        public static void DestroyAll()
        {
            foreach (KeyValuePair<KimodoConstraintRigType, PoseRigEntry> kv in Rigs)
            {
                PoseRigEntry entry = kv.Value;
                if (entry?.Root != null && entry.Root.gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(entry.Root.gameObject);
                }
                if (entry?.GeneratedMaterials != null)
                {
                    for (int i = 0; i < entry.GeneratedMaterials.Count; i++)
                    {
                        Material m = entry.GeneratedMaterials[i];
                        if (m != null)
                        {
                            UnityEngine.Object.DestroyImmediate(m);
                        }
                    }
                }
            }

            Rigs.Clear();
            activeRig = null;
            SceneView.RepaintAll();
        }

        private static void OnSelectionChanged()
        {
            if (Selection.activeObject is KimodoConstraintMarkerBase)
            {
                return;
            }

            if (!KimodoConstraintOverrideEditWindow.HasAnyOpenWindow())
            {
                Hide();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange _)
        {
            DestroyAll();
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            KimodoConstraintOverrideEditWindow window = KimodoConstraintOverrideEditWindow.GetOpenWindow();
            KimodoConstraintMarkerBase marker = window != null ? window.TargetMarker : null;
            if (window == null || marker == null || !marker.useOverride)
            {
                return;
            }
        }

        private static void ShowOnlyRig(PoseRigEntry entryToShow)
        {
            activeRig = entryToShow;
            foreach (KeyValuePair<KimodoConstraintRigType, PoseRigEntry> kv in Rigs)
            {
                PoseRigEntry entry = kv.Value;
                if (entry?.Root == null || entry.Root.gameObject == null)
                {
                    continue;
                }

                bool visible = ReferenceEquals(entry, activeRig);
                if (entry.Root.gameObject.activeSelf != visible)
                {
                    entry.Root.gameObject.SetActive(visible);
                }
            }
        }

        private static bool TryGetOrCreateRig(string modelName, KimodoConstraintRigType sampleRigType, out PoseRigEntry rig, out string error)
        {
            error = string.Empty;
            KimodoConstraintRigType rigType = sampleRigType != KimodoConstraintRigType.Unknown ? sampleRigType : ResolveRigTypeFromModelName(modelName);
            if (Rigs.TryGetValue(rigType, out rig) && rig != null && rig.Root != null && rig.Root.gameObject != null)
            {
                return true;
            }

            GameObject prefab = LoadRigPrefab(rigType);
            if (prefab == null)
            {
                error = $"pose rig prefab not found for rig type '{rigType}'";
                rig = null;
                return false;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = $"__KimodoPoseCache_{rigType}";
            instance.hideFlags = HideFlags.DontSave;
            instance.SetActive(false);
            List<Material> generatedMaterials = ConfigurePreviewMeshAppearance(instance);

            Transform root = instance.transform;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var nameMap = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t == null || string.IsNullOrWhiteSpace(t.name) || nameMap.ContainsKey(t.name))
                {
                    continue;
                }

                nameMap[t.name] = t;
            }

            rig = new PoseRigEntry
            {
                Root = root,
                NameMap = nameMap,
                RigType = rigType,
                GeneratedMaterials = generatedMaterials
            };

            Rigs[rigType] = rig;
            return true;
        }

        private static List<Material> ConfigurePreviewMeshAppearance(GameObject instance)
        {
            var generated = new List<Material>();
            if (instance == null)
            {
                return generated;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Transform tr = renderer.transform;
                if (tr != null)
                {
                    tr.localScale *= 1.1f;
                }

                Material[] shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                {
                    continue;
                }
                Material[] mats = new Material[shared.Length];
                for (int m = 0; m < mats.Length; m++)
                {
                    Material source = shared[m];
                    if (source == null)
                    {
                        mats[m] = null;
                        continue;
                    }

                    Material mat = new Material(source)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        name = $"{source.name}_PoseCache"
                    };
                    // Force semi-transparent red preview for all pose cache meshes.
                    SetMaterialTransparentRed(mat);
                    mats[m] = mat;
                    generated.Add(mat);
                }
                renderer.sharedMaterials = mats;
            }

            return generated;
        }

        private static void SetMaterialTransparentRed(Material mat)
        {
            if (mat == null)
            {
                return;
            }

            const float alpha = 0.35f;
            Color red = new Color(1f, 0f, 0f, alpha);
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", red);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", red);
            }

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
            }
            if (mat.HasProperty("_AlphaClip"))
            {
                mat.SetFloat("_AlphaClip", 0f);
            }
            if (mat.HasProperty("_SrcBlend"))
            {
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            }
            if (mat.HasProperty("_DstBlend"))
            {
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }
            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetInt("_ZWrite", 0);
            }
            if (mat.HasProperty("_Blend"))
            {
                mat.SetInt("_Blend", 0);
            }

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        private static bool ApplySampleToRig(
            KimodoMarkerSampleResult sample,
            string modelName,
            PoseRigEntry rig,
            out string error)
        {
            error = string.Empty;
            if (sample == null || rig == null || rig.Root == null || rig.NameMap == null)
            {
                error = "invalid sample or rig";
                return false;
            }

            string[] modelJointNames = KimodoMarkerSamplingUtility.GetJointNamesForModel(modelName);
            if (modelJointNames == null || modelJointNames.Length == 0)
            {
                error = $"model joint layout not found for '{modelName}'";
                return false;
            }

            int count = sample.localAxisAngles != null ? sample.localAxisAngles.Count : 0;
            int applyCount = Mathf.Min(modelJointNames.Length, count);
            for (int i = 0; i < applyCount; i++)
            {
                string jointName = modelJointNames[i];
                if (!rig.NameMap.TryGetValue(jointName, out Transform t) || t == null)
                {
                    error = $"joint '{jointName}' missing on pose rig";
                    return false;
                }

                t.localRotation = AxisAngleToQuaternion(sample.localAxisAngles[i]);
            }

            string rootJointName = KimodoMarkerSamplingUtility.GetRootJointNameForModel(modelName);
            if (!string.IsNullOrWhiteSpace(rootJointName) && rig.NameMap.TryGetValue(rootJointName, out Transform rootJoint) && rootJoint != null)
            {
                rootJoint.position = sample.rootPosition;
            }
            else
            {
                rig.Root.position = sample.rootPosition;
            }

            return true;
        }

        private static Quaternion AxisAngleToQuaternion(Vector3 axisAngle)
        {
            float angleRad = axisAngle.magnitude;
            if (angleRad <= 1e-8f)
            {
                return Quaternion.identity;
            }

            Vector3 axis = axisAngle / angleRad;
            return Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
        }

        private static bool TryResolveModelName(KimodoConstraintMarkerBase marker, out string modelName, out string error)
        {
            modelName = "Kimodo-SOMA-RP-v1";
            error = string.Empty;
            if (marker == null)
            {
                error = "marker is null";
                return false;
            }

            if (!KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(marker, out TimelineClip clipRange) || clipRange == null)
            {
                error = "clip range not found";
                return false;
            }

            if (clipRange.asset is KimodoPlayableClip playableClip && !string.IsNullOrWhiteSpace(playableClip.bridgeModelName))
            {
                modelName = playableClip.bridgeModelName.Trim();
            }

            return true;
        }

        private static KimodoConstraintRigType ResolveRigTypeFromModelName(string modelName)
        {
            string m = (modelName ?? string.Empty).Trim().ToLowerInvariant();
            if (m.Contains("smplx"))
            {
                return KimodoConstraintRigType.Smplx;
            }

            if (m.Contains("g1"))
            {
                return KimodoConstraintRigType.G1;
            }

            return KimodoConstraintRigType.Soma30;
        }

        private static GameObject LoadRigPrefab(KimodoConstraintRigType rigType)
        {
            string path = ResolveRigModelPath(rigType);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static string ResolveRigModelPath(KimodoConstraintRigType rigType)
        {
            string fileName;
            switch (rigType)
            {
                case KimodoConstraintRigType.Smplx:
                    fileName = "SMPLX.fbx";
                    break;
                case KimodoConstraintRigType.G1:
                    fileName = "G1.fbx";
                    break;
                case KimodoConstraintRigType.Soma30:
                default:
                    fileName = "SOMA30.fbx";
                    break;
            }

            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(KimodoConstraintPoseCache).Assembly);
            if (packageInfo != null)
            {
                string byAssemblyPackage = $"{NormalizeAssetPath(packageInfo.assetPath)}/Editor/Model/{fileName}";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(byAssemblyPackage) != null)
                {
                    return byAssemblyPackage;
                }
            }

            const string packageName = "com.unity.kimodo_unity_motion_tools";
            string byPackageName = $"Packages/{packageName}/Editor/Model/{fileName}";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(byPackageName) != null)
            {
                return byPackageName;
            }

            string byAssetsFolder = $"Assets/Editor/Model/{fileName}";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(byAssetsFolder) != null)
            {
                return byAssetsFolder;
            }

            return $"Editor/Model/{fileName}";
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
