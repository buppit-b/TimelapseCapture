using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;

namespace FrameWrite.Tests
{
    public class CullAndRenumberTests
    {
        [Fact]
        public void DeletesMarkedFrames_AndRenumbersGapless()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tlc_cull_" + Guid.NewGuid().ToString("N"));
            string frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            try
            {
                for (int i = 1; i <= 5; i++)
                    File.WriteAllText(Path.Combine(frames, $"{i:D5}.jpg"), $"frame{i}");

                int newCount = SessionManager.CullAndRenumber(dir, new HashSet<int> { 2, 4 });

                newCount.Should().Be(3);
                Directory.GetFiles(frames).Length.Should().Be(3);
                // old frames 1,3,5 survive and become a gapless 1,2,3 with content preserved.
                File.ReadAllText(Path.Combine(frames, "00001.jpg")).Should().Be("frame1");
                File.ReadAllText(Path.Combine(frames, "00002.jpg")).Should().Be("frame3");
                File.ReadAllText(Path.Combine(frames, "00003.jpg")).Should().Be("frame5");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void NothingMarked_LeavesSequenceUnchanged()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tlc_cull_" + Guid.NewGuid().ToString("N"));
            string frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            try
            {
                for (int i = 1; i <= 3; i++)
                    File.WriteAllText(Path.Combine(frames, $"{i:D5}.jpg"), $"frame{i}");

                int newCount = SessionManager.CullAndRenumber(dir, new HashSet<int>());

                newCount.Should().Be(3);
                File.ReadAllText(Path.Combine(frames, "00002.jpg")).Should().Be("frame2");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
