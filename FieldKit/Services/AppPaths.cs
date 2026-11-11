using System.Security.AccessControl;
using System.Security.Principal;

namespace FieldKit.Services;

/// <summary>
/// Where FieldKit keeps its own files. Everything lives under one folder at the
/// root of the system drive so it does not depend on which account approved the
/// elevation prompt, and so temp cleanup never touches the logs.
///
/// FieldKit runs elevated and both writes and deletes here, so the folder is a
/// privilege boundary. A standard user can create folders at the drive root, and
/// could plant a junction that points an elevated delete or write at a system
/// directory. The folder is therefore owned by Administrators with a protected
/// ACL (Users: read only), an existing folder is only adopted after its ACL has
/// been locked and its whole tree checked for links, and every subfolder is
/// checked again at the point of use.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();
    public static string Tools => Path.Combine(Root, "Tools");
    public static string Reports => Path.Combine(Root, "Reports");

    /// <summary>The log folder, created if missing. Throws if it is a link.</summary>
    public static string Logs => EnsureSubfolder("Logs");

    /// <summary>
    /// Creates a direct subfolder of the root if needed and returns its path.
    /// Refuses a link: writing or deleting through one would act on its target.
    /// </summary>
    public static string EnsureSubfolder(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        if (IsReparsePoint(path))
            throw new IOException($"{path} is a link. FieldKit will not write through it.");
        return path;
    }

    private static string ResolveRoot()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        string[] candidates =
        [
            Path.Combine(systemRoot, "FieldKit"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FieldKit"),
            Path.Combine(Path.GetTempPath(), "FieldKit")
        ];

        foreach (var candidate in candidates)
        {
            if (TryPrepare(candidate))
                return candidate;
        }

        return candidates[^1];
    }

    private static bool TryPrepare(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);

            if (SystemInfo.IsRunningAsAdministrator())
            {
                if (info.Exists)
                {
                    if (IsReparsePoint(path)) return false;

                    // Lock the folder first, then inspect it: once the ACL is in
                    // place nothing new can be planted by a standard user.
                    info.SetAccessControl(BuildRootSecurity());
                    if (FindReparsePoint(path) is not null) return false;
                    ResetChildSecurity(info);
                }
                else
                {
                    info.Create(BuildRootSecurity());
                }
            }
            else
            {
                // Not elevated (UI development only): there is no privilege to
                // protect, and an unelevated process cannot assign this owner.
                // The next elevated launch adopts the folder through the branch above.
                info.Create();
                if (IsReparsePoint(path)) return false;
            }

            var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or InvalidOperationException or IdentityNotMappedException
                                       or PrivilegeNotHeldException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>Owner Administrators; SYSTEM and Administrators full control; Users read and run. Nothing inherited from the parent.</summary>
    private static DirectorySecurity BuildRootSecurity()
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(Administrators,
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// Gives everything already inside an adopted folder the same owner and an
    /// inherit-only ACL. Without this, whoever created a child still owns it and
    /// could grant themselves write access again.
    /// </summary>
    private static void ResetChildSecurity(DirectoryInfo root)
    {
        foreach (var entry in root.EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"{entry.FullName} is a link.");

            if (entry is DirectoryInfo directory)
            {
                var security = new DirectorySecurity();
                security.SetOwner(Administrators);
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                directory.SetAccessControl(security);
                ResetChildSecurity(directory);
            }
            else if (entry is FileInfo file)
            {
                var security = new FileSecurity();
                security.SetOwner(Administrators);
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                file.SetAccessControl(security);
            }
        }
    }

    /// <summary>
    /// Returns the first symlink or junction found anywhere under <paramref name="root"/>,
    /// or null. Links are reported, never followed.
    /// </summary>
    public static string? FindReparsePoint(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return entry.FullName;
                if (entry is DirectoryInfo)
                    pending.Push(entry.FullName);
            }
        }

        return null;
    }

    /// <summary>
    /// True for a symlink or junction. Also true when the attributes cannot be
    /// read, so callers that are about to delete treat "unknown" as "do not touch".
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }
}
