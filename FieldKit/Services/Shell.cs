using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;

namespace FieldKit.Services;

public static class Shell
{
    /// <summary>
    /// Opens a folder, file or URL through explorer.exe. FieldKit runs elevated;
    /// handing the target to Explorer lets the already-running desktop shell open
    /// it, instead of starting a browser or editor with administrator rights.
    /// </summary>
    public static bool Open(string target, Logger? logger = null)
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = explorer,
                Arguments = $"\"{target}\"",
                UseShellExecute = false
            });
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            logger?.Log($"Could not open {target}: {ex.Message}", "WARN");
            return false;
        }
    }

    /// <summary>Name of the account signed in at the console, or null if it cannot be read.</summary>
    public static string? GetInteractiveUser()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UserName FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                    return obj["UserName"]?.ToString();
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Unknown is a valid answer here; the caller shows no notice.
        }
        return null;
    }

    /// <summary>
    /// True when the account FieldKit runs as is not the one signed in at the
    /// desktop, which happens when a standard user's elevation prompt is approved
    /// with a different administrator account.
    /// </summary>
    public static bool RunsAsDifferentUser(out string interactive, out string current)
    {
        current = $"{Environment.UserDomainName}\\{Environment.UserName}";
        interactive = GetInteractiveUser() ?? "";
        return interactive.Length > 0 && !interactive.Equals(current, StringComparison.OrdinalIgnoreCase);
    }
}
