namespace FieldKit.Tasks;

public sealed class WindowsUpdateTask : MaintenanceTask
{
    public WindowsUpdateTask() : base(
        "Windows Update",
        "Starts a Windows Update scan and opens the Update experience for review.",
        "Updates",
        "1-5 min",
        requiresInternet: true,
        mayRequireReboot: true)
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would trigger Windows Update via UsoClient.", "INFO");
            return TaskResult.Ok("Previewed Windows Update trigger");
        }

        var usoClient = Path.Combine(Environment.SystemDirectory, "UsoClient.exe");
        if (!File.Exists(usoClient))
            return TaskResult.Warn("UsoClient is unavailable; open Settings > Windows Update manually");

        var result = await RunCommandAsync(usoClient, "ScanInstallWait", ct, line => !string.IsNullOrWhiteSpace(line));
        if (result.ExitCode == 0)
            return TaskResult.Ok("Windows Update scan started. Review results in Settings.", rebootRecommended: true);

        return TaskResult.Warn("Windows Update trigger returned a non-zero exit code");
    }
}
