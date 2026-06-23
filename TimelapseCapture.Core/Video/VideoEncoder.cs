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
            int fps, string preset, int crf, CancellationToken ct = default)
        {
            var frames = SessionManager.GetFrameFiles(sessionFolder);
            if (frames.Length == 0)
                return new Result { Success = false, Error = "No frames to encode." };

            // ffmpeg concat demuxer file list (single-quote escaping per ffmpeg rules).
            string tempFolder = SessionManager.GetTempFolder(sessionFolder);
            Directory.CreateDirectory(tempFolder);
            string fileListPath = Path.Combine(tempFolder, "filelist.txt");
            using (var writer = new StreamWriter(fileListPath, false))
            {
                foreach (var f in frames)
                    writer.WriteLine($"file '{f.Replace("'", "'\\''")}'");
            }

            string outputFolder = SessionManager.GetOutputFolder(sessionFolder);
            Directory.CreateDirectory(outputFolder);
            string outputPath = Path.Combine(outputFolder, $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            if (fps < 1) fps = 30;
            crf = Math.Clamp(crf, 0, 51);
            if (string.IsNullOrWhiteSpace(preset)) preset = "medium";

            string args = $"-y -f concat -safe 0 -i \"{fileListPath}\" -r {fps} " +
                          $"-c:v libx264 -preset {preset} -crf {crf} \"{outputPath}\"";
            Logger.Log("VideoEncoder", $"Encoding {frames.Length} frames -> {outputPath} @ {fps}fps preset={preset} crf={crf}");

            var (exitCode, _, error) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args, ct);

            try { SessionManager.CleanTempFolder(sessionFolder); } catch { /* best effort */ }

            if (exitCode == 0 && File.Exists(outputPath))
                return new Result { Success = true, OutputPath = outputPath };

            return new Result
            {
                Success = false,
                OutputPath = outputPath,
                Error = string.IsNullOrWhiteSpace(error) ? $"ffmpeg exited with code {exitCode}" : error,
            };
        }
    }
}
