using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace KimodoUnityMotionTools.ProjectEditor.GenerationPipeline
{
    internal sealed class KimodoEditorRetargetService
    {
        public bool TryRetarget(KimodoPlayableClip clip, TimelineClip timelineClip, out string details)
        {
            details = string.Empty;
            bool didRetarget = false;

            if (clip.autoRetargetOnBinding)
            {
                if (CanDirectOutputByJointNameMatch(clip, timelineClip))
                {
                    Debug.Log("[Kimodo] Retarget skipped: all source joints are present on bound skeleton by name.");
                    return false;
                }

                bool retargetOk = KimodoRetargetPipeline.TryRetargetBakedClip(
                    clip,
                    timelineClip,
                    out KimodoRetargetResultMode retargetMode,
                    out string retargetDetails);

                if (retargetOk)
                {
                    details = $"Retarget success ({retargetMode}). {retargetDetails}";
                    Debug.Log($"[Kimodo] {details}");
                    didRetarget = true;
                }
                else
                {
                    details = $"Retarget fallback to SOMA. {retargetDetails}";
                    Debug.LogWarning($"[Kimodo] {details}");
                }
            }
            else if (clip.CustomRetargetAvatar != null)
            {
                if (KimodoRetargetPipeline.TryRetargetClipToAvatar(
                        clip.clip,
                        clip.CustomRetargetAvatar,
                        out AnimationClip customRetargetClip,
                        out string customRetargetDetails))
                {
                    if (customRetargetClip != null)
                    {
                        UnityEditor.AnimationUtility.SetAnimationClipSettings(
                            customRetargetClip,
                            new UnityEditor.AnimationClipSettings
                            {
                                loopTime = false,
                                keepOriginalPositionY = true
                            });

                        string clipPath = UnityEditor.AssetDatabase.GetAssetPath(clip.clip);
                        if (!string.IsNullOrWhiteSpace(clipPath))
                        {
                            UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(customRetargetClip);
                            clip.clip.ClearCurves();
                            for (int i = 0; i < bindings.Length; i++)
                            {
                                UnityEditor.EditorCurveBinding b = bindings[i];
                                AnimationCurve c = UnityEditor.AnimationUtility.GetEditorCurve(customRetargetClip, b);
                                clip.clip.SetCurve(b.path, b.type, b.propertyName, c);
                            }

                            clip.clip.frameRate = customRetargetClip.frameRate;
                            UnityEditor.EditorUtility.SetDirty(clip.clip);
                            UnityEditor.AssetDatabase.SaveAssets();
                            didRetarget = true;
                        }
                    }

                    details = $"Custom avatar retarget success. {customRetargetDetails}";
                    Debug.Log($"[Kimodo] {details}");
                }
                else
                {
                    details = $"Custom avatar retarget failed. {customRetargetDetails}";
                    Debug.LogWarning($"[Kimodo] {details}");
                }
            }

            if (didRetarget)
            {
                ApplyCurveFilterAfterRetarget(clip.clip, clip.curveFilterOptions);
            }

            return didRetarget;
        }

        private static void ApplyCurveFilterAfterRetarget(AnimationClip targetClip, KimodoCurveFilterOptions options)
        {
            if (targetClip == null || options == null)
            {
                return;
            }

            GameObject tempRoot = BuildHierarchyFromClipBindings(targetClip, "KimodoPostRetargetFilterRoot");
            tempRoot.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var recorder = new UnityEditor.Animations.GameObjectRecorder(tempRoot);
                recorder.BindComponentsOfType<Transform>(tempRoot, true);
                float fps = targetClip.frameRate > 0f ? targetClip.frameRate : 30f;
                int frameCount = Mathf.Max(2, Mathf.RoundToInt(targetClip.length * fps));
                float dt = 1f / fps;
                for (int f = 0; f < frameCount; f++)
                {
                    float t = f / fps;
                    targetClip.SampleAnimation(tempRoot, t);
                    recorder.TakeSnapshot(dt);
                }

                var filter = new UnityEditor.Animations.CurveFilterOptions
                {
                    keyframeReduction = true,
                    positionError = Mathf.Clamp01(options.positionError),
                    rotationError = Mathf.Clamp01(options.rotationError),
                    scaleError = Mathf.Clamp01(options.positionError),
                    floatError = Mathf.Clamp01(options.floatError),
                    unrollRotation = true
                };

                targetClip.ClearCurves();
                recorder.SaveToClip(targetClip, fps, filter);
                if (options.ensureQuaternionContinuity)
                {
                    targetClip.EnsureQuaternionContinuity();
                }
            }
            finally
            {
                Object.DestroyImmediate(tempRoot);
            }
        }

        private static GameObject BuildHierarchyFromClipBindings(AnimationClip clipAsset, string rootName)
        {
            var root = new GameObject(rootName);
            var created = new System.Collections.Generic.Dictionary<string, Transform>(System.StringComparer.Ordinal);
            created[string.Empty] = root.transform;

            UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(clipAsset);
            for (int i = 0; i < bindings.Length; i++)
            {
                string path = bindings[i].path ?? string.Empty;
                if (created.ContainsKey(path))
                {
                    continue;
                }

                EnsurePath(path, root.transform, created);
            }

            return root;
        }

        private static Transform EnsurePath(string path, Transform root, System.Collections.Generic.Dictionary<string, Transform> cache)
        {
            if (cache.TryGetValue(path, out Transform existing))
            {
                return existing;
            }

            if (string.IsNullOrEmpty(path))
            {
                cache[string.Empty] = root;
                return root;
            }

            int split = path.LastIndexOf('/');
            string parentPath = split > 0 ? path.Substring(0, split) : string.Empty;
            string selfName = split >= 0 ? path.Substring(split + 1) : path;
            Transform parent = EnsurePath(parentPath, root, cache);

            var go = new GameObject(string.IsNullOrWhiteSpace(selfName) ? "Bone" : selfName);
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            cache[path] = t;
            return t;
        }

        private static bool CanDirectOutputByJointNameMatch(KimodoPlayableClip playableClip, TimelineClip timelineClip)
        {
            if (playableClip == null || playableClip.jointNames == null || playableClip.jointNames.Length == 0)
            {
                return false;
            }

            if (!TryResolveBoundAnimatorForTimelineClip(timelineClip, out Animator animator))
            {
                return false;
            }

            Transform skeletonRoot = animator != null ? animator.transform : null;
            if (skeletonRoot == null)
            {
                return false;
            }

            var nameSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var stack = new System.Collections.Generic.Stack<Transform>();
            stack.Push(skeletonRoot);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(current.name))
                {
                    nameSet.Add(current.name);
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            for (int i = 0; i < playableClip.jointNames.Length; i++)
            {
                string jointName = playableClip.jointNames[i];
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    continue;
                }

                if (!nameSet.Contains(jointName))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveBoundAnimatorForTimelineClip(TimelineClip timelineClip, out Animator animator)
        {
            animator = null;
            if (timelineClip == null)
            {
                return false;
            }

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
            {
                return false;
            }

            PlayableDirector director = UnityEditor.Timeline.TimelineEditor.inspectedDirector;
            if (director == null)
            {
                return false;
            }

            TrackAsset currentTrack = track;
            while (currentTrack != null)
            {
                animator = director.GetGenericBinding(currentTrack) as Animator;
                if (animator != null)
                {
                    return true;
                }

                currentTrack = currentTrack.parent as TrackAsset;
            }

            return false;
        }
    }
}
