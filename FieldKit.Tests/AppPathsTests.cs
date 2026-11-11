using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
    public void A_junction_is_recognised_and_a_plain_folder_is_not()
    {
        var link = Path.Combine(_root, "Reports", "pc-1", "planted");
        MakeJunction(link, _outside);

        Assert.True(AppPaths.IsReparsePoint(link));
        Assert.False(AppPaths.IsReparsePoint(_root));
    }

    // ---- Trust rule for the kit folder -------------------------------

    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsers = new(WellKnownSidType.AuthenticatedUserSid, null);
    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    [Fact]
    public void The_security_FieldKit_creates_meets_its_own_trust_rule()
    {
        var security = AppPaths.BuildRootSecurity();

        Assert.Equal(Admins, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(security.AreAccessRulesProtected);
        Assert.True(AppPaths.IsTrusted(security));
    }

    [Fact]
    public void A_folder_owned_by_an_ordinary_account_is_not_trusted()
    {
        var security = AppPaths.BuildRootSecurity();
        security.SetOwner(Users);

        Assert.False(AppPaths.IsTrusted(security));
    }

    [Fact]
    public void A_folder_that_inherits_its_parents_acl_is_not_trusted()
    {
        var security = AppPaths.BuildRootSecurity();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);

        Assert.False(AppPaths.IsTrusted(security));
    }

    [Theory]
    [InlineData(FileSystemRights.Modify)]
    [InlineData(FileSystemRights.Write)]
    [InlineData(FileSystemRights.CreateFiles)]
    [InlineData(FileSystemRights.CreateDirectories)]
    [InlineData(FileSystemRights.Delete)]
    [InlineData(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.TakeOwnership)]
    public void Any_write_right_for_non_administrators_breaks_trust(FileSystemRights rights)
    {
        var security = AppPaths.BuildRootSecurity();
        security.AddAccessRule(new FileSystemAccessRule(AuthenticatedUsers, rights, Inherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.False(AppPaths.IsTrusted(security));
    }

    [Fact]
    public void Write_access_for_one_named_account_can_be_allowed_and_no_other()
    {
        var me = Users; // stands in for "the current account"
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, Inherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, Inherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.True(AppPaths.OnlyTheseCanWrite(security, me));
        Assert.False(AppPaths.OnlyTheseCanWrite(security, alsoAllowed: null));

        // A sandbox or service account with Modify: trees must not be deleted here.
        security.AddAccessRule(new FileSystemAccessRule(AuthenticatedUsers, FileSystemRights.Modify, Inherit,
            PropagationFlags.None, AccessControlType.Allow));
        Assert.False(AppPaths.OnlyTheseCanWrite(security, me));
    }

    [Fact]
    public void A_link_anywhere_in_an_unpacked_download_is_detected()
    {
        Assert.False(SupportKitService.ContainsLink(_root));
        MakeJunction(Path.Combine(_root, "Reports", "pc-1", "planted"), _outside);
        Assert.True(SupportKitService.ContainsLink(_root));
    }

    [Fact]
    public void Read_access_for_non_administrators_keeps_trust()
    {
        var security = AppPaths.BuildRootSecurity();
        security.AddAccessRule(new FileSystemAccessRule(AuthenticatedUsers, FileSystemRights.ReadAndExecute, Inherit,
            PropagationFlags.None, AccessControlType.Allow));

        Assert.True(AppPaths.IsTrusted(security));
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
    public void A_folder_is_as_new_as_the_newest_file_inside_it()
    {
        var old = DateTime.Now.AddDays(-10);
        var workDir = Path.Combine(_root, "installer-work");
        var deep = Path.Combine(workDir, "a", "b");
        Directory.CreateDirectory(deep);
        var fresh = Path.Combine(deep, "in-use.tmp");
        File.WriteAllText(fresh, "x");

        // Make every folder look old, the way an installer's working folder does.
        Directory.SetLastWriteTime(deep, old);
        Directory.SetLastWriteTime(Path.Combine(workDir, "a"), old);
        Directory.SetLastWriteTime(workDir, old);

        Assert.True(FieldKit.Tasks.ClearTemporaryFilesTask.NewestWriteUnder(workDir) > DateTime.Now.AddMinutes(-5));

        File.SetLastWriteTime(fresh, old);
        Assert.True(FieldKit.Tasks.ClearTemporaryFilesTask.NewestWriteUnder(workDir) < DateTime.Now.AddDays(-9));
    }

    [Fact]
    public void A_program_is_recognised_by_its_header_not_its_name()
    {
        var disguised = Path.Combine(_root, "readme.txt");
        File.Copy(typeof(AppPathsTests).Assembly.Location, disguised);
        var plain = Path.Combine(_root, "notes.exe");
        File.WriteAllText(plain, "just text");

        Assert.True(SupportKitService.IsWindowsProgram(disguised));
        Assert.False(SupportKitService.IsWindowsProgram(plain));
    }

    [Fact]
    public void A_path_that_cannot_be_read_counts_as_a_link()
    {
        Assert.True(AppPaths.IsReparsePoint(Path.Combine(_root, "does-not-exist")));
    }
}
