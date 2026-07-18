using System;
using System.Collections.Generic;

namespace FrameWrite
{
    /// <summary>
    /// Human-readable duration strings. Two flavours:
    /// <see cref="Duration"/> is compact/fuzzy for planning readouts ("2h 30m", "45m", "30s") and
    /// deliberately drops a stray seconds component in the hours branch; <see cref="DurationPrecise"/>
    /// never drops a nonzero part — use it wherever the value must echo an exact user-set time
    /// (the recording timer), which is where "record for 1h 30s" once showed as "1h".
    /// </summary>
    public static class HumanFormat
    {
        /// <summary>Compact size on disk ("1.21 GB" / "512.3 MB" / "48.2 KB"). Pure — unit-tested.</summary>
        public static string Bytes(long bytes) => bytes >= 1073741824L
            ? $"{bytes / 1073741824.0:0.##} GB"
            : bytes >= 1048576L ? $"{bytes / 1048576.0:0.#} MB" : $"{bytes / 1024.0:0.#} KB";

        /// <summary>Compact, planning-oriented duration ("2h 30m" / "45m" / "30s"). Fuzzy by design.</summary>
        public static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            if (t.TotalHours >= 1) return t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return t.Seconds == 0 ? $"{t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }

        /// <summary>Exact duration that never drops a nonzero h/m/s part ("1h 30s", "1h 1m 1s", "0s").</summary>
        public static string DurationPrecise(double seconds)
        {
            int total = (int)Math.Round(Math.Max(0, seconds));
            int h = total / 3600, m = total % 3600 / 60, s = total % 60;
            var parts = new List<string>();
            if (h > 0) parts.Add($"{h}h");
            if (m > 0) parts.Add($"{m}m");
            if (s > 0 || parts.Count == 0) parts.Add($"{s}s");
            return string.Join(" ", parts);
        }
    }
}
