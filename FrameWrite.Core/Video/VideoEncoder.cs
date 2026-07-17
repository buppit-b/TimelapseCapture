using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FrameWrite
{
    /// <summary>
    /// Encodes a session's captured frames into an MP4 (H.264) via the reused FfmpegRunner.
    /// Framework-free — usable from any front-end.
    /// </summary>
    public static class VideoEncoder
    {
        // The valid x264 -preset values; anything else is coerced to "medium" (see EncodeAsync).
        private static readonly string[] ValidPresets =
            { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow", "placebo" };

        public sealed class Result
        {
            public bool Success { get; init; }
            public string OutputPath { get; init; } = "";
            public string Error { get; init; } = "";
        }

        /// <summary>
        /// GIF tuning (palette-based, so CRF/preset don't apply): frame-rate cap and width cap keep
        /// files postable (both DROP resolution, never duration), palette size trades colors for
        /// bytes, dither picks the pattern that hides the reduction. Values are normalized (clamped
        /// + allowlisted) before they reach the ffmpeg args — a hand-edited settings.json can't
        /// inject or break the filter chain.
        /// </summary>
        public sealed record GifOptions(int MaxFps = 15, int MaxWidth = 720, int MaxColors = 256, string Dither = "bayer");

        /// <summary>Clamp/allowlist every GifOptions field. Pure — unit-tested.</summary>
        internal static GifOptions NormalizeGif(GifOptions? g)
        {
            g ??= new GifOptions();
            string dither = (g.Dither ?? "bayer").ToLowerInvariant() switch
            {
                "none" => "none",
                "floyd" or "floyd_steinberg" or "smooth" => "floyd",
                _ => "bayer",
            };
            return new GifOptions(
                MaxFps: Math.Clamp(g.MaxFps <= 0 ? 15 : g.MaxFps, 1, 50),        // GIF timing is 10ms units; >50 is fiction
                MaxWidth: Math.Clamp(g.MaxWidth <= 0 ? 720 : g.MaxWidth, 120, 3840),
                MaxColors: Math.Clamp(g.MaxColors <= 0 ? 256 : g.MaxColors, 4, 256),
                Dither: dither);
        }

        /// <summary>
        /// The GIF tail of the -vf chain, from NORMALIZED options: optional fps cap (drops frames,
        /// preserves duration), width cap (aspect kept, never upscales), then the two-pass palette —
        /// per-encode palettegen (stats_mode=diff suits mostly-static screen content) + paletteuse
        /// with the chosen dither. Pure — unit-tested.
        /// </summary>
        internal static string[] GifFilters(double fps, GifOptions g)
        {
            string use = g.Dither switch
            {
                "none" => "dither=none",
                "floyd" => "dither=floyd_steinberg",
                _ => "dither=bayer:bayer_scale=4",
            };
            var list = new System.Collections.Generic.List<string>();
            if (fps > g.MaxFps) list.Add($"fps={g.MaxFps}");
            list.Add($"scale='min({g.MaxWidth},iw)':-1:flags=lanczos");
            list.Add($"split[s0][s1];[s0]palettegen=stats_mode=diff:max_colors={g.MaxColors}[p];[s1][p]paletteuse={use}:diff_mode=rectangle");
            return list.ToArray();
        }

        /// <summary>
        /// The playback fps that makes <paramref name="inputFrames"/> frames (thinned by
        /// <paramref name="everyNth"/>) last exactly <paramref name="durationSeconds"/>.
        /// Clamped to [0.1, 240] — past ~240fps players choke, so very long sessions come out
        /// longer than asked rather than unplayable. Pure — unit-tested.
        /// </summary>
        public static double FpsForDuration(int inputFrames, int everyNth, double durationSeconds)
        {
            if (inputFrames <= 0 || durationSeconds <= 0) return 30;
            int n = Math.Max(1, everyNth);
            int encoded = (inputFrames + n - 1) / n;
            return Math.Clamp(encoded / durationSeconds, 0.1, 240);
        }

        /// <summary>
        /// Output extension + codec argument block per export format. Pure — unit-tested.
        /// mp4 = H.264 (universal playback); webm = VP9 (smaller, browser/Discord-friendly — the user's
        /// CRF is remapped since VP9's 0–63 scale reads ~9 higher than x264's for similar quality, and
        /// the x264 preset maps to VP9's cpu-used speed); gif = palette-based, handled by the filter
        /// chain (no codec block — trailing space kept so the arg string composes identically).
        /// Unknown formats fall back to mp4 (a stale/hand-edited setting must not build broken args).
        /// </summary>
        internal static (string ext, string codecArgs) FormatArgs(string? format, string preset, int crf)
        {
            switch ((format ?? "mp4").ToLowerInvariant())
            {
                case "webm":
                    int vp9Crf = Math.Clamp(crf + 9, 10, 55);
                    int cpuUsed = preset switch { "fast" or "veryfast" or "ultrafast" => 5, "slow" or "veryslow" => 1, _ => 2 };
                    return (".webm", $"-c:v libvpx-vp9 -b:v 0 -crf {vp9Crf} -row-mt 1 -cpu-used {cpuUsed} -pix_fmt yuv420p ");
                case "gif":
                    return (".gif", "");
                default:
                    return (".mp4", $"-c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p ");
            }
        }

        /// <summary>
        /// The <c>-frames:v</c> output cap for a trimmed encode. A trim of <paramref name="maxFrames"/>
        /// input frames, keeping every <paramref name="everyNth"/> (ceiling), plus any held-last-frame
        /// clones (<paramref name="holdFrames"/>) which must NOT be clipped by the cap. Returns 0 when
        /// there's no trim (<paramref name="maxFrames"/> ≤ 0) — meaning no cap, encode everything.
        /// Off-by-one here would clip the last kept frame or over-run the trim, so it's unit-tested.
        /// </summary>
        internal static int ComputeOutputLimit(int maxFrames, int everyNth, int holdFrames)
        {
            if (maxFrames <= 0) return 0;
            int n = Math.Max(1, everyNth);
            int limit = n > 1 ? (maxFrames + n - 1) / n : maxFrames;
            if (holdFrames > 0) limit += holdFrames;
            return limit;
        }

        public static async Task<Result> EncodeAsync(string ffmpegPath, string sessionFolder,
            double fps, string preset, int crf, CancellationToken ct = default,
            int startFrame = 1, int maxFrames = 0, string? outputName = null,
            Action<int>? onFrameProgress = null, int everyNth = 1, double holdLastSeconds = 0,
            System.Drawing.Rectangle? crop = null, string format = "mp4", GifOptions? gif = null)
        {
            var frames = SessionManager.GetFrameFiles(sessionFolder);
            if (frames.Length == 0)
                return new Result { Success = false, Error = "No frames to encode." };

            // The image2 demuxer reads one extension pattern. A folder with mixed extensions (a mid-session
            // format switch) would silently encode only the subset matching frames[0] — fail clearly instead.
            var exts = frames.Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
                             .Where(e => e.Length > 0).Distinct().ToArray();
            if (exts.Length > 1)
                return new Result { Success = false, Error =
                    $"This session mixes frame formats ({string.Join("/", exts)}). All frames must be the same format to encode — recapture in a single format." };

            // Use the image2 demuxer over the numbered frame sequence (00001.ext, 00002.ext, …):
            // exactly one output frame per captured image. The old concat demuxer with -r resampled
            // timestamps and dropped frames (a long session produced a far-too-short video).
            string framesFolder = SessionManager.GetFramesFolder(sessionFolder);
            string ext = exts.Length == 1 ? exts[0] : "jpg";
            // RELATIVE pattern + run ffmpeg with the frames folder as its working directory. The image2
            // demuxer applies printf expansion to the WHOLE -i path, so a stray '%' anywhere in the
            // absolute path (a session named "50% run", or a user output folder with '%') would corrupt
            // the pattern and fail the encode. Keeping the volatile path out of the pattern isolates the
            // printf expansion to just the intended %05d token.
            string pattern = "%05d." + ext;

            string outputFolder = SessionManager.GetOutputFolder(sessionFolder);
            Directory.CreateDirectory(outputFolder);
            var (outExt, codecArgs) = FormatArgs(format, preset, crf);
            // Caller (the VM) resolves the user's name template; fall back to a timestamp if it's blank.
            string baseName = SanitizeFileName(outputName);
            if (string.IsNullOrEmpty(baseName))
                baseName = $"timelapse_{DateTime.Now:yyyyMMdd_HHmmss}";
            string outputPath = Path.Combine(outputFolder, baseName + outExt);
            for (int n = 2; File.Exists(outputPath); n++)   // don't overwrite a prior encode with the same name
                outputPath = Path.Combine(outputFolder, $"{baseName}_{n}{outExt}");

            if (fps <= 0) fps = 30;
            fps = Math.Clamp(fps, 0.1, 240);   // fractional fps is valid (duration mode computes it)
            crf = Math.Clamp(crf, 0, 51);
            // Allowlist the x264 preset — it's interpolated UNQUOTED into the ffmpeg args, so a value containing
            // spaces (e.g. from a crafted/imported settings.json) could otherwise inject extra ffmpeg arguments.
            preset = (preset ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(ValidPresets, preset) < 0) preset = "medium";
            if (startFrame < 1) startFrame = 1;
            everyNth = Math.Clamp(everyNth, 1, 1000);
            double holdSec = Math.Clamp(holdLastSeconds, 0, 600);
            int holdFrames = holdSec > 0 ? (int)Math.Round(holdSec * fps) : 0;

            // Build the -vf filter chain. Frame-skip: keep every Nth input frame then re-pace timestamps
            // (setpts) for a clean constant rate. Hold-last-frame: tpad clones the final frame for
            // holdSec so the finished artwork lingers. NOTE -frames:v counts OUTPUT frames, so with the
            // filter active the trim range is enforced INSIDE select (lt(n, maxFrames)) — otherwise the
            // demuxer reads past the range end to fill the quota.
            var filters = new System.Collections.Generic.List<string>();
            var gifOpts = NormalizeGif(gif);   // single normalization — the cap check and the filter agree
            // GIF's fps cap (below) ALSO drops frames — so a trimmed GIF hits the same quota
            // problem and needs its range inside select too, or the demuxer reads ~2× the range.
            bool gifCapsRate = format == "gif" && fps > gifOpts.MaxFps;
            if (everyNth > 1 || (gifCapsRate && maxFrames > 0))
            {
                var terms = new System.Collections.Generic.List<string>();
                if (maxFrames > 0) terms.Add($"lt(n\\,{maxFrames})");
                if (everyNth > 1) terms.Add($"not(mod(n\\,{everyNth}))");
                filters.Add($"select='{string.Join("*", terms)}'");
                filters.Add("setpts=N/FRAME_RATE/TB");
            }
            // Crop-at-encode (non-destructive): validate against the REAL frame size (read once from the
            // first frame) so a stale/foreign crop can't fail the run — clamp on-frame, force even dims
            // (yuv420p), and ignore a degenerate result. Placed before tpad so the held frame is cropped too.
            if (crop is { } c0)
            {
                var frameSize = GetImageSize(frames[0]);
                var cr = ClampCrop(c0, frameSize);
                if (cr.Width >= 2 && cr.Height >= 2)
                    filters.Add($"crop={cr.Width}:{cr.Height}:{cr.X}:{cr.Y}");
                else
                    Logger.Log("VideoEncoder", $"Ignoring degenerate crop {c0} for frame size {frameSize}.");
            }
            if (holdFrames > 0)
                filters.Add($"tpad=stop_mode=clone:stop_duration={holdSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (format == "gif")
                filters.AddRange(GifFilters(fps, gifOpts));
            string vf = filters.Count > 0 ? $"-vf \"{string.Join(",", filters)}\" " : "";

            // Output-frame cap for a trim (skip reduces it; hold adds the cloned frames so they aren't clipped).
            int outputLimit = ComputeOutputLimit(maxFrames, everyNth, holdFrames);
            string limit = outputLimit > 0 ? $"-frames:v {outputLimit} " : "";

            // Provenance (ROADMAP item 10, approach 1): open metadata tags naming the app — readable by
            // ffprobe / MediaInfo / file properties. Fixed strings only (no user input reaches the args);
            // platforms that re-encode may strip them, which is fine — this identifies directly-shared files.
            string appVersion = typeof(VideoEncoder).Assembly.GetName().Version is { } v
                ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
            string meta = $"-metadata encoder=\"FrameWrite {appVersion}\" -metadata comment=\"Made with FrameWrite\" ";

            // Codec args come from FormatArgs (mp4/webm/gif); -framerate before -i sets the input rate.
            // Invariant format: a comma-decimal locale would render 24.6 as "24,6" and break the args.
            string fpsArg = fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string args = $"-y -framerate {fpsArg} -start_number {startFrame} -i \"{pattern}\" {vf}{limit}{meta}" +
                          $"{codecArgs}\"{outputPath}\"";
            Logger.Log("VideoEncoder", $"Encoding {frames.Length} {ext} frames -> {outputPath} @ {fps}fps format={format} preset={preset} crf={crf}" +
                (everyNth > 1 ? $" everyNth={everyNth}" : ""));

            // ffmpeg reports live progress on stderr as "frame=  123 fps=…" lines — tap them for the UI.
            Action<string>? tap = onFrameProgress == null ? null : line =>
            {
                if (!line.StartsWith("frame=", StringComparison.Ordinal)) return;
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^frame=\s*(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) onFrameProgress(n);
            };
            var (exitCode, _, error) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args, ct, tap, framesFolder);

            if (exitCode == 0 && File.Exists(outputPath))
                return new Result { Success = true, OutputPath = outputPath };

            return new Result
            {
                Success = false,
                OutputPath = outputPath,
                Error = string.IsNullOrWhiteSpace(error) ? $"ffmpeg exited with code {exitCode}" : error,
            };
        }

        // Strip anything illegal in a Windows filename; collapse whitespace; trim trailing dots/spaces.
        internal static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.', ' ');
        }

        /// <summary>
        /// Clamp a crop rectangle onto a frame: intersect with the frame bounds and force EVEN width/
        /// height (required by yuv420p). Pure — unit-tested. A degenerate result (w/h &lt; 2) means
        /// "don't crop".
        /// </summary>
        public static System.Drawing.Rectangle ClampCrop(System.Drawing.Rectangle crop, System.Drawing.Size frame)
        {
            var r = System.Drawing.Rectangle.Intersect(crop, new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height));
            r.Width -= r.Width % 2;
            r.Height -= r.Height % 2;
            return r;
        }

        private static System.Drawing.Size GetImageSize(string path)
        {
            try { using var img = System.Drawing.Image.FromFile(path); return img.Size; }
            catch { return System.Drawing.Size.Empty; }
        }
    }
}
