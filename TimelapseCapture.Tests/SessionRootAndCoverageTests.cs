using Xunit;
using FluentAssertions;
using System;
using System.Drawing;
using System.IO;

namespace TimelapseCapture.Tests
{
    /// <summary>
    /// SessionManager.FindSessionRoot — the resolver behind "drag anything from a session onto the
    /// window / pass it as an exe argument". It must find the owning session from any entry point
    /// inside it, and must NOT wander into unrelated folders.
    /// </summary>
    public class FindSessionRootTests
    {
        private static string MakeSession(out string root)
        {
            root = Path.Combine(Path.GetTempPath(), "tlc_root_" + Guid.NewGuid().ToString("N"));
            string captures = Path.Combine(root, "captures");
            return SessionManager.CreateNamedSession(captures, "TestSession", 1, null, "JPEG", 90);
        }

        [Fact]
        public void ResolvesFromTheSessionFolderItself()
        {
            string session = MakeSession(out string root);
            try { SessionManager.FindSessionRoot(session).Should().Be(session); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void ResolvesFromAFileInsideTheFramesSubfolder()
        {
            string session = MakeSession(out string root);
            try
            {
                string frames = Path.Combine(session, "frames");
                Directory.CreateDirectory(frames);
                string frame = Path.Combine(frames, "00001.jpg");
                File.WriteAllText(frame, "x");
                SessionManager.FindSessionRoot(frame).Should().Be(session);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void ResolvesFromTheSessionJsonFile()
        {
            string session = MakeSession(out string root);
            try { SessionManager.FindSessionRoot(Path.Combine(session, "session.json")).Should().Be(session); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void RefusesTheCapturesRoot_WalkingUpWouldFindTheWrongThing()
        {
            string session = MakeSession(out string root);
            try
            {
                // The captures root holds MANY sessions — resolving it to any one of them would be a guess.
                SessionManager.FindSessionRoot(Path.Combine(root, "captures")).Should().BeNull();
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void RefusesUnrelatedFoldersAndJunkInput()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tlc_plain_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                SessionManager.FindSessionRoot(dir).Should().BeNull();
                SessionManager.FindSessionRoot(null).Should().BeNull();
                SessionManager.FindSessionRoot("   ").Should().BeNull();
                SessionManager.FindSessionRoot(Path.Combine(dir, "no-such-file.txt")).Should().BeNull();
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }

    /// <summary>
    /// OverlayRenderer.ResolveTokens — the overlay text token substitution, with a fixed clock.
    /// </summary>
    public class OverlayTokenTests
    {
        private static readonly DateTime T = new(2026, 7, 3, 14, 5, 9);

        [Fact]
        public void ResolvesAllBuiltInTokens()
        {
            OverlayRenderer.ResolveTokens("{datetime}", T).Should().Be("2026-07-03 14:05:09");
            OverlayRenderer.ResolveTokens("{date}", T).Should().Be("2026-07-03");
            OverlayRenderer.ResolveTokens("{time}", T).Should().Be("14:05:09");
            OverlayRenderer.ResolveTokens("{time12}", T).Should().Be("2:05:09 PM");
        }

        [Fact]
        public void CustomFormatToken_AndLiteralsAroundIt()
        {
            OverlayRenderer.ResolveTokens("day {t:ddd}!", T).Should().Be("day Fri!");
        }

        [Fact]
        public void InvalidCustomFormat_IsLeftVerbatim_NotAThrow()
        {
            // A format that makes DateTime.ToString THROW ("%" alone is invalid) must come back
            // verbatim, not crash — a bad format must never break the capture loop.
            OverlayRenderer.ResolveTokens("{t:%}", T).Should().Be("{t:%}");
        }

        [Fact]
        public void NullAndPlainText()
        {
            OverlayRenderer.ResolveTokens(null!, T).Should().Be("");
            OverlayRenderer.ResolveTokens("plain label", T).Should().Be("plain label");
        }
    }

    /// <summary>
    /// WindowEnumerator.CoversArea — the pure math behind the fullscreen keep-on-top skip
    /// (the 0.9.4 alt-tab lockup fix). A fullscreen-sized window must be detected as covering
    /// its monitor; a normal window must not.
    /// </summary>
    public class CoversAreaTests
    {
        private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);

        [Fact]
        public void ExactFullscreenCovers() =>
            WindowEnumerator.CoversArea(new Rectangle(0, 0, 1920, 1080), Monitor, 0.98).Should().BeTrue();

        [Fact]
        public void OversizedWindowStillCovers() =>
            // Maximized windows hang a few px past the monitor edge — the OVERLAP is what counts.
            WindowEnumerator.CoversArea(new Rectangle(-8, -8, 1936, 1096), Monitor, 0.98).Should().BeTrue();

        [Fact]
        public void NinetyPercentWindowDoesNotCover() =>
            WindowEnumerator.CoversArea(new Rectangle(0, 0, 1920, 972), Monitor, 0.98).Should().BeFalse();

        [Fact]
        public void HalfScreenSnapDoesNotCover() =>
            WindowEnumerator.CoversArea(new Rectangle(0, 0, 960, 1080), Monitor, 0.98).Should().BeFalse();

        [Fact]
        public void DisjointWindowDoesNotCover() =>
            // Fullscreen-sized but on ANOTHER monitor — no overlap with this one.
            WindowEnumerator.CoversArea(new Rectangle(1920, 0, 1920, 1080), Monitor, 0.98).Should().BeFalse();

        [Fact]
        public void EmptyMonitorNeverCovers() =>
            WindowEnumerator.CoversArea(new Rectangle(0, 0, 100, 100), Rectangle.Empty, 0.98).Should().BeFalse();
    }
}
