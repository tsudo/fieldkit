using System.Security.AccessControl;
using System.Security.Principal;

namespace FieldKit.Services;

internal static class RecoveryStore
{
    internal static string DirectoryPath
    {
        get
        {
            if (!SystemInfo.IsRunningAsAdministrator() || AppPaths.IsTemporaryRoot)
                throw new InvalidOperationException("Recovery requires an administrator and a permanent FieldKit folder.");
            var folder = new DirectoryInfo(Path.Combine(AppPaths.Root, "Recovery"));
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            if (!folder.Exists)
            {
                var acl = new DirectorySecurity();
                acl.SetOwner(administrators);
                acl.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { administrators, system })
                    acl.AddAccessRule(new(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                folder.Create(acl);
            }
            if (AppPaths.IsReparsePoint(folder.FullName))
                throw new IOException("The recovery folder is a link or inaccessible.");
            var permissions = folder.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!AppPaths.IsTrusted(permissions) || permissions.GetAccessRules(true, true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>().Any(rule => rule.AccessControlType == AccessControlType.Allow &&
                        !rule.IdentityReference.Equals(administrators) && !rule.IdentityReference.Equals(system)))
                throw new UnauthorizedAccessException("The recovery folder must be private to Administrators and SYSTEM.");
            return folder.FullName;
        }
    }
}
