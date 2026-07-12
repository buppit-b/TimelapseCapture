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
        public void ProjectCaptureBudget_ComputesFramesAndTimeToBudget()
        {
            // 10 GB budget, 100 KB/frame, no frames yet, 2s interval.
            // 10240 MB * 1024 KB/MB / 100 KB = 104,857 frames; × 2s ≈ 209,715 s.
            var (frames, secs) = SystemMonitor.ProjectCaptureBudget(10 * 1024, 100, 0, 2.0);
            frames.Should().Be(104857);
            secs.Should().BeApproximately(104857 * 2.0, 1.0);
        }

        [Fact]
        public void ProjectCaptureBudget_AccountsForFramesAlreadyOnDisk()
        {
            // 1 GB budget, 100 KB/frame; 5000 frames already use ~488 MB, leaving ~536 MB.
            var (frames, _) = SystemMonitor.ProjectCaptureBudget(1024, 100, 5000, 1.0);
            double usedMB = 100.0 * 5000 / 1024.0;
            long expected = (long)((1024 - usedMB) * 1024.0 / 100.0);
            frames.Should().Be(expected);
        }

        [Theory]
        [InlineData(0, 100, 0, 1)]        // no budget
        [InlineData(1024, 0, 0, 1)]       // unknown frame size
        [InlineData(1024, 100, 0, 0)]     // non-positive interval
        public void ProjectCaptureBudget_DegenerateInputs_ReturnZero(double budgetMB, double kb, int frames, double interval)
        {
            SystemMonitor.ProjectCaptureBudget(budgetMB, kb, frames, interval).Should().Be((0L, 0.0));
        }

        [Fact]
        public void ProjectCaptureBudget_AlreadyOverBudget_ReturnsZero()
        {
            // 10000 frames at 100 KB = ~976 MB, over a 500 MB budget.
            SystemMonitor.ProjectCaptureBudget(500, 100, 10000, 1.0).Should().Be((0L, 0.0));
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
