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
