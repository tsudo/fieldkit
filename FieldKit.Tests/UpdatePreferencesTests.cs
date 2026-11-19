using System.IO;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public sealed class UpdatePreferencesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fieldkit-config-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void FirstReadCreatesDefaultsAndLaterReadsHonorEdits()
    {
        Assert.Equal(90, UpdatePreferences.Load(_root).LogRetentionDays);
        File.WriteAllText(Path.Combine(_root, "config.json"), """{"skipAppIds":["Mozilla.Firefox"],"logRetentionDays":0}""");
        var edited = UpdatePreferences.Load(_root);
        Assert.True(edited.IsSkipped("mozilla.firefox"));
        Assert.False(edited.IsSkipped("Mozilla.Other"));
        Assert.Equal(0, edited.LogRetentionDays);
    }

    [Theory]
    [InlineData("""{"skipAppId":["Mozilla.Firefox"]}""")]
    [InlineData("""{"logRetentionDays":-1}""")]
    [InlineData("null")]
    public void InvalidPreferencesFailWithoutReplacingTheFile(string invalid)
    {
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, invalid);
        Assert.Throws<InvalidDataException>(() => UpdatePreferences.Load(_root));
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void RetentionOnlyPrunesOldUpdateHistory()
    {
        string[] names = ["FieldKit-Update-old.log", "run-old-updates.json", "FieldKit-other.log", "run-old-maintain.json"];
        foreach (var name in names)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, "");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-100));
        }
        var recent = Path.Combine(_root, "FieldKit-Update-current.log");
        File.WriteAllText(recent, "");
        new UpdatePreferences().PruneUpdateLogs(_root);
        Assert.Equal(new[] { false, false, true, true }, names.Select(n => File.Exists(Path.Combine(_root, n))).ToArray());
        Assert.True(File.Exists(recent));
    }
}
