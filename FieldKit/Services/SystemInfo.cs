using System.Management;
using Microsoft.Win32;

namespace FieldKit.Services;

public static class SystemInfo
{
    public static string GetComputerName() => Environment.MachineName;

    public static string GetUserName() => Environment.UserName;

    public static string GetOsCaption()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
                return obj["Caption"]?.ToString() ?? "Unknown";
        }
        catch { }
        return "Unknown";
    }

    public static double? GetFreeSpaceGB(char driveLetter)
    {
        try
        {
            var drive = new DriveInfo(driveLetter.ToString());
            if (drive.IsReady)
                return Math.Round(drive.AvailableFreeSpace / 1_073_741_824.0, 2);
        }
        catch { }
        return null;
    }

    public static async Task<bool> HasInternetAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("http://www.msftconnecttest.com/connecttest.txt");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static string GetLastBootTime()
    {
        try
        {
            var uptime = Environment.TickCount64;
            var bootTime = DateTime.Now.AddMilliseconds(-uptime);
            var ago = DateTime.Now - bootTime;
            string agoText = ago.TotalDays >= 1
                ? $"{(int)ago.TotalDays}d ago"
                : $"{(int)ago.TotalHours}h ago";
            return $"{bootTime:MMM d} ({agoText})";
        }
        catch { return "Unknown"; }
    }

    public static string GetLastWindowsUpdate()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT InstalledOn FROM Win32_QuickFixEngineering");
            DateTime latest = DateTime.MinValue;
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var val = obj["InstalledOn"];
                    if (val is string s && DateTime.TryParse(s, out var dt) && dt > latest)
                        latest = dt;
                }
            }
            if (latest == DateTime.MinValue) return "Unknown";
            var daysAgo = (int)(DateTime.Now - latest).TotalDays;
            return daysAgo == 0 ? "Today" : $"{latest:MMM d} ({daysAgo}d ago)";
        }
        catch { return "Unknown"; }
    }

    public static bool HasPendingReboot()
    {
        string[] rebootKeys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"
        ];

        foreach (var keyPath in rebootKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key != null) return true;
            }
            catch { }
        }

        // PendingFileRenameOperations is intentionally excluded — it is commonly set
        // by app installers, AV software, and Windows servicing during normal operation
        // and does not reliably indicate that a reboot is actually needed.

        return false;
    }
}
