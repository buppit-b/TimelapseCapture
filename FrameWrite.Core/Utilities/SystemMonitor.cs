using System;
using System.Diagnostics;
using System.IO;

namespace FrameWrite
{
    /// <summary>
    /// Monitors system resources and estimates storage requirements.
    /// </summary>
    public static class SystemMonitor
    {
        /// <summary>
        /// Get available disk space in MB for a given path.
        /// </summary>
        public static long GetAvailableDiskSpaceMB(string path)
        {
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
                return driveInfo.AvailableFreeSpace / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get total disk space in MB for a given path.
        /// </summary>
        public static long GetTotalDiskSpaceMB(string path)
        {
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
                return driveInfo.TotalSize / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Estimate frame size based on resolution and format.
        /// Returns estimated size in KB per frame.
        /// </summary>
        public static double EstimateFrameSizeKB(int width, int height, string format, int jpegQuality = 90)
        {
            // Calculate total pixels
            long pixels = (long)width * height;

            if (format == "PNG")
            {
                // PNG: Roughly 0.5-1.5 bytes per pixel (compressed but lossless)
                // Use 1.0 as middle estimate
                return (pixels * 1.0) / 1024.0;
            }
            else if (format == "BMP")
            {
                // BMP: 3 bytes per pixel (uncompressed RGB)
                return (pixels * 3.0) / 1024.0;
            }
            else // JPEG
            {
                // JPEG: Highly variable based on quality and content
                // Quality 90: ~0.15-0.25 bytes per pixel
                // Quality 70: ~0.08-0.15 bytes per pixel
                // Quality 50: ~0.05-0.10 bytes per pixel
                
                double bytesPerPixel = jpegQuality switch
                {
                    >= 90 => 0.20,  // High quality
                    >= 70 => 0.12,  // Medium quality
                    >= 50 => 0.08,  // Low quality
                    _ => 0.05       // Very low quality
                };
                
                return (pixels * bytesPerPixel) / 1024.0;
            }
        }

        /// <summary>
        /// Estimate total storage needed for a capture session.
        /// Returns estimate in MB.
        /// </summary>
        public static double EstimateSessionStorageMB(
            int width,
            int height,
            string format,
            int jpegQuality,
            int frameCount)
        {
            double frameSizeKB = EstimateFrameSizeKB(width, height, format, jpegQuality);
            return (frameSizeKB * frameCount) / 1024.0;
        }

        /// <summary>
        /// Storage-consumption RATE in MB/hour for a given frame size and interval. Pure math (units:
        /// KB/frame × frames/hour ÷ 1024 → MB/hour). 0 for a non-positive frame size or interval.
        /// Powers the live rate readout and the "fills the drive in ~X" pre-flight warning.
        /// </summary>
        public static double StorageMbPerHour(double frameKb, double intervalSeconds)
        {
            if (frameKb <= 0 || intervalSeconds <= 0) return 0;
            return frameKb * (3600.0 / intervalSeconds) / 1024.0;
        }

        /// <summary>
        /// Hours to fill a drive with <paramref name="freeMb"/> free at <paramref name="mbPerHour"/>.
        /// PositiveInfinity when either input is non-positive (nothing to fill / no consumption).
        /// </summary>
        public static double HoursToFillDrive(double freeMb, double mbPerHour)
        {
            if (freeMb <= 0 || mbPerHour <= 0) return double.PositiveInfinity;
            return freeMb / mbPerHour;
        }

        /// <summary>
        /// Get current process CPU usage (0-100).
        /// Returns average over last measurement period.
        /// </summary>
        public static double GetProcessCpuUsage()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var startTime = DateTime.UtcNow;
                    var startCpuUsage = process.TotalProcessorTime;
                    
                    System.Threading.Thread.Sleep(100); // Sample period
                    
                    var endTime = DateTime.UtcNow;
                    var endCpuUsage = process.TotalProcessorTime;
                    
                    var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                    var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    
                    return cpuUsageTotal * 100.0;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get current process memory usage in MB.
        /// </summary>
        public static double GetProcessMemoryMB()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return process.WorkingSet64 / (1024.0 * 1024.0);
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get system-wide available memory in MB.
        /// </summary>
        public static long GetSystemAvailableMemoryMB()
        {
            try
            {
                var pc = new PerformanceCounter("Memory", "Available MBytes");
                return (long)pc.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Format bytes to human-readable string (KB, MB, GB).
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            else
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        /// <summary>
        /// Get actual average frame size from existing frames in a session.
        /// Returns size in KB, or 0 if no frames exist.
        /// </summary>
        public static double GetActualAverageFrameSizeKB(string sessionFolder)
        {
            try
            {
                var frameFiles = SessionManager.GetFrameFiles(sessionFolder);
                if (frameFiles.Length == 0)
                    return 0;

                // Estimate from the most-recent frames only — frame size is roughly stable, and statting
                // every file each refresh is O(n) on a folder that grows to tens of thousands on a long run.
                int sample = Math.Min(8, frameFiles.Length);
                long totalBytes = 0;
                for (int i = frameFiles.Length - sample; i < frameFiles.Length; i++)
                    totalBytes += new FileInfo(frameFiles[i]).Length;

                double avgBytes = totalBytes / (double)sample;
                return avgBytes / 1024.0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Disk-budget projection for the "Size" capture target: how many MORE frames (and how much
        /// more capture time at <paramref name="intervalSeconds"/>) fit before a session reaches
        /// <paramref name="budgetMB"/>, given the current frame count and average frame size.
        /// Pure — unit-tested. Returns (0,0) once the budget is already met, or when inputs are
        /// degenerate (unknown frame size / non-positive interval).
        /// </summary>
        public static (long framesRemaining, double secondsRemaining) ProjectCaptureBudget(
            double budgetMB, double avgFrameKB, int currentFrames, double intervalSeconds)
        {
            if (budgetMB <= 0 || avgFrameKB <= 0 || intervalSeconds <= 0) return (0, 0);
            double usedMB = avgFrameKB * Math.Max(0, currentFrames) / 1024.0;
            double remainingMB = budgetMB - usedMB;
            if (remainingMB <= 0) return (0, 0);
            long frames = (long)(remainingMB * 1024.0 / avgFrameKB);
            return (frames, frames * intervalSeconds);
        }

        /// <summary>
        /// The storage picture as structured numbers — one computation feeding both the WPF stat
        /// rows and the legacy string readout, so the two can never disagree.
        /// </summary>
        public sealed class StorageStats
        {
            public double FrameSizeKB;          // actual average when frames exist, else the estimate
            public bool FrameSizeIsActual;
            public int CurrentFrames;
            public double SessionMB;            // what the frames on disk use now
            public int RemainingFrames;         // to reach the projection target (0 when already there)
            public double RemainingMB;
            public double TotalAtTargetMB;      // session size when the target is reached
            public long AvailableMB;            // free space on the session's drive (0 = unknown)
            public string Drive = "";           // e.g. "D:" — the disk the low-disk safety watches
            public bool LowSpaceWarning;        // projected total (with 20% buffer) exceeds free space
        }

        public static StorageStats GetStorageStats(
            string? sessionFolder,
            int width,
            int height,
            string format,
            int jpegQuality,
            int currentFrames,
            int projectedFrames,
            double actualFrameSizeKBOverride = -1)   // >= 0: caller already sampled it — don't re-read the folder
        {
            double estimatedKB = EstimateFrameSizeKB(width, height, format, jpegQuality);
            double actualKB = 0;
            if (actualFrameSizeKBOverride >= 0)
                actualKB = actualFrameSizeKBOverride;
            else if (!string.IsNullOrEmpty(sessionFolder) && currentFrames > 0)
                actualKB = GetActualAverageFrameSizeKB(sessionFolder);

            var s = new StorageStats
            {
                FrameSizeIsActual = actualKB > 0,
                FrameSizeKB = actualKB > 0 ? actualKB : estimatedKB,
                CurrentFrames = currentFrames,
            };
            s.SessionMB = s.FrameSizeKB * currentFrames / 1024.0;
            s.RemainingFrames = Math.Max(0, projectedFrames - currentFrames);
            s.RemainingMB = s.FrameSizeKB * s.RemainingFrames / 1024.0;
            s.TotalAtTargetMB = s.FrameSizeKB * Math.Max(projectedFrames, currentFrames) / 1024.0;

            if (!string.IsNullOrEmpty(sessionFolder))
            {
                s.AvailableMB = GetAvailableDiskSpaceMB(sessionFolder);
                s.Drive = (Path.GetPathRoot(sessionFolder) ?? "").TrimEnd('\\');
                // The 20% buffer matches the long-standing string readout's warning.
                s.LowSpaceWarning = s.AvailableMB > 0 && s.AvailableMB < s.TotalAtTargetMB * 1.2;
            }
            return s;
        }

        /// <summary>
        /// Build storage info string for UI display (legacy readout — the WPF app binds the
        /// structured rows instead). Formats from GetStorageStats so the numbers always match.
        /// </summary>
        public static string GetStorageInfoString(
            string? sessionFolder,
            int width,
            int height,
            string format,
            int jpegQuality,
            int currentFrames,
            int projectedFrames,
            double actualFrameSizeKBOverride = -1)
        {
            var s = GetStorageStats(sessionFolder, width, height, format, jpegQuality,
                                    currentFrames, projectedFrames, actualFrameSizeKBOverride);
            var result = new System.Text.StringBuilder();

            result.AppendLine(s.FrameSizeIsActual
                ? $"📦 Frame Size: {s.FrameSizeKB:F1} KB (actual avg)"
                : $"📦 Frame Size: ~{s.FrameSizeKB:F1} KB (estimated)");

            if (s.CurrentFrames > 0)
                result.AppendLine($"💾 Current: {s.SessionMB:F1} MB ({s.CurrentFrames} frames)");

            if (s.RemainingFrames > 0)
            {
                result.AppendLine($"📊 Projected: +{s.RemainingMB:F1} MB ({s.RemainingFrames} more frames)");
                result.AppendLine($"📁 Total: {s.TotalAtTargetMB:F1} MB (when complete)");
            }

            if (s.AvailableMB > 0)
            {
                result.AppendLine($"💿 Available: {s.AvailableMB:F0} MB free{(s.Drive.Length > 0 ? $" on {s.Drive}" : "")}");
                if (s.LowSpaceWarning)
                    result.AppendLine("⚠️ WARNING: May run out of disk space!");
            }

            return result.ToString().TrimEnd();
        }

        /// <summary>
        /// Build system resources info string for UI display.
        /// Shows CPU and memory usage.
        /// </summary>
        public static string GetResourcesInfoString()
        {
            var result = new System.Text.StringBuilder();

            // Memory usage
            double memoryMB = GetProcessMemoryMB();
            result.AppendLine($"🧠 Memory: {memoryMB:F1} MB");

            // CPU usage (note: sampling adds 100ms delay, use sparingly)
            // Disabled by default to avoid UI stuttering
            // double cpuPercent = GetProcessCpuUsage();
            // result.AppendLine($"⚡ CPU: {cpuPercent:F1}%");

            return result.ToString().TrimEnd();
        }
    }
}
