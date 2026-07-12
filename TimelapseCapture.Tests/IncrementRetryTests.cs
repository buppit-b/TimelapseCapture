using Xunit;
using FluentAssertions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TimelapseCapture.Tests
{
    // The per-frame count increment must ride out the short exclusive locks that cloud-sync
    // clients and antivirus scanners take on session.json — a blip must not strike an unattended
    // run, and a failed save must never lead to a reused (overwritten) frame number.
    public class IncrementRetryTests
    {
        private static string NewSession(out string file, int frames = 5)
        {
            string dir = Path.Combine(Path.GetTempPath(), "tlc_incr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            SessionManager.SaveSession(dir, new SessionInfo { FramesCaptured = frames });
            file = Path.Combine(dir, "session.json");
            return dir;
        }

        [Fact]
        public async Task Increment_SurvivesTransientExclusiveLock()
        {
            string dir = NewSession(out string file);
            try
            {
                // Hold the file exclusively for ~60ms on another thread — inside the retry window.
                using var gate = new System.Threading.ManualResetEventSlim(false);
                var locker = Task.Run(() =>
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    gate.Set();
                    System.Threading.Thread.Sleep(60);
                });
                gate.Wait();

                var updated = SessionManager.IncrementFrameCount(dir);
                await locker;

                updated.Should().NotBeNull("a short sync/AV lock must not read as a capture failure");
                updated!.FramesCaptured.Should().Be(6);
                SessionManager.LoadSession(dir)!.FramesCaptured.Should().Be(6, "the count must actually land on disk");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Increment_MissingFile_ReturnsNullWithoutRetrying()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tlc_incr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                SessionManager.IncrementFrameCount(dir).Should().BeNull();
                sw.ElapsedMilliseconds.Should().BeLessThan(40, "a genuinely missing file must fail fast, not retry");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Increment_PersistentLock_FailsCleanly_WithoutCorruptingTheCount()
        {
            string dir = NewSession(out string file);
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // Locked for the whole call — all retries exhausted.
                    SessionManager.IncrementFrameCount(dir).Should().BeNull();
                }
                SessionManager.LoadSession(dir)!.FramesCaptured.Should().Be(5, "a failed increment must leave the count untouched");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
