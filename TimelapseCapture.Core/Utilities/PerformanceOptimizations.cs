using System;

namespace TimelapseCapture
{
    /// <summary>
    /// Performance optimization tips and efficiency improvements.
    /// </summary>
    public static class PerformanceOptimizations
    {
        /// <summary>
        /// Recommendations for lightweight operation alongside streaming applications.
        /// </summary>
        public static class Recommendations
        {
            public const string CAPTURE_INTERVAL = 
                "For streaming alongside:\n" +
                "• Use 5+ second intervals to minimize CPU usage\n" +
                "• Shorter intervals (<1s) increase overhead significantly\n" +
                "• Consider 10-30 second intervals for long timelapses";

            public const string IMAGE_FORMAT =
                "Format efficiency:\n" +
                "• JPEG Quality 70-85: Best balance (smaller files, good quality)\n" +
                "• JPEG Quality 90+: Larger files, minimal quality gain\n" +
                "• PNG: 3-5x larger than JPEG, lossless\n" +
                "• BMP: 10-20x larger, no compression - avoid for timelapses";

            public const string RESOLUTION =
                "Resolution tips:\n" +
                "• 1920×1080 (1080p): Standard, good quality\n" +
                "• 1280×720 (720p): Smaller files, still acceptable\n" +
                "• Higher resolutions significantly increase file sizes\n" +
                "• Consider your final video output resolution";

            public const string ENCODING_PRESET =
                "Encoding efficiency:\n" +
                "• Ultrafast: Quick encode, larger file, use for quick previews\n" +
                "• Fast: Good balance for most use cases\n" +
                "• Medium: Default, good quality vs. time\n" +
                "• Slow: Best quality, takes much longer - use for final output";

            public const string SYSTEM_IMPACT =
                "Minimize system impact:\n" +
                "• Close unnecessary applications\n" +
                "• This app uses <50MB RAM during capture\n" +
                "• CPU usage is minimal during capture (timer-based)\n" +
                "• Main CPU load occurs during encoding (can take minutes)\n" +
                "• Disk I/O is the main bottleneck (use fast SSD if possible)";

            public const string STREAMING_TIPS =
                "Running alongside OBS/streaming:\n" +
                "• Capture different regions to avoid overlap\n" +
                "• Use 10+ second intervals for minimal interference\n" +
                "• JPEG Quality 75-80 recommended\n" +
                "• Encode videos after stream ends, not during\n" +
                "• Test first - CPU/GPU load varies by hardware";
        }

        /// <summary>
        /// Get all optimization tips as formatted text.
        /// </summary>
        public static string GetAllTips()
        {
            return $"{Recommendations.CAPTURE_INTERVAL}\n\n" +
                   $"{Recommendations.IMAGE_FORMAT}\n\n" +
                   $"{Recommendations.RESOLUTION}\n\n" +
                   $"{Recommendations.ENCODING_PRESET}\n\n" +
                   $"{Recommendations.SYSTEM_IMPACT}\n\n" +
                   $"{Recommendations.STREAMING_TIPS}";
        }

        /// <summary>
        /// Calculate estimated CPU impact score (0-100).
        /// Lower is better for running alongside other applications.
        /// </summary>
        public static int EstimateCpuImpact(decimal intervalSeconds, string format, int jpegQuality, bool isEncoding)
        {
            int baseImpact = 5; // Minimal when not capturing

            if (isEncoding)
            {
                // Encoding is CPU-intensive (50-90%)
                return 75;
            }

            // Capture frequency impact
            int frequencyImpact = intervalSeconds switch
            {
                < 1 => 25,      // Very frequent - significant overhead
                < 5 => 15,      // Frequent - moderate overhead
                < 10 => 8,      // Reasonable - low overhead
                < 30 => 4,      // Infrequent - minimal overhead
                _ => 2          // Very infrequent - negligible
            };

            // Format impact (compression overhead)
            int formatImpact = format switch
            {
                "BMP" => 1,     // No compression
                "PNG" => 5,     // Lossless compression
                "JPEG" => jpegQuality > 90 ? 3 : 2, // JPEG compression
                _ => 2
            };

            return Math.Min(100, baseImpact + frequencyImpact + formatImpact);
        }

        /// <summary>
        /// Get impact level description.
        /// </summary>
        public static string GetImpactLevel(int impactScore)
        {
            return impactScore switch
            {
                < 10 => "✅ Negligible - Perfect for streaming",
                < 20 => "✅ Very Low - Safe for streaming",
                < 40 => "⚠️ Low-Moderate - Monitor performance",
                < 60 => "⚠️ Moderate - May affect streaming",
                < 80 => "❌ High - Not recommended during streaming",
                _ => "❌ Very High - Encoding in progress"
            };
        }

        /// <summary>
        /// Suggest optimizations based on current settings.
        /// </summary>
        public static string GetOptimizationSuggestions(decimal intervalSeconds, string format, int jpegQuality, bool isCapturing)
        {
            var suggestions = new System.Text.StringBuilder();
            bool hassuggestions = false;

            if (intervalSeconds < 5 && isCapturing)
            {
                suggestions.AppendLine("• Consider increasing interval to 5+ seconds");
                hassuggestions = true;
            }

            if (format == "BMP")
            {
                suggestions.AppendLine("• BMP creates very large files - switch to JPEG or PNG");
                hassuggestions = true;
            }

            if (format == "PNG")
            {
                suggestions.AppendLine("• PNG files are 3-5x larger than JPEG - consider JPEG for space savings");
                hassuggestions = true;
            }

            if (format == "JPEG" && jpegQuality > 90)
            {
                suggestions.AppendLine($"• Quality {jpegQuality} is very high - try 80-85 for smaller files");
                hassuggestions = true;
            }

            if (format == "JPEG" && jpegQuality < 70)
            {
                suggestions.AppendLine($"• Quality {jpegQuality} may show compression artifacts - try 75-85");
                hassuggestions = true;
            }

            return hassuggestions ? suggestions.ToString().TrimEnd() : "✅ Settings look good!";
        }
    }
}
