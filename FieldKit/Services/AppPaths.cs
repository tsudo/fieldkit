namespace FieldKit.Services;

/// <summary>
/// Where FieldKit keeps its own files. Everything lives under one folder at the
/// root of the system drive so it does not depend on which account approved the
/// elevation prompt, and so temp cleanup never touches the logs.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();
    public static string Logs => Ensure(Path.Combine(Root, "Logs"));
    public static string Tools => Path.Combine(Root, "Tools");
    public static string Reports => Path.Combine(Root, "Reports");

    private static string ResolveRoot()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        string[] candidates =
        [
            Path.Combine(systemRoot, "FieldKit"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FieldKit"),
            Path.Combine(Path.GetTempPath(), "FieldKit")
        ];

        foreach (var candidate in candidates)
        {
            if (IsUsable(candidate))
                return candidate;
        }

        return candidates[^1];
    }

    private static bool IsUsable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (IsReparsePoint(path))
                return false;

            var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// True for a symlink or junction. Also true when the attributes cannot be
    /// read, so callers that are about to delete treat "unknown" as "do not touch".
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
