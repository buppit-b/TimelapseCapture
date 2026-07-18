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

        private static string MakeSizedSession(string root, string name, int n, int w, int h, string ext)
        {
            string session = SessionManager.CreateNamedSession(Path.Combine(root, "captures"), name, 1, null, ext == "png" ? "PNG" : "JPEG", 90);
            string frames = SessionManager.GetFramesFolder(session);
            Directory.CreateDirectory(frames);
            var fmt = ext == "png" ? ImageFormat.Png : ImageFormat.Jpeg;
            for (int i = 1; i <= n; i++)
            {
                using var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(255, i * 9 % 256, 90, 60));
                bmp.Save(Path.Combine(frames, $"{i:D5}.{ext}"), fmt);
            }
            return session;
        }

        [Fact]
        public void Merge_Copy_MakesOneContinuableSession_AndKeepsSources()
        {
            string root = Path.Combine(Path.GetTempPath(), "tlc_merge_" + Guid.NewGuid().ToString("N"));
            try
            {
                string a = MakeSizedSession(root, "older", 10, 64, 48, "jpg");
                string b = MakeSizedSession(root, "newer", 5, 64, 48, "jpg");
                // Distinct capture times + accumulated time — the merged identity must sum/adopt them.
                var sa = SessionManager.LoadSession(a)!; sa.StartTime = new DateTime(2026, 1, 1); sa.TotalCaptureSeconds = 100; sa.Active = false; SessionManager.SaveSession(a, sa);
                var sb = SessionManager.LoadSession(b)!; sb.StartTime = new DateTime(2026, 2, 1); sb.TotalCaptureSeconds = 50; sb.Active = false; SessionManager.SaveSession(b, sb);

                string merged = SessionManager.MergeSessions(new[] { b, a }, Path.Combine(root, "captures"), move: false);

                var frames = SessionManager.GetFrameFiles(merged);
                frames.Length.Should().Be(15);
                Path.GetFileName(frames[0]).Should().Be("00001.jpg");
                Path.GetFileName(frames[14]).Should().Be("00015.jpg", "renumbering must be gapless");
                var m = SessionManager.LoadSession(merged)!;
                m.FramesCaptured.Should().Be(15);
                m.TotalCaptureSeconds.Should().Be(150, "capture time is the sum of the sources");
                m.StartTime.Should().Be(new DateTime(2026, 1, 1), "the story starts at the EARLIEST source");
                m.Active.Should().BeFalse();
                SessionManager.GetFrameFiles(a).Length.Should().Be(10, "copy mode leaves sources intact");
                SessionManager.GetFrameFiles(b).Length.Should().Be(5);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void Merge_Move_ConsumesSources_AndCarriesTheirOutputs()
        {
            string root = Path.Combine(Path.GetTempPath(), "tlc_mergemv_" + Guid.NewGuid().ToString("N"));
            try
            {
                string a = MakeSizedSession(root, "older", 8, 64, 48, "jpg");
                string b = MakeSizedSession(root, "newer", 4, 64, 48, "jpg");
                foreach (var f in new[] { a, b }) { var s = SessionManager.LoadSession(f)!; s.Active = false; SessionManager.SaveSession(f, s); }
                // A source's encoded videos are user artifacts — they must survive the consume.
                string outFile = Path.Combine(SessionManager.GetOutputFolder(a), "timelapse_old.mp4");
                Directory.CreateDirectory(SessionManager.GetOutputFolder(a));
                File.WriteAllText(outFile, "video");

                string merged = SessionManager.MergeSessions(new[] { a, b }, Path.Combine(root, "captures"), move: true);

                SessionManager.GetFrameFiles(merged).Length.Should().Be(12);
                Directory.Exists(a).Should().BeFalse("a fully-drained source folder is removed");
                Directory.Exists(b).Should().BeFalse();
                File.Exists(Path.Combine(SessionManager.GetOutputFolder(merged), "timelapse_old.mp4"))
                    .Should().BeTrue("source output videos are carried into the merged session");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void Merge_DeduplicatesRepeatedSourceEntries()
        {
            // A duplicated entry would move the same frames twice and throw mid-merge — the belt
            // normalizes + dedupes, so {a, a, b} behaves exactly like {a, b}.
            string root = Path.Combine(Path.GetTempPath(), "tlc_mergedup_" + Guid.NewGuid().ToString("N"));
            try
            {
                string a = MakeSizedSession(root, "one", 6, 64, 48, "jpg");
                string b = MakeSizedSession(root, "two", 4, 64, 48, "jpg");
                foreach (var f in new[] { a, b }) { var s = SessionManager.LoadSession(f)!; s.Active = false; SessionManager.SaveSession(f, s); }

                string merged = SessionManager.MergeSessions(new[] { a, a, b }, Path.Combine(root, "captures"), move: false);
                SessionManager.GetFrameFiles(merged).Length.Should().Be(10, "each source counts once");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void Merge_RefusesMismatchedSizes_AndMixedFormats()
        {
            string root = Path.Combine(Path.GetTempPath(), "tlc_mergebad_" + Guid.NewGuid().ToString("N"));
            try
            {
                string a = MakeSizedSession(root, "big", 3, 64, 48, "jpg");
                string b = MakeSizedSession(root, "small", 3, 32, 24, "jpg");
                string c = MakeSizedSession(root, "png", 3, 64, 48, "png");
                foreach (var f in new[] { a, b, c }) { var s = SessionManager.LoadSession(f)!; s.Active = false; SessionManager.SaveSession(f, s); }

                string captures = Path.Combine(root, "captures");
                Action sizes = () => SessionManager.MergeSessions(new[] { a, b }, captures, move: true);
                sizes.Should().Throw<InvalidOperationException>().WithMessage("*different frame sizes*");
                Action formats = () => SessionManager.MergeSessions(new[] { a, c }, captures, move: true);
                formats.Should().Throw<InvalidOperationException>().WithMessage("*different frame formats*");
                SessionManager.GetFrameFiles(a).Length.Should().Be(3, "validation failures must touch nothing");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Combine_JoinsSessions_LetterboxingOntoTheLargest()
        {
            if (Ffmpeg == null) return;
            string root = Path.Combine(Path.GetTempPath(), "tlc_comb_" + Guid.NewGuid().ToString("N"));
            try
            {
                // Different sizes AND different frame formats — each session is its own input, so
                // only within-session uniformity is required. 20 + 10 frames → 30 out, canvas 64×48.
                string a = MakeSizedSession(root, "older", 20, 64, 48, "jpg");
                string b = MakeSizedSession(root, "newer", 10, 32, 24, "png");
                var r = await VideoEncoder.CombineAsync(Ffmpeg, new[] { a, b }, 30, "ultrafast", 23);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(30);
                VideoSize(r.OutputPath!).Should().Be((64, 48));
                Path.GetDirectoryName(r.OutputPath).Should().Be(SessionManager.GetOutputFolder(a),
                    "the combined video lands in the FIRST session's output folder");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Combine_SpeedUp_ThinsTheJoinedTimeline()
        {
            if (Ffmpeg == null) return;
            string root = Path.Combine(Path.GetTempPath(), "tlc_comb2_" + Guid.NewGuid().ToString("N"));
            try
            {
                string a = MakeSizedSession(root, "s1", 15, 64, 48, "jpg");
                string b = MakeSizedSession(root, "s2", 15, 64, 48, "jpg");
                // Skip runs on the timeline AFTER the join: 30 combined frames, keep 1 in 3 → 10.
                var r = await VideoEncoder.CombineAsync(Ffmpeg, new[] { a, b }, 30, "ultrafast", 23, everyNth: 3);
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(10);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Archive_RoundTrip_Jpeg_RestoresEveryFrame()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_arch_");
            try
            {
                WriteFrames(session, 30, "jpg");
                SessionManager.MarkSessionInactive(session);   // only finished sessions archive

                var a = await SessionArchiver.ArchiveAsync(Ffmpeg, session);
                a.Success.Should().BeTrue(a.Error);
                a.Frames.Should().Be(30);
                File.Exists(SessionArchiver.GetArchivePath(session)).Should().BeTrue();
                SessionManager.GetFrameFiles(session).Should().BeEmpty("frames are replaced by the archive");
                var info = SessionManager.LoadSession(session)!;
                info.Archived.Should().BeTrue();
                info.ArchivedFrames.Should().Be(30);
                info.FramesCaptured.Should().Be(30, "metadata must survive for the picker");

                var u = await SessionArchiver.UnarchiveAsync(Ffmpeg, session);
                u.Success.Should().BeTrue(u.Error);
                var frames = SessionManager.GetFrameFiles(session);
                frames.Length.Should().Be(30);
                Path.GetFileName(frames[0]).Should().Be("00001.jpg", "restore renumbers gapless from 1");
                File.Exists(SessionArchiver.GetArchivePath(session)).Should().BeFalse("the archive is consumed");
                info = SessionManager.LoadSession(session)!;
                info.Archived.Should().BeFalse();
                info.FramesCaptured.Should().Be(30);
                using var img = Image.FromFile(frames[10]);
                img.Size.Should().Be(new Size(64, 48), "restored frames keep the session's dimensions");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Archive_RoundTrip_Png_IsPixelIdentical()
        {
            if (Ffmpeg == null) return;
            // PNG captures are lossless — the archive path promises MATHEMATICAL losslessness
            // (libx264rgb -qp 0), so a restored frame must match the original pixel for pixel.
            string session = MakeSession(out string root, "tlc_archpng_");
            try
            {
                WriteFrames(session, 8, "png");
                SessionManager.MarkSessionInactive(session);
                var beforePixels = ReadPixels(SessionManager.GetFrameFiles(session)[3]);

                var a = await SessionArchiver.ArchiveAsync(Ffmpeg, session);
                a.Success.Should().BeTrue(a.Error);
                var u = await SessionArchiver.UnarchiveAsync(Ffmpeg, session);
                u.Success.Should().BeTrue(u.Error);

                var frames = SessionManager.GetFrameFiles(session);
                frames.Length.Should().Be(8);
                Path.GetExtension(frames[3]).Should().Be(".png");
                ReadPixels(frames[3]).Should().Equal(beforePixels, "lossless capture → lossless archive");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Archive_RefusesAnActiveSession()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_archact_");
            try
            {
                WriteFrames(session, 3, "jpg");   // session is created Active=true
                var a = await SessionArchiver.ArchiveAsync(Ffmpeg, session);
                a.Success.Should().BeFalse();
                SessionManager.GetFrameFiles(session).Length.Should().Be(3, "nothing may be touched");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private static int[] ReadPixels(string path)
        {
            using var bmp = new Bitmap(path);
            var px = new int[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    px[y * bmp.Width + x] = bmp.GetPixel(x, y).ToArgb();
            return px;
        }

        [Fact]
        public async Task Gif_Encodes_WithPaletteChain_AndFpsCap()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_gif_");
            try
            {
                // 30 frames @ 30 fps = 1s; the GIF path caps the rate at 15 via the fps filter
                // (dropping frames, preserving duration) → exactly 15 output frames. The palette
                // split/palettegen/paletteuse chain is the risky arg construction this proves.
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23, format: "gif");
                r.Success.Should().BeTrue(r.Error);
                Path.GetExtension(r.OutputPath!).Should().Be(".gif");
                CountFrames(r.OutputPath!).Should().Be(15);
                VideoSize(r.OutputPath!).Should().Be((64, 48));   // min(720,iw) never upscales
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Gif_CustomTuning_AppliesToTheRealChain()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_giftune_");
            try
            {
                // 30 frames @ 30fps with a 10fps cap → exactly 10 output frames; 32-color palette +
                // no dither exercises the max_colors/dither args against real ffmpeg.
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23, format: "gif",
                    gif: new VideoEncoder.GifOptions(MaxFps: 10, MaxWidth: 480, MaxColors: 32, Dither: "none"));
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(10);
                VideoSize(r.OutputPath!).Should().Be((64, 48), "min(480,iw) never upscales");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Gif_Trim_DoesNotReadPastTheRange()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_giftrim_");
            try
            {
                // Trim to 10 frames @ 30fps: the GIF path caps at 15fps (dropping every 2nd), so the
                // correct output is 5 frames. -frames:v counts OUTPUT frames — without the range
                // enforced inside select, the demuxer reads ~20 inputs to fill the quota and the GIF
                // silently covers DOUBLE the trimmed range (10 output frames).
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23,
                    startFrame: 1, maxFrames: 10, format: "gif");
                r.Success.Should().BeTrue(r.Error);
                CountFrames(r.OutputPath!).Should().Be(5);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task Webm_Encodes_AllFrames()
        {
            if (Ffmpeg == null) return;
            string session = MakeSession(out string root, "tlc_webm_");
            try
            {
                WriteFrames(session, 30, "jpg");
                var r = await VideoEncoder.EncodeAsync(Ffmpeg, session, 30, "ultrafast", 23, format: "webm");
                r.Success.Should().BeTrue(r.Error);
                Path.GetExtension(r.OutputPath!).Should().Be(".webm");
                CountFrames(r.OutputPath!).Should().Be(30);
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
