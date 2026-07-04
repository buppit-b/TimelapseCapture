using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    public static class FfmpegRunner
    {
        public static string? FindFfmpeg(string? configuredPath)
        {
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            // Data dir first (where the one-click download lands), then next-to-exe for older
            // portable layouts that predate the data-dir split.
            var data = Path.Combine(AppPaths.DataDir, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(data))
                return data;
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(local))
                return local;

            try
            {
                var psi = new ProcessStartInfo("where", "ffmpeg")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.WaitForExit(2000);
                        var outp = p.StandardOutput.ReadLine();
                        if (!string.IsNullOrEmpty(outp) && File.Exists(outp))
                            return outp;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <param name="onStderrLine">Optional live tap of stderr lines (ffmpeg writes its progress there,
        /// e.g. "frame=  123 fps=…"). Invoked on a threadpool thread — callers marshal to UI as needed.</param>
        public static Task<(int exitCode, string output, string error)> RunFfmpegAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken = default,
            Action<string>? onStderrLine = null, string? workingDirectory = null)
        {
            var tcs = new TaskCompletionSource<(int, string, string)>();
            Process? p = null;
            CancellationTokenRegistration ctr = default;
            try
            {
                var psi = new ProcessStartInfo(ffmpegPath, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // The encode passes the frames folder so a relative -i "%05d.ext" keeps a '%' in the
                    // absolute path from corrupting the image2 pattern; other calls use the app dir.
                    WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? AppContext.BaseDirectory : workingDirectory
                };
                p = new Process();
                p.StartInfo = psi;
                // StringBuilder under a lock: OutputDataReceived and ErrorDataReceived fire on
                // separate threadpool threads, so bare string += is not safe and can lose data.
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var sync = new object();
                p.OutputDataReceived += (s,e) => { if (e.Data != null) lock (sync) { stdout.AppendLine(e.Data); } };
                p.ErrorDataReceived += (s,e) =>
                {
                    if (e.Data == null) return;
                    lock (sync) { stderr.AppendLine(e.Data); }
                    try { onStderrLine?.Invoke(e.Data); } catch { /* a progress-tap bug must not kill the run */ }
                };
                p.EnableRaisingEvents = true;
                var proc = p;
                p.Exited += (s,e) =>
                {
                    // WaitForExit() (no timeout) blocks until the async stdout/stderr pumps have
                    // flushed everything; without it the tail of ffmpeg's error output (the part
                    // shown to the user on failure) can be truncated.
                    try { proc.WaitForExit(); } catch { }
                    string outText, errText;
                    lock (sync) { outText = stdout.ToString(); errText = stderr.ToString(); }
                    ctr.Dispose();
                    tcs.TrySetResult((proc.ExitCode, outText, errText));
                    proc.Dispose();
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                // Cancellation: kill the ffmpeg process; its Exited handler then resolves the task.
                ctr = cancellationToken.Register(() =>
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                });
            }
            catch(Exception ex)
            {
                ctr.Dispose();
                p?.Dispose();
                tcs.TrySetResult((-1, "", ex.Message));
            }
            return tcs.Task;
        }
    }
}
