namespace FieldKit.Tasks;

public sealed class EmptyRecycleBinTask : MaintenanceTask
{
    public EmptyRecycleBinTask() : base(
        "Empty Recycle Bin",
        "Permanently removes files currently sitting in the Recycle Bin.",
        "Cleanup",
        "Quick")
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would empty the Recycle Bin.", "INFO");
            return TaskResult.Ok("Previewed Recycle Bin cleanup");
        }

        var result = await RunPowerShellAsync("Clear-RecycleBin -Force -ErrorAction Stop", ct, line => !string.IsNullOrWhiteSpace(line));
        if (result.ExitCode == 0)
            return TaskResult.Ok("Recycle Bin emptied");

        var combined = string.Join(" ", result.AllLines);
        if (combined.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("no items", StringComparison.OrdinalIgnoreCase))
        {
            return TaskResult.Ok("Recycle Bin was already empty");
        }

        return TaskResult.Warn("Recycle Bin could not be emptied automatically");
    }
}
