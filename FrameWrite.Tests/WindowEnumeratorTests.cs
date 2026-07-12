using Xunit;
using FluentAssertions;
using System;
using System.Drawing;

namespace FrameWrite.Tests
{
    public class WindowEnumeratorTests
    {
        [Fact]
        public void Enumerate_DoesNotThrow_AndEveryWindowPassesTheFilters()
        {
            var windows = WindowEnumerator.Enumerate();

            windows.Should().NotBeNull();
            // The curated filters: every returned window has a real title and a non-trivial size.
            foreach (var w in windows)
            {
                Assert.False(string.IsNullOrWhiteSpace(w.Title));
                Assert.True(w.Bounds.Width >= 50 && w.Bounds.Height >= 50);
                Assert.NotEqual(IntPtr.Zero, w.Handle);
            }
        }

        [Fact]
        public void TryGetLiveBounds_NullHandle_ReportsNotAlive()
        {
            bool ok = WindowEnumerator.TryGetLiveBounds(IntPtr.Zero, out var bounds, out bool minimized, out bool alive);

            Assert.False(ok);
            Assert.False(alive);
            Assert.False(minimized);
            bounds.Should().Be(Rectangle.Empty);
        }
    }
}
