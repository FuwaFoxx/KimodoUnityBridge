using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KimodoUnityMotionTools.Bridge
{
    public sealed class BridgeLogPump : IDisposable
    {
        private CancellationTokenSource cts;
        private Task pumpTask;
        private bool disposed;

        public void Start(string logPath, Action<string> onLine)
        {
            Stop();
            if (string.IsNullOrWhiteSpace(logPath) || onLine == null)
            {
                return;
            }

            cts = new CancellationTokenSource();
            pumpTask = Task.Run(() => PumpAsync(logPath, onLine, cts.Token));
        }

        public void Stop()
        {
            CancellationTokenSource current = cts;
            cts = null;
            if (current != null)
            {
                try { current.Cancel(); } catch { }
                current.Dispose();
            }
            pumpTask = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Stop();
        }

        private static async Task PumpAsync(string logPath, Action<string> onLine, CancellationToken token)
        {
            try
            {
                DateTime waitUntil = DateTime.UtcNow.AddSeconds(120);
                while (!token.IsCancellationRequested && !File.Exists(logPath))
                {
                    if (DateTime.UtcNow >= waitUntil)
                    {
                        return;
                    }

                    await Task.Delay(100, token);
                }

                if (!File.Exists(logPath))
                {
                    return;
                }

                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.CanSeek)
                {
                    fs.Seek(0, SeekOrigin.End);
                }
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (!token.IsCancellationRequested)
                {
                    string line = await reader.ReadLineAsync();
                    if (line != null)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            onLine(trimmed);
                        }
                        continue;
                    }

                    // The producer may truncate/recreate the same log file path between runs.
                    // If current read position is beyond current file length, rewind to start so
                    // the pump can continue tailing new content in the recreated file.
                    if (fs.CanSeek && fs.Length < fs.Position)
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                        reader.DiscardBufferedData();
                        continue;
                    }

                    if (fs.Length > fs.Position)
                    {
                        string tailChunk = await reader.ReadToEndAsync();
                        if (!string.IsNullOrWhiteSpace(tailChunk))
                        {
                            string[] parts = tailChunk.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < parts.Length; i++)
                            {
                                string trimmed = parts[i].Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                {
                                    onLine(trimmed);
                                }
                            }
                        }
                        continue;
                    }

                    await Task.Delay(100, token);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception e)
            {
                onLine($"[BridgeLogPump] stopped: {e.Message}");
            }
        }
    }
}
