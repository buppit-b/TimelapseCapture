using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace TimelapseCapture
{
    public static class FfmpegDownloader
    {
        private const string WindowsZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        public static async Task<string> EnsureFfmpegPresentAsync(string ffmpegDir)
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
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deleting existing extraction directory: {ex.Message}");
                    }
                }

                // Extract ZIP
                ZipFile.ExtractToDirectory(tempZip, extractDir);

                // Find ffmpeg.exe in extracted tree
                var matches = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (matches.Length == 0)
                {
                    Console.WriteLine("ffmpeg.exe not found in the extracted archive.");
                    return null!;
                }

                // Copy ffmpeg.exe to target folder
                File.Copy(matches[0], exePath, overwrite: true);

                // Also attempt to copy ffprobe.exe
                var fp = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories);
                if (fp.Length > 0)
                {
                    File.Copy(fp[0], Path.Combine(ffmpegDir, "ffprobe.exe"), overwrite: true);
                }

                // Clean up extraction folder
                try
                {
                    Directory.Delete(extractDir, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting extraction directory: {ex.Message}");
                }

                return exePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring ffmpeg presence: {ex.Message}");
                return null!;
            }
        }
    }
}
