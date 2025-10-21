using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;

namespace TimelapseCapture
{
    internal static class FfmpegDownloader
    {
        private const string FFMPEG_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        private const long MIN_EXPECTED_SIZE = 50_000_000; // 50MB minimum
        private const int MAX_RETRIES = 3;

        /// <summary>
        /// Progress callback for download status updates.
        /// Parameters: (bytesDownloaded, totalBytes, statusMessage)
        /// </summary>
        public delegate void ProgressCallback(long bytesDownloaded, long totalBytes, string status);

        /// <summary>
        /// Ensures ffmpeg.exe is available at the given path.
        /// If not found, attempts to download and extract it automatically with progress reporting.
        /// </summary>
        /// <param name="targetPath">Target directory for ffmpeg installation</param>
        /// <param name="progressCallback">Optional callback for progress updates</param>
        /// <returns>Path to ffmpeg.exe if successful, null otherwise</returns>
        public static async Task<string?> EnsureFfmpegPresentAsync(string? targetPath, ProgressCallback? progressCallback = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetPath))
                    targetPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");

                string exePath = Path.Combine(targetPath, "ffmpeg.exe");
                string probePath = Path.Combine(targetPath, "ffprobe.exe");

                // Check if both ffmpeg and ffprobe already exist and are valid
                if (File.Exists(exePath) && IsValidFfmpegExecutable(exePath))
                {
                    progressCallback?.Invoke(0, 0, "FFmpeg already installed");
                    return exePath;
                }

                progressCallback?.Invoke(0, 0, "Creating FFmpeg directory...");
                Directory.CreateDirectory(targetPath);
                string zipPath = Path.Combine(targetPath, "ffmpeg.zip");

                // Download with retries
                bool downloadSuccess = false;
                for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
                {
                    try
                    {
                        progressCallback?.Invoke(0, 0, $"Downloading FFmpeg (attempt {attempt}/{MAX_RETRIES})...");
                        await DownloadFileWithProgressAsync(FFMPEG_URL, zipPath, progressCallback);
                        downloadSuccess = true;
                        break;
                    }
                    catch (Exception ex) when (attempt < MAX_RETRIES)
                    {
                        Debug.WriteLine($"Download attempt {attempt} failed: {ex.Message}");
                        progressCallback?.Invoke(0, 0, $"Download failed, retrying... ({attempt}/{MAX_RETRIES})");
                        await Task.Delay(2000); // Wait before retry
                    }
                }

                if (!downloadSuccess)
                {
                    throw new Exception($"Failed to download FFmpeg after {MAX_RETRIES} attempts");
                }

                // Validate downloaded file
                var fileInfo = new FileInfo(zipPath);
                if (!fileInfo.Exists || fileInfo.Length < MIN_EXPECTED_SIZE)
                {
                    throw new InvalidDataException($"Downloaded file is invalid or too small ({fileInfo.Length} bytes)");
                }

                progressCallback?.Invoke(0, 0, "Extracting FFmpeg...");
                await Task.Run(() =>
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetPath, overwriteFiles: true);
                });

                progressCallback?.Invoke(0, 0, "Cleaning up...");
                File.Delete(zipPath);

                // Locate and organize executables
                progressCallback?.Invoke(0, 0, "Organizing FFmpeg files...");
                var foundExe = Directory.GetFiles(targetPath, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                var foundProbe = Directory.GetFiles(targetPath, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();

                if (foundExe == null)
                    throw new FileNotFoundException("ffmpeg.exe not found after extraction.");

                // Move executables to root if nested
                if (Path.GetDirectoryName(foundExe) != targetPath)
                {
                    File.Move(foundExe, exePath, overwrite: true);
                }

                if (foundProbe != null && Path.GetDirectoryName(foundProbe) != targetPath)
                {
                    File.Move(foundProbe, probePath, overwrite: true);
                }

                // Validate installation
                if (!IsValidFfmpegExecutable(exePath))
                {
                    throw new InvalidOperationException("FFmpeg installation validation failed");
                }

                // Clean up nested directories
                CleanupExtractedFolders(targetPath);

                progressCallback?.Invoke(0, 0, "✅ FFmpeg ready!");
                return exePath;
            }
            catch (HttpRequestException httpEx)
            {
                Debug.WriteLine($"Network error downloading FFmpeg: {httpEx.Message}");
                progressCallback?.Invoke(0, 0, "❌ Network error");
                return null;
            }
            catch (InvalidDataException dataEx)
            {
                Debug.WriteLine($"Invalid download: {dataEx.Message}");
                progressCallback?.Invoke(0, 0, "❌ Download corrupted");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error ensuring ffmpeg: {ex.Message}");
                progressCallback?.Invoke(0, 0, $"❌ Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Download a file with progress reporting.
        /// </summary>
        private static async Task DownloadFileWithProgressAsync(string url, string destinationPath, ProgressCallback? progressCallback)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long bytesDownloaded = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                bytesDownloaded += bytesRead;

                if (totalBytes.HasValue && progressCallback != null)
                {
                    progressCallback(bytesDownloaded, totalBytes.Value, 
                        $"Downloading... {bytesDownloaded / 1_000_000}MB / {totalBytes.Value / 1_000_000}MB");
                }
            }
        }

        /// <summary>
        /// Validate that ffmpeg.exe is a valid executable.
        /// </summary>
        private static bool IsValidFfmpegExecutable(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length < 10_000) return false; // Too small

                // Try to run ffmpeg -version as final validation
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000); // 5 second timeout
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clean up nested folders from extraction, keeping only executables.
        /// </summary>
        private static void CleanupExtractedFolders(string targetPath)
        {
            try
            {
                var directories = Directory.GetDirectories(targetPath);
                foreach (var dir in directories)
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Ignore cleanup errors - not critical
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
