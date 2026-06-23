using System;
using System.Diagnostics;
using System.IO;

namespace TimelapseCapture
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

                long totalBytes = 0;
                foreach (var file in frameFiles)
                {
                    var info = new FileInfo(file);
                    totalBytes += info.Length;
                }

                double avgBytes = totalBytes / (double)frameFiles.Length;
                return avgBytes / 1024.0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Build storage info string for UI display.
        /// Shows both estimated and actual (if available) storage usage.
        /// </summary>
        public static string GetStorageInfoString(
            string? sessionFolder,
            int width,
            int height,
            string format,
            int jpegQuality,
            int currentFrames,
            int projectedFrames)
        {
            var result = new System.Text.StringBuilder();

            // Calculate estimated frame size
            double estimatedFrameSizeKB = EstimateFrameSizeKB(width, height, format, jpegQuality);
            
            // Get actual average if we have captured frames
            double actualFrameSizeKB = 0;
            if (!string.IsNullOrEmpty(sessionFolder) && currentFrames > 0)
            {
                actualFrameSizeKB = GetActualAverageFrameSizeKB(sessionFolder);
            }

            // Show frame size info
            if (actualFrameSizeKB > 0)
            {
                result.AppendLine($"📦 Frame Size: {actualFrameSizeKB:F1} KB (actual avg)");
            }
            else
            {
                result.AppendLine($"📦 Frame Size: ~{estimatedFrameSizeKB:F1} KB (estimated)");
            }

            // Show current storage if frames exist
            if (currentFrames > 0)
            {
                double usedKB = actualFrameSizeKB > 0 
                    ? actualFrameSizeKB * currentFrames 
                    : estimatedFrameSizeKB * currentFrames;
                double usedMB = usedKB / 1024.0;
                
                result.AppendLine($"💾 Current: {usedMB:F1} MB ({currentFrames} frames)");
            }

            // Show projected storage for remaining frames
            if (projectedFrames > currentFrames)
            {
                double frameSize = actualFrameSizeKB > 0 ? actualFrameSizeKB : estimatedFrameSizeKB;
                double projectedKB = frameSize * (projectedFrames - currentFrames);
                double projectedMB = projectedKB / 1024.0;
                
                result.AppendLine($"📊 Projected: +{projectedMB:F1} MB ({projectedFrames - currentFrames} more frames)");
                
                double totalMB = (frameSize * projectedFrames) / 1024.0;
                result.AppendLine($"📁 Total: {totalMB:F1} MB (when complete)");
            }

            // Show available disk space
            if (!string.IsNullOrEmpty(sessionFolder))
            {
                long availableMB = GetAvailableDiskSpaceMB(sessionFolder);
                if (availableMB > 0)
                {
                    result.AppendLine($"💿 Available: {availableMB:F0} MB");
                    
                    // Warning if low space
                    double frameSize = actualFrameSizeKB > 0 ? actualFrameSizeKB : estimatedFrameSizeKB;
                    double projectedTotalMB = (frameSize * projectedFrames) / 1024.0;
                    
                    if (availableMB < projectedTotalMB * 1.2) // Need 20% buffer
                    {
                        result.AppendLine("⚠️ WARNING: May run out of disk space!");
                    }
                }
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
