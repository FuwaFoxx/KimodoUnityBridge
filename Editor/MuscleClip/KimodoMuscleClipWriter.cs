#if UNITY_EDITOR
using KimodoUnityMotionTools.Generation.Pipeline;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KimodoUnityMotionTools.ProjectEditor.MuscleClip
{
    public interface IGetMuscleData : IDisposable
    {
        float Duration { get; }
        float SampleRate { get; }
        bool IsReady { get; }
        bool TryGetHumanPose(float time, ref HumanPose pose, out string error);
    }

    internal sealed class ClipMuscleDataSource : IGetMuscleData
    {
        private readonly GameObject samplerRoot;
        private readonly PlayableGraph graph;
        private readonly AnimationClipPlayable clipPlayable;
        private readonly HumanPoseHandler poseHandler;
        private bool disposed;
        private bool ready;

        public ClipMuscleDataSource(AnimationClip sourceClip, Avatar sourceAvatar, float sampleRate, out string error)
        {
            error = string.Empty;
            SampleRate = sampleRate > 0f ? sampleRate : 30f;
            Duration = sourceClip != null ? Mathf.Max(sourceClip.length, 1f / Mathf.Max(1f, SampleRate)) : 0f;

            if (!IsValidHumanoid(sourceAvatar))
            {
                error = "Source avatar is null/invalid/non-humanoid.";
                return;
            }

            if (sourceClip == null)
            {
                error = "Source clip is null.";
                return;
            }

            samplerRoot = new GameObject("KimodoMuscleClip_SamplerRoot");
            samplerRoot.hideFlags = HideFlags.HideAndDontSave;
            samplerRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            samplerRoot.transform.localScale = Vector3.one;

            if (!KimodoRuntimeAvatarSkeletonBuilder.TryBuildHierarchyFromAvatarSkeleton(sourceAvatar, samplerRoot.transform, out error))
            {
                Dispose();
                return;
            }

            var animator = samplerRoot.GetComponent<Animator>();
            if (animator == null)
            {
                animator = samplerRoot.AddComponent<Animator>();
            }

            animator.avatar = sourceAvatar;

            animator.applyRootMotion = false;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            animator.Rebind();
            animator.Update(0f);

            poseHandler = new HumanPoseHandler(sourceAvatar, samplerRoot.transform);

            graph = PlayableGraph.Create("KimodoMuscleClip_SamplerGraph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clipPlayable = AnimationClipPlayable.Create(graph, sourceClip);
            clipPlayable.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(graph, "KimodoMuscleClip_SamplerOutput", animator);
            output.SetSourcePlayable(clipPlayable);
            graph.Play();
            ready = true;
        }

        public float Duration { get; }
        public float SampleRate { get; }
        public bool IsReady => ready && !disposed;

        public bool TryGetHumanPose(float time, ref HumanPose outputPose, out string error)
        {
            error = string.Empty;
            if (!IsReady)
            {
                error = "Muscle data source is not ready.";
                return false;
            }

            try
            {
                if (outputPose.muscles == null || outputPose.muscles.Length != HumanTrait.MuscleCount)
                {
                    outputPose.muscles = new float[HumanTrait.MuscleCount];
                }

                float clampedTime = Mathf.Clamp(time, 0f, Mathf.Max(0f, Duration));
                clipPlayable.SetTime(clampedTime);
                graph.Evaluate();
                poseHandler.GetHumanPose(ref outputPose);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (graph.IsValid())
            {
                graph.Destroy();
            }

            if (samplerRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(samplerRoot);
            }
        }

        private static bool IsValidHumanoid(Avatar avatar)
        {
            return avatar != null && avatar.isValid && avatar.isHuman;
        }
    }

    public static class KimodoMuscleClipWriter
    {
        private const string DefaultOutputFolder = "Assets/MuscleClips";

        public static bool TryCreateMuscleClipAsset(
            AnimationClip sourceClip,
            Avatar sourceAvatar,
            string outputFolder,
            string clipNamePrefix,
            float sampleRate,
            out AnimationClip outputClip,
            out string assetPath,
            out string error)
        {
            outputClip = null;
            assetPath = string.Empty;
            error = string.Empty;

            using var source = new ClipMuscleDataSource(sourceClip, sourceAvatar, sampleRate, out error);
            if (!source.IsReady)
            {
                return false;
            }

            return TryCreateMuscleClipAsset(source, outputFolder, clipNamePrefix, out outputClip, out assetPath, out error);
        }

        public static bool TryCreateMuscleClipAsset(
            IGetMuscleData source,
            string outputFolder,
            string clipNamePrefix,
            out AnimationClip outputClip,
            out string assetPath,
            out string error)
        {
            outputClip = null;
            assetPath = string.Empty;
            error = string.Empty;

            if (source == null)
            {
                error = "Muscle data source is null.";
                return false;
            }

            if (!source.IsReady)
            {
                error = "Muscle data source is not ready.";
                return false;
            }

            float fps = source.SampleRate > 0f ? source.SampleRate : 30f;
            float duration = Mathf.Max(source.Duration, 1f / Mathf.Max(1f, fps));
            int frameCount = Mathf.Max(2, Mathf.RoundToInt(duration * fps) + 1);

            string resolvedFolder = NormalizeAssetFolder(outputFolder);
            try
            {
                EnsureAssetFolder(resolvedFolder);
            }
            catch (Exception ex)
            {
                error = $"Invalid output folder: {ex.Message}";
                return false;
            }

            string safePrefix = string.IsNullOrWhiteSpace(clipNamePrefix) ? "MuscleClip_" : clipNamePrefix.Trim();
            string clipName = $"{safePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}";
            assetPath = AssetDatabase.GenerateUniqueAssetPath($"{resolvedFolder}/{clipName}.anim");

            var clip = new AnimationClip
            {
                name = clipName,
                legacy = false,
                frameRate = fps
            };

            AssetDatabase.CreateAsset(clip, assetPath);

            if (!TryWriteMuscleCurves(clip, source, frameCount, fps, out error))
            {
                AssetDatabase.DeleteAsset(assetPath);
                outputClip = null;
                assetPath = string.Empty;
                return false;
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            outputClip = clip;
            return true;
        }

        private static bool TryWriteMuscleCurves(
            AnimationClip clip,
            IGetMuscleData source,
            int frameCount,
            float fps,
            out string error)
        {
            error = string.Empty;
            if (clip == null)
            {
                error = "Target clip is null.";
                return false;
            }

            string[] muscleNames = HumanTrait.MuscleName;
            int muscleCount = Mathf.Min(HumanTrait.MuscleCount, muscleNames != null ? muscleNames.Length : 0);
            if (muscleCount <= 0)
            {
                error = "HumanTrait muscle list is empty.";
                return false;
            }

            AnimationCurve rootTx = new AnimationCurve();
            AnimationCurve rootTy = new AnimationCurve();
            AnimationCurve rootTz = new AnimationCurve();
            AnimationCurve rootQx = new AnimationCurve();
            AnimationCurve rootQy = new AnimationCurve();
            AnimationCurve rootQz = new AnimationCurve();
            AnimationCurve rootQw = new AnimationCurve();

            var muscleCurves = new AnimationCurve[muscleCount];
            for (int i = 0; i < muscleCount; i++)
            {
                muscleCurves[i] = new AnimationCurve();
            }

            var pose = new HumanPose
            {
                muscles = new float[HumanTrait.MuscleCount]
            };

            for (int frame = 0; frame < frameCount; frame++)
            {
                float time = frameCount <= 1 ? 0f : Mathf.Min(source.Duration, frame / Mathf.Max(1f, fps));
                if (!source.TryGetHumanPose(time, ref pose, out error))
                {
                    return false;
                }

                rootTx.AddKey(time, pose.bodyPosition.x);
                rootTy.AddKey(time, pose.bodyPosition.y);
                rootTz.AddKey(time, pose.bodyPosition.z);

                rootQx.AddKey(time, pose.bodyRotation.x);
                rootQy.AddKey(time, pose.bodyRotation.y);
                rootQz.AddKey(time, pose.bodyRotation.z);
                rootQw.AddKey(time, pose.bodyRotation.w);

                for (int muscle = 0; muscle < muscleCount; muscle++)
                {
                    float value = pose.muscles != null && muscle < pose.muscles.Length ? pose.muscles[muscle] : 0f;
                    muscleCurves[muscle].AddKey(time, value);
                }
            }

            clip.ClearCurves();
            AnimationUtility.SetAnimationClipSettings(
                clip,
                new AnimationClipSettings
                {
                    loopTime = false,
                    keepOriginalPositionY = true
                });

            SetFloatCurve(clip, "RootT.x", rootTx);
            SetFloatCurve(clip, "RootT.y", rootTy);
            SetFloatCurve(clip, "RootT.z", rootTz);
            SetFloatCurve(clip, "RootQ.x", rootQx);
            SetFloatCurve(clip, "RootQ.y", rootQy);
            SetFloatCurve(clip, "RootQ.z", rootQz);
            SetFloatCurve(clip, "RootQ.w", rootQw);

            for (int muscle = 0; muscle < muscleCount; muscle++)
            {
                string muscleName = muscleNames[muscle];
                if (string.IsNullOrWhiteSpace(muscleName))
                {
                    continue;
                }

                SetFloatCurve(clip, muscleName, muscleCurves[muscle]);
            }

            clip.EnsureQuaternionContinuity();
            return true;
        }

        private static void SetFloatCurve(AnimationClip clip, string propertyName, AnimationCurve curve)
        {
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static string NormalizeAssetFolder(string assetFolder)
        {
            string folder = string.IsNullOrWhiteSpace(assetFolder) ? DefaultOutputFolder : assetFolder.Trim();
            return folder.Replace('\\', '/');
        }

        private static void EnsureAssetFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            string[] parts = assetFolderPath.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid asset folder path.");
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
#endif
