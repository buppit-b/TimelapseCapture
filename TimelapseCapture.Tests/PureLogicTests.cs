using Xunit;
using FluentAssertions;
using System;
using System.Drawing;

namespace TimelapseCapture.Tests
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
}
