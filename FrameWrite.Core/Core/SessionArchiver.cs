using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FrameWrite
{
    /// <summary>
    /// Archive session: pack a finished session's frames folder into ONE video file (archive.mkv,
    /// H.264) via the bundled ffmpeg, then delete the frames — typically 5-15× smaller, because a
    /// screen timelapse is hugely temporally redundant and JPEG/PNG stacks can't exploit that.
    /// Fully reversible: Unarchive extracts the frames back (00001..N, same extension).
    ///
    /// Fidelity contract:
    ///  - JPEG sessions re-encode visually lossless (CRF 10, yuv444p so the already-subsampled
    ///    chroma isn't subsampled AGAIN). Restored frames are pixel-close, not byte-identical.
    ///  - PNG/BMP sessions (lossless captures) archive MATHEMATICALLY lossless (libx264rgb -qp 0):
    ///    restored frames are pixel-identical.
    ///
    /// Safety contract: the archive is written to a temp name, DECODED BACK and frame-counted, and
    /// only when that count matches the files on disk are the frames deleted (json state updates
    /// between the two, so a crash at any instant leaves both copies or a verified archive — never
    /// neither). Live capture never touches this path — only inactive sessions can be archived.
    /// </summary>
    public static class SessionArchiver
    {
        public const string ArchiveFileName = "archive.mkv";
        private const string TempArchiveFileName = "archive.tmp.mkv";

        public sealed class Result
        {
            public bool Success { get; init; }
            public bool Cancelled { get; init; }   // user-cancelled (not an error — nothing was changed)
            public string Error { get; init; } = "";
            public int Frames { get; init; }
            public long BytesBefore { get; init; }
            public long BytesAfter { get; init; }
        }

        public static string GetArchivePath(string sessionFolder)
            => Path.Combine(sessionFolder, ArchiveFileName);

        /// <summary>Bytes of the archive file, or 0 when none exists (picker size display).</summary>
        public static long GetArchiveSize(string sessionFolder)
        {
            try { var f = new FileInfo(GetArchivePath(sessionFolder)); return f.Exists ? f.Length : 0; }
            catch { return 0; }
        }

        /// <summary>
        /// Codec block per SOURCE frame extension. PNG/BMP captures are lossless, so their archive
        /// must be too (libx264rgb -qp 0 = exact RGB). JPEG is already lossy — CRF 10 at yuv444p is
        /// visually transparent on top of it. Pure — unit-tested.
        /// </summary>
        internal static string ArchiveCodecArgs(string ext) =>
            ext is "png" or "bmp"
                ? "-c:v libx264rgb -preset fast -qp 0 "
                : "-c:v libx264 -preset fast -crf 10 -pix_fmt yuv444p ";

        /// <summary>
        /// The frame count ffmpeg reports on stderr ("frame=  123 fps=…" lines) — the LAST one is
        /// the total. -1 when no frame line is present. Pure — unit-tested (this number gates the
        /// frame deletion, so a parse bug here must never mean "counts match").
        /// </summary>
        internal static int ParseLastFrameCount(string stderr)
        {
            int last = -1;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(stderr ?? "", @"^frame=\s*(\d+)",
                         System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                if (int.TryParse(m.Groups[1].Value, out int n)) last = n;
            }
            return last;
        }

        public static async Task<Result> ArchiveAsync(string ffmpegPath, string sessionFolder,
            CancellationToken ct = default, Action<int>? onFrameProgress = null)
        {
            var session = SessionManager.LoadSession(sessionFolder);
            if (session == null)
                return new Result { Success = false, Error = "Not a valid session (no session.json)." };
            if (session.Active)
                return new Result { Success = false, Error = "This session is marked as recording — stop it (or resolve crash recovery) first." };
            if (session.Archived)
                return new Result { Success = false, Error = "This session is already archived." };

            var frames = SessionManager.GetFrameFiles(sessionFolder);
            if (frames.Length == 0)
                return new Result { Success = false, Error = "No frames to archive." };
            var exts = frames.Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
                             .Where(e => e.Length > 0).Distinct().ToArray();
            if (exts.Length != 1)
                return new Result { Success = false, Error = $"This session mixes frame formats ({string.Join("/", exts)}) — can't archive." };
            string ext = exts[0];

            long bytesBefore = 0;
            foreach (var f in frames) { try { bytesBefore += new FileInfo(f).Length; } catch { } }

            string framesFolder = SessionManager.GetFramesFolder(sessionFolder);
            string tmpPath = Path.Combine(sessionFolder, TempArchiveFileName);
            string archivePath = GetArchivePath(sessionFolder);
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

            // Same image2 hygiene as the encoder: RELATIVE pattern + frames folder as the working
            // directory, so a '%' anywhere in the session path can't corrupt the -i pattern. The
            // OUTPUT path is literal for the mkv muxer (no printf expansion) — absolute is fine.
            int firstNum = int.TryParse(Path.GetFileNameWithoutExtension(frames[0]), out int fn) ? fn : 1;
            string args = $"-y -framerate 30 -start_number {firstNum} -i \"%05d.{ext}\" " +
                          "-metadata title=\"FrameWrite session archive\" " +
                          ArchiveCodecArgs(ext) + $"\"{tmpPath}\"";
            Logger.Log("Archiver", $"Archiving {frames.Length} {ext} frames of “{session.Name}” -> {ArchiveFileName}");

            var tap = MakeFrameTap(onFrameProgress);
            var (exitCode, _, error) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args, ct, tap, framesFolder);
            if (exitCode != 0 || !File.Exists(tmpPath))
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                if (ct.IsCancellationRequested)
                    return new Result { Success = false, Cancelled = true, Error = "Archive cancelled — nothing was changed." };
                return new Result { Success = false, Error = FirstErrorLine(error, exitCode) };
            }

            // VERIFY before anything is deleted: decode the archive and count its frames. A gap in
            // the numbering (foreign session) or a truncated write shows up here as a mismatch.
            int counted = await CountVideoFramesAsync(ffmpegPath, tmpPath, ct);
            if (counted != frames.Length)
            {
                try { File.Delete(tmpPath); } catch { }
                return new Result { Success = false, Cancelled = ct.IsCancellationRequested, Error = ct.IsCancellationRequested
                    ? "Archive cancelled — nothing was changed."
                    : $"Verification failed: the archive holds {counted} frames but {frames.Length} are on disk. Frames were NOT deleted." };
            }

            File.Move(tmpPath, archivePath, true);
            long bytesAfter = GetArchiveSize(sessionFolder);

            // State first, then delete: a crash mid-delete leaves "archived + leftover frames"
            // (wasted disk, zero data loss) rather than "not archived + missing frames".
            session = SessionManager.LoadSession(sessionFolder) ?? session;
            session.Archived = true;
            session.ArchivedFrames = frames.Length;
            session.ArchiveFrameExt = ext;
            session.FramesCaptured = frames.Length;   // disk truth (matches what the archive holds)
            SessionManager.SaveSession(sessionFolder, session);

            foreach (var f in frames)
            {
                try { File.Delete(f); }
                catch (Exception ex) { Logger.Log("Archiver", $"Delete {f} failed (archive is intact): {ex.Message}"); }
            }
            Logger.Log("Archiver", $"Archived “{session.Name}”: {frames.Length} frames, " +
                $"{bytesBefore / 1048576.0:0.#} MB -> {bytesAfter / 1048576.0:0.#} MB.");
            return new Result { Success = true, Frames = frames.Length, BytesBefore = bytesBefore, BytesAfter = bytesAfter };
        }

        public static async Task<Result> UnarchiveAsync(string ffmpegPath, string sessionFolder,
            CancellationToken ct = default, Action<int>? onFrameProgress = null)
        {
            var session = SessionManager.LoadSession(sessionFolder);
            if (session == null)
                return new Result { Success = false, Error = "Not a valid session (no session.json)." };
            string archivePath = GetArchivePath(sessionFolder);
            if (!session.Archived || !File.Exists(archivePath))
                return new Result { Success = false, Error = "This session has no archive to restore." };

            string ext = string.IsNullOrEmpty(session.ArchiveFrameExt) ? "jpg" : session.ArchiveFrameExt!;
            string framesFolder = SessionManager.GetFramesFolder(sessionFolder);
            Directory.CreateDirectory(framesFolder);

            // Extract back to a gapless 00001..N sequence. The OUTPUT pattern is image2 — same
            // relative-pattern + working-directory hygiene as everywhere else. JPEG re-extracts at
            // -q:v 2 (~q95): maximum fidelity from the archive; PNG/BMP are exact by construction.
            string q = ext == "jpg" ? "-q:v 2 " : "";
            string args = $"-y -i \"{archivePath}\" -start_number 1 {q}\"%05d.{ext}\"";
            Logger.Log("Archiver", $"Restoring {session.ArchivedFrames} {ext} frames of “{session.Name}” from {ArchiveFileName}");

            var tap = MakeFrameTap(onFrameProgress);
            var (exitCode, _, error) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath, args, ct, tap, framesFolder);
            int onDisk = SessionManager.GetFrameFiles(sessionFolder).Length;
            if (exitCode != 0 || onDisk != session.ArchivedFrames)
            {
                // A partial extraction alongside the intact archive is a confusing half-state —
                // remove what was written; the archive stays the single source of truth.
                foreach (var f in SessionManager.GetFrameFiles(sessionFolder))
                { try { File.Delete(f); } catch { } }
                if (ct.IsCancellationRequested)
                    return new Result { Success = false, Cancelled = true, Error = "Restore cancelled — the archive is untouched." };
                return new Result { Success = false, Error = exitCode != 0
                    ? FirstErrorLine(error, exitCode)
                    : $"Verification failed: extracted {onDisk} frames but the archive should hold {session.ArchivedFrames}. The archive was kept." };
            }

            session = SessionManager.LoadSession(sessionFolder) ?? session;
            session.Archived = false;
            session.ArchivedFrames = 0;
            session.ArchiveFrameExt = null;
            session.FramesCaptured = onDisk;
            SessionManager.SaveSession(sessionFolder, session);
            try { File.Delete(archivePath); }
            catch (Exception ex) { Logger.Log("Archiver", $"Deleting {ArchiveFileName} failed (frames are restored): {ex.Message}"); }

            Logger.Log("Archiver", $"Restored “{session.Name}”: {onDisk} frames.");
            return new Result { Success = true, Frames = onDisk };
        }

        /// <summary>Decode a video to the null muxer and return its frame count (-1 on failure).</summary>
        private static async Task<int> CountVideoFramesAsync(string ffmpegPath, string videoPath, CancellationToken ct)
        {
            var (exitCode, _, stderr) = await FfmpegRunner.RunFfmpegAsync(ffmpegPath,
                $"-i \"{videoPath}\" -map 0:v -f null -", ct);
            return exitCode == 0 ? ParseLastFrameCount(stderr) : -1;
        }

        // ffmpeg reports live progress on stderr as "frame=  123 fps=…" lines — tap them for the UI.
        private static Action<string>? MakeFrameTap(Action<int>? onFrameProgress)
            => onFrameProgress == null ? null : line =>
            {
                if (!line.StartsWith("frame=", StringComparison.Ordinal)) return;
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^frame=\s*(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) onFrameProgress(n);
            };

        // ffmpeg's stderr is pages long — surface the first genuinely error-looking tail line.
        private static string FirstErrorLine(string stderr, int exitCode)
        {
            var lines = (stderr ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            string? last = lines.LastOrDefault(l => !l.StartsWith("frame=", StringComparison.Ordinal));
            return string.IsNullOrEmpty(last) ? $"ffmpeg exited with code {exitCode}" : last!;
        }
    }
}
