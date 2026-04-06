namespace FieldKit.Tasks;

public sealed class CreateRestorePointTask : MaintenanceTask
{
    public CreateRestorePointTask() : base(
        "Create Restore Point",
        "Creates a system restore point before other changes run.",
        "Preparation",
        "Quick")
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would enable System Restore on C: and create a restore point.", "INFO");
            return TaskResult.Ok("Previewed restore point creation");
        }

        var description = $"Pre-Maintenance {DateTime.Now:yyyy-MM-dd}";
        var command = "$ErrorActionPreference='Stop'; " +
                      "Enable-ComputerRestore -Drive 'C:\\'; " +
                      $"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS";

        var result = await RunPowerShellAsync(command, ct, line => !string.IsNullOrWhiteSpace(line));
        if (result.ExitCode == 0)
            return TaskResult.Ok($"Restore point created: {description}");

        var combined = string.Join(" ", result.AllLines);
        if (combined.Contains("0x81000101", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("per day", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("24", StringComparison.OrdinalIgnoreCase))
        {
            return TaskResult.Warn("Windows already created a restore point recently");
        }

        return TaskResult.Warn("Restore point could not be created automatically");
    }
}
