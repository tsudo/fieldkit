using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldKit.Services;

public sealed class UpdatePreferences
{
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public string[] SkipAppIds { get; init; } = [];
    public int LogRetentionDays { get; init; } = 90;

    public bool IsSkipped(string id) => SkipAppIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public static UpdatePreferences Load() => Load(AppPaths.Root);

    internal static UpdatePreferences Load(string root)
    {
        var path = Path.Combine(root, "config.json");
        if (!File.Exists(path))
        {
            var staging = Path.Combine(root, $".config-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(staging, JsonSerializer.Serialize(new UpdatePreferences(), Format));
                try { File.Move(staging, path); }
                catch (IOException) when (File.Exists(path)) { /* Another process created it. */ }
            }
            finally { File.Delete(staging); }
        }
        if (AppPaths.IsReparsePoint(path))
            throw new InvalidDataException($"Cannot read a linked configuration file at {path}.");
        try
        {
            var config = JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path), Format);
            if (config is null || config.SkipAppIds is null || config.SkipAppIds.Any(string.IsNullOrWhiteSpace)
                || config.LogRetentionDays is < 0 or > 3650)
                throw new JsonException("Supply app IDs as strings and logRetentionDays between 0 and 3650.");
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Check {path}: {ex.Message}", ex);
        }
    }

    public void PruneUpdateLogs(string logsDirectory)
    {
        if (LogRetentionDays == 0 || AppPaths.IsReparsePoint(logsDirectory)) return;
        var cutoff = DateTime.UtcNow.AddDays(-LogRetentionDays);
        foreach (var pattern in new[] { "FieldKit-Update-*.log", "run-*-updates.json" })
        {
            foreach (var path in Directory.EnumerateFiles(logsDirectory, pattern))
            {
                if (!AppPaths.IsReparsePoint(path) && File.GetLastWriteTimeUtc(path) < cutoff)
                    File.Delete(path);
            }
        }
    }
}
