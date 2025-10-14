using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    internal static class FfmpegDownloader
    {
        private const string WindowsZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        /// <summary>
        /// Ensures ffmpeg.exe is available in the target folder.
        /// If not, downloads and extracts from ZIP.
        /// Returns the full path to ffmpeg.exe, or null if failed.
        /// </summary>
        public static async Task<string?> EnsureFfmpegPresentAsync(string ffmpegDir)
        {
            try
            {
                Directory.CreateDirectory(ffmpegDir);
                string exePath = Path.Combine(ffmpegDir, "ffmpeg.exe");
                if (File.Exists(exePath))
                {
                    return exePath;
                }

                string tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg_download.zip");
                string extractDir = Path.Combine(Path.GetTempPath(), "ffmpeg_extracted");

                // Download ZIP if not already present
                if (!File.Exists(tempZip))
                {
                    using (var http = new HttpClient())
                    {
                        var bytes = await http.GetByteArrayAsync(WindowsZipUrl);
                        await File.WriteAllBytesAsync(tempZip, bytes);
                    }
                }

                // Clean up previous extract directory
                if (Directory.Exists(extractDir))
                {
                    try
                    {
                        Directory.Delete(extractDir, true);
                    }
                    catch { }
                }

                ZipFile.ExtractToDirectory(tempZip, extractDir);

                // Find ffmpeg.exe in extracted tree
                var matches = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (matches.Length == 0)
                {
                    return null;
                }

                // Copy ffmpeg.exe to target folder
                File.Copy(matches[0], exePath, overwrite: true);

                // Also attempt ffprobe.exe
                var fp = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories);
                if (fp.Length > 0)
                {
                    File.Copy(fp[0], Path.Combine(ffmpegDir, "ffprobe.exe"), overwrite: true);
                }

                // Clean up extraction folder (optional)
                try
                {
                    Directory.Delete(extractDir, true);
                }
                catch { }

                return exePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FFmpegDownloader: error ensuring ffmpeg: " + ex.Message);
                return null;
            }
        }
    }
}
