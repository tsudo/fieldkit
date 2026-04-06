using System.Management;

namespace FieldKit.Tasks;

public class RestorePointTask : MaintenanceTask
{
    public RestorePointTask() : base(
        "Create Restore Point",
        "Saves a snapshot of your system so you can undo changes if needed") { }

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\default", "SELECT * FROM SystemRestore");
            DateTime latest = DateTime.MinValue;
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var dateStr = obj["CreationTime"]?.ToString();
                    if (dateStr != null && ManagementDateTimeConverter.ToDateTime(dateStr) is var dt && dt > latest)
                        latest = dt;
                }
            }
            if (latest == DateTime.MinValue)
                ContextInfo = "No restore points";
            else
            {
                var daysAgo = (int)(DateTime.Now - latest).TotalDays;
                ContextInfo = daysAgo == 0 ? "Latest: today" : $"Latest: {daysAgo}d ago";
            }
        }
        catch { }
    }, ct);

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            if (dryRun)
            {
                Log("[DryRun] Would create system restore point", "INFO");
                return TaskResult.Ok("Dry run");
            }

            try
            {
                // Enable System Restore on C:
                Log("Enabling System Restore on C:\\...", "INFO");
                var enablePsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                        @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -Command \"Enable-ComputerRestore -Drive 'C:\\'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using (var proc = System.Diagnostics.Process.Start(enablePsi))
                {
                    if (proc != null)
                    {
                        var stderr = proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit(30_000);
                        var errText = stderr.GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(errText))
                            Log($"  Enable-ComputerRestore: {errText.Trim()}", "WARN");
                    }
                    else
                    {
                        Log("Could not start PowerShell to enable System Restore.", "WARN");
                    }
                }

                // Create restore point via WMI
                var desc = $"Pre-Maintenance {DateTime.Now:yyyy-MM-dd}";
                Log($"Creating restore point: '{desc}'...", "INFO");

                var scope = new ManagementScope(@"\\.\root\default");
                var path = new ManagementPath("SystemRestore");
                using var sr = new ManagementClass(scope, path, null);
                var inParams = sr.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = desc;
                inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                using var outParams = sr.InvokeMethod("CreateRestorePoint", inParams, null);
                var returnVal = (uint)(outParams["ReturnValue"] ?? 1);

                if (returnVal == 0)
                {
                    Log("Restore point created.", "OK");
                    return TaskResult.Ok(desc);
                }
                else if (returnVal == 0x80070001)
                {
                    Log("Skipped: a restore point was already created within the last 24 hours.", "WARN");
                    return TaskResult.Warn("24-hour cooldown in effect");
                }
                else
                {
                    Log($"CreateRestorePoint returned 0x{returnVal:X8}", "WARN");
                    return TaskResult.Warn($"Return value: 0x{returnVal:X8}");
                }
            }
            catch (ManagementException ex) when (ex.Message.Contains("0x81000101") ||
                                                   ex.Message.Contains("per day") ||
                                                   ex.Message.Contains("24 hour"))
            {
                Log("Skipped: a restore point was already created within the last 24 hours.", "WARN");
                return TaskResult.Warn("24-hour cooldown in effect");
            }
            catch (Exception ex)
            {
                Log($"Could not create restore point: {ex.Message}", "WARN");
                return TaskResult.Warn(ex.Message);
            }
        }, ct);
    }
}
