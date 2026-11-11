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
/// could plant a junction or hard link that points an elevated delete or write
/// at a system file. The rule is therefore: FieldKit only uses a folder that is
/// owned by Administrators or SYSTEM and that nobody else can write to. It
/// creates the folder that way. A folder that exists and does not meet the rule
/// is never adopted or repaired: it is renamed aside, contents untouched, and a
/// fresh one is created.
/// </summary>
public static class AppPaths
{
    // Lazy rather than a field initializer: static fields initialize in file
    // order, and an initializer here once ran before a field it depended on.
    private static readonly Lazy<string> LazyRoot = new(ResolveRoot);

    public static string Root => LazyRoot.Value;

    /// <summary>
    /// True when neither the drive root nor ProgramData gave a trusted folder and
    /// this launch is using a throwaway folder in temp. The window says so,
    /// because tools and logs will not be where the user expects.
    /// </summary>
    public static bool IsTemporaryRoot =>
        Path.GetFileName(Root).StartsWith("FieldKit-", StringComparison.OrdinalIgnoreCase);
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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FieldKit")
        ];

        var elevated = SystemInfo.IsRunningAsAdministrator();
        foreach (var candidate in candidates)
        {
            if (elevated ? TryPrepareTrusted(candidate) : TryPreparePlain(candidate))
                return candidate;
        }

        // Nothing usable. A per-launch folder in temp keeps the app running; it
        // is new and randomly named, so nothing could have been planted in it.
        // Known-folder path, not Path.GetTempPath(): that one reads the TEMP variable.
        var userTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        var fallback = Path.Combine(userTemp, $"FieldKit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>Elevated: end with a folder that meets the trust rule, or report failure.</summary>
    private static bool TryPrepareTrusted(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);

            if (info.Exists && !(IsTrustedFolder(info)))
            {
                // Rename, don't inspect or fix. Moving a directory entry never
                // follows a link, and the contents stay available to whoever
                // made them.
                // The random suffix stops anyone pre-creating the destination name to block the rename.
                var suffix = Guid.NewGuid().ToString("N")[..8];
                Directory.Move(path, $"{path}.untrusted-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}");
                info.Refresh();
            }

            if (!info.Exists)
                info.Create(BuildRootSecurity());

            // Create() returns quietly if something appeared at the path first,
            // so the result is checked rather than assumed.
            info.Refresh();
            if (!IsTrustedFolder(info)) return false;

            return CanWrite(path);
        }
        catch (Exception ex) when (IsFileSystemOrSecurityFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Not elevated (UI development only): there is no privilege to protect and
    /// an unelevated process cannot create an Administrators-owned folder. The
    /// next elevated launch will rename this folder aside and make its own.
    /// </summary>
    private static bool TryPreparePlain(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return !IsReparsePoint(path) && CanWrite(path);
        }
        catch (Exception ex) when (IsFileSystemOrSecurityFailure(ex))
        {
            return false;
        }
    }

    private static bool CanWrite(string path)
    {
        var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(probe, "");
        File.Delete(probe);
        return true;
    }

    private static bool IsFileSystemOrSecurityFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException
            or InvalidOperationException or IdentityNotMappedException
            or PrivilegeNotHeldException or System.Security.SecurityException;

    private static bool IsTrustedFolder(DirectoryInfo info) =>
        !info.Attributes.HasFlag(FileAttributes.ReparsePoint)
        && IsTrusted(info.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access));

    /// <summary>Owner Administrators; SYSTEM and Administrators full control; Users read and run. Nothing inherited from the parent.</summary>
    internal static DirectorySecurity BuildRootSecurity()
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(administrators,
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system,
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(users,
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// The trust rule: owned by Administrators or SYSTEM, takes nothing from its
    /// parent's ACL, and grants no one else anything beyond read and execute.
    /// </summary>
    internal static bool IsTrusted(DirectorySecurity security)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        bool IsPrivileged(IdentityReference? identity) =>
            identity is SecurityIdentifier sid && (sid.Equals(administrators) || sid.Equals(system));

        if (!IsPrivileged(security.GetOwner(typeof(SecurityIdentifier)))) return false;
        if (!security.AreAccessRulesProtected) return false;

        return OnlyTheseCanWrite(security, alsoAllowed: null);
    }

    /// <summary>
    /// True when nobody except Administrators, SYSTEM and (optionally) one named
    /// account is granted more than read and execute. Used to decide whether a
    /// folder is safe to delete trees from: if anyone else can write there, they
    /// can swap a subfolder for a junction while an elevated delete is walking it.
    /// </summary>
    internal static bool OnlyTheseCanWrite(FileSystemSecurity security, SecurityIdentifier? alsoAllowed)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        const FileSystemRights readOnly = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is SecurityIdentifier sid
                && (sid.Equals(administrators) || sid.Equals(system) || (alsoAllowed is not null && sid.Equals(alsoAllowed))))
            {
                continue;
            }
            if ((rule.FileSystemRights & ~readOnly) != 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a folder's own ACL lets only Administrators, SYSTEM and the
    /// current account write to it. False when the ACL cannot be read.
    /// </summary>
    public static bool IsWritableOnlyByThisAccount(string directory)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access);
            return OnlyTheseCanWrite(security, identity.User);
        }
        catch (Exception ex) when (IsFileSystemOrSecurityFailure(ex))
        {
            return false;
        }
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
