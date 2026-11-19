using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class UpdateServiceTests
{
    private static readonly WingetUpgrade[] Found =
    [
        new("Alpha", "Test.Alpha", "1", "2"),
        new("Beta", "Test.Beta", "3", "4")
    ];

    [Fact]
    public void SelectionIncludesOnlyRequestedScannedPackagesOnce()
    {
        var selected = UpdateService.SelectApps(Found, ["test.beta", "TEST.BETA"]);
        Assert.Equal(Found[1], Assert.Single(selected));
        Assert.Empty(UpdateService.SelectApps(Found, []));
    }

    [Fact]
    public void UnscannedPackageIdIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UpdateService.SelectApps(Found, ["Test.Alpha", "Test.Other"]));
    }

    [Fact]
    public void ATableOfOnlyPinnedPackagesNeverBecomesAnUpdateAllPlan()
    {
        var parsed = WingetService.ParseUpgradeTable([
            "The following packages have an upgrade available, but require explicit targeting for upgrade:",
            "Name    Id            Version Available",
            "---------------------------------------",
            "Pinned  Vendor.Pinned 1.0     2.0"
        ]);
        Assert.Empty(parsed);
    }
}
