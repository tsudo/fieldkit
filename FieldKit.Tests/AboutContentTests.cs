using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class AboutContentTests
{
    [Theory]
    [InlineData("2.0.1", "v2.0.1", "latest stable release")]
    [InlineData("2.1.0-preview.2", "v2.1.0", "is available")]
    [InlineData("2.1.0-preview", "v2.0.1", "This build is newer")]
    [InlineData("2.1.0+local", "v2.10.0", "is available")]
    [InlineData("2.1.0", "nightly", "Compare it")]
    public void ComparesInstalledBuildWithStableTag(string installed, string tag, string expected) =>
        Assert.Contains(expected, AboutContent.Compare(installed, tag));

    [Fact]
    public void LegalTextsAreBundledForOfflineUse()
    {
        Assert.Contains("Copyright (c) 2026 Keith S. Crawford", AboutContent.License);
        Assert.StartsWith("This software is provided", AboutContent.Disclaimer);
        Assert.EndsWith("Licensed under the MIT License.", AboutContent.Disclaimer.TrimEnd());
    }
}
