namespace FieldKit.Tasks;

public class TempCleanupTask : MaintenanceTask
{
    public TempCleanupTask() : base(
        "Clear Temporary Files",
        "Removes temporary files that build up over time and waste disk space") { }

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            var tempPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIfNotNull(tempPaths, Environment.GetEnvironmentVariable("TEMP"));
            AddIfNotNull(tempPaths, Environment.GetEnvironmentVariable("TMP"));
            AddIfNotNull(tempPaths, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
            var winDir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            tempPaths.Add(Path.Combine(winDir, "Temp"));

            long totalSize = 0;
            int totalFiles = 0;
            foreach (var path in tempPaths)
            {
                if (!Directory.Exists(path)) continue;
                try
                {
                    foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        totalSize += f.Length;
                        totalFiles++;
                    }
                }
                catch { }
            }
            ContextInfo = totalFiles == 0 ? "Clean" : $"{totalFiles} files, {FormatBytes(totalSize)}";
        }
        catch { }
    }, ct);

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var tempPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIfNotNull(tempPaths, Environment.GetEnvironmentVariable("TEMP"));
            AddIfNotNull(tempPaths, Environment.GetEnvironmentVariable("TMP"));
            AddIfNotNull(tempPaths, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
            var winDir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            tempPaths.Add(Path.Combine(winDir, "Temp"));

            int totalTargeted = 0;
            int totalRemaining = 0;

            foreach (var path in tempPaths)
            {
                if (!Directory.Exists(path)) continue;

                int before = CountItems(path);
                totalTargeted += before;

                if (dryRun)
                {
                    Log($"  [DryRun] {path}: {before} items would be targeted", "INFO");
                    continue;
                }

                DeleteContents(path);
                int after = CountItems(path);
                totalRemaining += after;
                Log($"  {path}: {before} found, {before - after} removed, {after} locked/skipped", "INFO");
            }

            int removed = totalTargeted - totalRemaining;
            Log($"Temp cleanup complete: {removed} removed of {totalTargeted} targeted ({totalRemaining} locked).", "OK");

            if (totalRemaining > 0)
                Log("  Locked files are held by running processes. They will clear on next reboot.", "WARN");

            return TaskResult.Ok($"{removed} of {totalTargeted} items cleared");
        }, ct);
    }

    private static void AddIfNotNull(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrEmpty(value)) set.Add(value);
    }

    private static int CountItems(string path)
    {
        try
        {
            return Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories).Length;
        }
        catch { return 0; }
    }

    private static void DeleteContents(string path)
    {
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(file); } catch { }
            }
            foreach (var dir in Directory.GetDirectories(path))
            {
                try
                {
                    // Skip symlinks and junctions to prevent traversal attacks
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(dir, false); // remove the link itself, don't follow it
                        continue;
                    }
                    // Recurse into real directories, then delete
                    DeleteContents(dir);
                    Directory.Delete(dir, false);
                }
                catch { }
            }
        }
        catch { }
    }
}
