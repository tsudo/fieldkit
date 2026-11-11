using System.Diagnostics;
using System.IO;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fieldkit-tests-{Guid.NewGuid():N}");
    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"fieldkit-outside-{Guid.NewGuid():N}");

    public AppPathsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Reports", "pc-1"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_root, "Reports", "pc-1", "system.txt"), "report");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "must not be touched");
    }

    private readonly List<string> _junctions = new();

    public void Dispose()
    {
        // Remove each junction as a plain directory entry first. A recursive
        // delete over a junction throws when not elevated, although it only
        // ever removes the link.
        foreach (var junction in _junctions)
            if (Directory.Exists(junction)) Directory.Delete(junction, recursive: false);

        // Whatever a test did, the folder behind the junction must be intact.
        Assert.True(File.Exists(Path.Combine(_outside, "secret.txt")));

        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        if (Directory.Exists(_outside)) Directory.Delete(_outside, recursive: true);
    }

    private void MakeJunction(string link, string target)
    {
        _junctions.Add(link);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void Clean_tree_has_no_links()
    {
        Assert.Null(AppPaths.FindReparsePoint(_root));
        Assert.False(AppPaths.IsReparsePoint(_root));
    }

    [Fact]
    public void Junction_anywhere_in_the_tree_is_found()
    {
        var link = Path.Combine(_root, "Reports", "pc-1", "planted");
        MakeJunction(link, _outside);

        Assert.Equal(link, AppPaths.FindReparsePoint(_root));
        Assert.True(AppPaths.IsReparsePoint(link));
    }

    [Fact]
    public void File_walk_does_not_leave_through_a_junction()
    {
        MakeJunction(Path.Combine(_root, "Reports", "planted"), _outside);

        var files = SupportKitService.EnumerateFilesWithoutFollowingLinks(Path.Combine(_root, "Reports")).ToList();

        var only = Assert.Single(files);
        Assert.EndsWith("system.txt", only);
    }

    [Fact]
    public void File_walk_refuses_a_root_that_is_itself_a_junction()
    {
        var link = Path.Combine(_root, "Logs");
        MakeJunction(link, _outside);

        Assert.Empty(SupportKitService.EnumerateFilesWithoutFollowingLinks(link));
        Assert.True(File.Exists(Path.Combine(_outside, "secret.txt")));
    }

    [Fact]
    public void A_path_that_cannot_be_read_counts_as_a_link()
    {
        Assert.True(AppPaths.IsReparsePoint(Path.Combine(_root, "does-not-exist")));
    }
}
