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

        public static async Task<string?> EnsureFfmpegPresentAsync(string ffmpegDir)
        {
            try
            {
                Directory.CreateDirectory(ffmpegDir);
                string exePath = Path.Combine(ffmpegDir, "ffmpeg.exe");
                if (File.Exists(exePath))
                    return exePath;

                string tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
                string extractDir = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");

                // Download fresh each time if not already cached
                if (!File.Exists(tempZip))
                {
                    using var http = new HttpClient();
                    var data = await http.GetByteArrayAsync(WindowsZipUrl);
                    await File.WriteAllBytesAsync(tempZip, data);
                }

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);

                ZipFile.ExtractToDirectory(tempZip, extractDir);

                // Find ffmpeg.exe inside extracted folders
                var found = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (found.Length == 0)
                    throw new Exception("ffmpeg.exe not found in downloaded archive.");

                File.Copy(found[0], exePath, overwrite: true);

                var ffprobe = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories);
                if (ffprobe.Length > 0)
                    File.Copy(ffprobe[0], Path.Combine(ffmpegDir, "ffprobe.exe"), overwrite: true);

                try
                {
                    Directory.Delete(extractDir, true);
                }
                catch { }

                return exePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FFmpeg download/extract failed: " + ex.Message);
                return null;
            }
        }
    }
}
