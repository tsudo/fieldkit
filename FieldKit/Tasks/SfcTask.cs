namespace FieldKit.Tasks;

public sealed class SfcTask : MaintenanceTask
{
    public SfcTask() : base(
        "Check System Files",
        "Runs System File Checker to detect and repair corrupted Windows system files.",
        "Repair & Optimize",
        "10-30 min",
        selectedByDefault: false,
        isAdvanced: true,
        mayRequireReboot: true)
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would run sfc /scannow", "INFO");
            return TaskResult.Ok("Previewed SFC scan");
        }

        var sfc = Path.Combine(Environment.SystemDirectory, "sfc.exe");
        var result = await RunCommandAsync(sfc, "/scannow", ct, line =>
            line.Contains('%') ||
            line.Contains("Windows Resource Protection", StringComparison.OrdinalIgnoreCase));

        var output = string.Join(" ", result.AllLines);
        if (output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Ok("No integrity violations found");
        if (output.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Warn("Corrupt files were repaired. Reboot recommended.", rebootRecommended: true);
        if (output.Contains("unable to fix", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Fail("SFC found corruption it could not repair");
        if (result.ExitCode == 0)
            return TaskResult.Ok("SFC completed");

        return TaskResult.Warn("SFC completed with warnings. Review the log for details.");
    }
}
