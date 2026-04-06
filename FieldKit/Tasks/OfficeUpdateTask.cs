namespace FieldKit.Tasks;

public sealed class OfficeUpdateTask : MaintenanceTask
{
    public OfficeUpdateTask() : base(
        "Update Microsoft Office",
        "Triggers Click-to-Run updates for Microsoft 365 and compatible Office installs.",
        "Updates",
        "Quick",
        requiresInternet: true)
    {
    }

    public override Task GatherContextAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ContextInfo = FindClickToRunClient() is null
                ? "Click-to-Run client not detected"
                : "Click-to-Run client detected";
        }, ct);
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        var client = FindClickToRunClient();
        if (client is null)
            return TaskResult.Warn("Office Click-to-Run client not found. Update manually from an Office app.");

        if (dryRun)
        {
            Log($"Would run {client} /update user", "INFO");
            return TaskResult.Ok("Previewed Office update");
        }

        var result = await RunCommandAsync(client, "/update user", ct, line => !string.IsNullOrWhiteSpace(line));
        return result.ExitCode == 0
            ? TaskResult.Ok("Office update triggered")
            : TaskResult.Warn("Office update command returned a non-zero exit code");
    }

    private static string? FindClickToRunClient()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Microsoft Shared", "ClickToRun", "OfficeC2RClient.exe"),
            Path.Combine(Environment.GetEnvironmentVariable("CommonProgramFiles(x86)") ?? string.Empty, "Microsoft Shared", "ClickToRun", "OfficeC2RClient.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
