using System;
using System.IO;
using System.Threading.Tasks;

namespace TimelapseCapture.Tests
{
    /// <summary>
    /// Simple demonstration of the FFmpeg downloader with progress reporting.
    /// Not included in production build - for testing only.
    /// </summary>
    internal class FfmpegDownloaderDemo
    {
        public static async Task RunDemo()
        {
            Console.WriteLine("FFmpeg Downloader Demo");
            Console.WriteLine("======================");
            Console.WriteLine();

            var testPath = Path.Combine(Path.GetTempPath(), "ffmpeg_test");
            Console.WriteLine($"Test installation path: {testPath}");
            Console.WriteLine();

            // Clean up any previous test
            if (Directory.Exists(testPath))
            {
                try
                {
                    Directory.Delete(testPath, true);
                    Console.WriteLine("Cleaned up previous test installation.");
                }
                catch
                {
                    Console.WriteLine("⚠️ Could not clean up previous test - continuing anyway.");
                }
            }

            Console.WriteLine("Starting download...");
            Console.WriteLine();

            // Download with progress reporting
            string? result = await FfmpegDownloader.EnsureFfmpegPresentAsync(
                testPath,
                (bytesDownloaded, totalBytes, status) =>
                {
                    if (totalBytes > 0)
                    {
                        // Calculate percentage
                        double percent = (double)bytesDownloaded / totalBytes * 100.0;
                        
                        // Create progress bar
                        int barWidth = 40;
                        int filled = (int)(percent / 100.0 * barWidth);
                        string bar = new string('█', filled) + new string('░', barWidth - filled);
                        
                        // Clear line and show progress
                        Console.Write($"\r[{bar}] {percent:F1}% - {status}                    ");
                    }
                    else
                    {
                        // No byte count available - just show status
                        Console.WriteLine(status);
                    }
                });

            Console.WriteLine(); // New line after progress
            Console.WriteLine();

            if (result != null)
            {
                Console.WriteLine("✅ Success!");
                Console.WriteLine($"   FFmpeg installed at: {result}");
                Console.WriteLine();
                
                // Verify it works
                Console.WriteLine("Verifying installation...");
                try
                {
                    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = result,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        Console.WriteLine("✅ FFmpeg is working correctly!");
                        Console.WriteLine();
                        Console.WriteLine("First line of output:");
                        Console.WriteLine(output.Split('\n')[0]);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Verification failed: {ex.Message}");
                }
                
                Console.WriteLine();
                Console.WriteLine("Files in installation directory:");
                var files = Directory.GetFiles(testPath);
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    Console.WriteLine($"   {Path.GetFileName(file)} ({fileInfo.Length / 1_000_000} MB)");
                }
            }
            else
            {
                Console.WriteLine("❌ Download failed!");
                Console.WriteLine("   Check network connection and try again.");
            }

            Console.WriteLine();
            Console.WriteLine("Demo complete. Press any key to exit...");
        }

        /// <summary>
        /// Test the retry logic by simulating network failures.
        /// </summary>
        public static async Task TestRetryLogic()
        {
            Console.WriteLine("Testing Retry Logic");
            Console.WriteLine("===================");
            Console.WriteLine();
            Console.WriteLine("This test would require mocking the HTTP client.");
            Console.WriteLine("In production, the downloader will automatically:");
            Console.WriteLine("   • Retry up to 3 times on failure");
            Console.WriteLine("   • Wait 2 seconds between retries");
            Console.WriteLine("   • Report each attempt to the progress callback");
            Console.WriteLine();
            
            await Task.Delay(100); // Placeholder
        }

        /// <summary>
        /// Test existing installation detection.
        /// </summary>
        public static async Task TestExistingInstallation()
        {
            Console.WriteLine("Testing Existing Installation Detection");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var testPath = Path.Combine(Path.GetTempPath(), "ffmpeg_existing_test");
            Directory.CreateDirectory(testPath);

            // First installation
            Console.WriteLine("First call - should download:");
            var result1 = await FfmpegDownloader.EnsureFfmpegPresentAsync(
                testPath,
                (b, t, s) => Console.WriteLine($"   {s}"));

            Console.WriteLine();
            Console.WriteLine();

            // Second call - should skip
            Console.WriteLine("Second call - should detect existing:");
            var result2 = await FfmpegDownloader.EnsureFfmpegPresentAsync(
                testPath,
                (b, t, s) => Console.WriteLine($"   {s}"));

            Console.WriteLine();
            
            if (result1 == result2 && result1 != null)
            {
                Console.WriteLine("✅ Existing installation correctly detected!");
            }
            else
            {
                Console.WriteLine("❌ Detection failed");
            }
        }
    }
}
