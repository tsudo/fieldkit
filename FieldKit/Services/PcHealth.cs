using System.Text.Json;

namespace FieldKit.Services;

internal sealed record HealthReading(string State, string Detail)
{
    public static HealthReading Unavailable(string detail) => new("Unavailable", detail);
}

internal sealed record HealthCheck(string Title, string Action, Func<Task<HealthReading>> Read);

internal static class PcHealth
{
    private static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');

    internal static IReadOnlyList<HealthCheck> Checks() =>
    [
        new("Active antivirus", "Security providers", () => Task.Run(() => DescribeAntivirus(SecurityProviders.Read()))),
        new("Microsoft Defender", "Virus protection", DefenderAsync),
        new("Windows Firewall", "Firewall settings", FirewallAsync),
        new("System drive space", "Storage settings", () => Task.Run(Space)),
        new("System disk health", "Disk settings", DiskHealthAsync),
        new("Scheduled drive optimization", "Optimize drives", OptimizationAsync),
        new("BitLocker protection", "Device encryption", BitLockerAsync),
        new("Startup changes", "Startup review", () => Task.Run(() => new HealthReading("History", StartupReviewService.GetChangeSummary()))),
        new("Windows restart", "Updates", () => Task.Run(() => SystemInfo.HasPendingReboot()
            ? new HealthReading("Restart pending", "Save your work and restart when convenient.")
            : new HealthReading("Clear", "No pending Windows servicing restart detected.")))
    ];

    internal static async Task<HealthReading> ReadSafelyAsync(HealthCheck check, Logger? logger = null)
    {
        try { return await check.Read().WaitAsync(TimeSpan.FromSeconds(35)); }
        catch (Exception ex)
        {
            logger?.Log($"[PC health] {check.Title}: {ex.Message}", "WARN");
            return HealthReading.Unavailable($"Windows could not report {check.Title.ToLowerInvariant()}.");
        }
    }

    // Each script is fixed, read-only, runs with system modules only, and emits a
    // small typed projection. Avoid logging raw WMI or BitLocker objects.
    private static async Task<JsonElement> QueryAsync(string script)
    {
        var result = await CommandRunner.RunPowerShellAsync(
            "$ErrorActionPreference='Stop'; $WarningPreference='SilentlyContinue'; " + script,
            timeout: TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0 || result.Output.Count == 0)
            throw new InvalidOperationException($"The Windows provider query failed ({result.ExitCode}).");
        using var data = JsonDocument.Parse(string.Join("\n", result.Output));
        return data.RootElement.Clone();
    }

    internal static HealthReading DescribeAntivirus(IReadOnlyList<AntivirusProduct> products)
    {
        var active = products.Where(p => p.State == 0).Select(p => p.Name).ToArray();
        if (active.Length > 0) return new("Active", string.Join(", ", active));
        if (products.Any(p => p.State is < 0 or > 3)) return HealthReading.Unavailable("Windows reported an unknown provider state.");
        return new("Review", products.Count == 0 ? "Windows Security lists no registered antivirus provider."
            : $"No provider is reporting active protection. Registered: {string.Join(", ", products.Select(p => p.Name))}.");
    }

    private static async Task<HealthReading> DefenderAsync()
    {
        var row = await QueryAsync("""
            Get-MpComputerStatus | Select-Object AMRunningMode,RealTimeProtectionEnabled,DefenderSignaturesOutOfDate,AntivirusSignatureAge | ConvertTo-Json -Compress
            """);
        return DescribeDefender(row);
    }

    internal static HealthReading DescribeDefender(JsonElement row)
    {
        var mode = row.GetProperty("AMRunningMode").GetString();
        if (string.IsNullOrWhiteSpace(mode)) return HealthReading.Unavailable("Defender did not report its running mode.");
        if (!mode.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return new("Provider status", $"Defender mode: {mode}. See Active antivirus for the registered provider.");
        var realtime = row.GetProperty("RealTimeProtectionEnabled");
        var outdated = row.GetProperty("DefenderSignaturesOutOfDate");
        var age = row.GetProperty("AntivirusSignatureAge");
        var ageText = age.ValueKind == JsonValueKind.Number && age.TryGetUInt32(out var days) && days < 65535
            ? $" ({days} days old)" : "";
        var protection = realtime.ValueKind switch { JsonValueKind.True => "on", JsonValueKind.False => "off", _ => "unknown" };
        var definitions = outdated.ValueKind switch { JsonValueKind.True => "out of date", JsonValueKind.False => "current", _ => "age status unknown" };
        var state = realtime.ValueKind == JsonValueKind.False || outdated.ValueKind == JsonValueKind.True ? "Review"
            : realtime.ValueKind == JsonValueKind.True && outdated.ValueKind == JsonValueKind.False ? "Active" : "Unavailable";
        return new(state, $"Real-time protection {protection}. Security intelligence {definitions}{ageText}.");
    }

    private static async Task<HealthReading> FirewallAsync()
    {
        var profiles = await QueryAsync("""
            $policy = New-Object -ComObject HNetCfg.FwPolicy2
            $active = $policy.CurrentProfileTypes
            $rows = @(foreach ($profile in 1,2,4) {
                if ($active -band $profile) {
                    [pscustomobject]@{ Name = @{1='Domain';2='Private';4='Public'}[$profile]; Enabled = $policy.FirewallEnabled($profile) }
                }
            })
            ConvertTo-Json -InputObject $rows -Compress
            """);
        var active = profiles.EnumerateArray().ToList();
        if (active.Count == 0) return HealthReading.Unavailable("No active firewall profile was reported.");
        var details = active.Select(p => $"{p.GetProperty("Name").GetString()}: {(p.GetProperty("Enabled").GetBoolean() ? "on" : "off")}");
        return new(active.All(p => p.GetProperty("Enabled").GetBoolean()) ? "Enabled" : "Review", string.Join(" · ", details));
    }

    private static HealthReading Space()
    {
        var drive = new DriveInfo(SystemDrive);
        var free = drive.AvailableFreeSpace / 1073741824d;
        var total = drive.TotalSize / 1073741824d;
        return new(free < 10 ? "Low space" : "Available",
            $"{drive.Name} has {free:N1} GB available of {total:N1} GB. FieldKit flags less than 10 GB.");
    }

    private static async Task<HealthReading> DiskHealthAsync()
    {
        var row = await QueryAsync("""
            $letter = [IO.Path]::GetPathRoot([Environment]::SystemDirectory)[0]
            $disk = Get-Partition -DriveLetter $letter | Get-Disk
            if (@($disk).Count -ne 1) { throw 'Ambiguous system disk' }
            [pscustomobject]@{ Health = [string]$disk.HealthStatus; Name = [string]$disk.FriendlyName } | ConvertTo-Json -Compress
            """);
        return DescribeDisk(row.GetProperty("Health").GetString(), row.GetProperty("Name").GetString());
    }

    internal static HealthReading DescribeDisk(string? health, string? name)
    {
        if (health is not ("Healthy" or "Warning" or "Unhealthy"))
            return HealthReading.Unavailable("Windows returned no usable health status for the system disk.");
        return new(health == "Healthy" ? "Healthy" : "Review", $"{name}: Windows disk status is {health.ToLowerInvariant()}.");
    }

    private static async Task<HealthReading> OptimizationAsync()
    {
        var row = await QueryAsync("""
            $task = Get-ScheduledTask -TaskPath '\Microsoft\Windows\Defrag\' -TaskName 'ScheduledDefrag'
            $triggers = @($task.Triggers | Where-Object { $_.Enabled -ne $false })
            [pscustomobject]@{ Enabled = $task.Settings.Enabled; Triggers = $triggers.Count } | ConvertTo-Json -Compress
            """);
        var enabled = row.GetProperty("Enabled").GetBoolean() && row.GetProperty("Triggers").GetInt32() > 0;
        return new(enabled ? "Scheduled" : "Review", enabled ? "Windows has scheduled drive optimization enabled."
            : "The Windows optimization task or its triggers are disabled.");
    }

    private static async Task<HealthReading> BitLockerAsync()
    {
        var row = await QueryAsync("""
            $drive = [IO.Path]::GetPathRoot([Environment]::SystemDirectory).Substring(0,2)
            $v = Get-CimInstance -Namespace root\CIMV2\Security\MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter "DriveLetter='$drive'" -Property ProtectionStatus,ConversionStatus
            if ($null -eq $v) { throw 'No system volume status' }
            $v | Select-Object ProtectionStatus,ConversionStatus | ConvertTo-Json -Compress
            """);
        return DescribeBitLocker(row.GetProperty("ProtectionStatus").GetInt32(), row.GetProperty("ConversionStatus").GetInt32());
    }

    internal static HealthReading DescribeBitLocker(int protection, int conversion) => (protection, conversion) switch
    {
        (0, 0) => new("Unencrypted", "The Windows volume is fully decrypted."),
        (0, 1) => new("Suspended", "The Windows volume is encrypted with key protection off. Review suspended protection or incomplete activation."),
        (1, 1) => new("Protected", "The Windows volume is encrypted and key protection is on."),
        (0 or 1, >= 2 and <= 5) => new("In progress", $"Volume conversion: {ConversionStates[conversion - 2]}. Key protection is {(protection == 1 ? "on" : "off")}."),
        _ => HealthReading.Unavailable("Windows did not confirm the volume's encryption and protection state.")
    };

    private static readonly string[] ConversionStates = ["encrypting", "decrypting", "encryption paused", "decryption paused"];
}
