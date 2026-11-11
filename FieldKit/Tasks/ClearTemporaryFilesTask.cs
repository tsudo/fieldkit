namespace FieldKit.Tasks;

public sealed class ClearTemporaryFilesTask : MaintenanceTask
{
    /// <summary>
    /// Entries changed more recently than this are left alone. A running installer
    /// or an open app often has working files in temp that it still needs.
    /// </summary>
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromHours(24);

    public ClearTemporaryFilesTask() : base(
        "Clear temporary files",
        "Removes temp files older than 24 hours. Files in use are left alone.",
        "Clean up",
        "Under 1 min")
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
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            var cutoff = DateTime.Now - MinimumAge;
            int removed = 0, inUse = 0, recent = 0, links = 0;

            foreach (var path in tempPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(path))
                    continue;

                foreach (var entry in EnumerateEntries(path))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var attrs = File.GetAttributes(entry);

                        // Never follow symlinks, junctions, or reparse points —
                        // a planted junction could redirect deletion to system directories.
                        if (attrs.HasFlag(FileAttributes.ReparsePoint))
                        {
                            links++;
                            continue;
                        }

                        var isDirectory = attrs.HasFlag(FileAttributes.Directory);
                        var lastWrite = isDirectory ? Directory.GetLastWriteTime(entry) : File.GetLastWriteTime(entry);
                        if (lastWrite > cutoff)
                        {
                            recent++;
                            continue;
                        }

                        if (dryRun)
                        {
                            removed++;
                            continue;
                        }

                        if (isDirectory)
                        {
                            Directory.Delete(entry, recursive: true);
                        }
                        else
                        {
                            if (attrs.HasFlag(FileAttributes.ReadOnly))
                                File.SetAttributes(entry, attrs & ~FileAttributes.ReadOnly);
                            File.Delete(entry);
                        }

                        removed++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Locked or protected: expected for anything a running program holds open.
                        inUse++;
                    }
                }
            }

            Log($"{(dryRun ? "Would remove" : "Removed")} {removed} item(s). Left alone: {recent} newer than 24 hours, {inUse} in use, {links} link(s).",
                "INFO");

            return Summarize(dryRun, removed, inUse, recent);
        }, ct);
    }

    internal static TaskResult Summarize(bool dryRun, int removed, int inUse, int recent)
    {
        var kept = inUse + recent;
        var keptNote = kept > 0 ? $", {kept} kept (recent or in use)" : "";

        if (dryRun)
            return TaskResult.Ok($"Would remove {removed} item(s){keptNote}");

        // Files in use are normal. Only call it a warning when nothing at all
        // could be removed and something was blocked.
        if (removed == 0 && inUse > 0)
            return TaskResult.Warn($"Nothing removed; {inUse} item(s) were in use");

        return TaskResult.Ok($"Removed {removed} item(s){keptNote}");
    }

    private static IEnumerable<string> EnumerateEntries(string root)
    {
        IEnumerable<string> directories;
        IEnumerable<string> files;

        try { directories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { directories = []; }

        try { files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { files = []; }

        return directories.Concat(files);
    }
}
