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
}
