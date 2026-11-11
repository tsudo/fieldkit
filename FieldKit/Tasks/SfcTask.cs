using System.Text;

namespace FieldKit.Tasks;

public sealed class SfcTask : MaintenanceTask
{
    public SfcTask() : base(
        "Check system files",
        "Runs System File Checker to find and repair corrupted Windows system files.",
        "Repair",
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
            return TaskResult.Ok("Would scan system files");
        }

        var sfc = Path.Combine(Environment.SystemDirectory, "sfc.exe");

        // sfc.exe writes UTF-16 when its output is redirected. Read with any
        // other encoding, every character is followed by a NUL and none of the
        // result phrases below can match.
        var result = await RunCommandAsync(sfc, "/scannow", ct,
            line => line.Contains('%') || line.Contains("Windows Resource Protection", StringComparison.OrdinalIgnoreCase),
            Encoding.Unicode);

        return Interpret(result.ExitCode, string.Join(" ", result.AllLines));
    }

    internal static TaskResult Interpret(int exitCode, string output)
    {
        if (output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Ok("No integrity violations found");
        if (output.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Warn("Corrupt files were repaired. Restart the PC.", rebootRecommended: true);
        if (output.Contains("unable to fix", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Fail("Found corruption it could not repair. Run Repair Windows image, then this again.");
        if (output.Contains("could not perform the requested operation", StringComparison.OrdinalIgnoreCase))
            return TaskResult.Fail("SFC could not run. Try again after a restart.");

        return exitCode == 0
            ? TaskResult.Warn("SFC finished but its result could not be read. Check the log.")
            : TaskResult.Warn($"SFC exited with code {exitCode}. Check the log.");
    }
}
