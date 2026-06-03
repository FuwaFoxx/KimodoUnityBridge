using System;
using UnityEditor;
using UnityEditor.Compilation;

namespace KimodoBridge.Editor
{
    [InitializeOnLoad]
    internal static class EditorCompilationStateGate
    {
        private static int compilingDepth;
        private static int reloadDepth;

        internal static event Action<bool> StateChanged;

        internal static bool IsCompilingOrReloading => compilingDepth > 0 || reloadDepth > 0 || EditorApplication.isCompiling;

        static EditorCompilationStateGate()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
        }

        private static void OnCompilationStarted(object _)
        {
            compilingDepth++;
            UnityEngine.Debug.Log($"[Kimodo][CompileGate] compilation started depth={compilingDepth}");
            StateChanged?.Invoke(true);
        }

        private static void OnCompilationFinished(object _)
        {
            compilingDepth = Math.Max(0, compilingDepth - 1);
            bool active = IsCompilingOrReloading;
            UnityEngine.Debug.Log($"[Kimodo][CompileGate] compilation finished depth={compilingDepth}, active={active}");
            StateChanged?.Invoke(active);
        }

        private static void OnBeforeReload()
        {
            reloadDepth++;
            UnityEngine.Debug.Log($"[Kimodo][CompileGate] before reload depth={reloadDepth}");
            StateChanged?.Invoke(true);
        }

        private static void OnAfterReload()
        {
            reloadDepth = Math.Max(0, reloadDepth - 1);
            bool active = IsCompilingOrReloading;
            UnityEngine.Debug.Log($"[Kimodo][CompileGate] after reload depth={reloadDepth}, active={active}");
            StateChanged?.Invoke(active);
        }
    }
}
