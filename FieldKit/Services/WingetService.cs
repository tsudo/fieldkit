using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FieldKit.Services;

public sealed record WingetUpgrade(string Name, string Id, string InstalledVersion, string AvailableVersion);

/// <summary>
/// What a scan learned. <see cref="InstalledIds"/> is null when winget could not
/// say what is installed: that is "unknown", which is not the same as "nothing".
/// <see cref="Problem"/> is set when part of the scan could not be trusted.
/// </summary>
public sealed record WingetScan(IReadOnlySet<string>? InstalledIds, IReadOnlyList<WingetUpgrade> Upgrades, string? Problem);

public sealed record WingetOutcome(bool Succeeded, int ExitCode, string Message);

/// <summary>
/// Everything FieldKit asks of winget: what is installed, what has an update,
/// install one package, upgrade one package. Only the community "winget" source
/// is used, and every package is addressed by exact ID.
/// </summary>
public sealed partial class WingetService
{
    private const string CommonFlags = "--source winget --accept-source-agreements --disable-interactivity";

    // winget's own result codes (APPINSTALLER_CLI_ERROR_*), from
    // doc/windows/package-manager/winget/returnCodes.md in microsoft/winget-cli.
    private const int NoApplicableUpgrade = unchecked((int)0x8A15002B);
    private const int NoPackagesFound = unchecked((int)0x8A150014);
    private const int AlreadyInstalled = unchecked((int)0x8A150061);
    private const int InstallerFailed = unchecked((int)0x8A150006);
    private const int PackageInUse = unchecked((int)0x8A150101);
    private const int RebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int RebootInitiated = unchecked((int)0x8A15010B);
    private const int CancelledInInstaller = unchecked((int)0x8A15010C);
    private const int InstallerSaysAlreadyInstalled = unchecked((int)0x8A15010D);
    private const int BlockedByPolicy = unchecked((int)0x8A15003A);

    private readonly Logger _logger;

    public WingetService(Logger logger) => _logger = logger;

    /// <summary>
    /// Path to winget.exe, or null. winget is a per-user app alias, so when a
    /// different administrator account approved the elevation prompt it may be
    /// missing from that account even though the signed-in user has it.
    /// </summary>
    public static string? ResolvePath()
    {
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(alias)) return null;

        // The real entry is an app execution alias, which is a reparse point
        // created by Windows. An ordinary file at this user-writable path is
        // something else wearing the name, and FieldKit would run it elevated.
        try { return File.GetAttributes(alias).HasFlag(FileAttributes.ReparsePoint) ? alias : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task<WingetScan> ScanAsync(CancellationToken ct)
    {
        var winget = ResolvePath() ?? throw new InvalidOperationException("winget is not installed for this account.");

        var installed = await GetInstalledIdsAsync(winget, ct);
        var problems = new List<string>();
        if (installed is null)
            problems.Add("winget could not report which apps are installed, so installed apps are not marked.");

        var result = await CommandRunner.RunAsync(winget, $"upgrade {CommonFlags}",
            cancellationToken: ct, outputEncoding: Encoding.UTF8, timeout: TimeSpan.FromMinutes(3));

        if (result.ExitCode != 0 && result.ExitCode != NoApplicableUpgrade)
        {
            foreach (var line in result.AllLines.Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(5))
                _logger.Log($"[Apps] winget: {line.Trim()}", "WARN");
            throw new InvalidOperationException($"winget could not list updates (code 0x{result.ExitCode:X8}).");
        }

        var upgrades = ParseUpgradeTable(result.Output);
        var announced = ParseAnnouncedCount(result.Output);
        if (announced is { } expected && expected != upgrades.Count)
        {
            problems.Add($"winget reported {expected} update(s) but FieldKit could read {upgrades.Count} of them. " +
                         "Run \"winget upgrade\" in a terminal to see the full list.");
        }

        var problem = problems.Count == 0 ? null : string.Join(" ", problems);
        _logger.Log($"[Apps] Scan found {(installed is null ? "an unknown number of" : installed.Count.ToString())} installed package(s) known to winget, " +
                    $"{upgrades.Count} with an update.{(problem is null ? "" : " " + problem)}", problem is null ? "INFO" : "WARN");
        return new WingetScan(installed, upgrades, problem);
    }

    /// <summary>The number in winget's "N upgrades available." line, or null when there is no such line.</summary>
    public static int? ParseAnnouncedCount(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = AnsiPattern().Replace(raw, "").Trim();
            var match = CountLinePattern().Match(line);
            if (match.Success && int.TryParse(line.Split(' ', 2)[0], out var count))
                return count;
        }
        return null;
    }

    /// <summary>Installed package IDs, or null when winget's export could not be produced or read.</summary>
    private async Task<IReadOnlySet<string>?> GetInstalledIdsAsync(string winget, CancellationToken ct)
    {
        // `winget export` is the one command that reports installed packages as
        // JSON, so installed state never depends on parsing a text table.
        var exportFile = Path.Combine(AppPaths.Root, $".winget-export-{Guid.NewGuid():N}.json");
        try
        {
            // winget can exit non-zero here and still write a usable file (it warns
            // about installed apps that no source knows). The file decides.
            var export = await CommandRunner.RunAsync(winget, $"export -o \"{exportFile}\" {CommonFlags}",
                cancellationToken: ct, outputEncoding: Encoding.UTF8, timeout: TimeSpan.FromMinutes(3));

            if (!File.Exists(exportFile))
            {
                _logger.Log($"[Apps] winget export produced no file (code 0x{export.ExitCode:X8}); installed state is unknown.", "WARN");
                return null;
            }

            return ParseExport(await File.ReadAllTextAsync(exportFile, ct));
        }
        catch (JsonException ex)
        {
            _logger.Log($"[Apps] Could not read winget's export: {ex.Message}", "WARN");
            return null;
        }
        finally
        {
            try { File.Delete(exportFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Log($"[Apps] Could not remove {exportFile}: {ex.Message}", "WARN");
            }
        }
    }

    public static IReadOnlySet<string> ParseExport(string json)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return ids;

        foreach (var source in sources.EnumerateArray())
        {
            if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var package in packages.EnumerateArray())
            {
                if (package.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { Length: > 0 } value)
                    ids.Add(value);
            }
        }

        return ids;
    }

    /// <summary>
    /// Reads the table `winget upgrade` prints. There is no JSON form of this
    /// command, so each row is split from the right: the last columns (available
    /// version, installed version, ID) never contain spaces, while the name can.
    /// That holds whatever the column widths are, and whether or not winget adds
    /// a Source column. A row whose ID does not look like Publisher.Package is
    /// dropped rather than guessed at.
    /// </summary>
    public static IReadOnlyList<WingetUpgrade> ParseUpgradeTable(IEnumerable<string> lines)
    {
        var upgrades = new List<WingetUpgrade>();
        var inTable = false;
        var hasSourceColumn = false;

        foreach (var raw in lines)
        {
            var line = AnsiPattern().Replace(raw, "").TrimEnd();
            // A progress spinner can share the header's line, separated by carriage returns.
            var lastReturn = line.LastIndexOf('\r');
            if (lastReturn >= 0) line = line[(lastReturn + 1)..];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (!inTable)
            {
                if (tokens.Length >= 4 && tokens[0] == "Name" && tokens.Contains("Id")
                    && tokens.Contains("Version") && tokens.Contains("Available"))
                {
                    inTable = true;
                    hasSourceColumn = tokens[^1] == "Source";
                }
                continue;
            }

            if (line.StartsWith('-')) continue;

            // The count line ends the first table. Anything after it is the
            // "require explicit targeting" list: packages the user has pinned.
            if (CountLinePattern().IsMatch(line)) break;

            var tail = hasSourceColumn ? 1 : 0;
            if (tokens.Length < 4 + tail) continue;
            if (hasSourceColumn && !tokens[^1].Equals("winget", StringComparison.OrdinalIgnoreCase)) continue;

            var available = tokens[^(1 + tail)];
            var installed = tokens[^(2 + tail)];
            var idIndex = tokens.Length - 3 - tail;

            // "< 1.2.3" is how winget shows an installed version it could only bound.
            if (idIndex >= 2 && tokens[idIndex] is "<" or ">")
            {
                installed = $"{tokens[idIndex]} {installed}";
                idIndex--;
            }

            var id = tokens[idIndex];
            if (idIndex < 1 || !PackageIdPattern().IsMatch(id)) continue;

            var name = string.Join(' ', tokens[..idIndex]);
            upgrades.Add(new WingetUpgrade(name, id, installed, available));
        }

        return upgrades;
    }

    public Task<WingetOutcome> InstallAsync(string packageId, CancellationToken ct) =>
        RunPackageCommandAsync("install", packageId, ct);

    public Task<WingetOutcome> UpgradeAsync(string packageId, CancellationToken ct) =>
        RunPackageCommandAsync("upgrade", packageId, ct);

    public static string BuildPackageArguments(string verb, string packageId)
    {
        if (!PackageIdPattern().IsMatch(packageId))
            throw new ArgumentException($"Not a winget package ID: {packageId}", nameof(packageId));

        return $"{verb} --id \"{packageId}\" --exact --silent --accept-package-agreements {CommonFlags}";
    }

    private async Task<WingetOutcome> RunPackageCommandAsync(string verb, string packageId, CancellationToken ct)
    {
        var winget = ResolvePath();
        if (winget is null)
            return new WingetOutcome(false, -1, "winget is not installed for this account");

        var result = await CommandRunner.RunAsync(
            winget,
            BuildPackageArguments(verb, packageId),
            (line, level) => _logger.Log($"[{packageId}] {line}", level),
            ct,
            line => !string.IsNullOrWhiteSpace(line),
            Encoding.UTF8,
            TimeSpan.FromMinutes(30));

        return DescribeExit(verb, result.ExitCode);
    }

    public static WingetOutcome DescribeExit(string verb, int exitCode) => exitCode switch
    {
        0 => new WingetOutcome(true, 0, verb == "install" ? "Installed" : "Updated"),
        NoApplicableUpgrade => new WingetOutcome(true, exitCode, "Already up to date"),
        AlreadyInstalled or InstallerSaysAlreadyInstalled => new WingetOutcome(true, exitCode, "Already installed"),
        RebootRequiredToFinish or RebootInitiated => new WingetOutcome(true, exitCode, "Done. Restart the PC to finish."),
        CancelledInInstaller => new WingetOutcome(false, exitCode, "Cancelled in the app's installer"),
        NoPackagesFound => new WingetOutcome(false, exitCode, "winget could not find this package"),
        PackageInUse => new WingetOutcome(false, exitCode, "The app is running. Close it and try again."),
        BlockedByPolicy => new WingetOutcome(false, exitCode, "Blocked by a policy on this PC"),
        InstallerFailed => new WingetOutcome(false, exitCode, "The app's installer failed. See the log."),
        _ => new WingetOutcome(false, exitCode, $"winget returned 0x{exitCode:X8}. See the log.")
    };

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex(@"^\d+\s+upgrades?\s+available", RegexOptions.IgnoreCase)]
    private static partial Regex CountLinePattern();

    // Publisher.Package with at least one dot; letters, digits and . _ + - only.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_+\-]*(\.[A-Za-z0-9_+\-]+)+$")]
    private static partial Regex PackageIdPattern();
}
