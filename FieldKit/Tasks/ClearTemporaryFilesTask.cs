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
            // Known-folder paths, not the TEMP and TMP environment variables: a
            // per-user environment value must not choose what an elevated process deletes.
            var userTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            var sharedTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

            var cutoff = DateTime.Now - MinimumAge;
            var counts = new Counts();

            // Folder trees are deleted only where nobody but this account and
            // administrators can write. That is the default for a profile's temp,
            // but software can widen it (sandbox accounts are a real example), and
            // then it gets the same loose-files-only treatment as the shared temp.
            var userTempIsPrivate = Services.AppPaths.IsWritableOnlyByThisAccount(userTemp);
            if (!userTempIsPrivate)
                Log($"Other accounts can write to {userTemp}, so only loose files are removed there.", "INFO");
            Clean(userTemp, includeDirectories: userTempIsPrivate, dryRun, cutoff, counts, ct);

            // C:\Windows\Temp is writable by every user. Deleting a folder tree there
            // by path can be redirected mid-walk by swapping a subfolder for a
            // junction, which would turn this into deleting an arbitrary folder as
            // administrator. So only loose files are removed here; folders are left.
            Clean(sharedTemp, includeDirectories: false, dryRun, cutoff, counts, ct);

            Log($"{(dryRun ? "Would remove" : "Removed")} {counts.Removed} item(s). Left alone: {counts.Recent} newer than 24 hours, " +
                $"{counts.InUse} in use, {counts.Links} link(s), {counts.SharedFolders} folder(s) that other accounts can write to.", "INFO");

            return Summarize(dryRun, counts.Removed, counts.InUse, counts.Recent + counts.SharedFolders);
        }, ct);
    }

    private sealed class Counts
    {
        public int Removed, InUse, Recent, Links, SharedFolders;
    }

    private static void Clean(string root, bool includeDirectories, bool dryRun, DateTime cutoff, Counts counts, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return;

        foreach (var entry in EnumerateEntries(root))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var attrs = File.GetAttributes(entry);

                // Never follow symlinks, junctions, or reparse points.
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    counts.Links++;
                    continue;
                }

                var isDirectory = attrs.HasFlag(FileAttributes.Directory);
                // A folder whose own ACL lets someone else write gets the same
                // treatment as the shared temp, wherever it sits.
                if (isDirectory && (!includeDirectories || !Services.AppPaths.IsWritableOnlyByThisAccount(entry)))
                {
                    counts.SharedFolders++;
                    continue;
                }

                var lastWrite = isDirectory ? NewestWriteUnder(entry) : File.GetLastWriteTime(entry);
                if (lastWrite > cutoff)
                {
                    counts.Recent++;
                    continue;
                }

                if (dryRun)
                {
                    counts.Removed++;
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

                counts.Removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or protected: expected for anything a running program holds open.
                counts.InUse++;
            }
        }
    }

    /// <summary>
    /// The most recent write anywhere in a folder tree. A folder's own timestamp
    /// only moves when its direct children are added or removed, so a folder that
    /// looks old can still hold a file something wrote a minute ago.
    /// </summary>
    internal static DateTime NewestWriteUnder(string directory)
    {
        var newest = Directory.GetLastWriteTime(directory);
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                if (entry.LastWriteTime > newest) newest = entry.LastWriteTime;
                if (entry is DirectoryInfo && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    pending.Push(entry.FullName);
            }
        }

        return newest;
    }

    internal static TaskResult Summarize(bool dryRun, int removed, int inUse, int kept)
    {
        var left = inUse + kept;
        var keptNote = left > 0 ? $", {left} kept (recent, in use or shared folders)" : "";

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
