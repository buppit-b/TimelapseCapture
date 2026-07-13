using Xunit;
using FluentAssertions;
using System;
using System.Drawing;
using System.IO;

namespace FrameWrite.Tests
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
    /// PresetManager merge logic — the guarantee that applying a preset swaps the capture/look setup
    /// but NEVER the user's output folder, safety caps, or UI state.
    /// </summary>
    public class PresetMergeTests
    {
        [Fact]
        public void ApplyOnto_TakesCarriedFieldsFromPreset()
        {
            var preset = new CaptureSettings { IntervalSecondsExact = 5m, Format = "PNG", JpegQuality = 95, Theme = "Ocean", EncodeEveryNth = 3 };
            var live = new CaptureSettings { IntervalSecondsExact = 1m, Format = "JPEG", Theme = "Terminal" };
            var merged = PresetManager.ApplyOnto(preset, live);
            merged.IntervalSecondsExact.Should().Be(5m);
            merged.Format.Should().Be("PNG");
            merged.JpegQuality.Should().Be(95);
            merged.Theme.Should().Be("Ocean");
            merged.EncodeEveryNth.Should().Be(3);
        }

        [Fact]
        public void ApplyOnto_PreservesExcludedIdentityAndSafetyFromLive()
        {
            var preset = new CaptureSettings
            {
                SaveFolder = @"C:\SomeoneElse\folder", FfmpegPath = @"C:\wrong\ffmpeg.exe",
                AutoStopOnLowDisk = false, LowDiskStopMB = 1, MaxDurationEnabled = true,
                StopAtStorageEnabled = true, SimpleMode = true, FirstRunCompleted = true,
                HotkeysEnabled = true, HotkeyVk = 0x41, NotifyOnFinish = false, StopAtTarget = true,
            };
            var live = new CaptureSettings
            {
                SaveFolder = @"D:\MyArt", FfmpegPath = @"D:\ff\ffmpeg.exe",
                AutoStopOnLowDisk = true, LowDiskStopMB = 500, MaxDurationEnabled = false,
                StopAtStorageEnabled = false, SimpleMode = false, FirstRunCompleted = false,
                HotkeysEnabled = false, HotkeyVk = 0x78, NotifyOnFinish = true, StopAtTarget = false,
            };
            var merged = PresetManager.ApplyOnto(preset, live);
            // Everything the user owns about THIS machine/session/safety stays put:
            merged.SaveFolder.Should().Be(@"D:\MyArt");
            merged.FfmpegPath.Should().Be(@"D:\ff\ffmpeg.exe");
            merged.AutoStopOnLowDisk.Should().BeTrue();
            merged.LowDiskStopMB.Should().Be(500);
            merged.MaxDurationEnabled.Should().BeFalse();
            merged.StopAtStorageEnabled.Should().BeFalse();
            merged.SimpleMode.Should().BeFalse();
            merged.FirstRunCompleted.Should().BeFalse();
            merged.HotkeysEnabled.Should().BeFalse();
            merged.HotkeyVk.Should().Be(0x78);
            merged.NotifyOnFinish.Should().BeTrue();
            merged.StopAtTarget.Should().BeFalse();
        }

        [Fact]
        public void ApplyOnto_NeverCarries_KeymapOrUserGlobalState()
        {
            var preset = new CaptureSettings
            {
                Hotkeys = new() { new HotkeyBinding { Action = "startstop", Modifiers = 1, Vk = 0x41 } },
                SuppressedPrompts = new() { "some-prompt-from-another-machine" },
                EncodePanelExpanded = true,
                SmartPanelExpanded = true,
            };
            var live = new CaptureSettings
            {
                Hotkeys = new() { new HotkeyBinding { Action = "startstop", Modifiers = 6, Vk = 0x78 } },
                SuppressedPrompts = new() { "stop-active-timer" },
                EncodePanelExpanded = false,
                SmartPanelExpanded = false,
            };
            var merged = PresetManager.ApplyOnto(preset, live);
            // A preset must never rebind the user's keys, resurrect/dismiss confirmations, or fold panels.
            merged.Hotkeys.Should().BeEquivalentTo(live.Hotkeys);
            merged.SuppressedPrompts.Should().BeEquivalentTo(live.SuppressedPrompts);
            merged.EncodePanelExpanded.Should().BeFalse();
            merged.SmartPanelExpanded.Should().BeFalse();
        }

        [Fact]
        public void StripIdentity_ClearsMachinePathsButKeepsLook()
        {
            var live = new CaptureSettings { SaveFolder = @"D:\MyArt", FfmpegPath = @"D:\ff.exe", Theme = "Synth", IntervalSecondsExact = 7m };
            var stripped = PresetManager.StripIdentity(live);
            stripped.SaveFolder.Should().BeNull();       // never leak a personal path into a shareable preset
            stripped.FfmpegPath.Should().BeNull();
            stripped.Theme.Should().Be("Synth");         // look is carried
            stripped.IntervalSecondsExact.Should().Be(7m);
        }
    }

    /// <summary>
    /// AppPaths.ResolveDataDir — the portable-vs-installed data-directory rule. Getting this wrong
    /// either breaks dev setups (settings suddenly elsewhere) or breaks installs (writes into
    /// Program Files) — so the pure rule is pinned by tests.
    /// </summary>
    public class AppPathsTests
    {
        [Fact]
        public void PortableSettingsNextToExe_StaysPortable() =>
            AppPaths.ResolveDataDir(@"C:\Apps\FW", @"C:\Users\u\AppData\Roaming", portableSettingsExist: true)
                .Should().Be(@"C:\Apps\FW");

        [Fact]
        public void FreshInstall_UsesAppData() =>
            AppPaths.ResolveDataDir(@"C:\Program Files\FW", @"C:\Users\u\AppData\Roaming", portableSettingsExist: false)
                .Should().Be(@"C:\Users\u\AppData\Roaming\FrameWrite");

        [Fact]
        public void MissingAppData_FallsBackToExeDir() =>
            AppPaths.ResolveDataDir(@"C:\Apps\FW", "", portableSettingsExist: false)
                .Should().Be(@"C:\Apps\FW");
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
        public void FrameToken_SubstitutesTheFrameNumber()
        {
            OverlayRenderer.ResolveTokens("frame {frame}", T, 42).Should().Be("frame 42");
            OverlayRenderer.ResolveTokens("{frame} @ {date}", T, 7).Should().Be("7 @ 2026-07-03");
            OverlayRenderer.ResolveTokens("no token", T, 99).Should().Be("no token");
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
    /// VideoEncoder.ClampCrop — the crop-at-encode safety math (on-frame + even dims).
    /// </summary>
    public class ClampCropTests
    {
        private static readonly Size Frame = new(1920, 1080);

        [Fact]
        public void InBoundsEvenCrop_Unchanged() =>
            VideoEncoder.ClampCrop(new Rectangle(100, 50, 800, 600), Frame)
                .Should().Be(new Rectangle(100, 50, 800, 600));

        [Fact]
        public void OddDimensions_ForcedEven() =>
            VideoEncoder.ClampCrop(new Rectangle(0, 0, 801, 601), Frame)
                .Should().Be(new Rectangle(0, 0, 800, 600));

        [Fact]
        public void OverhangingCrop_ClampsToFrame() =>
            VideoEncoder.ClampCrop(new Rectangle(1800, 1000, 500, 500), Frame)
                .Should().Be(new Rectangle(1800, 1000, 120, 80));

        [Fact]
        public void FullyOutside_IsDegenerate() =>
            VideoEncoder.ClampCrop(new Rectangle(5000, 5000, 100, 100), Frame)
                .Width.Should().BeLessThan(2);

        [Fact]
        public void NegativeOrigin_ClampsToZero() =>
            VideoEncoder.ClampCrop(new Rectangle(-50, -50, 200, 200), Frame)
                .Should().Be(new Rectangle(0, 0, 150, 150));
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

    /// <summary>
    /// SessionManager.ValidateSessionSettings — the guard that keeps a session's frames uniform (same
    /// WxH + format + JPEG quality) so the image2 encode works. Once a session has frames, region /
    /// format / quality can't change under it; an empty session is unconstrained.
    /// </summary>
    public class ValidateSessionSettingsTests
    {
        static SessionInfo Session(long frames, Rectangle? region, string? format, int quality) =>
            new() { FramesCaptured = frames, CaptureRegion = region, ImageFormat = format, JpegQuality = quality };

        static readonly Rectangle R = new(0, 0, 800, 600);
        static readonly Rectangle Other = new(0, 0, 1920, 1080);

        [Fact]
        public void EmptySession_IsAlwaysValid()
        {
            // No frames yet → any region/format/quality is fine (nothing to conflict with).
            SessionManager.ValidateSessionSettings(Session(0, null, null, 90), R, "JPEG", 85).Should().BeTrue();
            SessionManager.ValidateSessionSettings(Session(0, Other, "PNG", 90), R, "JPEG", 85).Should().BeTrue();
        }

        [Fact]
        public void MatchingRegionFormatQuality_IsValid() =>
            SessionManager.ValidateSessionSettings(Session(10, R, "JPEG", 85), R, "JPEG", 85).Should().BeTrue();

        [Fact]
        public void RegionChange_WithFrames_IsRejected() =>
            // Changing the region mid-session breaks frame uniformity (image2 needs identical WxH).
            SessionManager.ValidateSessionSettings(Session(10, R, "JPEG", 85), Other, "JPEG", 85).Should().BeFalse();

        [Fact]
        public void FormatChange_WithFrames_IsRejected() =>
            SessionManager.ValidateSessionSettings(Session(10, R, "JPEG", 85), R, "PNG", 85).Should().BeFalse();

        [Fact]
        public void JpegQualityChange_WithFrames_IsRejected() =>
            SessionManager.ValidateSessionSettings(Session(10, R, "JPEG", 85), R, "JPEG", 70).Should().BeFalse();

        [Fact]
        public void PngIgnoresQuality() =>
            // Quality only matters for JPEG — a PNG session isn't invalidated by a different quality number.
            SessionManager.ValidateSessionSettings(Session(10, R, "PNG", 90), R, "PNG", 30).Should().BeTrue();

        [Fact]
        public void NullRegionWithFrames_SkipsRegionCheck_TheKnownRecoverableState() =>
            // FramesCaptured>0 && CaptureRegion==null is the documented corruption state — the region
            // check is skipped (HasValue false), so validation passes on format alone; repair is on load.
            SessionManager.ValidateSessionSettings(Session(10, null, "JPEG", 85), R, "JPEG", 85).Should().BeTrue();
    }
}
