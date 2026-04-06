namespace FieldKit.Tasks;

public sealed class ClearTemporaryFilesTask : MaintenanceTask
{
    public ClearTemporaryFilesTask() : base(
        "Clear Temporary Files",
        "Removes common temp files to recover disk space. Files in use are skipped.",
        "Cleanup",
        "Quick")
    {
    }

    public override Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var tempPaths = new[]
            {
                Environment.GetEnvironmentVariable("TEMP"),
                Environment.GetEnvironmentVariable("TMP"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            int targeted = 0;
            int removed = 0;
            int skipped = 0;

            foreach (var path in tempPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(path))
                    continue;

                foreach (var entry in EnumerateEntries(path))
                {
                    targeted++;
                    if (dryRun)
                        continue;

                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, recursive: true);
                        }
                        else if (File.Exists(entry))
                        {
                            File.SetAttributes(entry, FileAttributes.Normal);
                            File.Delete(entry);
                        }

                        removed++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }
            }

            if (dryRun)
            {
                Log($"Would target approximately {targeted} temp items.", "INFO");
                return TaskResult.Ok($"{targeted} temp items identified");
            }

            Log($"Removed {removed} temp items. Skipped {skipped} locked or protected items.", skipped > 0 ? "WARN" : "OK");
            return skipped > 0
                ? TaskResult.Warn($"{removed} temp items removed, {skipped} skipped")
                : TaskResult.Ok($"{removed} temp items removed");
        }, ct);
    }

    private static IEnumerable<string> EnumerateEntries(string root)
    {
        IEnumerable<string> directories;
        IEnumerable<string> files;

        try { directories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly); }
        catch { directories = []; }

        try { files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly); }
        catch { files = []; }

        foreach (var entry in directories.Concat(files))
            yield return entry;
    }
}
