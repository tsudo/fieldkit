namespace FieldKit.Tasks;

public sealed class WingetUpdateTask : MaintenanceTask
{
    private const int WingetNoApplicableUpdates = unchecked((int)0x8A150021);

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

        if (dryRun)
        {
            var listResult = await RunCommandAsync(winget, "upgrade", ct, line => !string.IsNullOrWhiteSpace(line));
            return listResult.ExitCode == 0
                ? TaskResult.Ok("Listed available winget upgrades")
                : TaskResult.Warn("winget upgrade list returned a non-zero exit code");
        }

        Log("Some installers may still show prompts even when silent mode is requested.", "WARN");
        var result = await RunCommandAsync(
            winget,
            "upgrade --all --silent --accept-source-agreements --accept-package-agreements",
            ct,
            line => !string.IsNullOrWhiteSpace(line));

        if (result.ExitCode == 0)
            return TaskResult.Ok("winget upgrades completed");
        if (result.ExitCode == WingetNoApplicableUpdates)
            return TaskResult.Ok("No applicable winget updates were found");

        return TaskResult.Warn($"winget returned exit code {result.ExitCode}");
    }

    private static string? ResolveWingetPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packagesRoot = Path.Combine(localAppData, "Microsoft", "WindowsApps");
        var directPath = Path.Combine(packagesRoot, "winget.exe");
        return File.Exists(directPath) ? directPath : null;
    }
}
