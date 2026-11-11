using System.Windows;
using FieldKit.Services;

namespace FieldKit.Tasks;

public sealed class WingetUpdateTask : MaintenanceTask
{
    private const int WingetNoApplicableUpdates = unchecked((int)0x8A150021);
    private readonly WingetUpdateService _wingetUpdateService = new();

    public WingetUpdateTask() : base(
        "Update Installed Apps",
        "Updates third-party apps with winget when App Installer is available.",
        "Updates",
        "1-5 min",
        requiresInternet: true)
    {
    }

    public override Task GatherContextAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ContextInfo = ResolveWingetPath() is null
                ? "winget not found"
                : "winget available";
        }, ct);
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        var winget = ResolveWingetPath();
        if (winget is null)
            return TaskResult.Warn("winget is not installed. Install App Installer from Microsoft Store.");

        Log("Scanning available app updates with winget. No packages will be installed before confirmation.", "INFO");
        var listResult = await RunCommandAsync(winget, "upgrade", ct, line => !string.IsNullOrWhiteSpace(line));
        var candidates = _wingetUpdateService.ParseUpgradeCandidates(listResult.AllLines);

        if (listResult.ExitCode == WingetNoApplicableUpdates || candidates.Count == 0)
            return TaskResult.Ok("No applicable winget updates were found");

        Log($"{candidates.Count} winget upgrade candidate(s) found.", "INFO");
        foreach (var candidate in candidates)
            Log($"Candidate: {candidate.Name} [{candidate.Id}] {candidate.InstalledVersion} -> {candidate.AvailableVersion}", "INFO");

        if (dryRun)
            return TaskResult.Ok($"Listed {candidates.Count} available winget upgrade(s)");

        var selection = await ShowSelectionWindowAsync(candidates, ct);
        if (selection is null)
            return TaskResult.Skipped($"Scanned {candidates.Count} app update(s); user cancelled");

        if (selection.Count == 0)
            return TaskResult.Ok($"Scanned {candidates.Count} app update(s); no packages installed");

        Log($"Installing {selection.Count} selected app update(s).", "WARN");

        var succeeded = 0;
        var failed = 0;
        foreach (var candidate in selection)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            Log($"Starting {candidate.Name} [{candidate.Id}] {candidate.InstalledVersion} -> {candidate.AvailableVersion}", "INFO");

            var result = await RunCommandAsync(
                winget,
                WingetUpdateService.BuildUpgradeArguments(candidate.Id),
                ct,
                line => !string.IsNullOrWhiteSpace(line));

            sw.Stop();
            if (result.ExitCode == 0 || result.ExitCode == WingetNoApplicableUpdates)
            {
                succeeded++;
                Log($"Completed {candidate.Name} [{candidate.Id}] in {FormatDuration(sw.Elapsed)}", "OK");
            }
            else
            {
                failed++;
                Log($"Failed {candidate.Name} [{candidate.Id}] in {FormatDuration(sw.Elapsed)}. winget exit code {result.ExitCode}", "WARN");
            }
        }

        return failed == 0
            ? TaskResult.Ok($"Installed {succeeded} selected app update(s)")
            : TaskResult.Warn($"Installed {succeeded} selected app update(s); {failed} failed");
    }

    private static Task<IReadOnlyList<WingetPackageCandidate>?> ShowSelectionWindowAsync(
        IReadOnlyList<WingetPackageCandidate> candidates,
        CancellationToken ct)
    {
        var app = Application.Current;
        if (app is null)
            return Task.FromResult<IReadOnlyList<WingetPackageCandidate>?>([]);

        return app.Dispatcher.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var window = new WingetUpdateWindow(candidates)
            {
                Owner = app.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            };

            var accepted = window.ShowDialog() == true;
            if (!accepted)
                return null;

            return window.ApplyUpdates
                ? window.SelectedCandidates
                : [];
        }).Task;
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

        return $"{Math.Max(1, (int)Math.Round(elapsed.TotalSeconds))}s";
    }

    private static string? ResolveWingetPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packagesRoot = Path.Combine(localAppData, "Microsoft", "WindowsApps");
        var directPath = Path.Combine(packagesRoot, "winget.exe");
        return File.Exists(directPath) ? directPath : null;
    }
}
