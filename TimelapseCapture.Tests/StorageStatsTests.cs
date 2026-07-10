using Xunit;
using FluentAssertions;

namespace TimelapseCapture.Tests
{
    // The structured storage stats feeding the WPF stat rows (and the legacy string readout).
    public class StorageStatsTests
    {
        [Fact]
        public void UsesActualAverage_WhenProvided()
        {
            var s = SystemMonitor.GetStorageStats(null, 1920, 1080, "JPEG", 85,
                currentFrames: 10, projectedFrames: 100, actualFrameSizeKBOverride: 100);

            s.FrameSizeIsActual.Should().BeTrue();
            s.FrameSizeKB.Should().Be(100);
            s.SessionMB.Should().BeApproximately(10 * 100 / 1024.0, 0.001);
            s.RemainingFrames.Should().Be(90);
            s.RemainingMB.Should().BeApproximately(90 * 100 / 1024.0, 0.001);
            s.TotalAtTargetMB.Should().BeApproximately(100 * 100 / 1024.0, 0.001);
            s.AvailableMB.Should().Be(0);           // no folder → unknown
            s.LowSpaceWarning.Should().BeFalse();   // unknown space never warns
        }

        [Fact]
        public void FallsBackToEstimate_WithNoFrames()
        {
            var s = SystemMonitor.GetStorageStats(null, 1920, 1080, "JPEG", 85,
                currentFrames: 0, projectedFrames: 50);

            s.FrameSizeIsActual.Should().BeFalse();
            s.FrameSizeKB.Should().BeGreaterThan(0, "the estimate must produce something usable");
            s.SessionMB.Should().Be(0);
            s.RemainingFrames.Should().Be(50);
        }

        [Fact]
        public void PastTarget_ReportsNoRemaining_AndKeepsTotalAtActualSize()
        {
            var s = SystemMonitor.GetStorageStats(null, 800, 600, "PNG", 90,
                currentFrames: 120, projectedFrames: 100, actualFrameSizeKBOverride: 50);

            s.RemainingFrames.Should().Be(0);
            s.RemainingMB.Should().Be(0);
            // Total tracks whichever is larger — a session past its target is bigger than the plan.
            s.TotalAtTargetMB.Should().BeApproximately(120 * 50 / 1024.0, 0.001);
        }

        [Fact]
        public void StringReadout_MatchesStructuredNumbers()
        {
            string text = SystemMonitor.GetStorageInfoString(null, 1920, 1080, "JPEG", 85,
                currentFrames: 10, projectedFrames: 100, actualFrameSizeKBOverride: 100);

            text.Should().Contain("100.0 KB (actual avg)");
            text.Should().Contain($"{10 * 100 / 1024.0:F1} MB (10 frames)");
            text.Should().Contain($"+{90 * 100 / 1024.0:F1} MB (90 more frames)");
        }
    }
}
