using System.ServiceProcess;

namespace FieldKit.Tasks;

public sealed class FlushDnsCacheTask : MaintenanceTask
{
    public FlushDnsCacheTask() : base(
        "Clear Network Cache",
        "Flushes the DNS client cache to clear stale name lookups.",
        "Cleanup",
        "Quick")
    {
    }

    public override Task GatherContextAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var service = new ServiceController("Dnscache");
                ContextInfo = $"DNS Client service: {service.Status}";
            }
            catch
            {
                ContextInfo = "DNS Client service status unavailable";
            }
        }, ct);
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        try
        {
            using var service = new ServiceController("Dnscache");
            if (service.Status != ServiceControllerStatus.Running)
                return TaskResult.Warn("DNS Client service is not running on this system");
        }
        catch
        {
            return TaskResult.Warn("DNS Client service could not be queried");
        }

        if (dryRun)
        {
            Log("Would flush the DNS client cache.", "INFO");
            return TaskResult.Ok("Previewed DNS cache flush");
        }

        var result = await RunPowerShellAsync("Clear-DnsClientCache -ErrorAction Stop", ct);
        return result.ExitCode == 0
            ? TaskResult.Ok("DNS cache flushed")
            : TaskResult.Warn("DNS cache flush failed");
    }
}
