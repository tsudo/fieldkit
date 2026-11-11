using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldKit.Services;

public sealed record RunItem(string Name, string State, string Summary);

/// <summary>What one job did, written beside the log so the Reports page can list past runs.</summary>
public sealed record RunSummary(
    string Kind,
    string Title,
    DateTime Started,
    DateTime Finished,
    int Success,
    int Warning,
    int Error,
    int Skipped,
    bool Preview,
    bool Stopped,
    bool RebootRecommended,
    double? FreeGbBefore,
    double? FreeGbAfter,
    string LogFile,
    IReadOnlyList<RunItem> Items)
{
    [JsonIgnore]
    public TimeSpan Duration => Finished - Started;

    [JsonIgnore]
    public string CountsText
    {
        get
        {
            var parts = new List<string>();
            if (Success > 0) parts.Add($"{Success} done");
            if (Warning > 0) parts.Add($"{Warning} to check");
            if (Error > 0) parts.Add($"{Error} failed");
            if (Skipped > 0) parts.Add($"{Skipped} skipped");
            return parts.Count == 0 ? "Nothing ran" : string.Join(", ", parts);
        }
    }

    [JsonIgnore]
    public string Tone => Error > 0 ? "Error" : Warning > 0 || Stopped ? "Warn" : "Ok";

    public string ToLogText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{Title}: {(Stopped ? "stopped early" : Preview ? "preview complete" : "complete")}");
        sb.AppendLine($"  Started:   {Started:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Finished:  {Finished:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Elapsed:   {RowViewModel.FormatClock(Duration)}");
        sb.AppendLine($"  Result:    {CountsText}");
        foreach (var item in Items)
            sb.AppendLine($"    - {item.Name}: {item.State} - {item.Summary}");
        if (FreeGbBefore is { } before && FreeGbAfter is { } after)
            sb.AppendLine($"  System drive: {before:F2} GB -> {after:F2} GB ({after - before:+0.00;-0.00;0.00} GB)");
        if (RebootRecommended)
            sb.AppendLine("  Restart the PC before relying on repair results.");
        return sb.ToString();
    }
}

public static class RunHistory
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Saves the summary as JSON in the log folder. Returns false and logs if it cannot.</summary>
    public static bool Save(RunSummary summary, Logger logger)
    {
        try
        {
            // Milliseconds in the name: a preview and a real run can start in the same second.
            var name = $"run-{summary.Started:yyyyMMdd-HHmmss-fff}-{Slug(summary.Kind)}.json";
            File.WriteAllText(Path.Combine(AppPaths.Logs, name), Serialize(summary));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Log($"Could not save the run summary for the Reports page: {ex.Message}", "WARN");
            return false;
        }
    }

    /// <summary>Newest first. Files that cannot be read are counted, not thrown.</summary>
    public static (IReadOnlyList<RunSummary> Runs, int Unreadable) LoadAll()
    {
        var runs = new List<RunSummary>();
        var unreadable = 0;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(AppPaths.Logs, "run-*.json").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (runs, 0); }

        foreach (var file in files)
        {
            try
            {
                var run = Deserialize(File.ReadAllText(file));
                if (run is not null) runs.Add(run);
                else unreadable++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                unreadable++;
            }
        }

        return (runs.OrderByDescending(run => run.Started).ToList(), unreadable);
    }

    internal static string Serialize(RunSummary summary) => JsonSerializer.Serialize(summary, Options);

    internal static RunSummary? Deserialize(string json) => JsonSerializer.Deserialize<RunSummary>(json);

    private static string Slug(string value) =>
        new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}
