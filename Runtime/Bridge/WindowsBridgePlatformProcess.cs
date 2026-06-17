using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace KimodoBridge
{
    internal sealed class WindowsBridgePlatformProcess : IBridgePlatformProcess
    {
        public bool SupportsCurrentPlatform()
        {
            return Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer;
        }

        public ProcessStartInfo BuildLauncherStartInfo(string launcherPath, string modelName, bool highVram, bool forceSetup, string modelsRoot, int idleTimeoutSeconds, int ownerProcessId)
        {
            string ext = Path.GetExtension(launcherPath)?.ToLowerInvariant() ?? string.Empty;
            if (ext != ".bat" && ext != ".cmd")
            {
                throw new NotSupportedException($"Windows launcher must be .bat/.cmd, got: {ext}");
            }

            string qLauncher = QuoteForCmd(launcherPath);
            string qModel = QuoteForCmd(string.IsNullOrWhiteSpace(modelName) ? "Kimodo-SOMA-RP-v1" : modelName.Trim());
            string modelsArg = string.IsNullOrWhiteSpace(modelsRoot)
                ? string.Empty
                : $" --models-root {QuoteForCmd(modelsRoot.Trim())}";
            string forceSetupArg = forceSetup ? " --force-setup" : string.Empty;
            string watchPidArg = ownerProcessId > 0 ? $" --watchpid {ownerProcessId}" : string.Empty;
            string args = $"--model {qModel}{(highVram ? " --highvram" : string.Empty)}{modelsArg}{forceSetupArg}{watchPidArg} --output file";
            string idleTimeoutSet = $"set KIMODO_IDLE_TIMEOUT_SEC={Math.Max(0, idleTimeoutSeconds)}";

            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c \"set KIMODO_SERVER_WINDOW_STYLE=Hidden && {idleTimeoutSet} && call {qLauncher} {args}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? Environment.CurrentDirectory
            };
        }

        private static string QuoteForCmd(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            string escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

    }
}
