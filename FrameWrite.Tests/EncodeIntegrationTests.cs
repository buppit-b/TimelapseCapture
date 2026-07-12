using Xunit;
using FluentAssertions;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FrameWrite.Tests
{
    /// <summary>
    /// End-to-end encode smoke tests: write synthetic frames, run the REAL VideoEncoder + the bundled
    /// ffmpeg, and verify the output video's frame count with ffprobe. This is the coverage pure-logic
    /// tests can't give — it exercises the actual ffmpeg argument construction (image2 pattern, the
    /// frame-skip filter, trim ranges, and a '%' in the path). Skips (passes) if ffmpeg isn't found,
    /// so CI without ffmpeg stays green; a dev build has it bundled, so it runs in the normal loop.
    /// </summary>
    public class EncodeIntegrationTests
    {
        private static readonly string? Ffmpeg = FindFfmpeg();

        private static string? FindFfmpeg()
        {
            var found = FfmpegRunner.FindFfmpeg(null);
            if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;
            // Fall back to the copy bundled in the WPF build output (walk up to the repo root).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FrameWrite.sln")))
                dir = dir.Parent;
            var wpfBin = dir == null ? null : Path.Combine(dir.FullName, "FrameWrite.Wpf", "bin");
            return wpfBin != null && Directory.Exists(wpfBin)
                ? Directory.EnumerateFiles(wpfBin, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }

        private static string MakeSession(out string root, string prefix)
        {
            root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            return SessionManager.CreateNamedSession(Path.Combine(root, "captures"), "EncodeTest", 1, null, "JPEG", 90);
        }

        private static void WriteFrames(string sessionFolder, int n, string ext)
        {
            string frames = SessionManager.GetFramesFolder(sessionFolder);
            Directory.CreateDirectory(frames);
            var fmt = ext == "png" ? ImageFormat.Png : ImageFormat.Jpeg;
            for (int i = 1; i <= n; i++)
            {
                using var bmp = new Bitmap(64, 48);   // even dims for yuv420p
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(255, i * 7 % 256, 60, 120));
                bmp.Save(Path.Combine(frames, $"{i:D5}.{ext}"), fmt);
            }
        }

        private static string Probe(string mp4, string entries)
        {
            string ffprobe = Path.Combine(Path.GetDirectoryName(Ffmpeg!)!, "ffprobe.exe");
            if (!File.Exists(ffprobe)) return "";
            var psi = new ProcessStartInfo(ffprobe,
                $"-v error -count_frames -select_streams v -show_entries {entries} -of csv=p=0 \"{mp4}\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(15000);
            return outp;
        }

        private static int CountFrames(string mp4)
            => int.TryParse(Probe(mp4, "stream=nb_read_frames"), out int c) ? c : -1;

        private static (int w, int h) VideoSize(string mp4)
        {
            var parts = Probe(mp4, "stream=width,height").Split(',');
            return parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
                ? (w, h) : (-1, -1);
        }

        [Fact]
        public async Task Cancellation_KillsTheFfmpegProcess()
        {
            if (Ffmpeg == null) return;
            // A deliberately slow run (30s of synthetic video to the null muxer). Cancelling must kill
            // the process and resolve the task promptly — this is what stops a closed app from leaving
            // an invisible ffmpeg running.
            using var cts = new System.Threading.CancellationTokenSource();
            // -re: consume the input at its native (real-time) rate — without it lavfi renders the
            // whole 30s in a few hundred ms and ffmpeg exits before the cancel fires.
            var run = FfmpegRunner.RunFfmpegAsync(Ffmpeg,
                "-re -f lavfi -i testsrc=duration=30:size=320x240:rate=30 -f null -", cts.Token);
            await Task.Delay(500);   // let ffmpeg spin up
            cts.Cancel();

            var finished = await Task.WhenAny(run, Task.Delay(5000));
            finished.Should().Be(run, "cancel must kill the ffmpeg process, not leave it running");
            (await run).exitCode.Should().NotBe(0, "a killed run must not report success");
        }

        [Fact]
        public async Task Encodes_AllFrames_Exactly()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_enc_");
            try
            {
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(30);
                // Provenance: the metadata tags must land in the container (mp4 stores them as text).
                var bytes = File.ReadAllBytes(r.OutputPath!);
                System.Text.Encoding.ASCII.GetString(bytes).Should().Contain("FrameWrite",
                    "encodes carry open provenance metadata");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task EncodeToDuration_ProducesTheRequestedLength()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_dur_");
            try
            {
                // 90 frames aimed at exactly 3 seconds → 30 fps → 90 output frames.
                WriteFrames(session, 90, "jpg");
                double fps = VideoEncoder.FpsForDuration(90, 1, 3.0);
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, fps, "ultrafast", 23);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(90);

                // The container duration should land on ~3s (allow codec/container rounding slack).
                double.TryParse(Probe(r.OutputPath!, "format=duration"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out double seconds).Should().BeTrue();
                seconds.Should().BeApproximately(3.0, 0.3, "encode-to-duration must hit the requested length");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task FrameSkip_KeepsOneInN()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_enc_");
            try
            {
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23, everyNth: 3);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(10);   // 30 → keep 1 in 3
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task HoldLastFrame_AppendsClonedFrames()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_enc_");
            try
            {
                WriteFrames(session, 30, "jpg");
                // 30 frames @ 30fps = 1s; hold the last frame 2s → +60 frames → 90 total.
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23, holdLastSeconds: 2);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(90);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Trim_EncodesOnlyTheRange()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_enc_");
            try
            {
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23, startFrame: 6, maxFrames: 15);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(15);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task CropAtEncode_ProducesCroppedDimensions()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_enc_");
            try
            {
                WriteFrames(session, 10, "jpg");   // frames are 64×48
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23,
                    crop: new Rectangle(10, 8, 32, 24));
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(10);
                VideoSize(r.OutputPath!).Should().Be((32, 24));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task CropAtEncode_OversizedCropIsClampedNotFatal()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_enc_");
            try
            {
                WriteFrames(session, 5, "jpg");   // 64×48
                // A stale/foreign crop bigger than the frame must clamp, not fail the encode.
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23,
                    crop: new Rectangle(40, 30, 500, 500));
                r.Success.Should().BeTrue(r.Error);
                VideoSize(r.OutputPath!).Should().Be((24, 18));   // 64-40=24, 48-30=18 (both already even)
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void DestructiveCropFrames_RewritesEveryFrameSmaller()
        {
            string session = MakeSession(out string root, "tlc_crop_");
            try
            {
                WriteFrames(session, 8, "jpg");   // 64×48
                int done = SessionManager.CropFrames(session, new Rectangle(10, 8, 32, 24), 90);
                done.Should().Be(8);
                foreach (var f in SessionManager.GetFrameFiles(session))
                {
                    using var img = Image.FromFile(f);
                    img.Size.Should().Be(new Size(32, 24));
                }
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task PercentInPath_StillEncodes()
        {
            if (Ffmpeg == null) return;
            // Regression for the image2 %-path fix: a '%' in the frames path must not corrupt the -i pattern.
            string session = MakeSession(out string root, "tlc_enc_50%_");
            try
            {
                WriteFrames(session, 12, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(12);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
