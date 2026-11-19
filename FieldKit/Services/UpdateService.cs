namespace FieldKit.Services;

public sealed class UpdateScan
{
    internal IReadOnlyList<WindowsPackage> WindowsPackages { get; set; } = [];
    public IReadOnlyList<string> WindowsTitles => WindowsPackages.Select(p => p.Title).ToList();
    public IReadOnlyList<WingetUpgrade> Apps { get; internal set; } = [];
    public string? WindowsProblem { get; internal set; }
    public string? AppsProblem { get; internal set; }
    public string? HistoryProblem { get; internal set; }
    public bool RestartPending { get; internal set; }
    public DateTime CheckedAt { get; internal set; }
}

/// <summary>The GUI and command line use the same discovery, install and history paths.</summary>
public sealed class UpdateService(Logger logger)
{
    private const string WindowsSource = "Windows Update";
    private const string AppsSource = "App updates (winget)";

    public async Task<UpdateScan> ScanAsync(bool windows, bool apps, CancellationToken ct)
    {
        using var log = logger.CreateUpdateLog();
        var started = DateTime.Now;
        var scan = new UpdateScan();
        var items = new List<RunItem>();
        var stopped = false;
        async Task ScanWindows()
        {
            try
            {
                log.Log("Checking Windows Update...");
                scan.WindowsPackages = await Task.Run(new WindowsUpdates().Scan, ct)
                    .WaitAsync(TimeSpan.FromMinutes(5), ct);
                foreach (var title in scan.WindowsTitles) log.Log($"Pending Windows update: {title}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                scan.WindowsProblem = ex is TimeoutException ? "Windows Update did not respond within five minutes." : ex.Message;
            }
        }
        async Task ScanApps()
        {
            try
            {
                var preferences = UpdatePreferences.Load();
                log.Log("Checking winget for app updates...");
                var (found, problem) = await new WingetService(log).ListUpgradesAsync(ct);
                scan.Apps = found.Where(app => !preferences.IsSkipped(app.Id))
                    .DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase).ToList();
                scan.AppsProblem = problem;
                foreach (var app in scan.Apps) log.Log($"Pending app update: {app.Id} {app.InstalledVersion} → {app.AvailableVersion}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { scan.AppsProblem = ex.Message; }
        }
        try
        {
            await Task.WhenAll(windows ? ScanWindows() : Task.CompletedTask, apps ? ScanApps() : Task.CompletedTask);
            scan.RestartPending = SystemInfo.HasPendingReboot();
            if (windows) items.Add(ScanResult(WindowsSource, scan.WindowsTitles.Count, scan.WindowsProblem));
            if (apps) items.Add(ScanResult(AppsSource, scan.Apps.Count, scan.AppsProblem));
            return scan;
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            items.Add(new("Update check", "Skipped", "Stopped before the check completed."));
            throw;
        }
        finally
        {
            scan.CheckedAt = DateTime.Now;
            var history = Finish("Check for updates", started, items, true, stopped, scan.RestartPending, log);
            scan.HistoryProblem = history.Items.LastOrDefault(i => i.Name == "Update history")?.Summary;
        }
    }

    private static RunItem ScanResult(string source, int count, string? problem) =>
        new(source, problem is null ? "Success" : "Error",
            problem is null ? $"{count} eligible update(s)." : $"{count} confirmed. {problem}");

    public async Task<RunSummary> ApplyAsync(UpdateScan scan, bool windows, IReadOnlyCollection<string> appIds,
        CancellationToken ct, bool includeApps = false)
    {
        using var log = logger.CreateUpdateLog();
        var started = DateTime.Now;
        var items = new List<RunItem>();
        bool reboot = scan.RestartPending, stopped = false;
        void Record(RunItem item)
        {
            items.Add(item);
            log.Log($"{item.Name}: {item.Summary}", item.State == "Error" ? "ERROR" : "INFO");
        }
        try
        {
            if (scan.HistoryProblem is not null) Record(new("Update history", "Error", scan.HistoryProblem));
            using var lease = AcquireLease();
            ct.ThrowIfCancellationRequested();
            if (windows)
            {
                if (scan.WindowsProblem is not null) Record(new(WindowsSource, "Error", scan.WindowsProblem));
                else if (scan.WindowsPackages.Count == 0) Record(new(WindowsSource, "Success", "No eligible updates."));
                else
                {
                    try
                    {
                        await Task.Run(() => new WindowsUpdates().Install(scan.WindowsPackages, Record,
                            () => reboot = true, log, ct), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Record(new(WindowsSource, "Error", ex.Message));
                    }
                }
            }
            if (includeApps || appIds.Count > 0)
            {
                if (scan.AppsProblem is not null) Record(new(AppsSource, "Error", scan.AppsProblem));
                else if (appIds.Count == 0) Record(new(AppsSource, "Success", "No eligible app updates."));
                var preferences = UpdatePreferences.Load();
                var winget = new WingetService(log);
                foreach (var app in SelectApps(scan.Apps, appIds))
                {
                    ct.ThrowIfCancellationRequested();
                    if (preferences.IsSkipped(app.Id))
                    {
                        Record(new(app.Name, "Skipped", $"{app.Id} is excluded in config.json."));
                        continue;
                    }
                    try
                    {
                        log.Log($"Upgrading {app.Name} [{app.Id}]");
                        var outcome = await winget.UpgradeAsync(app.Id, ct);
                        reboot |= outcome.Message.Contains("Restart", StringComparison.Ordinal);
                        Record(new(app.Name, outcome.Succeeded ? "Success" : "Error", outcome.Message));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Record(new(app.Name, "Error", ex.Message));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
            Record(new("Updates", "Skipped", "Stopped before the run completed. Check again for remaining updates."));
        }
        catch (Exception ex) { Record(new("Updates", "Error", ex.Message)); }
        return Finish("Install updates", started, items, false, stopped, reboot, log);
    }

    internal static IReadOnlyList<WingetUpgrade> SelectApps(IReadOnlyList<WingetUpgrade> found, IReadOnlyCollection<string> requested)
    {
        var ids = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = found.Where(app => ids.Remove(app.Id)).ToList();
        if (ids.Count != 0)
            throw new InvalidOperationException($"Package was not in this update check: {string.Join(", ", ids)}");
        return selected;
    }

    private static FileStream AcquireLease()
    {
        if (AppPaths.IsTemporaryRoot)
            throw new InvalidOperationException("A permanent FieldKit folder is required to coordinate update runs.");
        try
        {
            return new FileStream(Path.Combine(AppPaths.Root, ".update-running.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) == 32)
        {
            throw new InvalidOperationException("Another FieldKit update run is already active on this PC.", ex);
        }
    }

    private static RunSummary Finish(string title, DateTime started, IReadOnlyList<RunItem> items,
        bool preview, bool stopped, bool reboot, Logger log)
    {
        var summary = new RunSummary("Updates", title, started, DateTime.Now,
            items.Count(i => i.State == "Success"), items.Count(i => i.State == "Warning"),
            items.Count(i => i.State == "Error"), items.Count(i => i.State == "Skipped"),
            preview, stopped, reboot, null, null, log.LogFilePath, items);
        log.LogLines(summary.ToLogText(), summary.Error > 0 ? "ERROR" : "INFO");
        if (log.WriteFailed)
            summary = WithHistoryError(summary, $"Could not write the full update log to {log.LogFilePath}.");
        if (!RunHistory.Save(summary, log))
            summary = WithHistoryError(summary, "Could not save this run in update history.");
        try { UpdatePreferences.Load().PruneUpdateLogs(AppPaths.Logs); }
        catch (Exception ex) { log.Log($"Could not apply update-log retention: {ex.Message}", "WARN"); }
        return summary;
    }

    private static RunSummary WithHistoryError(RunSummary run, string problem) =>
        run with { Error = run.Error + 1, Items = [.. run.Items, new("Update history", "Error", problem)] };
}
