# Setting Up Automated Testing for TimelapseCapture

## Quick Start (5 Minutes)

### Step 1: Create Test Project

```bash
cd C:\Users\Spike\source\TimelapseCapture
dotnet new xunit -n TimelapseCapture.Tests
cd TimelapseCapture.Tests
dotnet add reference ../TimelapseCapture.csproj
dotnet add package FluentAssertions --version 6.12.0
```

### Step 2: Copy the Test Files

1. Copy the starter test suite from Claude's artifact to:
   `C:\Users\Spike\source\TimelapseCapture\TimelapseCapture.Tests\SessionManagerTests.cs`

2. Run the tests:
```bash
dotnet test
```

You should see output like:
```
Passed!  - Failed:     0, Passed:    24, Skipped:     0, Total:    24
```

---

## What Gets Tested

### ✅ Currently Tested (24 tests)
1. **SessionManager** (18 tests)
   - Session creation with valid/invalid parameters
   - Metadata persistence
   - Frame counting
   - Active session detection
   - Corrupted session handling
   - Multiple session scenarios

2. **ValidationHelper** (3 tests)
   - Region dimension validation (even/odd/negative)
   - Disk space checks
   - Common resolution validation

3. **AspectRatio** (3 tests)
   - Ratio calculation accuracy
   - Name formatting
   - 16:9 constraint enforcement

### ❌ Not Yet Tested (High Priority)
- Multi-monitor capture coordination
- Thread safety in capture timer
- FFmpeg integration and error handling
- Settings save debouncing
- Activity monitor event firing
- Region state synchronization (the 3-state problem)
- UI state transitions
- Error recovery flows

---

## Running Tests

### Run all tests
```bash
dotnet test
```

### Run with detailed output
```bash
dotnet test --verbosity detailed
```

### Run only SessionManager tests
```bash
dotnet test --filter "FullyQualifiedName~SessionManagerTests"
```

### Run with code coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

---

## Continuous Integration Setup

### GitHub Actions (Recommended)

Create `.github/workflows/tests.yml`:

```yaml
name: Run Tests

on:
  push:
    branches: [ main, ui-redesign-2025 ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: windows-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore --configuration Release
    
    - name: Run tests
      run: dotnet test --no-build --configuration Release --verbosity normal
    
    - name: Upload test results
      if: always()
      uses: actions/upload-artifact@v3
      with:
        name: test-results
        path: '**/TestResults/*.trx'
```

This will:
- ✅ Run tests on every push
- ✅ Run tests on every pull request
- ✅ Block merging if tests fail
- ✅ Store test results as artifacts

---

## Adding New Tests

### When to Write a Test

**Always write a test when:**
1. Fixing a bug (regression test)
2. Adding a new feature
3. Refactoring existing code

**Test structure:**
```csharp
[Fact] // or [Theory] for parameterized tests
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - Set up test data
    var input = "test data";
    
    // Act - Execute the code being tested
    var result = MethodUnderTest(input);
    
    // Assert - Verify the result
    result.Should().Be("expected output");
}
```

### Example: Testing a Bug Fix

Let's say you find a bug where session creation fails with null regions:

```csharp
[Fact]
public void CreateSession_WithNullRegion_DoesNotThrow()
{
    // This test documents the bug fix
    // Before fix: Would throw NullReferenceException
    // After fix: Should handle null gracefully
    
    // Arrange & Act
    var action = () => SessionManager.CreateNamedSession(
        _testDirectory, "NullRegionTest", 5, "JPEG", 90, null, 30);
    
    // Assert
    action.Should().NotThrow("session creation should handle null region");
    
    var sessionFolder = action();
    var session = SessionManager.LoadSession(sessionFolder);
    session!.CaptureRegion.Should().BeNull();
}
```

---

## Test-Driven Development Workflow

### The Red-Green-Refactor Cycle

1. **Red** - Write a failing test
```csharp
[Fact]
public void NewFeature_Works()
{
    // Test for feature that doesn't exist yet
    var result = MyNewFeature();
    result.Should().Be(expected);
}
```

2. **Green** - Write minimal code to make it pass
```csharp
public string MyNewFeature() 
{
    return expected; // Simplest implementation
}
```

3. **Refactor** - Improve the code
```csharp
public string MyNewFeature() 
{
    // Now add real logic, tests ensure it still works
    return CalculateProperResult();
}
```

---

## Code Coverage Goals

### Current Coverage: ~0% (no tests)
### Target Coverage by Phase:

- **Phase 1 (Week 1)**: 30% coverage
  - Core SessionManager
  - Validation helpers
  - Aspect ratio calculations

- **Phase 2 (Week 2)**: 50% coverage
  - Activity monitoring
  - Settings management
  - Basic capture flow

- **Phase 3 (Month 1)**: 70% coverage
  - FFmpeg integration
  - Error handling
  - UI state management

**Note**: 100% coverage is NOT the goal. Focus on testing:
- Critical business logic
- Complex algorithms
- Error-prone areas
- Public APIs

**Don't test**:
- Simple getters/setters
- UI layout code
- Third-party libraries

---

## Viewing Coverage Reports

### Install ReportGenerator
```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

### Generate HTML Report
```bash
# Run tests with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Generate report
reportgenerator -reports:TimelapseCapture.Tests/coverage.opencover.xml -targetdir:coveragereport

# Open in browser
start coveragereport/index.html
```

---

## Advanced Testing (Future)

### Mutation Testing
Tests your tests by introducing bugs and checking if tests catch them:
```bash
dotnet tool install -g dotnet-stryker
cd TimelapseCapture.Tests
dotnet stryker
```

### Performance Testing
Track how fast operations run:
```csharp
[Fact]
public void CaptureFrame_CompletesUnder100ms()
{
    var stopwatch = Stopwatch.StartNew();
    
    CaptureFrame(region);
    
    stopwatch.Stop();
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
}
```

### Integration Testing with FFmpeg
```csharp
[Fact]
public async Task EncodingPipeline_CreatesValidVideo()
{
    // Requires FFmpeg to be available
    // Tests the full encode flow
    var videoPath = await EncodeSession(sessionFolder);
    
    File.Exists(videoPath).Should().BeTrue();
    
    // Verify video is valid using FFprobe
    var probe = await FfmpegRunner.ProbeVideo(videoPath);
    probe.FrameCount.Should().Be(expectedFrames);
}
```

---

## Testing Best Practices

### ✅ DO
- Test one thing per test
- Use descriptive test names
- Clean up test data (use Dispose)
- Make tests independent (can run in any order)
- Use Theory for similar tests with different inputs
- Mock external dependencies

### ❌ DON'T
- Test implementation details
- Use Thread.Sleep (use proper async)
- Share state between tests
- Test third-party code
- Write brittle tests that break on minor changes
- Ignore failing tests

---

## Troubleshooting

### Tests fail with "Access Denied"
- Close the application if it's running
- Check antivirus isn't blocking test file creation

### Tests fail intermittently
- Likely a timing issue (race condition)
- Add proper synchronization
- Don't use arbitrary delays

### Coverage report shows 0%
- Make sure you're using `/p:CollectCoverage=true`
- Check that Coverlet package is installed
- Verify you're running tests from the test project directory

---

## Next Steps

1. **Today**: 
   - Set up test project ✅
   - Run the 24 starter tests ✅
   - Verify all pass ✅

2. **This Week**:
   - Add 5 tests for bug fixes
   - Write tests for any new features
   - Set up GitHub Actions CI

3. **Next Week**:
   - Add Activity Monitor tests
   - Test multi-monitor scenarios
   - Add FFmpeg integration tests

4. **This Month**:
   - Reach 50% code coverage
   - Add UI automation tests
   - Set up mutation testing

---

## Resources

- **xUnit Documentation**: https://xunit.net/
- **FluentAssertions**: https://fluentassertions.com/
- **Moq (Mocking)**: https://github.com/moq/moq4
- **Coverlet (Coverage)**: https://github.com/coverlet-coverage/coverlet
- **Test-Driven Development**: https://martinfowler.com/bliki/TestDrivenDevelopment.html

---

## Questions?

If tests fail or you're unsure how to test something:
1. Check test output for specific error messages
2. Run with `--verbosity detailed` for more info
3. Use Logger.Log in your code to debug
4. Add `[Fact(Skip = "WIP")]` to temporarily skip tests

Remember: **A failing test is better than no test!** 
It documents expected behavior and will pass once fixed.
