using System.IO.Compression;
using System.Text;
using Microsoft.Win32;

namespace FieldKit.Services;

public sealed record KitTool(string Key, string Name, string Note, string ZipName, string Size);

public sealed record KitReport(string Key, string Name, string Note, string FileName);

public sealed record KitOutcome(bool Succeeded, string Message);

/// <summary>
/// Builds the support kit: Sysinternals tools downloaded from Microsoft, plus
/// text snapshots of this PC, in one folder under <see cref="AppPaths.Root"/>.
/// The tools are downloaded when the kit is built because the Sysinternals
/// licence does not allow them to be redistributed inside FieldKit.
/// </summary>
public sealed class SupportKitService
{
    // The only host FieldKit downloads executables from. The tool list is fixed
    // in code; nothing the user types becomes part of a download URL.
    private const string DownloadBase = "https://download.sysinternals.com/files/";

    public static readonly IReadOnlyList<KitTool> Tools =
    [
        new("autoruns", "Autoruns", "Everything that starts with Windows", "Autoruns.zip", "3 MB"),
        new("procexp", "Process Explorer", "Task Manager with full detail", "ProcessExplorer.zip", "4 MB"),
        new("procmon", "Process Monitor", "Live file, registry and process activity", "ProcessMonitor.zip", "3 MB"),
        new("tcpview", "TCPView", "Which program owns each network connection", "TCPView.zip", "2 MB"),
        new("suite", "Full Sysinternals suite", "Every Sysinternals tool", "SysinternalsSuite.zip", "360 MB")
    ];

    public static readonly IReadOnlyList<KitReport> Reports =
    [
        new("system", "System summary", "Hardware, Windows version, storage, recent updates", "system-summary.txt"),
        new("network", "Network configuration", "Output of ipconfig /all", "network-configuration.txt"),
        new("apps", "Installed apps", "Name, version and publisher from the registry", "installed-apps.txt"),
        new("startup", "Startup items", "Programs set to run at sign-in", "startup-items.txt"),
        new("events", "Recent errors", "Error and critical events from the last 7 days", "recent-errors.txt")
    ];

    private const string SharingNote =
        "NOTE: This file contains identifying details about this computer. Review it before sharing.";

    private readonly Logger _logger;

    public SupportKitService(Logger logger) => _logger = logger;

    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "FieldKit support kit.url");

    public static string ToolFolder(KitTool tool) => Path.Combine(AppPaths.Tools, Path.GetFileNameWithoutExtension(tool.ZipName));

    public static bool IsToolPresent(KitTool tool)
    {
        try { return Directory.Exists(ToolFolder(tool)) && Directory.EnumerateFiles(ToolFolder(tool), "*.exe").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // ----------------------------------------------------------------
    // Tools
    // ----------------------------------------------------------------

    public async Task<KitOutcome> InstallToolAsync(KitTool tool, CancellationToken ct)
    {
        var staging = Path.Combine(AppPaths.Tools, $".staging-{tool.Key}");
        var zipPath = Path.Combine(AppPaths.Tools, $".download-{tool.Key}.zip");
        var target = ToolFolder(tool);

        try
        {
            // Throws if Tools is a link. Leftovers from an earlier attempt are
            // removed as entries, never followed, before anything is written.
            AppPaths.EnsureSubfolder("Tools");
            DeleteDirectoryIfPresent(staging);
            DeleteFileIfPresent(zipPath);

            _logger.Log($"[Support kit] Downloading {tool.Name} from {DownloadBase}{tool.ZipName}", "INFO");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
            using (var response = await client.GetAsync(DownloadBase + tool.ZipName, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    return new KitOutcome(false, $"Download failed: HTTP {(int)response.StatusCode}");

                await using var file = File.Create(zipPath);
                await response.Content.CopyToAsync(file, ct);
            }

            // ExtractToDirectory rejects entries that would land outside the target.
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, staging), ct);

            // Decide what stays by content, not by file name. Every Windows
            // program (anything starting with the "MZ" header, whatever its
            // extension) must be Microsoft-signed or the whole download is
            // rejected. Plain .txt files stay. Everything else is removed: help
            // files, scripts and shortcuts cannot be signed, and a DLL placed
            // beside a signed tool would be loaded by it.
            var executables = new List<string>();
            var dropped = 0;
            foreach (var file in EnumerateFilesWithoutFollowingLinks(staging).ToList())
            {
                ct.ThrowIfCancellationRequested();

                if (IsWindowsProgram(file))
                {
                    if (!Authenticode.IsSignedByMicrosoft(file, out var reason))
                    {
                        _logger.Log($"[Support kit] {Path.GetFileName(file)}: {reason}", "ERROR");
                        return Reject(staging, $"{Path.GetFileName(file)} failed its signature check; nothing was kept");
                    }
                    if (Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        executables.Add(file);
                }
                else if (!Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    dropped++;
                }
            }

            if (executables.Count == 0)
                return Reject(staging, "The download contained no programs");
            if (dropped > 0)
                _logger.Log($"[Support kit] {tool.Name}: removed {dropped} file(s) that cannot be verified (help files and similar).", "INFO");

            if (!DeleteDirectoryIfPresent(target))
            {
                DeleteDirectoryIfPresent(staging);
                return new KitOutcome(false, $"The existing copy of {tool.Name} is open. Close it and build the kit again.");
            }
            Directory.Move(staging, target);

            _logger.Log($"[Support kit] {tool.Name}: {executables.Count} Microsoft-signed program(s) in {target}", "OK");
            return new KitOutcome(true, $"Ready, {executables.Count} signed program(s)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeleteDirectoryIfPresent(staging);
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient reports its own timeout as a cancellation. Nobody pressed Stop.
            _logger.Log($"[Support kit] {tool.Name}: the download timed out.", "ERROR");
            DeleteDirectoryIfPresent(staging);
            return new KitOutcome(false, "The download timed out. Check the internet connection and try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.Log($"[Support kit] {tool.Name} failed: {ex.Message}", "ERROR");
            DeleteDirectoryIfPresent(staging);
            return new KitOutcome(false, ex is HttpRequestException ? "Download failed. Check the internet connection." : ex.Message);
        }
        finally
        {
            DeleteFileIfPresent(zipPath);
        }
    }

    /// <summary>True when the file starts with the "MZ" header every Windows executable, DLL and driver has.</summary>
    internal static bool IsWindowsProgram(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    private KitOutcome Reject(string staging, string message)
    {
        DeleteDirectoryIfPresent(staging);
        return new KitOutcome(false, message);
    }

    // ----------------------------------------------------------------
    // Reports
    // ----------------------------------------------------------------

    /// <summary>Creates a folder for one snapshot, named for this PC and the time.</summary>
    public static string CreateSnapshotFolder()
    {
        var folder = Path.Combine(AppPaths.EnsureSubfolder("Reports"), $"{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        if (AppPaths.IsReparsePoint(folder))
            throw new IOException($"{folder} is a link. FieldKit will not write through it.");
        return folder;
    }

    public async Task<KitOutcome> WriteReportAsync(KitReport report, string folder, CancellationToken ct)
    {
        try
        {
            var body = report.Key switch
            {
                "system" => await Task.Run(SystemInfo.BuildFullReport, ct),
                "network" => await CaptureAsync(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/all", ct),
                "apps" => await Task.Run(BuildInstalledApps, ct),
                "startup" => await Task.Run(BuildStartupItems, ct),
                "events" => await CaptureRecentErrorsAsync(ct),
                _ => throw new ArgumentOutOfRangeException(nameof(report), report.Key, "Unknown report")
            };

            var header = report.Key == "system"
                ? ""
                : $"{report.Name} - {Environment.MachineName} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{SharingNote}{Environment.NewLine}{Environment.NewLine}";

            var path = Path.Combine(folder, report.FileName);
            await File.WriteAllTextAsync(path, header + body, Encoding.UTF8, ct);
            _logger.Log($"[Support kit] Wrote {path}", "OK");
            return new KitOutcome(true, "Written");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            _logger.Log($"[Support kit] {report.Name} failed: {ex.Message}", "ERROR");
            return new KitOutcome(false, ex.Message);
        }
    }

    private static async Task<string> CaptureAsync(string fileName, string arguments, CancellationToken ct)
    {
        var result = await CommandRunner.RunAsync(fileName, arguments, cancellationToken: ct, timeout: TimeSpan.FromMinutes(2));
        return string.Join(Environment.NewLine, result.AllLines);
    }

    private static async Task<string> CaptureRecentErrorsAsync(CancellationToken ct)
    {
        // "No events matched" is a clean result. Any other failure to read the
        // log must not be written up as "no errors": the script exits 3 instead.
        const string script =
            "$events = @(); " +
            "try { $events = @(Get-WinEvent -FilterHashtable @{LogName='System','Application'; Level=1,2; StartTime=(Get-Date).AddDays(-7)} " +
            "-MaxEvents 300 -ErrorAction Stop) } " +
            "catch { if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { 'FIELDKIT_EVENTLOG_FAILED ' + $_.Exception.Message; exit 3 } } " +
            "if ($events.Count -eq 0) { 'No error or critical events in the last 7 days.' } else { " +
            "$events | ForEach-Object { '{0:yyyy-MM-dd HH:mm}  {1,-11} {2}  (ID {3})' -f $_.TimeCreated, $_.LevelDisplayName, $_.ProviderName, $_.Id; " +
            "$first = ($_.Message -split \"`n\")[0]; if ($first) { '    ' + $first.Trim() } } }";

        var result = await CommandRunner.RunPowerShellAsync(script, cancellationToken: ct, timeout: TimeSpan.FromMinutes(3));
        if (result.ExitCode != 0)
        {
            var detail = result.AllLines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? $"exit code {result.ExitCode}";
            throw new InvalidOperationException($"The event log could not be read: {detail.Replace("FIELDKIT_EVENTLOG_FAILED ", "")}");
        }
        return string.Join(Environment.NewLine, result.AllLines);
    }

    private static string BuildInstalledApps()
    {
        var apps = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Read(RegistryHive hive, RegistryView view)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) return;

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display)) continue;
                    if (key.GetValue("SystemComponent") is int system && system == 1) continue;

                    var version = key.GetValue("DisplayVersion") as string ?? "";
                    var publisher = key.GetValue("Publisher") as string ?? "";
                    apps[$"{display}|{version}"] = $"{display,-60} {version,-22} {publisher}";
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                apps[$"~unreadable {hive} {view}"] = $"(could not read {hive} {view}: {ex.Message})";
            }
        }

        Read(RegistryHive.LocalMachine, RegistryView.Registry64);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32);
        Read(RegistryHive.CurrentUser, RegistryView.Default);

        var sb = new StringBuilder();
        sb.AppendLine($"{"Name",-60} {"Version",-22} Publisher");
        sb.AppendLine(new string('-', 100));
        foreach (var line in apps.Values) sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine($"{apps.Count} entries. Microsoft Store apps are not listed here.");
        return sb.ToString();
    }

    private static string BuildStartupItems()
    {
        var sb = new StringBuilder();

        void ReadRunKey(RegistryHive hive, RegistryView view, string path, string label)
        {
            sb.AppendLine(label);
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(path);
                var names = key?.GetValueNames() ?? [];
                if (names.Length == 0) sb.AppendLine("  (none)");
                foreach (var name in names)
                    sb.AppendLine($"  {name} = {key!.GetValue(name)}");
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                sb.AppendLine($"  (could not read: {ex.Message})");
            }
            sb.AppendLine();
        }

        void ReadFolder(Environment.SpecialFolder folder, string label)
        {
            sb.AppendLine(label);
            try
            {
                var files = Directory.EnumerateFiles(Environment.GetFolderPath(folder))
                    .Where(file => !Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (files.Count == 0) sb.AppendLine("  (none)");
                foreach (var file in files) sb.AppendLine($"  {Path.GetFileName(file)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                sb.AppendLine($"  (could not read: {ex.Message})");
            }
            sb.AppendLine();
        }

        const string run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string runOnce = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

        sb.AppendLine($"Per-user entries below belong to the account FieldKit ran as: {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine();
        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, run, "All users: Run (64-bit)");
        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, run, "All users: Run (32-bit)");
        ReadRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, runOnce, "All users: RunOnce");
        ReadRunKey(RegistryHive.CurrentUser, RegistryView.Default, run, "This account: Run");
        ReadRunKey(RegistryHive.CurrentUser, RegistryView.Default, runOnce, "This account: RunOnce");
        ReadFolder(Environment.SpecialFolder.CommonStartup, "All users: Startup folder");
        ReadFolder(Environment.SpecialFolder.Startup, "This account: Startup folder");
        sb.AppendLine("Services, scheduled tasks and drivers are not listed here. Autoruns shows all of them.");
        return sb.ToString();
    }

    // ----------------------------------------------------------------
    // Shortcut, zip, remove
    // ----------------------------------------------------------------

    public KitOutcome CreateDesktopShortcut()
    {
        try
        {
            var url = new Uri(AppPaths.Root).AbsoluteUri;
            // Remove whatever is there first so the write can never go through a planted link.
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            File.WriteAllText(ShortcutPath, $"[InternetShortcut]{Environment.NewLine}URL={url}{Environment.NewLine}");
            return new KitOutcome(true, "Shortcut added to the desktop for all users");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log($"[Support kit] Desktop shortcut failed: {ex.Message}", "WARN");
            return new KitOutcome(false, ex.Message);
        }
    }

    /// <summary>
    /// Zips Reports and Logs for handing to someone else. Tools are left out:
    /// they are Microsoft's to distribute, and anyone can download them.
    /// </summary>
    public async Task<(KitOutcome Outcome, string? ZipPath)> ZipForSharingAsync(CancellationToken ct)
    {
        var zipPath = Path.Combine(AppPaths.Root, $"FieldKit-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        try
        {
            var count = await Task.Run(() =>
            {
                var added = 0;
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                foreach (var folder in new[] { AppPaths.Reports, Path.Combine(AppPaths.Root, "Logs") })
                {
                    foreach (var file in EnumerateFilesWithoutFollowingLinks(folder))
                    {
                        ct.ThrowIfCancellationRequested();

                        var entry = archive.CreateEntry(Path.GetRelativePath(AppPaths.Root, file).Replace('\\', '/'));
                        // ReadWrite sharing: the current session's log is still open for writing.
                        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var destination = entry.Open();
                        source.CopyTo(destination);
                        added++;
                    }
                }
                return added;
            }, ct);

            if (count == 0)
            {
                DeleteFileIfPresent(zipPath);
                return (new KitOutcome(false, "There are no reports or logs to zip yet"), null);
            }

            _logger.Log($"[Support kit] Zipped {count} file(s) to {zipPath}", "OK");
            return (new KitOutcome(true, $"{count} file(s) zipped"), zipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log($"[Support kit] Zip failed: {ex.Message}", "ERROR");
            DeleteFileIfPresent(zipPath);
            return (new KitOutcome(false, ex.Message), null);
        }
    }

    /// <summary>
    /// Removes tools, reports, zips, earlier logs and the desktop shortcut. The
    /// log of the session doing the removing stays: it is open, and it is the
    /// record that the removal happened.
    /// </summary>
    public KitOutcome RemoveKit()
    {
        if (AppPaths.IsReparsePoint(AppPaths.Root))
            return new KitOutcome(false, "The kit folder is a link; refusing to delete through it");

        var problems = 0;
        var removed = 0;

        foreach (var folder in new[] { AppPaths.Tools, AppPaths.Reports })
        {
            if (!Directory.Exists(folder)) continue;
            if (DeleteDirectoryIfPresent(folder)) removed++; else problems++;
        }

        try
        {
            foreach (var zip in Directory.EnumerateFiles(AppPaths.Root, "FieldKit-*.zip"))
                if (DeleteFileIfPresent(zip)) removed++; else problems++;

            // Not AppPaths.Logs: that would recreate the folder, and it throws on a link.
            var logs = Path.Combine(AppPaths.Root, "Logs");
            if (Directory.Exists(logs))
            {
                if (AppPaths.IsReparsePoint(logs))
                {
                    // A link where the log folder should be: remove the link itself
                    // and touch nothing behind it.
                    if (DeleteDirectoryIfPresent(logs)) removed++; else problems++;
                }
                else
                {
                    foreach (var file in Directory.EnumerateFiles(logs))
                    {
                        if (file.Equals(_logger.LogFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (DeleteFileIfPresent(file)) removed++; else problems++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log($"[Support kit] Could not list the kit folder: {ex.Message}", "WARN");
            problems++;
        }

        if (File.Exists(ShortcutPath) && !DeleteFileIfPresent(ShortcutPath)) problems++;

        _logger.Log($"[Support kit] Removed {removed} item(s) from {AppPaths.Root}; {problems} could not be removed.",
            problems == 0 ? "OK" : "WARN");

        return problems == 0
            ? new KitOutcome(true, "Kit removed. This session's log is kept until FieldKit closes.")
            : new KitOutcome(false, $"{problems} item(s) could not be removed. Close any kit tools that are open and try again.");
    }

    /// <summary>
    /// Every regular file under <paramref name="root"/>. A folder or file that is
    /// a symlink or junction is skipped, so the walk cannot leave the kit folder.
    /// </summary>
    internal static IEnumerable<string> EnumerateFilesWithoutFollowingLinks(string root)
    {
        if (!Directory.Exists(root) || AppPaths.IsReparsePoint(root))
            yield break;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry is DirectoryInfo) pending.Push(entry.FullName);
                else yield return entry.FullName;
            }
        }
    }

    private bool DeleteDirectoryIfPresent(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return true;

            // A link is removed as a link; its target is never followed.
            if (AppPaths.IsReparsePoint(path)) Directory.Delete(path, recursive: false);
            else Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log($"[Support kit] Could not remove {path}: {ex.Message}", "WARN");
            return false;
        }
    }

    private bool DeleteFileIfPresent(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log($"[Support kit] Could not remove {path}: {ex.Message}", "WARN");
            return false;
        }
    }
}
