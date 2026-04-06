using Microsoft.Win32;

namespace FieldKit.Tasks;

public class StorageSenseTask : MaintenanceTask
{
    public StorageSenseTask() : base(
        "Turn On Auto-Cleanup",
        "Enables Windows Storage Sense so Windows can clean temp files and old Recycle Bin content on a schedule.",
        "Cleanup",
        "Quick") { }

    private const string RegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            var val = key?.GetValue("01");
            ContextInfo = val is int i && i == 1 ? "Currently enabled" : "Currently disabled";
        }
        catch { }
    }, ct);

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            const string regPath = RegPath;

            if (dryRun)
            {
                Log("[DryRun] Would configure Storage Sense (enabled, monthly, 30-day thresholds)", "INFO");
                return TaskResult.Ok("Dry run");
            }

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(regPath, true);
                if (key == null)
                    return TaskResult.Fail("Could not open/create Storage Sense registry key");

                key.SetValue("01", 1, RegistryValueKind.DWord);     // Enable
                key.SetValue("04", 30, RegistryValueKind.DWord);    // Monthly
                key.SetValue("2048", 30, RegistryValueKind.DWord);  // Temp files > 30 days
                key.SetValue("08", 1, RegistryValueKind.DWord);     // Clean Recycle Bin
                key.SetValue("256", 30, RegistryValueKind.DWord);   // Recycle Bin > 30 days

                Log("Configured Storage Sense (enabled, runs monthly, 30-day thresholds).", "OK");
                Log("  Open Settings > System > Storage > Storage Sense to review.", "INFO");
                return TaskResult.Ok("Enabled, monthly, 30-day thresholds");
            }
            catch (Exception ex)
            {
                Log($"Could not configure Storage Sense: {ex.Message}", "WARN");
                Log("  Configure manually: Settings > System > Storage > Storage Sense", "WARN");
                return TaskResult.Warn(ex.Message);
            }
        }, ct);
    }
}
