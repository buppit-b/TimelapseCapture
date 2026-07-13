using System;
using System.Globalization;

namespace FrameWrite
{
    /// <summary>
    /// Pure conversions between capture interval (seconds) and rate (fps), plus interval
    /// normalization. Extracted from the VM so the round-trip is unit-testable: a 4-dp interval
    /// rounding once made a typed 60 fps display as 59.88 (1/60 = 0.016667 → 0.0167 → 59.88).
    /// Keeping 6 dp lets round-number fps in the valid range land back on themselves.
    /// </summary>
    public static class IntervalMath
    {
        /// <summary>One frame an hour — beyond this an entry is almost certainly a typo, not a plan.</summary>
        public const decimal MaxIntervalSeconds = 3600m;

        /// <summary>fps → interval seconds (0 for a non-positive rate). 6 dp so round-number fps round-trip.</summary>
        public static decimal FpsToInterval(decimal fps) => fps > 0 ? Math.Round(1m / fps, 6) : 0m;

        /// <summary>interval seconds → fps (0 for a non-positive interval), 2 dp for display.</summary>
        public static decimal IntervalToFps(decimal intervalSeconds) =>
            intervalSeconds > 0 ? Math.Round(1m / intervalSeconds, 2) : 0m;

        /// <summary>
        /// Clamp an interval to [floor, 3600] and normalize to 6 dp with trailing zeros stripped, so a
        /// pasted "0.1000000000" or an fps round-trip artefact both display as "0.1".
        /// </summary>
        public static decimal Normalize(decimal value, decimal floor)
        {
            decimal v = value < floor ? floor : value > MaxIntervalSeconds ? MaxIntervalSeconds : value;
            return decimal.Parse(Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
        }
    }
}
