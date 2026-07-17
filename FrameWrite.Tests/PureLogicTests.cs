using Xunit;
using FluentAssertions;
using System;
using System.Drawing;

namespace FrameWrite.Tests
{
    // Scale-rect math for window-tracking resize modes (CaptureEngine.ComputeScaledDest).
    public class ScaleToLockedMathTests
    {
        [Fact]
        public void Stretch_FillsTheWholeFrame()
        {
            var r = CaptureEngine.ComputeScaledDest(new Size(100, 100), new Size(800, 600), CaptureEngine.ResizeStretch);
            r.Should().Be(new Rectangle(0, 0, 800, 600));
        }

        [Fact]
        public void Fit_SquareIntoWide_CentresHorizontally_PreservingAspect()
        {
            // 100x100 into 800x600 → scale 6 → 600x600, centred: x = (800-600)/2 = 100
            var r = CaptureEngine.ComputeScaledDest(new Size(100, 100), new Size(800, 600), CaptureEngine.ResizeFit);
            r.Should().Be(new Rectangle(100, 0, 600, 600));
        }

        [Fact]
        public void Fit_SameAspect_FillsExactly()
        {
            var r = CaptureEngine.ComputeScaledDest(new Size(800, 600), new Size(400, 300), CaptureEngine.ResizeFit);
            r.Should().Be(new Rectangle(0, 0, 400, 300));
        }

        [Fact]
        public void Fit_WideIntoSquare_StaysInsideFrame_AndKeepsAspect()
        {
            var dest = new Size(600, 600);
            var r = CaptureEngine.ComputeScaledDest(new Size(1920, 1080), dest, CaptureEngine.ResizeFit);

            r.Width.Should().Be(600);
            Assert.True(r.X >= 0 && r.Y >= 0, "must not start outside the frame");
            Assert.True(r.X + r.Width <= dest.Width, "must not overflow horizontally");
            Assert.True(r.Y + r.Height <= dest.Height, "must not overflow vertically");
            Assert.True(Math.Abs((double)r.Width / r.Height - 1920.0 / 1080.0) < 0.02, "aspect preserved");
        }
    }

    /// <summary>
    /// Smart-interval capture decision (CaptureEngine.ShouldCaptureWhileSmart). Locks the semantics
    /// that were once inverted: the main interval is the ACTIVE rate; idle SLOWS (or skips).
    /// </summary>
    public class SmartCaptureDecisionTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Active_AlwaysCaptures_RegardlessOfSkip(bool skip)
        {
            // Active → working rate == poll rate → capture every tick.
            CaptureEngine.ShouldCaptureWhileSmart(isActive: true, skipIdleFrames: skip,
                msSinceLastCapture: 0, baseIntervalMs: 1000, idleIntervalMs: 5000).Should().BeTrue();
        }

        [Fact]
        public void IdleAndSkip_NeverCaptures()
        {
            CaptureEngine.ShouldCaptureWhileSmart(false, skipIdleFrames: true,
                msSinceLastCapture: 999999, baseIntervalMs: 1000, idleIntervalMs: 5000).Should().BeFalse();
        }

        [Fact]
        public void IdleAndSlowed_CapturesOnlyOnceIdleRateElapses()
        {
            // Idle rate 5s: 4s in → wait; 5s in → capture.
            CaptureEngine.ShouldCaptureWhileSmart(false, false, 4000, 1000, 5000).Should().BeFalse();
            CaptureEngine.ShouldCaptureWhileSmart(false, false, 5000, 1000, 5000).Should().BeTrue();
        }

        [Fact]
        public void IdleSlowed_UsesTheSlowerOfWorkingAndIdle_SoIdleNeverCapturesFasterThanWorking()
        {
            // Even if idle rate were set faster than working (500 < 1000), the working rate floors it —
            // this is the guard against the old bug where a fast main interval captured MORE while idle.
            CaptureEngine.ShouldCaptureWhileSmart(false, false, 700, baseIntervalMs: 1000, idleIntervalMs: 500)
                .Should().BeFalse();
            CaptureEngine.ShouldCaptureWhileSmart(false, false, 1000, baseIntervalMs: 1000, idleIntervalMs: 500)
                .Should().BeTrue();
        }
    }

    // The sign-in Run-key command line (StartupRegistration.RunValue) — must survive paths with spaces.
    public class StartupRunValueTests
    {
        [Fact]
        public void QuotesThePath_SoSpacesSurvive() =>
            StartupRegistration.RunValue(@"C:\Program Files\FrameWrite\FrameWrite.exe")
                .Should().Be("\"C:\\Program Files\\FrameWrite\\FrameWrite.exe\"");

        [Fact]
        public void PlainPath_StillQuoted() =>
            StartupRegistration.RunValue(@"C:\fw\FrameWrite.exe").Should().StartWith("\"").And.EndWith("\"");
    }

    // Output-filename sanitiser that feeds the (quoted) ffmpeg command (VideoEncoder.SanitizeFileName).
    public class SanitizeFileNameTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("..")]      // trailing dots trimmed → empty (no traversal component survives)
        [InlineData(". ")]
        public void BlankOrDotsOnly_BecomeEmpty(string? input)
        {
            VideoEncoder.SanitizeFileName(input).Should().BeEmpty();
        }

        [Theory]
        [InlineData("a/b\\c", "a_b_c")]                      // path separators removed → no traversal
        [InlineData("a\"b", "a_b")]                          // quote removed → can't break ffmpeg arg quoting
        [InlineData("a<b>c|d:e*f?g", "a_b_c_d_e_f_g")]
        [InlineData("my timelapse", "my timelapse")]         // interior spaces are valid
        [InlineData("name.", "name")]                        // trailing dot trimmed
        public void StripsIllegalChars(string input, string expected)
        {
            VideoEncoder.SanitizeFileName(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("..\\..\\Windows\\System32\\evil")]
        [InlineData("C:\\Windows\\x")]
        [InlineData("name\"; rm -rf\\x")]
        public void ResultNeverContainsSeparatorsQuotesOrColons(string input)
        {
            var s = VideoEncoder.SanitizeFileName(input);
            s.Should().NotContain("\\");
            s.Should().NotContain("/");
            s.Should().NotContain("\"");
            s.Should().NotContain(":");
        }
    }

    /// <summary>
    /// FpsForDuration: the playback fps that makes a frame set last exactly N seconds —
    /// the engine of the encode-to-duration feature.
    /// </summary>
    public class FpsForDurationTests
    {
        [Fact]
        public void ComputesExactFps_ForWholeSession()
        {
            // 300 frames over 30s = exactly 10 fps.
            VideoEncoder.FpsForDuration(300, 1, 30).Should().BeApproximately(10.0, 0.0001);
        }

        [Fact]
        public void AccountsForFrameSkip()
        {
            // 300 frames, keep 1-in-3 = 100 encoded frames; over 10s = 10 fps.
            VideoEncoder.FpsForDuration(300, 3, 10).Should().BeApproximately(10.0, 0.0001);
        }

        [Fact]
        public void ClampsToCeiling_SoLongSessionsStayPlayable()
        {
            // 100k frames in 10s would want 10,000 fps → clamped to 240 (comes out longer, but plays).
            VideoEncoder.FpsForDuration(100_000, 1, 10).Should().Be(240);
        }

        [Fact]
        public void ClampsToFloor_ForVeryFewFramesOverLongDuration()
        {
            // 2 frames stretched over 10 minutes wants 0.0033 fps → clamped to the 0.1 floor.
            VideoEncoder.FpsForDuration(2, 1, 600).Should().Be(0.1);
        }

        [Theory]
        [InlineData(0, 1, 30)]     // no frames
        [InlineData(300, 1, 0)]    // zero duration
        [InlineData(300, 1, -5)]   // negative duration
        public void FallsBackTo30_OnDegenerateInput(int frames, int everyNth, double seconds)
        {
            VideoEncoder.FpsForDuration(frames, everyNth, seconds).Should().Be(30);
        }
    }

    /// <summary>
    /// Archive-session pure logic (SessionArchiver): the codec choice encodes the fidelity promise
    /// (lossless captures archive lossless), and the stderr frame-count parse GATES frame deletion —
    /// a parse bug must fail toward "mismatch", never toward "counts match".
    /// </summary>
    public class SessionArchiverLogicTests
    {
        [Theory]
        [InlineData("png")]
        [InlineData("bmp")]
        public void LosslessCaptures_ArchiveMathematicallyLossless(string ext)
        {
            SessionArchiver.ArchiveCodecArgs(ext).Should().Contain("libx264rgb").And.Contain("-qp 0");
        }

        [Fact]
        public void JpegCaptures_ArchiveVisuallyLossless_WithoutASecondChromaSubsample()
        {
            var args = SessionArchiver.ArchiveCodecArgs("jpg");
            args.Should().Contain("libx264 ").And.Contain("-crf 10").And.Contain("yuv444p");
        }

        [Fact]
        public void ParseLastFrameCount_TakesTheLastProgressLine()
        {
            string stderr = "Input #0, matroska\nframe=   10 fps=0.0 q=28.0\nsome noise\nframe=  165 fps=99 q=-1.0 Lsize=N/A\n";
            SessionArchiver.ParseLastFrameCount(stderr).Should().Be(165);
        }

        [Theory]
        [InlineData("")]
        [InlineData("no progress lines at all")]
        [InlineData("  frame= 12 indented does not count")]   // ^ anchor: real lines start at column 0
        public void ParseLastFrameCount_NoFrameLine_IsMinusOne_NeverZeroOrAMatch(string stderr)
        {
            SessionArchiver.ParseLastFrameCount(stderr).Should().Be(-1);
        }
    }

    /// <summary>
    /// Export-format codec args (VideoEncoder.FormatArgs) — these strings are interpolated straight
    /// into the ffmpeg command line, so each format must produce exactly the right block.
    /// </summary>
    public class FormatArgsTests
    {
        [Fact]
        public void Mp4_IsH264_WithPresetAndCrf()
        {
            var (ext, args) = VideoEncoder.FormatArgs("mp4", "medium", 23);
            ext.Should().Be(".mp4");
            args.Should().Contain("libx264").And.Contain("-preset medium").And.Contain("-crf 23")
                .And.Contain("yuv420p").And.EndWith(" ");
        }

        [Fact]
        public void Webm_IsVp9_WithRemappedCrf_AndSpeedFromPreset()
        {
            // VP9's 0-63 CRF reads ~9 higher than x264's for similar quality; presets map to cpu-used.
            var (ext, args) = VideoEncoder.FormatArgs("webm", "veryslow", 23);
            ext.Should().Be(".webm");
            args.Should().Contain("libvpx-vp9").And.Contain("-crf 32").And.Contain("-b:v 0")
                .And.Contain("-cpu-used 1");
            VideoEncoder.FormatArgs("webm", "ultrafast", 23).codecArgs.Should().Contain("-cpu-used 5");
        }

        [Fact]
        public void Gif_HasNoCodecBlock_PaletteLivesInTheFilterChain()
        {
            var (ext, args) = VideoEncoder.FormatArgs("gif", "medium", 23);
            ext.Should().Be(".gif");
            args.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("avi")]      // stale/hand-edited settings must not build broken args
        [InlineData("MP4")]      // case-insensitive
        public void UnknownOrCasedFormats_FallBackToMp4(string? format)
        {
            VideoEncoder.FormatArgs(format, "medium", 23).ext.Should().Be(".mp4");
        }

        [Fact]
        public void Vp9Crf_IsClampedToItsValidRange()
        {
            VideoEncoder.FormatArgs("webm", "medium", 0).codecArgs.Should().Contain("-crf 10");
            VideoEncoder.FormatArgs("webm", "medium", 51).codecArgs.Should().Contain("-crf 55");
        }
    }

    /// <summary>
    /// The -frames:v output cap for a trimmed encode (VideoEncoder.ComputeOutputLimit): trim range,
    /// frame-skip ceiling, and held-last-frame clones. Off-by-one here clips or over-runs the trim.
    /// </summary>
    public class OutputLimitTests
    {
        [Fact]
        public void NoTrim_ReturnsZero_MeaningNoCap()
        {
            VideoEncoder.ComputeOutputLimit(0, 1, 0).Should().Be(0);
            VideoEncoder.ComputeOutputLimit(0, 3, 50).Should().Be(0);   // hold without a trim → still no cap
        }

        [Fact]
        public void PlainTrim_IsTheFrameCount()
        {
            VideoEncoder.ComputeOutputLimit(100, 1, 0).Should().Be(100);
        }

        [Theory]
        [InlineData(100, 3, 34)]   // ceil(100/3)
        [InlineData(99, 3, 33)]    // exact
        [InlineData(100, 2, 50)]
        [InlineData(1, 5, 1)]      // a single kept frame still counts
        public void FrameSkip_CeilingsTheKeptCount(int maxFrames, int everyNth, int expected)
        {
            VideoEncoder.ComputeOutputLimit(maxFrames, everyNth, 0).Should().Be(expected);
        }

        [Fact]
        public void HoldFrames_AreAddedOnTop_SoTheyAreNotClipped()
        {
            VideoEncoder.ComputeOutputLimit(100, 1, 10).Should().Be(110);
            VideoEncoder.ComputeOutputLimit(100, 3, 10).Should().Be(44);   // ceil(100/3)=34 + 10
        }
    }

    /// <summary>
    /// Interval ⇄ fps conversion + normalization (IntervalMath). Guards the round-trip bug where a
    /// typed 60 fps displayed as 59.88 because the interval was rounded to 4 dp (1/60 = 0.0167 → 59.88).
    /// </summary>
    public class IntervalMathTests
    {
        [Theory]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(24)]
        [InlineData(25)]
        [InlineData(30)]
        [InlineData(48)]
        [InlineData(50)]
        [InlineData(60)]
        [InlineData(75)]
        [InlineData(90)]
        [InlineData(100)]
        public void RoundNumberFps_RoundTripsToItself(int fps)
        {
            // fps → interval → fps must land exactly back (the 4-dp bug broke 60 and 30 specifically).
            var interval = IntervalMath.FpsToInterval(fps);
            IntervalMath.IntervalToFps(interval).Should().Be(fps);
        }

        [Fact]
        public void SixtyFps_DoesNotDriftTo59Point88()
        {
            IntervalMath.IntervalToFps(IntervalMath.FpsToInterval(60m)).Should().Be(60m);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void NonPositiveInputs_ReturnZero(int bad)
        {
            IntervalMath.FpsToInterval(bad).Should().Be(0m);
            IntervalMath.IntervalToFps(bad).Should().Be(0m);
        }

        [Fact]
        public void Normalize_ClampsBelowFloorUpToFloor()
        {
            IntervalMath.Normalize(0.001m, 0.01m).Should().Be(0.01m);
        }

        [Fact]
        public void Normalize_ClampsAboveCeilingDownTo3600()
        {
            IntervalMath.Normalize(99999m, 0.01m).Should().Be(3600m);
        }

        [Fact]
        public void Normalize_StripsTrailingZeros()
        {
            // A pasted "0.1000000000" (or an fps round-trip artefact) should display as a clean 0.1.
            IntervalMath.Normalize(0.1000000000m, 0.01m).Should().Be(0.1m);
        }

        [Fact]
        public void Normalize_KeepsSubTenthValues_ForVideoRate()
        {
            // 0.05s (20 fps) is valid — must survive normalization unclamped.
            IntervalMath.Normalize(0.05m, 0.01m).Should().Be(0.05m);
        }
    }

    /// <summary>
    /// Human-readable duration formatting (HumanFormat). Locks in both the fuzzy planning form and
    /// the precise timer form — the difference is the "record for 1h 30s → 1h" bug the precise form fixes.
    /// </summary>
    public class HumanFormatTests
    {
        [Theory]
        [InlineData(30, "30s")]
        [InlineData(60, "1m")]
        [InlineData(90, "1m 30s")]
        [InlineData(3600, "1h")]
        [InlineData(5400, "1h 30m")]
        [InlineData(0, "0s")]
        public void Duration_CompactPlanningForm(double seconds, string expected)
        {
            HumanFormat.Duration(seconds).Should().Be(expected);
        }

        [Fact]
        public void Duration_DropsStraySeconds_InHoursBranch_ByDesign()
        {
            // Fuzzy: 1h 0m 30s reads as "1h" for planning readouts (the precise form differs — below).
            HumanFormat.Duration(3630).Should().Be("1h");
        }

        [Theory]
        [InlineData(30, "30s")]
        [InlineData(90, "1m 30s")]
        [InlineData(3600, "1h")]
        [InlineData(3630, "1h 30s")]   // the bug fix: never collapse "1h 30s" to "1h"
        [InlineData(3661, "1h 1m 1s")]
        [InlineData(0, "0s")]
        public void DurationPrecise_NeverDropsANonzeroPart(double seconds, string expected)
        {
            HumanFormat.DurationPrecise(seconds).Should().Be(expected);
        }

        [Fact]
        public void DurationPrecise_ClampsNegativeToZero()
        {
            HumanFormat.DurationPrecise(-42).Should().Be("0s");
        }
    }
}
