using System.ServiceProcess;

namespace FieldKit.Tasks;

public class DnsFlushTask : MaintenanceTask
{
    public DnsFlushTask() : base(
        "Clear Network Cache",
        "Clears saved website address lookups, which can fix some browsing issues") { }

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            using var svc = new ServiceController("Dnscache");
            ContextInfo = svc.Status == ServiceControllerStatus.Running ? "DNS service running" : "DNS service stopped";
        }
        catch { ContextInfo = "Unable to query"; }
    }, ct);

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var svc = new ServiceController("Dnscache");
                if (svc.Status != ServiceControllerStatus.Running)
                {
                    Log($"DNS Client service is '{svc.Status}' — cannot flush cache.", "WARN");
                    Log("  Some privacy-hardened configurations disable this service.", "WARN");
                    Log("  Alternative: run 'ipconfig /flushdns' from an elevated Command Prompt.", "WARN");
                    return TaskResult.Warn("DNS Client service not running");
                }
            }
            catch
            {
                // Service query failed — try flushing anyway
            }

            if (dryRun)
            {
                Log("[DryRun] Would flush DNS cache", "INFO");
                return TaskResult.Ok("Dry run");
            }

            var result = RunProcess(SystemExe("ipconfig.exe"), "/flushdns", false, ct, 15_000);
            if (result.State == TaskState.Success)
            {
                Log("Flushed DNS cache.", "OK");
                return TaskResult.Ok();
            }

            Log("Failed to flush DNS cache.", "WARN");
            Log("  Fallback: run 'ipconfig /flushdns' from an elevated Command Prompt.", "WARN");
            return TaskResult.Warn(result.Note);
        }, ct);
    }
}
