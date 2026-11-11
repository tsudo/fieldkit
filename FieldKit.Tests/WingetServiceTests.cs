using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class WingetServiceTests
{
    // Captured from `winget upgrade --source winget` on 2026-11-10: no Source column.
    private static readonly string[] FourColumnTable =
    [
        "Name                                            Id                                 Version      Available",
        "---------------------------------------------------------------------------------------------------------------",
        "Audacity 3.7.9                                  Audacity.Audacity                  3.7.9        4.0.2.263060742",
        "AWS Command Line Interface v2                   Amazon.AWSCLI                      2.39.1.0     2.40.0",
        "calibre 64bit                                   calibre.calibre                    9.17.0       9.18.0",
        "GitHub CLI                                      GitHub.cli                         2.102.0      2.103.3",
        "4 upgrades available."
    ];

    [Fact]
    public void Parses_table_without_source_column()
    {
        var upgrades = WingetService.ParseUpgradeTable(FourColumnTable);

        Assert.Equal(4, upgrades.Count);
        Assert.Equal(new WingetUpgrade("AWS Command Line Interface v2", "Amazon.AWSCLI", "2.39.1.0", "2.40.0"), upgrades[1]);
        Assert.Equal("Audacity 3.7.9", upgrades[0].Name);
    }

    [Fact]
    public void Parses_table_with_source_column_and_skips_other_sources()
    {
        string[] lines =
        [
            "Name            Id                  Version  Available Source",
            "---------------------------------------------------------------",
            "Notepad++       Notepad++.Notepad++ 8.6.0    8.7.1     winget",
            "Some Store App  9NKSQGP7F2NH        1.0      2.0       msstore",
            "2 upgrades available."
        ];

        var upgrades = WingetService.ParseUpgradeTable(lines);

        var only = Assert.Single(upgrades);
        Assert.Equal("Notepad++.Notepad++", only.Id);
        Assert.Equal("8.7.1", only.AvailableVersion);
    }

    [Fact]
    public void Does_not_depend_on_column_alignment()
    {
        // A wide-character name shifts every later column; fixed offsets would misread the ID.
        string[] lines =
        [
            "Name            Id                  Version  Available",
            "-------------------------------------------------------",
            "微信 WeChat for Windows   Tencent.WeChat   3.9.0   4.0.1",
            "1 upgrades available."
        ];

        var only = Assert.Single(WingetService.ParseUpgradeTable(lines));
        Assert.Equal("Tencent.WeChat", only.Id);
        Assert.Equal("微信 WeChat for Windows", only.Name);
    }

    [Fact]
    public void Keeps_bounded_installed_version_together()
    {
        string[] lines =
        [
            "Name       Id               Version   Available",
            "-----------------------------------------------",
            "Some Tool  Vendor.SomeTool  < 2.0.0   2.1.0",
            "1 upgrades available."
        ];

        var only = Assert.Single(WingetService.ParseUpgradeTable(lines));
        Assert.Equal("Vendor.SomeTool", only.Id);
        Assert.Equal("< 2.0.0", only.InstalledVersion);
    }

    [Fact]
    public void Stops_at_count_line_so_pinned_packages_are_left_out()
    {
        string[] lines =
        [
            "Name    Id            Version Available",
            "---------------------------------------",
            "Alpha   Vendor.Alpha  1.0     2.0",
            "1 upgrades available.",
            "",
            "The following packages have an upgrade available, but require explicit targeting for upgrade:",
            "Name    Id            Version Available",
            "---------------------------------------",
            "Pinned  Vendor.Pinned 1.0     2.0"
        ];

        var only = Assert.Single(WingetService.ParseUpgradeTable(lines));
        Assert.Equal("Vendor.Alpha", only.Id);
    }

    [Fact]
    public void Drops_rows_with_truncated_or_malformed_ids()
    {
        string[] lines =
        [
            "Name    Id            Version Available",
            "---------------------------------------",
            "Long    Vendor.LongNam… 1.0   2.0",
            "NoDot   JustAWord     1.0     2.0",
            "Good    Vendor.Good   1.0     2.0",
            "3 upgrades available."
        ];

        var only = Assert.Single(WingetService.ParseUpgradeTable(lines));
        Assert.Equal("Vendor.Good", only.Id);
    }

    [Fact]
    public void Ignores_spinner_and_ansi_noise_before_the_header()
    {
        string[] lines =
        [
            "   - ",
            "   \\ ",
            "\u001b[?25l   | \rName  Id          Version Available",
            "-----------------------------------",
            "Beta  Vendor.Beta 1.0     1.1",
            "1 upgrades available."
        ];

        Assert.Single(WingetService.ParseUpgradeTable(lines));
    }

    [Fact]
    public void Returns_nothing_when_there_is_no_table()
    {
        Assert.Empty(WingetService.ParseUpgradeTable(["No installed package found matching input criteria."]));
    }

    [Fact]
    public void Reads_installed_ids_from_export_json()
    {
        const string json = """
            {
              "$schema": "https://aka.ms/winget-packages.schema.2.0.json",
              "Sources": [
                { "Packages": [ { "PackageIdentifier": "7zip.7zip" }, { "PackageIdentifier": "Mozilla.Firefox" } ] },
                { "Packages": [ { "PackageIdentifier": "Git.Git" } ] }
              ]
            }
            """;

        var ids = WingetService.ParseExport(json);

        Assert.Equal(3, ids.Count);
        Assert.Contains("mozilla.firefox", ids); // lookups ignore case
    }

    [Fact]
    public void Export_without_sources_is_empty_not_an_error()
    {
        Assert.Empty(WingetService.ParseExport("{}"));
    }

    [Fact]
    public void Package_arguments_address_one_exact_id()
    {
        var args = WingetService.BuildPackageArguments("install", "Notepad++.Notepad++");

        Assert.StartsWith("install --id \"Notepad++.Notepad++\" --exact --silent", args);
        Assert.Contains("--source winget", args);
        Assert.Contains("--disable-interactivity", args);
    }

    [Theory]
    [InlineData("Vendor.App\" --override \"/x")]
    [InlineData("Vendor.App & calc")]
    [InlineData("NoDot")]
    [InlineData("")]
    public void Package_arguments_reject_anything_that_is_not_an_id(string id)
    {
        Assert.Throws<ArgumentException>(() => WingetService.BuildPackageArguments("install", id));
    }

    [Fact]
    public void Exit_codes_map_to_plain_results()
    {
        Assert.True(WingetService.DescribeExit("install", 0).Succeeded);
        Assert.Equal("Updated", WingetService.DescribeExit("upgrade", 0).Message);
        Assert.True(WingetService.DescribeExit("upgrade", unchecked((int)0x8A15002B)).Succeeded);

        Assert.False(WingetService.DescribeExit("upgrade", unchecked((int)0x8A150101)).Succeeded); // app in use
        Assert.Contains("Restart", WingetService.DescribeExit("install", unchecked((int)0x8A150109)).Message);

        var unknown = WingetService.DescribeExit("install", unchecked((int)0x8A150FFF));
        Assert.False(unknown.Succeeded);
        Assert.Contains("0x8A150FFF", unknown.Message);
    }
}
