namespace FieldKit.Tasks;

public sealed class OptimizeDrivesTask : MaintenanceTask
{
    public OptimizeDrivesTask() : base(
        "Optimize Drives",
        "Runs Windows drive optimization. SSDs are retrimmed and HDDs are defragmented as appropriate.",
        "Repair & Optimize",
        "1-5 min",
        selectedByDefault: false,
        isAdvanced: true)
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would run defrag /C /O /H /U /V", "INFO");
            return TaskResult.Ok("Previewed drive optimization");
        }

        var result = await RunCommandAsync("defrag.exe", "/C /O /H /U /V", ct, line => !string.IsNullOrWhiteSpace(line));
        return result.ExitCode == 0
            ? TaskResult.Ok("Drive optimization completed")
            : TaskResult.Warn($"Drive optimization exited with code {result.ExitCode}");
    }
}
