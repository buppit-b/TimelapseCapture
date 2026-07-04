using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    /// <summary>
    /// Encodes a session's captured frames into an MP4 (H.264) via the reused FfmpegRunner.
    /// Framework-free — usable from any front-end.
    /// </summary>
    public static class VideoEncoder
    {
        // The valid x264 -preset values; anything else is coerced to "medium" (see EncodeAsync).
        private static readonly string[] ValidPresets =
            { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow", "placebo" };

        public sealed class Result
        {
            public bool Success { get; init; }
            public string OutputPath { get; init; } = "";
            public string Error { get; init; } = "";
        }

        public static async Task<Result> EncodeAsync(string ffmpegPath, string sessionFolder,
            int fps, string preset, int crf, CancellationToken ct = default,
            int startFrame = 1, int maxFrames = 0, string? outputName = null,
            Action<int>? onFrameProgress = null, int everyNth = 1)
        {
            var frames = SessionManager.GetFrameFiles(sessionFolder);
            if (frames.Length == 0)
                return new Result { Success = false, Error = "No frames to encode." };

            // The image2 demuxer reads one extension pattern. A folder with mixed extensions (a mid-session
            // format switch) would silently encode only the subset matching frames[0] — fail clearly instead.
            var exts = frames.Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
                             .Where(e => e.Length > 0).Distinct().ToArray();
            if (exts.Length > 1)
                return new Result { Success = false, Error =
                    $"This session mixes frame formats ({string.Join("/", exts)}). All frames must be the same format to encode — recapture in a single format." };

            // Use the image2 demuxer over the numbered frame sequence (00001.ext, 00002.ext, …):
            // exactly one output frame per captured image. The old concat demuxer with -r resampled
            // timestamps and dropped frames (a long session produced a far-too-short video).
            string framesFolder = SessionManager.GetFramesFolder(sessionFolder);
            string ext = exts.Length == 1 ? exts[0] : "jpg";
            // RELATIVE pattern + run ffmpeg with the frames folder as its working directory. The image2
            // demuxer applies printf expansion to the WHOLE -i path, so a stray '%' anywhere in the
            // absolute path (a session named "50% run", or a user output folder with '%') would corrupt
            // the pattern and fail the encode. Keeping the volatile path out of the pattern isolates the
            // printf expansion to just the intended %05d token.
            string pattern = "%05d." + ext;

            string outputFolder = SessionManager.GetOutputFolder(sessionFolder);
            Directory.CreateDirectory(outputFolder);
            // Caller (the VM) resolves the user's name template; fall back to a timestamp if it's blank.
            string baseName = SanitizeFileName(outputName);
            if (string.IsNullOrEmpty(baseName))
                baseName = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}";
            string outputPath = Path.Combine(outputFolder, baseName + ".mp4");
            for (int n = 2; File.Exists(outputPath); n++)   // don't overwrite a prior encode with the same name
                outputPath = Path.Combine(outputFolder, $"{baseName}_{n}.mp4");

            if (fps < 1) fps = 30;
            crf = Math.Clamp(crf, 0, 51);
            // Allowlist the x264 preset — it's interpolated UNQUOTED into the ffmpeg args, so a value containing
            // spaces (e.g. from a crafted/imported settings.json) could otherwise inject extra ffmpeg arguments.
            preset = (preset ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(ValidPresets, preset) < 0) preset = "medium";
            if (startFrame < 1) startFrame = 1;
            everyNth = Math.Clamp(everyNth, 1, 1000);
            string limit = maxFrames > 0 ? $"-frames:v {maxFrames} " : ""; // trim: only this many frames from startFrame

            // Frame-skip speed-up: keep every Nth input frame, then re-pace timestamps (setpts) so the
            // output is clean constant-rate. NOTE -frames:v counts OUTPUT frames, so with a filter active
            // the trim range must be enforced INSIDE select (lt(n, maxFrames), n being the 0-based input
            // index from startFrame) — otherwise the demuxer reads past the range end to fill the quota.
            string vf = "";
            if (everyNth > 1)
            {
                string sel = maxFrames > 0
                    ? $"lt(n\\,{maxFrames})*not(mod(n\\,{everyNth}))"
                    : $"not(mod(n\\,{everyNth}))";
                vf = $"-vf \"select='{sel}',setpts=N/FRAME_RATE/TB\" ";
                if (maxFrames > 0) limit = $"-frames:v {(maxFrames + everyNth - 1) / everyNth} ";
            }

            // -pix_fmt yuv420p for broad player compatibility; -framerate before -i sets the input rate.
            string args = $"-y -framerate {fps} -start_number {startFrame} -i \"{pattern}\" {vf}{limit}" +
                          $"-c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p \"{outputPath}\"";
            Logger.Log("VideoEncoder", $"Encoding {frames.Length} {ext} frames -> {outputPath} @ {fps}fps preset={preset} crf={crf}" +
                (everyNth > 1 ? $" everyNth={everyNth}" : ""));

            // ffmpeg reports live progress on stderr as "frame=  123 fps=…" lines — tap them for the UI.
            Action<string>? tap = onFrameProgress == null ? null : line =>
            {
                if (!line.StartsWith("frame=", StringComparison.Ordinal)) return;
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^frame=\s*(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) onFrameProgress(n);
            };
            var (exitCode, _, error) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args, ct, tap, framesFolder);

            if (exitCode == 0 && File.Exists(outputPath))
                return new Result { Success = true, OutputPath = outputPath };

            return new Result
            {
                Success = false,
                OutputPath = outputPath,
                Error = string.IsNullOrWhiteSpace(error) ? $"ffmpeg exited with code {exitCode}" : error,
            };
        }

        // Strip anything illegal in a Windows filename; collapse whitespace; trim trailing dots/spaces.
        internal static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.', ' ');
        }
    }
}
