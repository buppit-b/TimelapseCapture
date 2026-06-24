// ============================================================================
// STARTER TEST SUITE FOR TIMELAPSECAPTURE
// Auto-generated and fixed for actual API
// ============================================================================

using Xunit;
using FluentAssertions;
using System;
using System.Drawing;
using System.IO;

namespace TimelapseCapture.Tests
{
    public class SessionManagerTests : IDisposable
    {
        private readonly string _testDirectory;

        public SessionManagerTests()
        {
            _testDirectory = Path.Combine(
                Path.GetTempPath(),
                "TimelapseTests",
                Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        [Fact]
        public void CreateNamedSession_WithValidParameters_CreatesCompleteStructure()
        {
            // Arrange
            string sessionName = "TestSession";
            var region = new Rectangle(0, 0, 1920, 1080);

            // Act
            string sessionFolder = SessionManager.CreateNamedSession(
                capturesRoot: _testDirectory,
                sessionName: sessionName,
                intervalSeconds: 5,
                region: region,
                format: "JPEG",
                jpegQuality: 90);

            // Assert
            Directory.Exists(sessionFolder).Should().BeTrue("session folder should exist");
            Directory.Exists(Path.Combine(sessionFolder, "frames")).Should().BeTrue("frames folder should exist");
            Directory.Exists(Path.Combine(sessionFolder, "output")).Should().BeTrue("output folder should exist");
        }

        [Fact]
        public void CreateNamedSession_SavesCorrectMetadata()
        {
            // Arrange
            var region = new Rectangle(100, 200, 1920, 1080);

            // Act
            string sessionFolder = SessionManager.CreateNamedSession(
                _testDirectory, 
                "MetadataTest", 
                intervalSeconds: 10, 
                region: region, 
                format: "PNG", 
                jpegQuality: 100);

            var session = SessionManager.LoadSession(sessionFolder);

            // Assert
            session.Should().NotBeNull();
            session!.Name.Should().Be("MetadataTest");
            session.IntervalSeconds.Should().Be(10);
        }

        [Fact]
        public void IncrementFrameCount_IncreasesCountByOne()
        {
            // Arrange
            var sessionFolder = SessionManager.CreateNamedSession(
                _testDirectory, 
                "IncrementTest", 
                intervalSeconds: 5, 
                region: new Rectangle(0, 0, 1920, 1080), 
                format: "JPEG", 
                jpegQuality: 90);
            
            // Act
            SessionManager.IncrementFrameCount(sessionFolder);
            
            // Assert
            var session = SessionManager.LoadSession(sessionFolder);
            session!.FramesCaptured.Should().Be(1);
        }

        [Fact]
        public void LoadSession_WithNonExistentPath_ReturnsNull()
        {
            // Arrange
            string fakePath = Path.Combine(_testDirectory, "DoesNotExist");

            // Act
            var session = SessionManager.LoadSession(fakePath);

            // Assert
            session.Should().BeNull("loading non-existent session should return null");
        }

        [Fact]
        public void FindActiveSession_WithNoActiveSessions_ReturnsNull()
        {
            // Arrange
            var session1Folder = SessionManager.CreateNamedSession(
                _testDirectory, 
                "Inactive1", 
                intervalSeconds: 5, 
                region: new Rectangle(0, 0, 1920, 1080), 
                format: "JPEG", 
                jpegQuality: 90);
            var session1 = SessionManager.LoadSession(session1Folder);
            session1!.Active = false;
            SessionManager.SaveSession(session1Folder, session1);

            // Act
            var result = SessionManager.FindActiveSession(_testDirectory);

            // Assert
            result.Should().BeNull("should return null when no active sessions exist");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                    Directory.Delete(_testDirectory, recursive: true);
            }
            catch { }
        }
    }

    public class ValidationHelperTests
    {
        [Theory]
        [InlineData(1920, 1080, true)]
        [InlineData(1921, 1080, false)]
        [InlineData(0, 1080, false)]
        public void IsValidRegion_ValidatesDimensions(int width, int height, bool expected)
        {
            // Arrange
            var region = new Rectangle(0, 0, width, height);
            
            // Act
            var result = ValidationHelper.IsValidRegion(region);
            
            // Assert
            result.Should().Be(expected,
                $"region {width}x{height} should be {(expected ? "valid" : "invalid")}");
        }
    }

    public class ScreenHelperTests
    {
        [Fact]
        public void FitRegionOnScreen_FullyVisible_ReturnedUnchanged()
        {
            var bounds = new Rectangle(0, 0, 1920, 1080);
            var saved = new Rectangle(100, 100, 800, 600);

            var result = ScreenHelper.FitRegionOnScreen(saved, bounds, out var moved);

            moved.Should().BeFalse();
            result.Should().Be(saved);
        }

        [Fact]
        public void FitRegionOnScreen_OnRemovedMonitor_RelocatedSameSize()
        {
            // Region was on a second monitor at x=1920; now only the primary remains.
            var bounds = new Rectangle(0, 0, 1920, 1080);
            var saved = new Rectangle(1920, 0, 870, 470);

            var result = ScreenHelper.FitRegionOnScreen(saved, bounds, out var moved);

            moved.Should().BeTrue();
            result.Should().NotBeNull();
            result!.Value.Size.Should().Be(saved.Size, "size must stay constant for frame consistency");
            bounds.Contains(result.Value).Should().BeTrue("the relocated region must be fully on screen");
            result.Value.Should().Be(new Rectangle(1050, 0, 870, 470));
        }

        [Fact]
        public void FitRegionOnScreen_LargerThanDesktop_ReturnsNull()
        {
            var bounds = new Rectangle(0, 0, 1280, 720);
            var saved = new Rectangle(0, 0, 1920, 1080);

            var result = ScreenHelper.FitRegionOnScreen(saved, bounds, out var moved);

            result.Should().BeNull();
            moved.Should().BeFalse();
        }

        [Fact]
        public void FitRegionOnScreen_NegativeOriginDesktop_RelocatesIntoBounds()
        {
            // Primary at 0,0 with a second monitor to the left → virtual origin is negative.
            var bounds = new Rectangle(-1920, 0, 3840, 1080);
            var saved = new Rectangle(5000, 0, 800, 600); // far off to the right

            var result = ScreenHelper.FitRegionOnScreen(saved, bounds, out var moved);

            moved.Should().BeTrue();
            result.Should().NotBeNull();
            bounds.Contains(result!.Value).Should().BeTrue();
            result.Value.Size.Should().Be(saved.Size);
        }
    }
}
