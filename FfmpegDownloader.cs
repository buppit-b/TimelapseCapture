using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    internal static class FfmpegDownloader
    {
        private const string FFMPEG_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        /// <summary>
        /// Ensures ffmpeg.exe is available at the given path.
        /// If not found, attempts to download and extract it automatically.
        /// </summary>
        public static async Task<string?> EnsureFfmpegPresentAsync(string? targetPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetPath))
                    targetPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

                string exePath = Path.Combine(targetPath, "ffmpeg.exe");

                if (File.Exists(exePath))
                    return exePath;

                Directory.CreateDirectory(targetPath);
                string zipPath = Path.Combine(targetPath, "ffmpeg.zip");

                using var client = new HttpClient();
                using var response = await client.GetAsync(FFMPEG_URL);
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fs);

                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetPath, overwriteFiles: true);
                File.Delete(zipPath);

                // Attempt to locate ffmpeg.exe in extracted structure
                string? foundExe = Directory.GetFiles(targetPath, "ffmpeg.exe", SearchOption.AllDirectories)
                                            .FirstOrDefault();

                if (foundExe == null)
                    throw new FileNotFoundException("ffmpeg.exe not found after extraction.");

                // Move it to root path if nested
                if (Path.GetDirectoryName(foundExe) != targetPath)
                    File.Move(foundExe, exePath, overwrite: true);

                return exePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error ensuring ffmpeg: " + ex.Message);
                return null;
            }
        }
    }
}
