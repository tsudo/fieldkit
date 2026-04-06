using System.Text;

namespace FieldKit.Tasks;

public class SfcScanTask : MaintenanceTask
{
    public SfcScanTask() : base(
        "Check System Files",
        "Scans Windows for corrupted or missing system files and repairs them automatically",
        TaskDuration.Long) { }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            if (dryRun)
            {
                Log("[DryRun] Would run: sfc /scannow", "INFO");
                return TaskResult.Ok("Dry run");
            }

            Log("Running SFC /scannow \u2014 this may take 10\u201330 minutes...", "INFO");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SystemExe("sfc.exe"),
                    Arguments = "/scannow",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.Unicode, // sfc.exe outputs UTF-16LE
                    StandardErrorEncoding = Encoding.Unicode
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return TaskResult.Fail("Failed to start sfc.exe");

                using var killReg = ct.Register(() =>
                {
                    try { proc.Kill(true); } catch { }
                });

                // Drain stderr asynchronously to prevent pipe buffer deadlock
                var stderrTask = proc.StandardError.ReadToEndAsync();

                // Collect stdout for result parsing; only log milestone lines
                var stdoutLines = new List<string>();
                int lastLoggedPct = -1;

                while (!proc.StandardOutput.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = proc.StandardOutput.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var trimmed = line.Trim();
                    stdoutLines.Add(trimmed);

                    // Filter progress spam: only log at 0%, 25%, 50%, 75%, 100%
                    if (trimmed.Contains("% complete", StringComparison.OrdinalIgnoreCase))
                    {
                        int pct = ExtractPercent(trimmed);
                        if (pct >= 0 && pct / 25 != lastLoggedPct / 25)
                        {
                            lastLoggedPct = pct;
                            Log($"  SFC scan: {pct}% complete", "INFO");
                        }
                        continue;
                    }

                    // Log non-progress lines (phase changes, results)
                    if (!string.IsNullOrEmpty(trimmed))
                        Log($"  [SFC] {trimmed}", "INFO");
                }

                proc.WaitForExit(1_800_000); // 30 min

                var stderr = stderrTask.GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(stderr))
                    Log($"  [SFC stderr] {stderr.Trim()}", "WARN");

                // --- Parse result: try stdout first, then CBS.log ---
                var stdoutResult = ParseSfcResult(string.Join(" ", stdoutLines));
                if (stdoutResult != null)
                    return stdoutResult;

                // Fallback: parse CBS.log
                var winDir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                var cbsLog = Path.Combine(winDir, @"Logs\CBS\CBS.log");

                if (!File.Exists(cbsLog))
                {
                    Log("CBS.log not found. SFC may not have completed properly.", "WARN");
                    return TaskResult.Warn("CBS.log not found");
                }

                var tailLines = ReadTail(cbsLog, 5000);
                var srLines = tailLines.Where(l => l.Contains("[SR]")).ToList();
                var summary = srLines.LastOrDefault(l => l.Contains("Windows Resource Protection"));

                if (summary != null)
                {
                    var cbsResult = ParseSfcResult(summary);
                    if (cbsResult != null) return cbsResult;
                }

                Log("SFC completed but result could not be determined. Review CBS.log.", "WARN");
                Log($"  {cbsLog}", "INFO");
                return TaskResult.Warn("Review CBS.log for details");
            }
            catch (OperationCanceledException)
            {
                return TaskResult.Warn("Cancelled");
            }
            catch (Exception ex)
            {
                Log($"SFC failed to launch: {ex.Message}", "ERROR");
                return TaskResult.Fail(ex.Message);
            }
        }, ct);
    }

    private TaskResult? ParseSfcResult(string text)
    {
        if (text.Contains("did not find any integrity violations"))
        {
            Log("SFC: No integrity violations found. System files are healthy.", "OK");
            return TaskResult.Ok("No violations found");
        }
        if (text.Contains("successfully repaired"))
        {
            Log("SFC: Corrupt files were found and successfully repaired.", "WARN");
            Log("  A reboot is recommended. Re-run SFC after rebooting to confirm.", "WARN");
            return TaskResult.Warn("Files repaired \u2014 reboot then re-run SFC");
        }
        if (text.Contains("unable to fix"))
        {
            Log("SFC: Corrupt files found but COULD NOT be repaired.", "ERROR");
            Log("  Run DISM /Online /Cleanup-Image /RestoreHealth, reboot, then re-run SFC.", "ERROR");
            return TaskResult.Fail("Unfixable corruption \u2014 run DISM, reboot, re-run SFC");
        }
        if (text.Contains("could not perform"))
        {
            Log("SFC: Could not perform the requested operation.", "ERROR");
            Log("  Try rebooting and re-running.", "WARN");
            return TaskResult.Fail("Could not perform operation \u2014 try after reboot");
        }
        return null;
    }

    private static int ExtractPercent(string line)
    {
        // Match patterns like "Verification 42% complete" or "42 % complete"
        int idx = line.IndexOf('%');
        if (idx < 0) return -1;

        // Walk backward to find the start of the number
        int end = idx - 1;
        while (end >= 0 && line[end] == ' ') end--;
        int start = end;
        while (start > 0 && char.IsDigit(line[start - 1])) start--;

        if (start > end) return -1;
        if (int.TryParse(line[start..(end + 1)], out int pct)) return pct;
        return -1;
    }

    private static List<string> ReadTail(string path, int lineCount)
    {
        try
        {
            var allLines = File.ReadAllLines(path);
            return allLines.Skip(Math.Max(0, allLines.Length - lineCount)).ToList();
        }
        catch { return []; }
    }
}
