namespace FieldKit.Tasks;

public class WingetUpgradeTask : MaintenanceTask
{
    public WingetUpgradeTask() : base(
        "Update Installed Apps",
        "Updates third-party applications like Chrome, Zoom, VLC, etc.",
        TaskDuration.Medium) { }

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run((Action)(() =>
    {
        try
        {
            string? wingetPath = FindWinget();
            if (wingetPath == null) { ContextInfo = "winget not found"; return; }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = wingetPath,
                Arguments = "upgrade --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(30_000);

            // Count upgrade lines: winget outputs a table with headers then a separator line of dashes
            // Upgradeable packages appear after the last "---" separator line
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int sepIndex = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].TrimStart().StartsWith("---") || lines[i].TrimStart().StartsWith("___"))
                { sepIndex = i; break; }
            }

            if (sepIndex >= 0)
            {
                int count = 0;
                for (int i = sepIndex + 1; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (!string.IsNullOrEmpty(trimmed) &&
                        !trimmed.StartsWith("upgrades available", StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                ContextInfo = count == 0 ? "Up to date" : $"{count} updates available";
            }
        }
        catch { }
    }), ct);

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run<TaskResult>(() =>
        {
            // Find winget
            string? wingetPath = FindWinget();
            if (wingetPath == null)
            {
                Log("winget not found on this system.", "WARN");
                Log("  To install: open Microsoft Store > search 'App Installer' > Install.", "WARN");
                return TaskResult.Warn("winget not installed");
            }

            Log($"winget found: {wingetPath}", "INFO");

            if (dryRun)
            {
                Log("[DryRun] Would run: winget upgrade --all --silent --accept-source-agreements --accept-package-agreements", "INFO");
                return TaskResult.Ok("Dry run");
            }

            try
            {
                Log("Running winget upgrade --all (this may take several minutes)...", "INFO");
                Log("  NOTE: Some installers may show interactive prompts.", "WARN");

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wingetPath,
                    Arguments = "upgrade --all --silent --accept-source-agreements --accept-package-agreements",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                    return TaskResult.Fail("Failed to start winget");

                using var killReg = ct.Register(() =>
                {
                    try { proc.Kill(true); } catch { }
                });

                // Drain stderr asynchronously to prevent pipe buffer deadlock
                var stderrTask = proc.StandardError.ReadToEndAsync();

                // Stream output, filtering progress/spinner noise
                while (!proc.StandardOutput.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = proc.StandardOutput.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var trimmed = line.Trim();

                    // Skip spinner characters (- \ | /)
                    if (trimmed.Length <= 2 && (trimmed is "-" or "\\" or "|" or "/"))
                        continue;

                    // Skip block-character progress bars and download progress
                    // Check for Unicode block chars and also percent-only lines
                    if (trimmed.Contains('\u2588') || trimmed.Contains('\u2591') ||
                        trimmed.Contains('\u2592') || trimmed.Contains('\u2593'))
                        continue;

                    // Skip lines that are mostly non-ASCII (mojibake progress bars)
                    int nonAscii = 0;
                    foreach (char c in trimmed)
                        if (c > 127) nonAscii++;
                    if (nonAscii > trimmed.Length / 2 && trimmed.Contains('%'))
                        continue;

                    // Skip KB/MB download progress lines
                    if (trimmed.Contains(" KB / ") || trimmed.Contains(" MB / "))
                        continue;

                    Log($"  [winget] {trimmed}", "INFO");
                }

                proc.WaitForExit(600_000);

                var stderr = stderrTask.GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(stderr))
                    Log($"  [winget stderr] {stderr.Trim()}", "WARN");

                int exitCode = proc.ExitCode;

                const int WINGET_NO_UPDATES = -1978335135;   // 0x8A150021
                const int WINGET_INSTALL_ERRORS = -1978335188; // 0x8A15000C — some packages failed
                if (exitCode == 0 || exitCode == WINGET_NO_UPDATES)
                {
                    Log("winget upgrade complete.", "OK");
                    return TaskResult.Ok();
                }
                else if (exitCode == WINGET_INSTALL_ERRORS)
                {
                    Log("winget completed but some packages failed to install.", "WARN");
                    Log("  Run 'winget upgrade' manually to see remaining items.", "WARN");
                    return TaskResult.Warn("Some packages failed — check manually");
                }
                else
                {
                    Log($"winget exited with code {exitCode}. Some upgrades may have failed.", "WARN");
                    Log("  Run 'winget upgrade' manually to see remaining items.", "WARN");
                    return TaskResult.Warn($"Exit code {exitCode} — check manually");
                }
            }
            catch (OperationCanceledException)
            {
                return TaskResult.Warn("Cancelled");
            }
            catch (Exception ex)
            {
                Log($"winget upgrade failed: {ex.Message}", "ERROR");
                return TaskResult.Fail(ex.Message);
            }
        }, ct);
    }

    private static string? FindWinget()
    {
        // Check PATH first
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "winget.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // Check known location
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var knownPath = Path.Combine(localAppData, @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(knownPath)) return knownPath;

        return null;
    }
}
