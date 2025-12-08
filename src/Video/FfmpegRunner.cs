using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    public static class FfmpegRunner
    {
        public static string? FindFfmpeg(string? configuredPath)
        {
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            var appDir = AppContext.BaseDirectory;
            var local = Path.Combine(appDir, "ffmpeg", "ffmpeg.exe");
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

        public static Task<(int exitCode, string output, string error)> RunFfmpegAsync(string ffmpegPath, string arguments)
        {
            var tcs = new TaskCompletionSource<(int, string, string)>();
            try
            {
                var psi = new ProcessStartInfo(ffmpegPath, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory
                };
                var p = new Process();
                p.StartInfo = psi;
                var stdout = "";
                var stderr = "";
                p.OutputDataReceived += (s,e) => { if (e.Data != null) stdout += e.Data + Environment.NewLine; };
                p.ErrorDataReceived += (s,e) => { if (e.Data != null) stderr += e.Data + Environment.NewLine; };
                p.EnableRaisingEvents = true;
                p.Exited += (s,e) =>
                {
                    tcs.TrySetResult((p.ExitCode, stdout, stderr));
                    p.Dispose();
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch(Exception ex)
            {
                tcs.TrySetResult((-1, "", ex.Message));
            }
            return tcs.Task;
        }
    }
}
