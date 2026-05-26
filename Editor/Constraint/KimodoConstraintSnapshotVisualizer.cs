using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor
{
    [InitializeOnLoad]
    internal static class KimodoConstraintSnapshotVisualizer
    {
        private const double RebuildDebounceSeconds = 0.05;
        private const string LogPrefix = "[Kimodo][ConstraintSnapshot]";

        private static readonly Dictionary<string, RigCacheEntry> RigCache = new Dictionary<string, RigCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SnapshotRenderEntry> ActiveRenders = new List<SnapshotRenderEntry>();

        private static bool dirty = true;
        private static bool hooksReady;
        private static double rebuildAfterTime;
        private static int lastSelectionHash;
        private static int lastMarkerStateHash;

        static KimodoConstraintSnapshotVisualizer()
        {
            EnsureHooks();
            MarkDirty();
        }

        private static void EnsureHooks()
        {
            if (hooksReady)
            {
                return;
            }

            hooksReady = true;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnSelectionChanged()
        {
            if (CollectSelectedMarkers().Count == 0)
            {
                HideAllRigsAndClearActiveRenders();
                SceneView.RepaintAll();
            }
            MarkDirty();
        }

        private static void OnUndoRedoPerformed()
        {
            MarkDirty();
        }

        private static void OnPlayModeChanged(PlayModeStateChange _)
        {
            MarkDirty();
        }

        private static void OnBeforeAssemblyReload()
        {
            ClearAllCaches();
        }

        private static void MarkDirty()
        {
            dirty = true;
            rebuildAfterTime = EditorApplication.timeSinceStartup + RebuildDebounceSeconds;
        }

        internal static void RequestManualRefresh()
        {
            MarkDirty();
            SceneView.RepaintAll();
        }

        private static void OnEditorUpdate()
        {
            if (!dirty)
            {
                if (SelectionHash() != lastSelectionHash)
                {
                    MarkDirty();
                }
                else if (ComputeMarkerStateHash() != lastMarkerStateHash)
                {
                    MarkDirty();
                }
                return;
            }

            if (EditorApplication.timeSinceStartup < rebuildAfterTime)
            {
                return;
            }

            RebuildSnapshots();
            dirty = false;
            SceneView.RepaintAll();
        }

        private static int SelectionHash()
        {
            unchecked
            {
                int hash = 17;
                UnityEngine.Object[] selected = Selection.objects;
                for (int i = 0; i < selected.Length; i++)
                {
                    hash = hash * 31 + (selected[i] != null ? selected[i].GetInstanceID() : 0);
                }

                return hash;
            }
        }

        private static int ComputeMarkerStateHash()
        {
            unchecked
            {
                int hash = 41;
                List<KimodoConstraintMarkerBase> markers = CollectSelectedMarkers();
                for (int i = 0; i < markers.Count; i++)
                {
                    KimodoConstraintMarkerBase marker = markers[i];
                    if (marker == null)
                    {
                        continue;
                    }

                    hash = hash * 31 + marker.GetInstanceID();
                    hash = hash * 31 + marker.time.GetHashCode();
                    if (KimodoConstraintMarkerEditorUtility.TryGetClipRangeForMarker(marker, out TimelineClip clipRange) && clipRange != null)
                    {
                        hash = hash * 31 + clipRange.start.GetHashCode();
                        hash = hash * 31 + clipRange.end.GetHashCode();
                        hash = hash * 31 + clipRange.duration.GetHashCode();
                    }
                }
                return hash;
            }
        }

        private static void RebuildSnapshots()
        {
            lastSelectionHash = SelectionHash();
            lastMarkerStateHash = ComputeMarkerStateHash();
            SetAllRigVisibility(false);
            ActiveRenders.Clear();

            List<KimodoConstraintMarkerBase> markers = CollectSelectedMarkers();
            if (markers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < markers.Count; i++)
            {
                KimodoConstraintMarkerBase marker = markers[i];
                if (!TryBuildRenderEntry(marker, out SnapshotRenderEntry entry, out string error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Debug.LogWarning($"{LogPrefix} Skip marker '{marker?.name}': {error}");
                    }
                    continue;
                }

                ActiveRenders.Add(entry);
            }
        }

        private static List<KimodoConstraintMarkerBase> CollectSelectedMarkers()
        {
            var result = new List<KimodoConstraintMarkerBase>();
            UnityEngine.Object[] selected = Selection.objects;
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] is KimodoConstraintMarkerBase marker)
                {
                    result.Add(marker);
                }
            }

            // Keep preview alive while override edit session is active, even when selection changes.
            KimodoConstraintOverrideEditSession.AppendActiveMarkers(result);
            return result;
        }

        private static bool TryBuildRenderEntry(
            KimodoConstraintMarkerBase marker,
            out SnapshotRenderEntry renderEntry,
            out string error)
        {
            renderEntry = default;
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

            if (!(clipRange.asset is KimodoPlayableClip playableClip))
            {
                error = "clip asset is not KimodoPlayableClip";
                return false;
            }

            TrackAsset track = clipRange.GetParentTrack();
            if (track == null && marker.parent is TrackAsset markerTrack)
            {
                track = markerTrack;
            }
            if (track == null)
            {
                error = "parent track not found";
                return false;
            }

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            if (!TryResolveBoundAnimatorForTrack(director, track, out Animator boundAnimator, out string bindError))
            {
                error = bindError;
                return false;
            }

            SkeletonPreviewRigType rigType = ResolveRigTypeFromModel(playableClip.bridgeModelName);
            RigCacheEntry rig = GetOrCreateRig(rigType);
            if (rig.Root == null || rig.Transforms == null || rig.Transforms.Length == 0)
            {
                error = "preview rig unavailable";
                return false;
            }

            string modelName = playableClip.bridgeModelName;
            if (!TryBuildSnapshot(marker, clipRange, boundAnimator, modelName, out SnapshotPose cache, out error))
            {
                return false;
            }

            if (!ApplySnapshotToRig(cache, rig))
            {
                error = "failed to apply snapshot to rig";
                return false;
            }

            if (rig.Root != null && !rig.Root.gameObject.activeSelf)
            {
                rig.Root.gameObject.SetActive(true);
            }

            renderEntry = new SnapshotRenderEntry
            {
                Marker = marker,
                ClipRange = clipRange,
                RigType = rigType,
                RigKey = rig.Key,
                FrameIndex = cache.FrameIndex
            };

            return true;
        }

        private static bool TryResolveBoundAnimatorForTrack(
            PlayableDirector director,
            TrackAsset track,
            out Animator animator,
            out string error)
        {
            animator = null;
            error = string.Empty;

            if (director == null)
            {
                error = "Timeline inspected director is null";
                return false;
            }

            if (track == null)
            {
                error = "track is null";
                return false;
            }

            // 1) Prefer the binding directly on this clip's own track.
            animator = director.GetGenericBinding(track) as Animator;
            if (animator != null && animator.transform != null)
            {
                return true;
            }

            // 2) Fallback to parent track bindings only when self track has no binding.
            TrackAsset current = track.parent as TrackAsset;
            while (current != null)
            {
                animator = director.GetGenericBinding(current) as Animator;
                if (animator != null && animator.transform != null)
                {
                    return true;
                }

                current = current.parent as TrackAsset;
            }

            error = "track has no animator binding on self track or parent tracks";
            return false;
        }

        private static bool TryBuildSnapshot(
            KimodoConstraintMarkerBase marker,
            TimelineClip clipRange,
            Animator boundAnimator,
            string modelName,
            out SnapshotPose snapshot,
            out string error)
        {
            snapshot = default;
            error = string.Empty;

            int frameIndex = KimodoConstraintMarkerEditorUtility.TimeToKimodoFrameIndex(clipRange, marker.time);
            KimodoMarkerSampleResult unityPose;

            if (!TryResolveUnityPoseForMarker(marker, clipRange, boundAnimator, frameIndex, out unityPose, out error))
            {
                return false;
            }

            if (unityPose == null || unityPose.localAxisAngles == null || unityPose.localAxisAngles.Count == 0)
            {
                error = "unity pose is empty";
                return false;
            }

            if (!TryBuildLocalPoseArrays(unityPose, out Vector3 rootPos, out Quaternion[] localRotations))
            {
                error = "failed to build local pose arrays";
                return false;
            }

            List<int> sampledIndices = unityPose.sampledJointIndices != null
                ? unityPose.sampledJointIndices
                : null;

            ResolveSnapshotJointPose(
                marker,
                modelName,
                localRotations,
                sampledIndices,
                out string[] jointNames,
                out Quaternion[] rotations,
                out string resolveError);
            if (!string.IsNullOrWhiteSpace(resolveError))
            {
                error = resolveError;
                return false;
            }

            snapshot = new SnapshotPose
            {
                FrameIndex = frameIndex,
                RootPosition = rootPos,
                LocalRotations = rotations,
                JointNames = jointNames,
                RootJointName = KimodoMarkerSamplingUtility.GetRootJointNameForModel(modelName)
            };

            return true;
        }

        private static void ResolveSnapshotJointPose(
            KimodoConstraintMarkerBase marker,
            string modelName,
            Quaternion[] sourceRotations,
            List<int> sampledIndices,
            out string[] jointNames,
            out Quaternion[] rotations,
            out string error)
        {
            error = string.Empty;
            if (marker is KimodoEndEffectorConstraintMarker eeMarker)
            {
                if (!TryResolveEndEffectorSnapshotPose(
                    eeMarker,
                    modelName,
                    sourceRotations,
                    sampledIndices,
                    out jointNames,
                    out rotations,
                    out error))
                {
                    return;
                }
                return;
            }

            if (!TryResolveSnapshotJointNames(modelName, sourceRotations != null ? sourceRotations.Length : 0, out jointNames, out error))
            {
                rotations = Array.Empty<Quaternion>();
                return;
            }

            rotations = sourceRotations ?? Array.Empty<Quaternion>();
            if (sampledIndices != null &&
                sampledIndices.Count > 0 &&
                TryFilterSnapshotBySampledIndices(rotations, jointNames, sampledIndices, out Quaternion[] filteredRots, out string[] filteredNames))
            {
                rotations = filteredRots;
                jointNames = filteredNames;
            }
            else if (sampledIndices != null && sampledIndices.Count > 0)
            {
                error = "sampled joint filter removed all joints.";
                rotations = Array.Empty<Quaternion>();
                jointNames = Array.Empty<string>();
            }
        }

        private static bool TryResolveEndEffectorSnapshotPose(
            KimodoEndEffectorConstraintMarker marker,
            string modelName,
            Quaternion[] sourceRotations,
            List<int> sampledIndices,
            out string[] jointNames,
            out Quaternion[] rotations,
            out string error)
        {
            error = string.Empty;
            sourceRotations ??= Array.Empty<Quaternion>();
            if (marker == null || marker.jointNames == null || marker.jointNames.Count == 0)
            {
                error = "end-effector marker has no joint_names.";
                jointNames = Array.Empty<string>();
                rotations = Array.Empty<Quaternion>();
                return false;
            }

            if (sourceRotations.Length == marker.jointNames.Count)
            {
                int exactCount = marker.jointNames.Count;
                jointNames = new string[exactCount];
                rotations = new Quaternion[exactCount];
                for (int i = 0; i < exactCount; i++)
                {
                    jointNames[i] = marker.jointNames[i];
                    rotations[i] = sourceRotations[i];
                }
                return true;
            }

            string[] modelJointNames = KimodoMarkerSamplingUtility.GetJointNamesForModel(modelName);
            if (modelJointNames == null || modelJointNames.Length == 0)
            {
                error = $"end-effector model joint layout not found for model '{modelName}'.";
                jointNames = Array.Empty<string>();
                rotations = Array.Empty<Quaternion>();
                return false;
            }

            if (sourceRotations.Length != modelJointNames.Length)
            {
                error = $"end-effector rotation count mismatch: rotations={sourceRotations.Length}, modelJoints={modelJointNames.Length}, markerJoints={marker.jointNames.Count}.";
                jointNames = Array.Empty<string>();
                rotations = Array.Empty<Quaternion>();
                return false;
            }

            var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < modelJointNames.Length; i++)
            {
                string name = modelJointNames[i];
                if (!string.IsNullOrWhiteSpace(name) && !indexByName.ContainsKey(name))
                {
                    indexByName[name] = i;
                }
            }

            var mappedNames = new List<string>(marker.jointNames.Count);
            var mappedRotations = new List<Quaternion>(marker.jointNames.Count);
            HashSet<int> sampledSet = sampledIndices != null && sampledIndices.Count > 0
                ? new HashSet<int>(sampledIndices)
                : null;
            for (int i = 0; i < marker.jointNames.Count; i++)
            {
                string targetName = marker.jointNames[i];
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    continue;
                }

                if (!indexByName.TryGetValue(targetName, out int sourceIndex))
                {
                    error = $"end-effector joint '{targetName}' not found in model joint layout.";
                    jointNames = Array.Empty<string>();
                    rotations = Array.Empty<Quaternion>();
                    return false;
                }

                if (sourceIndex < 0 || sourceIndex >= sourceRotations.Length)
                {
                    error = $"end-effector joint '{targetName}' index out of range in sampled rotations.";
                    jointNames = Array.Empty<string>();
                    rotations = Array.Empty<Quaternion>();
                    return false;
                }

                if (sampledSet != null && !sampledSet.Contains(sourceIndex))
                {
                    error = $"end-effector joint '{targetName}' missing from sampled joint indices.";
                    jointNames = Array.Empty<string>();
                    rotations = Array.Empty<Quaternion>();
                    return false;
                }

                mappedNames.Add(targetName);
                mappedRotations.Add(sourceRotations[sourceIndex]);
            }

            if (mappedNames.Count != marker.jointNames.Count)
            {
                error = $"end-effector mapped joint count mismatch: mapped={mappedNames.Count}, marker={marker.jointNames.Count}.";
                jointNames = Array.Empty<string>();
                rotations = Array.Empty<Quaternion>();
                return false;
            }

            jointNames = mappedNames.ToArray();
            rotations = mappedRotations.ToArray();
            return true;
        }

        private static bool TryResolveSnapshotJointNames(
            string modelName,
            int rotationCount,
            out string[] jointNames,
            out string error)
        {
            error = string.Empty;
            jointNames = Array.Empty<string>();
            if (rotationCount <= 0)
            {
                error = "rotation count is zero.";
                return false;
            }

            string[] modelJointNames = KimodoMarkerSamplingUtility.GetJointNamesForModel(modelName);
            if (modelJointNames == null || modelJointNames.Length == 0)
            {
                error = $"model joint layout not found for model '{modelName}'.";
                return false;
            }

            if (modelJointNames.Length != rotationCount)
            {
                error = $"rotation count mismatch for model '{modelName}': rotations={rotationCount}, modelJoints={modelJointNames.Length}.";
                return false;
            }

            jointNames = modelJointNames;
            return true;
        }

        private static bool TryFilterSnapshotBySampledIndices(
            Quaternion[] localRotations,
            string[] jointNames,
            List<int> sampledIndices,
            out Quaternion[] filteredRotations,
            out string[] filteredJointNames)
        {
            filteredRotations = localRotations ?? Array.Empty<Quaternion>();
            filteredJointNames = jointNames ?? Array.Empty<string>();
            if (localRotations == null || jointNames == null || sampledIndices == null || sampledIndices.Count == 0)
            {
                return false;
            }

            var rots = new List<Quaternion>(sampledIndices.Count);
            var names = new List<string>(sampledIndices.Count);
            for (int i = 0; i < sampledIndices.Count; i++)
            {
                int index = sampledIndices[i];
                if (index < 0 || index >= localRotations.Length || index >= jointNames.Length)
                {
                    continue;
                }

                rots.Add(localRotations[index]);
                names.Add(jointNames[index]);
            }

            if (rots.Count == 0 || names.Count == 0)
            {
                return false;
            }

            filteredRotations = rots.ToArray();
            filteredJointNames = names.ToArray();
            return true;
        }

        private static bool TryResolveUnityPoseForMarker(
            KimodoConstraintMarkerBase marker,
            TimelineClip clipRange,
            Animator boundAnimator,
            int frameIndex,
            out KimodoMarkerSampleResult unityPose,
            out string error)
        {
            unityPose = null;
            error = string.Empty;

            if (marker == null || clipRange == null || boundAnimator == null)
            {
                error = "invalid marker/clip/animator";
                return false;
            }

            bool isCustomEndEffector = marker is KimodoEndEffectorConstraintMarker ee &&
                                       string.Equals(ee.ConstraintType, "end-effector", StringComparison.OrdinalIgnoreCase);
            bool useOverride = marker.useOverride && !isCustomEndEffector;
            bool readPoseOk = KimodoConstraintPosePipeline.TryBuildUnityPoseFromMarker(marker, out unityPose, out error);
            if (!readPoseOk)
            {
                if (useOverride)
                {
                    error = "failed to read override pose from marker";
                }
                return false;
            }

            if (marker is KimodoRoot2DConstraintMarker root2D)
            {
                if (!TrySampleUnityPoseFromTimeline(
                        marker,
                        clipRange,
                        boundAnimator,
                        frameIndex,
                        out KimodoMarkerSampleResult sampledPose,
                        out string sampleError))
                {
                    error = sampleError;
                    return false;
                }

                // root2d markers only store planar root data, so keep rotations and Y from sampled timeline pose.
                Vector2 r = root2D.smoothRoot2D;
                unityPose.rootPosition = new Vector3(r.x, sampledPose.rootPosition.y, r.y);
                unityPose.localAxisAngles = sampledPose.localAxisAngles != null
                    ? new List<Vector3>(sampledPose.localAxisAngles)
                    : new List<Vector3>();
                return true;
            }

            return true;
        }

        private static bool TrySampleUnityPoseFromTimeline(
            KimodoConstraintMarkerBase marker,
            TimelineClip clipRange,
            Animator boundAnimator,
            int frameIndex,
            out KimodoMarkerSampleResult unityPose,
            out string error)
        {
            unityPose = null;
            error = string.Empty;

            if (!KimodoConstraintExportUtility.TrySamplePoseFromClipAsset(
                    clipRange,
                    boundAnimator,
                    boundAnimator.transform,
                    marker.time,
                    frameIndex,
                    marker.ConstraintType,
                    out KimodoMarkerSampleResult kimodoPose,
                    out error))
            {
                return false;
            }

            unityPose = KimodoSpaceConversionUtility.ToUnitySample(kimodoPose);
            if (unityPose == null)
            {
                error = "Kimodo->Unity pose conversion failed";
                return false;
            }

            return true;
        }

        private static bool TryBuildLocalPoseArrays(
            KimodoMarkerSampleResult unityPose,
            out Vector3 rootPosition,
            out Quaternion[] localRotations)
        {
            rootPosition = unityPose.rootPosition;
            int count = unityPose.localAxisAngles != null ? unityPose.localAxisAngles.Count : 0;
            localRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 aa = unityPose.localAxisAngles[i];
                float angleRad = aa.magnitude;
                if (angleRad <= 1e-8f)
                {
                    localRotations[i] = Quaternion.identity;
                    continue;
                }
                Vector3 axis = aa / angleRad;
                localRotations[i] = Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);
            }

            return true;
        }

        private static RigCacheEntry GetOrCreateRig(SkeletonPreviewRigType rigType)
        {
            string key = rigType.ToString();
            if (RigCache.TryGetValue(key, out RigCacheEntry entry) && entry.IsAlive)
            {
                return entry;
            }

            RigCacheEntry created = CreateRig(rigType);
            if (created.IsAlive)
            {
                RigCache[key] = created;
            }
            return created;
        }

        private static RigCacheEntry CreateRig(SkeletonPreviewRigType rigType)
        {
            GameObject prefab = LoadRigPrefab(rigType);
            if (prefab == null)
            {
                return default;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = $"__KimodoSnapshot_{rigType}";
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.SetActive(false);

            Transform root = instance.transform;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            if (transforms == null || transforms.Length == 0)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                return default;
            }

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            return new RigCacheEntry
            {
                Key = rigType.ToString(),
                RigType = rigType,
                Root = root,
                Transforms = transforms,
                Animator = animator
            };
        }

        private static string ResolveRigModelPath(SkeletonPreviewRigType rigType)
        {
            string fileName = ResolveRigFileName(rigType);

            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(KimodoConstraintSnapshotVisualizer).Assembly);
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

            // Legacy relative path fallback for older local layouts.
            return $"Editor/Model/{fileName}";
        }

        private static GameObject LoadRigPrefab(SkeletonPreviewRigType rigType)
        {
            string path = ResolveRigModelPath(rigType);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static string ResolveRigFileName(SkeletonPreviewRigType rigType)
        {
            switch (rigType)
            {
                case SkeletonPreviewRigType.Smplx:
                    return "SMPLX.fbx";
                case SkeletonPreviewRigType.G1:
                    return "G1.fbx";
                case SkeletonPreviewRigType.Soma30:
                default:
                    return "SOMA30.fbx";
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool ApplySnapshotToRig(SnapshotPose snapshot, RigCacheEntry rig)
        {
            if (!rig.IsAlive)
            {
                return false;
            }

            Transform[] t = rig.Transforms;
            if (t == null || t.Length == 0)
            {
                return false;
            }

            Dictionary<string, Transform> nameMap = BuildTransformNameMap(t);
            int rotCount = snapshot.LocalRotations != null ? snapshot.LocalRotations.Length : 0;
            int nameCount = snapshot.JointNames != null ? snapshot.JointNames.Length : 0;
            int count = Mathf.Min(rotCount, nameCount);
            for (int i = 0; i < count; i++)
            {
                string jointName = snapshot.JointNames[i];
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    continue;
                }

                if (!nameMap.TryGetValue(jointName, out Transform jointTransform) || jointTransform == null)
                {
                    continue;
                }

                Quaternion rotation = snapshot.LocalRotations[i];
                if (Quaternion.Dot(rotation, Quaternion.identity) >= 0.999999f)
                {
                    continue;
                }

                jointTransform.localRotation = rotation;
            }

            string rootJointName = snapshot.RootJointName;
            if (string.IsNullOrWhiteSpace(rootJointName) && snapshot.JointNames != null && snapshot.JointNames.Length > 0)
            {
                rootJointName = snapshot.JointNames[0];
            }
            if (!string.IsNullOrWhiteSpace(rootJointName) && nameMap.TryGetValue(rootJointName, out Transform rootTransform) && rootTransform != null)
            {
                rootTransform.position = snapshot.RootPosition;
            }
            else
            {
                t[0].position = snapshot.RootPosition;
            }
            return true;
        }

        private static Dictionary<string, Transform> BuildTransformNameMap(Transform[] transforms)
        {
            var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            if (transforms == null || transforms.Length == 0)
            {
                return map;
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform tr = transforms[i];
                if (tr == null || string.IsNullOrWhiteSpace(tr.name))
                {
                    continue;
                }

                if (!map.ContainsKey(tr.name))
                {
                    map[tr.name] = tr;
                }
            }

            return map;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            // If no marker is selected now, clear stale renders immediately so gizmos disappear at once.
            if (CollectSelectedMarkers().Count == 0)
            {
                if (ActiveRenders.Count > 0)
                {
                    HideAllRigsAndClearActiveRenders();
                    SceneView.RepaintAll();
                }
                return;
            }

            if (ActiveRenders.Count == 0)
            {
                return;
            }

            for (int i = 0; i < ActiveRenders.Count; i++)
            {
                SnapshotRenderEntry entry = ActiveRenders[i];
                if (!RigCache.TryGetValue(entry.RigKey, out RigCacheEntry rig) || !rig.IsAlive)
                {
                    continue;
                }

                DrawRigGizmo(rig, entry, i);
            }
        }

        private static void DrawRigGizmo(RigCacheEntry rig, SnapshotRenderEntry entry, int index)
        {
            Transform[] ts = rig.Transforms;
            if (ts == null || ts.Length == 0)
            {
                return;
            }

            Color c = Color.HSVToRGB((index * 0.17f) % 1f, 0.85f, 1f);
            c.a = 0.9f;
            Handles.color = c;

            for (int i = 1; i < ts.Length; i++)
            {
                Transform child = ts[i];
                Transform parent = child.parent;
                if (child == null || parent == null)
                {
                    continue;
                }
                Handles.DrawLine(parent.position, child.position, 1.5f);
            }

            float size = HandleUtility.GetHandleSize(ts[0].position) * 0.03f;
            for (int i = 0; i < ts.Length; i++)
            {
                Handles.SphereHandleCap(0, ts[i].position, Quaternion.identity, size, EventType.Repaint);
            }

            string label = $"{entry.Marker.name} [{entry.RigType}] f={entry.FrameIndex}";
            Handles.Label(ts[0].position + Vector3.up * size * 6f, label);
        }

        internal static SkeletonPreviewRigType ResolveRigTypeFromModel(string modelName)
        {
            string m = (modelName ?? string.Empty).Trim().ToLowerInvariant();
            if (m.Contains("smplx"))
            {
                return SkeletonPreviewRigType.Smplx;
            }

            if (m.Contains("g1"))
            {
                return SkeletonPreviewRigType.G1;
            }

            return SkeletonPreviewRigType.Soma30;
        }

        private static void ClearAllCaches()
        {
            ActiveRenders.Clear();

            foreach (KeyValuePair<string, RigCacheEntry> kv in RigCache)
            {
                RigCacheEntry rig = kv.Value;
                if (rig.Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(rig.Root.gameObject);
                }
            }
            RigCache.Clear();
        }

        private static void HideAllRigsAndClearActiveRenders()
        {
            ActiveRenders.Clear();
            SetAllRigVisibility(false);
        }

        private static void SetAllRigVisibility(bool visible)
        {
            foreach (KeyValuePair<string, RigCacheEntry> kv in RigCache)
            {
                RigCacheEntry rig = kv.Value;
                if (rig.Root == null || rig.Root.gameObject == null)
                {
                    continue;
                }

                if (rig.Root.gameObject.activeSelf != visible)
                {
                    rig.Root.gameObject.SetActive(visible);
                }
            }
        }

        internal enum SkeletonPreviewRigType
        {
            Soma30 = 0,
            Smplx = 1,
            G1 = 2
        }

        private struct RigCacheEntry
        {
            public string Key;
            public SkeletonPreviewRigType RigType;
            public Transform Root;
            public Transform[] Transforms;
            public Animator Animator;

            public bool IsAlive => Root != null && Root.gameObject != null;
        }

        private struct SnapshotPose
        {
            public int FrameIndex;
            public Vector3 RootPosition;
            public Quaternion[] LocalRotations;
            public string[] JointNames;
            public string RootJointName;
        }

        private struct SnapshotRenderEntry
        {
            public KimodoConstraintMarkerBase Marker;
            public TimelineClip ClipRange;
            public SkeletonPreviewRigType RigType;
            public string RigKey;
            public int FrameIndex;
        }
    }
}
