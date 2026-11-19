using System.Text.Json;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class PcHealthTests
{
    [Fact]
    public void ActiveThirdPartyProviderIsShownEvenWhenDefenderIsOff()
    {
        var result = PcHealth.DescribeAntivirus([new("Microsoft Defender", 1), new("Third-party AV", 0)]);
        Assert.Equal("Active", result.State);
        Assert.Equal("Third-party AV", result.Detail);
    }

    [Theory]
    [InlineData(0, 0, "Unencrypted")]
    [InlineData(0, 1, "Suspended")]
    [InlineData(1, 1, "Protected")]
    [InlineData(0, 4, "In progress")]
    [InlineData(2, 1, "Unavailable")]
    public void BitLockerSeparatesSuspendedAndUnencrypted(int protection, int conversion, string expected) =>
        Assert.Equal(expected, PcHealth.DescribeBitLocker(protection, conversion).State);

    [Theory]
    [InlineData("Unknown", "Unavailable")]
    [InlineData(null, "Unavailable")]
    [InlineData("Healthy", "Healthy")]
    [InlineData("Warning", "Review")]
    public void DiskHealthDoesNotAssumeSuccess(string? reported, string expected) =>
        Assert.Equal(expected, PcHealth.DescribeDisk(reported, "System disk").State);

    [Theory]
    [InlineData("Normal", true, false, "Active")]
    [InlineData("Normal", false, false, "Review")]
    [InlineData("Normal", true, true, "Review")]
    [InlineData("Normal", true, null, "Unavailable")]
    [InlineData("Passive", false, true, "Provider status")]
    public void DefenderConsidersRunningModeAndMissingData(string mode, bool realtime, bool? stale, string state)
    {
        var sample = JsonSerializer.SerializeToElement(new
        {
            AMRunningMode = mode, RealTimeProtectionEnabled = realtime,
            DefenderSignaturesOutOfDate = stale, AntivirusSignatureAge = 1
        });
        Assert.Equal(state, PcHealth.DescribeDefender(sample).State);
    }

    [Fact]
    public async Task AFailedProviderLeavesIndependentChecksAvailable()
    {
        var checks = new HealthCheck[]
        {
            new("Disk", "", () => throw new UnauthorizedAccessException()),
            new("Restart", "", () => Task.FromResult(new HealthReading("Clear", "No pending restart")))
        };
        var readings = await Task.WhenAll(checks.Select(check => PcHealth.ReadSafelyAsync(check)));
        Assert.Equal(new[] { "Unavailable", "Clear" }, readings.Select(r => r.State));
    }
}
