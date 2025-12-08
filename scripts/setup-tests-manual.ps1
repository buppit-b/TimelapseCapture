# ============================================================================
# SIMPLE Test Setup - Manual Steps
# ============================================================================
# Run these commands one at a time to set up testing
# ============================================================================

Write-Host "Manual Test Setup Instructions" -ForegroundColor Cyan
Write-Host "=============================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Copy and run these commands one at a time:" -ForegroundColor Yellow
Write-Host ""

$commands = @"
# Step 1: Remove old test project if it exists
Remove-Item -Recurse -Force TimelapseCapture.Tests -ErrorAction SilentlyContinue

# Step 2: Create new test project directory
New-Item -ItemType Directory TimelapseCapture.Tests

# Step 3: Go into test directory
cd TimelapseCapture.Tests

# Step 4: Create the .csproj file
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TimelapseCapture.csproj" />
  </ItemGroup>
</Project>
'@ | Out-File -Encoding UTF8 TimelapseCapture.Tests.csproj

# Step 5: Restore packages
dotnet restore

# Step 6: Create a simple test
@'
using Xunit;
using FluentAssertions;
using System.Drawing;

namespace TimelapseCapture.Tests
{
    public class SimpleTest
    {
        [Fact]
        public void ValidationHelper_WorksCorrectly()
        {
            var region = new Rectangle(0, 0, 1920, 1080);
            var result = ValidationHelper.IsValidRegion(region);
            result.Should().BeTrue();
        }
    }
}
'@ | Out-File -Encoding UTF8 SimpleTest.cs

# Step 7: Build the test project
dotnet build

# Step 8: Run tests
dotnet test

# Step 9: Go back to root
cd ..
"@

Write-Host $commands -ForegroundColor White
Write-Host ""
Write-Host "Or run this automated version:" -ForegroundColor Yellow
Write-Host "  .\setup-tests-simple.ps1" -ForegroundColor Cyan
