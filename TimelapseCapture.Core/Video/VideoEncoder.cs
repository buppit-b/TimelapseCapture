using System;
using System.IO;
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
        public sealed class Result
        {
            public bool Success { get; init; }
            public string OutputPath { get; init; } = "";
            public string Error { get; init; } = "";
        }

        public static async Task<Result> EncodeAsync(string ffmpegPath, string sessionFolder,
            int fps, string preset, int crf, CancellationToken ct = default,
            int startFrame = 1, int maxFrames = 0, string? outputName = null)
        {
            var frames = SessionManager.GetFrameFiles(sessionFolder);
            if (frames.Length == 0)
                return new Result { Success = false, Error = "No frames to encode." };

            // Use the image2 demuxer over the numbered frame sequence (00001.ext, 00002.ext, …):
            // exactly one output frame per captured image. The old concat demuxer with -r resampled
            // timestamps and dropped frames (a long session produced a far-too-short video).
            string framesFolder = SessionManager.GetFramesFolder(sessionFolder);
            string ext = Path.GetExtension(frames[0]).TrimStart('.');
            if (string.IsNullOrEmpty(ext)) ext = "jpg";
            string pattern = Path.Combine(framesFolder, "%05d." + ext);

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
            if (string.IsNullOrWhiteSpace(preset)) preset = "medium";
            if (startFrame < 1) startFrame = 1;
            string limit = maxFrames > 0 ? $"-frames:v {maxFrames} " : ""; // trim: only this many frames from startFrame

            // -pix_fmt yuv420p for broad player compatibility; -framerate before -i sets the input rate.
            string args = $"-y -framerate {fps} -start_number {startFrame} -i \"{pattern}\" {limit}" +
                          $"-c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p \"{outputPath}\"";
            Logger.Log("VideoEncoder", $"Encoding {frames.Length} {ext} frames -> {outputPath} @ {fps}fps preset={preset} crf={crf}");

            var (exitCode, _, error) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args, ct);

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
