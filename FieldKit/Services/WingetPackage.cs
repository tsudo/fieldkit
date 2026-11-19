using System.Runtime.InteropServices;
using System.Text;

namespace FieldKit.Services;

internal static class WingetPackage
{
    private const string Family = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
    private const int InsufficientBuffer = 122;
    private enum PackageOrigin { Inbox = 2, Store = 3, LineOfBusiness = 6 }

    public static string? FindExecutable()
    {
        uint count = 0, chars = 0;
        if (GetPackagesByPackageFamily(Family, ref count, null, ref chars, IntPtr.Zero) != InsufficientBuffer)
            return null;
        var names = new IntPtr[count];
        var buffer = Marshal.AllocHGlobal(checked((int)chars * sizeof(char)));
        try
        {
            if (GetPackagesByPackageFamily(Family, ref count, names, ref chars, buffer) != 0) return null;
            for (var i = 0; i < count; i++)
            {
                var name = Marshal.PtrToStringUni(names[i]);
                if (name is null || GetStagedPackageOrigin(name, out var origin) != 0 ||
                    origin is not (PackageOrigin.Store or PackageOrigin.Inbox or PackageOrigin.LineOfBusiness))
                    continue;

                uint length = 0;
                if (GetPackagePathByFullName(name, ref length, null) != InsufficientBuffer) continue;
                var path = new StringBuilder(checked((int)length));
                if (GetPackagePathByFullName(name, ref length, path) != 0) continue;
                var exe = GetExecutablePath(name, path.ToString());
                if (exe is not null && File.Exists(exe)) return exe;
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static string? GetExecutablePath(string fullName, string installFolder)
    {
        // Ignore loose development registrations and resource packages. An
        // installed MSIX lives under WindowsApps with its full package name.
        var folder = new DirectoryInfo(installFolder);
        if (!fullName.StartsWith("Microsoft.DesktopAppInstaller_", StringComparison.OrdinalIgnoreCase)
            || !fullName.EndsWith("__8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase)
            || !folder.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(folder.Parent?.Name, "WindowsApps", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.Combine(folder.FullName, "winget.exe");
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count,
        [Out] IntPtr[]? names, ref uint bufferLength, IntPtr buffer);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);

    [DllImport("kernelbase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetStagedPackageOrigin(string fullName, out PackageOrigin origin);
}
