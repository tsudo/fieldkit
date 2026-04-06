namespace FieldKit.Tasks;

public class DiskOptimizeTask : MaintenanceTask
{
    public DiskOptimizeTask() : base(
        "Optimize Drives",
        "Defragments hard drives or optimizes SSDs to improve performance",
        TaskDuration.Medium) { }

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat == "NTFS")
                .ToList();
            ContextInfo = drives.Count == 0 ? "No NTFS volumes" :
                string.Join(", ", drives.Select(d => $"{d.Name[0]}:"));
        }
        catch { }
    }, ct);

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Find fixed NTFS volumes
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat == "NTFS")
                    .ToList();

                if (drives.Count == 0)
                {
                    Log("No fixed NTFS volumes found to optimize.", "WARN");
                    return TaskResult.Warn("No NTFS volumes found");
                }

                int optimized = 0;
                foreach (var drive in drives)
                {
                    ct.ThrowIfCancellationRequested();
                    var letter = drive.Name[0];
                    var freeGB = Math.Round(drive.AvailableFreeSpace / 1_073_741_824.0, 1);
                    var sizeGB = Math.Round(drive.TotalSize / 1_073_741_824.0, 1);

                    Log($"Optimizing {letter}: ({freeGB} GB free of {sizeGB} GB)...", "INFO");

                    if (dryRun)
                    {
                        Log($"  [DryRun] Would run: Optimize-Volume -DriveLetter {letter}", "INFO");
                        optimized++;
                        continue;
                    }

                    // Use defrag.exe which auto-selects defrag vs retrim
                    var result = RunProcess(SystemExe("defrag.exe"), $"{letter}: /O", false, ct, 600_000);
                    if (result.State == TaskState.Success || result.State == TaskState.Warning)
                        optimized++;
                    else
                        Log($"  Could not optimize {letter}: {result.Note}", "WARN");
                }

                Log($"Disk optimization complete ({optimized} of {drives.Count} drives processed).", "OK");
                return TaskResult.Ok($"{optimized} of {drives.Count} drives optimized");
            }
            catch (OperationCanceledException)
            {
                return TaskResult.Warn("Cancelled");
            }
            catch (Exception ex)
            {
                Log($"Disk optimization error: {ex.Message}", "ERROR");
                return TaskResult.Fail(ex.Message);
            }
        }, ct);
    }
}
