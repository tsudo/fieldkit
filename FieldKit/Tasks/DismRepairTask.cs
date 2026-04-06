namespace FieldKit.Tasks;

public class DismRepairTask : MaintenanceTask
{
    public DismRepairTask() : base(
        "Repair Windows Image",
        "Deep repair of Windows system components — fixes issues that Check System Files cannot",
        TaskDuration.Long) { }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            Log("Checking internet connectivity (required for DISM /RestoreHealth)...", "INFO");
            if (!Services.SystemInfo.HasInternet())
            {
                Log("No internet connection. DISM /RestoreHealth requires internet access.", "ERROR");
                Log("  To run offline, mount a Windows ISO and use:", "WARN");
                Log("  DISM /Online /Cleanup-Image /RestoreHealth /Source:WIM:D:\\sources\\install.wim:1 /LimitAccess", "WARN");
                return TaskResult.Fail("No internet — skipped");
            }

            if (dryRun)
            {
                Log("[DryRun] Would run: DISM /Online /Cleanup-Image /RestoreHealth", "INFO");
                return TaskResult.Ok("Dry run");
            }

            Log("Running DISM /RestoreHealth — this may take 10-20 minutes...", "INFO");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SystemExe("dism.exe"),
                    Arguments = "/Online /Cleanup-Image /RestoreHealth",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return TaskResult.Fail("Failed to start dism.exe");

                using var killReg = ct.Register(() =>
                {
                    try { proc.Kill(true); } catch { }
                });

                // Drain stderr asynchronously to prevent pipe buffer deadlock
                var stderrTask = proc.StandardError.ReadToEndAsync();

                // Filter progress bar spam — only log at 10% intervals
                int lastLoggedPct = -1;

                while (!proc.StandardOutput.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = proc.StandardOutput.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var trimmed = line.Trim();

                    // Detect DISM ASCII progress bars like "[===  12.6%  ]"
                    if (trimmed.StartsWith('[') && trimmed.Contains('%'))
                    {
                        int pct = ExtractPercent(trimmed);
                        if (pct >= 0)
                        {
                            int bucket = pct / 10;
                            if (bucket != lastLoggedPct / 10)
                            {
                                lastLoggedPct = pct;
                                Log($"  DISM progress: {pct}%", "INFO");
                            }
                            continue;
                        }
                    }

                    // Log non-progress lines (version info, results, errors)
                    Log($"  [DISM] {trimmed}", "INFO");
                }

                proc.WaitForExit(1_200_000); // 20 min

                var stderr = stderrTask.GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(stderr))
                    Log($"  [DISM stderr] {stderr.Trim()}", "WARN");

                int exitCode = proc.ExitCode;

                return exitCode switch
                {
                    0 => LogAndReturn("DISM: Image health verified/repaired successfully.", "OK",
                        TaskResult.Ok("No issues found")),
                    3010 => LogAndReturn("DISM: Repair successful — a reboot is required.", "WARN",
                        TaskResult.Warn("Repaired — reboot required")),
                    // 0x800f081f
                    -2146498529 => LogAndReturn("DISM Error 0x800f081f: Source files not found.", "ERROR",
                        TaskResult.Fail("0x800f081f — source not found")),
                    // 0x800f0906
                    -2146498810 => LogAndReturn("DISM Error 0x800f0906: Could not download repair files.", "ERROR",
                        TaskResult.Fail("0x800f0906 — download failed")),
                    // 0x800f0907
                    -2146498809 => LogAndReturn("DISM Error 0x800f0907: Invalid or unsupported component.", "ERROR",
                        TaskResult.Fail("0x800f0907 — unsupported operation")),
                    _ => LogAndReturn($"DISM completed with exit code {exitCode}.", "WARN",
                        TaskResult.Warn($"Exit code {exitCode} — review dism.log"))
                };
            }
            catch (OperationCanceledException)
            {
                return TaskResult.Warn("Cancelled");
            }
            catch (Exception ex)
            {
                Log($"DISM failed to launch: {ex.Message}", "ERROR");
                return TaskResult.Fail(ex.Message);
            }
        }, ct);
    }

    private TaskResult LogAndReturn(string message, string level, TaskResult result)
    {
        Log(message, level);
        return result;
    }

    private static int ExtractPercent(string line)
    {
        int pctIdx = line.IndexOf('%');
        if (pctIdx < 0) return -1;

        // Walk backward past spaces to find the number
        int end = pctIdx - 1;
        while (end >= 0 && line[end] == ' ') end--;
        // Walk backward past the decimal part (e.g. ".6")
        if (end >= 1 && line[end] >= '0' && line[end] <= '9' && line[end - 1] == '.')
            end -= 2;
        else if (end >= 0 && line[end] == '.')
            end--;

        int start = end;
        while (start > 0 && char.IsDigit(line[start - 1])) start--;

        if (start > end || end < 0) return -1;
        if (int.TryParse(line[start..(end + 1)], out int pct)) return pct;
        return -1;
    }
}
