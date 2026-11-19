using System.IO;
using Microsoft.Win32;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

// Isolated fixtures exercise Windows registry types without opening the user's Storage Sense key.
public sealed class StorageSenseSettingsTests : IDisposable
{
    private readonly string _keyPath = $@"Software\FieldKit-Test-{Guid.NewGuid():N}";
    private readonly string _directory = Directory.CreateTempSubdirectory("fieldkit-undo-").FullName;
    private StorageSenseSettings Settings => new(Registry.CurrentUser, _keyPath, "fixture", _directory);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatedEnablePreservesOriginalValueAndSchedule(bool hadPreviousValue)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue("04", 7, RegistryValueKind.DWord);
        if (hadPreviousValue) key.SetValue("01", "old", RegistryValueKind.String);
        Settings.Enable();
        Settings.Enable();
        Assert.Equal(1, key.GetValue("01"));
        Settings.Restore();
        Assert.False(Settings.CanUndo);
        Assert.Equal(hadPreviousValue ? "old" : null, key.GetValue("01"));
        if (hadPreviousValue) Assert.Equal(RegistryValueKind.String, key.GetValueKind("01"));
        Assert.Equal(7, key.GetValue("04"));
    }

    [Fact]
    public void RestorePreservesLaterExternalChangeAndRecoveryRecord()
    {
        Settings.Enable();
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, true)!;
        key.SetValue("01", 0);
        Assert.Throws<InvalidOperationException>(() => Settings.Restore());
        Assert.Equal(0, key.GetValue("01"));
        Assert.True(Settings.CanUndo);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        Directory.Delete(_directory, true);
    }
}
