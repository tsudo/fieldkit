namespace FieldKit.Tasks;

public sealed class DismTask : MaintenanceTask
{
    public DismTask() : base(
        "Repair Windows Image",
        "Runs DISM RestoreHealth to repair the Windows component store.",
        "Repair & Optimize",
        "10-30 min",
        selectedByDefault: false,
        isAdvanced: true,
        requiresInternet: true,
        mayRequireReboot: true)
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would run DISM /Online /Cleanup-Image /RestoreHealth", "INFO");
            return TaskResult.Ok("Previewed DISM repair");
        }

        var dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
        var result = await RunCommandAsync(
            dism,
            "/Online /Cleanup-Image /RestoreHealth",
            ct,
            line => line.Contains('%') || line.Contains("The restore operation", StringComparison.OrdinalIgnoreCase));

        var output = string.Join(" ", result.AllLines);
        if (result.ExitCode == 0)
            return TaskResult.Ok("DISM completed successfully");
        if (result.ExitCode == 3010)
            return TaskResult.Warn("DISM completed and requested a reboot", rebootRecommended: true);
        if (output.Contains("0x800f081f", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Fail("DISM could not find source files");
        if (output.Contains("0x800f0906", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Fail("DISM could not download repair files");

        return TaskResult.Warn($"DISM exited with code {result.ExitCode}");
    }
}
