namespace FieldKit.Tasks;

public sealed class CreateRestorePointTask : MaintenanceTask
{
    internal const string CreatedMarker = "FIELDKIT_RESTORE_CREATED";
    internal const string SkippedMarker = "FIELDKIT_RESTORE_SKIPPED";

    public CreateRestorePointTask() : base(
        "Create restore point",
        "Creates a system restore point before other changes run.",
        "Prepare",
        "Under 1 min")
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would enable System Restore on the system drive and create a restore point.", "INFO");
            return TaskResult.Ok("Would create a restore point");
        }

        var description = $"FieldKit {DateTime.Now:yyyy-MM-dd HH:mm}";

        // Checkpoint-Computer exits 0 without creating anything when Windows made a
        // restore point in the last 24 hours. Comparing the newest sequence number
        // before and after is the only reliable way to tell which happened.
        var script =
            "$ErrorActionPreference='Stop'; " +
            "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\"; " +
            "$before = (Get-ComputerRestorePoint | Measure-Object -Property SequenceNumber -Maximum).Maximum; " +
            $"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS -WarningAction SilentlyContinue; " +
            "$after = (Get-ComputerRestorePoint | Measure-Object -Property SequenceNumber -Maximum).Maximum; " +
            $"if ($after -gt $before) {{ '{CreatedMarker}' }} else {{ '{SkippedMarker}' }}";

        var result = await RunPowerShellAsync(script, ct, line =>
            !string.IsNullOrWhiteSpace(line) && !line.StartsWith("FIELDKIT_", StringComparison.Ordinal));

        return Interpret(result.ExitCode, result.Output, description);
    }

    internal static TaskResult Interpret(int exitCode, IReadOnlyList<string> output, string description)
    {
        if (exitCode != 0)
            return TaskResult.Warn("Restore point could not be created. System Restore may be turned off by policy.");

        if (output.Any(line => line.Trim() == CreatedMarker))
            return TaskResult.Ok($"Created \"{description}\"");

        if (output.Any(line => line.Trim() == SkippedMarker))
            return TaskResult.Ok("Windows skipped it: a restore point from the last 24 hours already exists");

        return TaskResult.Warn("Restore point command finished but did not report a result");
    }
}
