using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace FieldKit.Services;

public static class SystemInfo
{
    public static string GetProductName() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "FieldKit";

    public static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

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

    public static string GetOsVersionSummary()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    return $"{obj["Caption"]} {obj["Version"]} build {obj["BuildNumber"]} ({obj["OSArchitecture"]})";
                }
            }
        }
        catch { }

        return "Unknown";
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

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string BuildRunManifest(IEnumerable<string> selectedOperations, string logPath)
    {
        var operationList = selectedOperations.ToList();
        var sb = new StringBuilder();

        sb.AppendLine("Run manifest");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  FieldKit:      {GetVersion()}");
        sb.AppendLine($"  Windows:       {GetOsVersionSummary()}");
        sb.AppendLine($"  Computer:      {GetComputerName()}");
        sb.AppendLine($"  User:          {GetUserName()}");
        sb.AppendLine($"  Elevated:      {(IsRunningAsAdministrator() ? "Yes" : "No")}");
        sb.AppendLine($"  Last boot:     {GetLastBootTime()}");
        sb.AppendLine($"  Pending reboot:{(HasPendingReboot() ? " Yes" : " No")}");
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            var driveLetter = root[0];
            var freeSpace = GetFreeSpaceGB(driveLetter);
            if (freeSpace.HasValue)
                sb.AppendLine($"  {driveLetter}: free:       {freeSpace.Value:F2} GB");
        }
        sb.AppendLine($"  Log:           {logPath}");
        sb.AppendLine($"  Selected:      {operationList.Count}");
        foreach (var operation in operationList)
            sb.AppendLine($"    - {operation}");

        return sb.ToString();
    }

    public static string BuildFullReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"System Information Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Generated by {GetProductName()} v{GetVersion()}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        sb.AppendLine("NOTE: This report contains identifying information about");
        sb.AppendLine("your computer (hostname, username, serial numbers, MAC and");
        sb.AppendLine("IP addresses). Review before sharing.");
        sb.AppendLine();

        sb.AppendLine("COMPUTER");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Name:          {Environment.MachineName}");
        sb.AppendLine($"  User:          {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"  Uptime:        {FormatUptime()}");
        sb.AppendLine();

        sb.AppendLine("OPERATING SYSTEM");
        sb.AppendLine(new string('-', 60));
        AppendWmi(sb, "Win32_OperatingSystem", obj =>
        {
            sb.AppendLine($"  OS:            {obj["Caption"]}");
            sb.AppendLine($"  Version:       {obj["Version"]}  (Build {obj["BuildNumber"]})");
            sb.AppendLine($"  Architecture:  {obj["OSArchitecture"]}");
            sb.AppendLine($"  Install date:  {FormatWmiDate(obj["InstallDate"]?.ToString())}");
        });
        sb.AppendLine();

        sb.AppendLine("PROCESSOR");
        sb.AppendLine(new string('-', 60));
        AppendWmi(sb, "Win32_Processor", obj =>
        {
            sb.AppendLine($"  Name:          {obj["Name"]?.ToString()?.Trim()}");
            sb.AppendLine($"  Cores:         {obj["NumberOfCores"]} cores, {obj["NumberOfLogicalProcessors"]} threads");
            sb.AppendLine($"  Max speed:     {obj["MaxClockSpeed"]} MHz");
        });
        sb.AppendLine();

        sb.AppendLine("MEMORY");
        sb.AppendLine(new string('-', 60));
        AppendWmi(sb, "Win32_ComputerSystem", obj =>
        {
            var totalBytes = Convert.ToDouble(obj["TotalPhysicalMemory"]);
            sb.AppendLine($"  Total:         {totalBytes / (1024 * 1024 * 1024):F1} GB");
        });
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer FROM Win32_PhysicalMemory");
            int slot = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    slot++;
                    var cap = Convert.ToDouble(obj["Capacity"]) / (1024 * 1024 * 1024);
                    var speed = obj["Speed"];
                    var mfr = obj["Manufacturer"]?.ToString()?.Trim();
                    sb.AppendLine($"  Slot {slot}:        {cap:F0} GB{(speed != null ? $"  {speed} MHz" : "")}{(string.IsNullOrEmpty(mfr) ? "" : $"  ({mfr})")}");
                }
            }
        }
        catch { }
        sb.AppendLine();

        sb.AppendLine("BIOS / MOTHERBOARD");
        sb.AppendLine(new string('-', 60));
        AppendWmi(sb, "Win32_BIOS", obj =>
        {
            sb.AppendLine($"  Manufacturer:  {obj["Manufacturer"]}");
            sb.AppendLine($"  Version:       {obj["SMBIOSBIOSVersion"]}");
            sb.AppendLine($"  Serial:        {obj["SerialNumber"]}");
        });
        AppendWmi(sb, "Win32_BaseBoard", obj =>
        {
            sb.AppendLine($"  Board:         {obj["Manufacturer"]} {obj["Product"]}");
        });
        sb.AppendLine();

        sb.AppendLine("GRAPHICS");
        sb.AppendLine(new string('-', 60));
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var ram = Convert.ToDouble(obj["AdapterRAM"] ?? 0);
                    var ramText = ram > 0 ? $"  ({ram / (1024 * 1024 * 1024):F0} GB)" : "";
                    sb.AppendLine($"  {obj["Name"]}{ramText}");
                    sb.AppendLine($"    Driver:      {obj["DriverVersion"]}");
                }
            }
        }
        catch { sb.AppendLine("  Unable to query"); }
        sb.AppendLine();

        sb.AppendLine("STORAGE");
        sb.AppendLine(new string('-', 60));
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            foreach (var d in drives)
            {
                var total = d.TotalSize / (1024.0 * 1024 * 1024);
                var free = d.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                var pct = (1 - free / total) * 100;
                sb.AppendLine($"  {d.Name,-4} {d.DriveFormat,-6}  {total:F0} GB total,  {free:F1} GB free  ({pct:F0}% used)");
            }
        }
        catch { sb.AppendLine("  Unable to query"); }
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType, Size FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var size = Convert.ToDouble(obj["Size"] ?? 0) / (1024.0 * 1024 * 1024);
                    var mediaType = obj["MediaType"]?.ToString() ?? "";
                    sb.AppendLine($"  {obj["Model"]}  ({size:F0} GB, {mediaType})");
                }
            }
        }
        catch { }
        sb.AppendLine();

        sb.AppendLine("NETWORK");
        sb.AppendLine(new string('-', 60));
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            foreach (var adapter in adapters)
            {
                sb.AppendLine($"  {adapter.Name}  ({adapter.NetworkInterfaceType})");
                sb.AppendLine($"    MAC:         {adapter.GetPhysicalAddress()}");
                var props = adapter.GetIPProperties();
                var gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (gateway is not null)
                    sb.AppendLine($"    Gateway:     {gateway.Address}");
                foreach (var dns in props.DnsAddresses.Where(d => d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                    sb.AppendLine($"    DNS:         {dns}");
                foreach (var ip in props.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        sb.AppendLine($"    IPv4:        {ip.Address}");
                    else if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                             && !ip.Address.IsIPv6LinkLocal)
                        sb.AppendLine($"    IPv6:        {ip.Address}");
                }
            }
        }
        catch { sb.AppendLine("  Unable to query"); }
        sb.AppendLine();

        sb.AppendLine("RECENT WINDOWS UPDATES");
        sb.AppendLine(new string('-', 60));
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT HotFixID, InstalledOn, Description FROM Win32_QuickFixEngineering");
            var patches = new List<(string Id, DateTime Date, string Desc)>();
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var id = obj["HotFixID"]?.ToString() ?? "";
                    var desc = obj["Description"]?.ToString() ?? "";
                    if (obj["InstalledOn"] is string s && DateTime.TryParse(s, out var dt))
                        patches.Add((id, dt, desc));
                }
            }
            foreach (var p in patches.OrderByDescending(p => p.Date).Take(10))
                sb.AppendLine($"  {p.Date:yyyy-MM-dd}  {p.Id,-12}  {p.Desc}");
            if (patches.Count > 10)
                sb.AppendLine($"  ... and {patches.Count - 10} more");
        }
        catch { sb.AppendLine("  Unable to query"); }
        sb.AppendLine();

        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"{GetProductName()} v{GetVersion()}");
        sb.AppendLine("Created by Keith S. Crawford (@tsudo)");
        sb.AppendLine("Licensed under the MIT License");
        sb.AppendLine("Not affiliated with Microsoft Corporation.");
        sb.AppendLine("https://github.com/tsudo/fieldkit");

        return sb.ToString();
    }

    private static void AppendWmi(StringBuilder sb, string className, Action<ManagementObject> handler)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj) { handler(obj); }
            }
        }
        catch { sb.AppendLine("  Unable to query"); }
    }

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var boot = DateTime.Now - uptime;
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h (since {boot:MMM d, HH:mm})";
        return $"{uptime.Hours}h {uptime.Minutes}m (since {boot:HH:mm})";
    }

    private static string FormatWmiDate(string? wmiDate)
    {
        if (wmiDate == null) return "Unknown";
        try
        {
            var dt = ManagementDateTimeConverter.ToDateTime(wmiDate);
            return dt.ToString("yyyy-MM-dd");
        }
        catch { return wmiDate; }
    }
}
