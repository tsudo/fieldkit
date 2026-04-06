using System.Management;

namespace FieldKit.Tasks;

public class StoreUpdateTask : MaintenanceTask
{
    public StoreUpdateTask() : base(
        "Update Store Apps",
        "Triggers a Microsoft Store update scan when the required Windows management interface is available.",
        "Updates",
        "Quick",
        requiresInternet: true) { }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            if (dryRun)
            {
                Log("[DryRun] Would invoke MDM Store update scan", "INFO");
                return TaskResult.Ok("Dry run");
            }

            try
            {
                Log("Triggering Microsoft Store update scan via MDM CIM interface...", "INFO");

                var scope = new ManagementScope(@"\\.\root\cimv2\mdm\dmmap");
                scope.Connect();

                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT * FROM MDM_EnterpriseModernAppManagement_AppManagement01"));

                foreach (ManagementObject obj in searcher.Get())
                {
                    var result = obj.InvokeMethod("UpdateScanMethod", null, null);
                    var retVal = result?["ReturnValue"];
                    Log($"Store update scan triggered (return value: {retVal}).", "OK");
                    Log("  Updates install in the background. Open the Store to verify.", "INFO");
                    return TaskResult.Ok("Store update scan started");
                }

                Log("MDM class found but no instances returned.", "WARN");
                return TaskResult.Warn("No MDM instances — open Store > Library > Get updates");
            }
            catch (ManagementException ex)
            {
                Log($"MDM Store update scan failed: {ex.Message}", "WARN");
                if (ex.Message.Contains("not found") || ex.Message.Contains("Invalid"))
                    Log("  The MDM WMI class was not found — this is normal on non-enterprise Windows.", "WARN");
                Log("  ACTION REQUIRED: Open the Microsoft Store > Library > Get updates.", "WARN");
                return TaskResult.Warn("Manual update required — open Store > Library");
            }
            catch (Exception ex)
            {
                Log($"Store update failed: {ex.Message}", "WARN");
                return TaskResult.Warn(ex.Message);
            }
        }, ct);
    }
}
