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

                // Download ffmpeg zip to a temp path
                string tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
                using (var http = new HttpClient())
                {
                    var data = await http.GetByteArrayAsync(WindowsZipUrl);
                    await File.WriteAllBytesAsync(tempZip, data);
                }

                // Extract just ffmpeg.exe and ffprobe.exe from archive
                string extractDir = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(tempZip, extractDir);

                string[] found = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (found.Length == 0)
                    return null;

                File.Copy(found[0], exePath, overwrite: true);

                // Optional: copy ffprobe too
                var ffprobe = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories);
                if (ffprobe.Length > 0)
                    File.Copy(ffprobe[0], Path.Combine(ffmpegDir, "ffprobe.exe"), overwrite: true);

                // Clean up temp
                try { File.Delete(tempZip); Directory.Delete(extractDir, true); } catch { }

                return exePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FFmpeg download failed: " + ex.Message);
                return null;
            }
        }
    }
}
