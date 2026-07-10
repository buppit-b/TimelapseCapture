using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace TimelapseCapture.Tests
{
    // The retroactive overlay bake's contract: pixels change, per-frame timestamps come from each
    // FILE's write time (its capture moment), and that write time survives the rewrite — plus the
    // same write-time preservation on the other on-disk rewrite ops.
    public class OverlayBakeTests
    {
        private static string NewSessionDir(out string frames)
        {
            string dir = Path.Combine(Path.GetTempPath(), "tlc_bake_" + Guid.NewGuid().ToString("N"));
            frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            return dir;
        }

        private static void WriteFrame(string path, DateTime capturedAt, int w = 120, int h = 80)
        {
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
                bmp.Save(path, path.EndsWith(".png") ? ImageFormat.Png : ImageFormat.Jpeg);
            }
            File.SetLastWriteTime(path, capturedAt);
        }

        [Fact]
        public void Bake_DrawsPerFrameTimes_AndPreservesWriteTimes()
        {
            string dir = NewSessionDir(out string frames);
            try
            {
                var t1 = new DateTime(2025, 1, 1, 10, 0, 0);
                var t2 = new DateTime(2025, 1, 1, 11, 11, 11);
                string f1 = Path.Combine(frames, "00001.png"), f2 = Path.Combine(frames, "00002.png");
                WriteFrame(f1, t1);
                WriteFrame(f2, t2);
                byte[] orig1 = File.ReadAllBytes(f1), orig2 = File.ReadAllBytes(f2);

                var progress = new List<(int done, int total)>();
                int baked = SessionManager.BakeOverlay(dir,
                    new OverlayConfig { Text = "{time}", FontSize = 12, FontFamily = "Arial", Position = 0 },
                    progress: (i, n) => progress.Add((i, n)));

                baked.Should().Be(2);
                byte[] now1 = File.ReadAllBytes(f1), now2 = File.ReadAllBytes(f2);
                now1.Should().NotEqual(orig1, "the overlay must actually land in the pixels");
                now2.Should().NotEqual(orig2);
                // Identical black frames + per-file times → the rendered text (10:00:00 vs 11:11:11)
                // is the only difference. If these matched, the bake used one clock for everything.
                now1.Should().NotEqual(now2, "each frame must be stamped with ITS OWN capture time");
                File.GetLastWriteTime(f1).Should().Be(t1, "the capture moment must survive the rewrite");
                File.GetLastWriteTime(f2).Should().Be(t2);
                progress.Should().Contain((2, 2));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Bake_EmptyText_TouchesNothing()
        {
            string dir = NewSessionDir(out string frames);
            try
            {
                var t = new DateTime(2025, 2, 3, 4, 5, 6);
                string f = Path.Combine(frames, "00001.png");
                WriteFrame(f, t);
                byte[] orig = File.ReadAllBytes(f);

                SessionManager.BakeOverlay(dir, new OverlayConfig { Text = "  " }).Should().Be(0);

                File.ReadAllBytes(f).Should().Equal(orig);
                File.GetLastWriteTime(f).Should().Be(t);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void CropFrames_PreservesWriteTime()
        {
            string dir = NewSessionDir(out string frames);
            try
            {
                var t = new DateTime(2025, 3, 4, 5, 6, 7);
                string f = Path.Combine(frames, "00001.jpg");
                WriteFrame(f, t);

                SessionManager.CropFrames(dir, new Rectangle(0, 0, 60, 40)).Should().Be(1);

                using (var img = Image.FromFile(f)) img.Size.Should().Be(new Size(60, 40));
                File.GetLastWriteTime(f).Should().Be(t, "cropping must not destroy the capture-moment record");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Renderer_HonoursColourAndOpacity()
        {
            using var bmp = new Bitmap(160, 90);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);

            // Solid green box, red text, top-left corner.
            OverlayRenderer.Draw(bmp, new OverlayConfig
            {
                Text = "X",
                FontSize = 20,
                FontFamily = "Arial",
                Position = 0,
                TextColor = "#FF0000",
                TextOpacity = 100,
                BackColor = "#00FF00",
                BackOpacity = 100,
            });

            // The box's left padding (x-5 from text x=8) is pure backdrop — no glyph there.
            bmp.GetPixel(4, 10).ToArgb().Should().Be(Color.FromArgb(255, 0, 255, 0).ToArgb());
            bool redSeen = false;
            for (int y = 0; y < bmp.Height && !redSeen; y++)
                for (int x = 0; x < bmp.Width && !redSeen; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    redSeen = p.R > 180 && p.G < 90 && p.B < 90;
                }
            redSeen.Should().BeTrue("the text must render in the chosen colour");
        }

        [Fact]
        public void Renderer_ZeroBackOpacity_DrawsNoBox()
        {
            using var bmp = new Bitmap(160, 90);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);

            OverlayRenderer.Draw(bmp, new OverlayConfig
            {
                Text = "X",
                FontSize = 20,
                FontFamily = "Arial",
                Position = 0,
                BackOpacity = 0,
            });

            // The padding pixel that the box would cover stays untouched background.
            bmp.GetPixel(4, 10).ToArgb().Should().Be(Color.FromArgb(255, 0, 0, 0).ToArgb());
        }

        [Fact]
        public void BackupSession_CopiesFramesAndSessionJson_SkipsOutput_PreservesTimes()
        {
            string dir = NewSessionDir(out string frames);
            try
            {
                var t = new DateTime(2025, 5, 6, 7, 8, 9);
                WriteFrame(Path.Combine(frames, "00001.png"), t);
                File.WriteAllText(Path.Combine(dir, "session.json"), "{\"Name\":\"test\",\"Active\":false}");
                Directory.CreateDirectory(Path.Combine(dir, "output"));
                File.WriteAllText(Path.Combine(dir, "output", "video.mp4"), "not really a video");

                string dest = SessionManager.BackupSession(dir);

                Directory.Exists(dest).Should().BeTrue();
                File.Exists(Path.Combine(dest, "session.json")).Should().BeTrue();
                File.Exists(Path.Combine(dest, "frames", "00001.png")).Should().BeTrue();
                Directory.Exists(Path.Combine(dest, "output")).Should().BeFalse("videos are products, not session data");
                File.GetLastWriteTime(Path.Combine(dest, "frames", "00001.png"))
                    .Should().Be(t, "the capture-moment record must travel with the backup");

                // A second backup must land in its own folder, never overwrite the first.
                string dest2 = SessionManager.BackupSession(dir);
                dest2.Should().NotBe(dest);
                Directory.Exists(dest2).Should().BeTrue();

                try { Directory.Delete(dest, true); } catch { }
                try { Directory.Delete(dest2, true); } catch { }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void ConvertFrames_CarriesWriteTimeAcross()
        {
            string dir = NewSessionDir(out string frames);
            try
            {
                var t = new DateTime(2025, 4, 5, 6, 7, 8);
                WriteFrame(Path.Combine(frames, "00001.jpg"), t);

                SessionManager.ConvertFramesToFormat(dir, "png").Should().Be(1);

                File.GetLastWriteTime(Path.Combine(frames, "00001.png")).Should().Be(t);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
