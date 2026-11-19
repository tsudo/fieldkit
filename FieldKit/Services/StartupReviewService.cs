using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace FieldKit.Services;

internal sealed record StartupEntry(string Category, string Account, string Location, string Name, string Enabled,
    string Signer, string Company, string Image, string Command)
{
    public string Publisher => Signer.Length == 0 ? Company : Signer;
    public string DisplayAccount => Account.Length == 0 ? "Unspecified" : Account;
    public string DisplayPath => Image.Length == 0 ? Command : Image;
    public bool IsLogon => Category.Equals("Logon", StringComparison.OrdinalIgnoreCase);
}

internal sealed record StartupChange(string Kind, string Name, string Account, string Detail);
internal sealed record StartupSnapshot(DateTimeOffset CheckedAt, List<StartupEntry> Entries, List<StartupChange> Changes,
    bool Baseline, string[]? Warnings = null)
{
    public string Summary => Baseline ? $"Baseline saved with {Entries.Count} entries"
        : $"{Changes.Count} change(s) since the previous scan";
}

internal static class StartupReviewService
{
    internal static readonly KitTool Tool = SupportKitService.Tools.Single(tool => tool.Key == "autoruns");
    private const int MaxBytes = 24 * 1024 * 1024;
    private static string Folder => RecoveryStore.DirectoryPath;
    private const string SnapshotFile = "startup-latest.json";

    internal static StartupSnapshot? ReadLatest() => ReadLatest(Folder);

    private static StartupSnapshot? ReadLatest(string folder)
    {
        var path = Path.Combine(folder, SnapshotFile);
        if (!File.Exists(path)) return null;
        if (AppPaths.IsReparsePoint(path) || new FileInfo(path).Length > MaxBytes * 3L)
            throw new IOException("Cannot read the saved startup snapshot.");
        using var file = File.OpenRead(path);
        var snapshot = JsonSerializer.Deserialize<StartupSnapshot>(file);
        if (snapshot?.Entries is null || snapshot.Changes is null)
            throw new InvalidDataException("The saved startup snapshot is incomplete.");
        return snapshot;
    }

    internal static string GetChangeSummary()
    {
        var latest = ReadLatest();
        return latest is null ? "No Autorunsc scan saved yet." : $"{latest.CheckedAt:g} · {latest.Summary}" +
            (latest.Warnings is { Length: > 0 } ? " (partial coverage)" : "");
    }

    internal static string? FindProgram(bool gui)
    {
        var name = gui ? "Autoruns64.exe" : "autorunsc64.exe";
        foreach (var tool in new[] { Tool, SupportKitService.Tools.Single(t => t.Key == "suite") })
        {
            var folder = SupportKitService.ToolFolder(tool);
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            if (AppPaths.IsReparsePoint(AppPaths.Tools) || AppPaths.IsReparsePoint(folder) || AppPaths.IsReparsePoint(path))
                throw new IOException("Cannot use Autoruns through a linked path.");
            return path;
        }
        return null;
    }

    internal static string VerifyProgram(bool gui)
    {
        var path = FindProgram(gui) ?? throw new FileNotFoundException("Autoruns is not installed in the support kit.");
        if (!Authenticode.IsSignedByMicrosoft(path, out var reason))
            throw new InvalidDataException($"Autoruns verification failed: {reason}.");
        return path;
    }

    internal static async Task<StartupSnapshot> ScanAsync(CancellationToken ct)
    {
        var folder = Folder;
        // Keep the baseline ordered when separate FieldKit processes scan at the same time.
        var lockPath = Path.Combine(folder, "startup-scan.lock");
        if (File.Exists(lockPath) && AppPaths.IsReparsePoint(lockPath)) throw new IOException("Linked startup scan lock.");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var exe = VerifyProgram(false);
        using var process = new Process { StartInfo = new ProcessStartInfo(exe, "-accepteula -nobanner -a lst -s -c *")
        {
            UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }};
        if (!process.Start()) throw new InvalidOperationException("Autorunsc could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            var stderr = ReadTextAsync(process.StandardError.BaseStream, 512 * 1024, timeout.Token);
            var stdout = ReadTextAsync(process.StandardOutput.BaseStream, MaxBytes, timeout.Token);
            await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new InvalidOperationException($"Autorunsc exited with code {process.ExitCode}.");
            var entries = ParseCsv(await stdout);
            var warnings = (await stderr).Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            ct.ThrowIfCancellationRequested();
            return Save(folder, entries, warnings);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Autorunsc did not finish within four minutes.");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }
    }

    private static async Task<string> ReadTextAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + count > limit) throw new InvalidDataException("Autorunsc output exceeded the scan limit.");
            bytes.Write(buffer, 0, count);
        }
        return Decode(bytes.ToArray());
    }

    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 255 && bytes[1] == 254) || bytes[1] == 0))
            return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return CommandRunner.OemEncoding.GetString(bytes); }
    }

    internal static List<StartupEntry> ParseCsv(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv)) { TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException("Autorunsc returned no CSV header.");
        int Column(string key)
        {
            var index = Array.FindIndex(header, h => h.Equals(key, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : throw new InvalidDataException($"Autorunsc output is missing the {key} column.");
        }
        var columns = new[] { "Category", "Profile", "Entry Location", "Entry", "Enabled", "Signer", "Company",
            "Image Path", "Launch String" }.Select(Column).ToArray();
        var entries = new List<StartupEntry>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields();
            if (row is null || row.Length != header.Length) throw new InvalidDataException("Incomplete Autorunsc row.");
            string Get(int index) => row[columns[index]];
            if (Get(3).Length == 0) continue;
            entries.Add(new(Get(0), Get(1), Get(2), Get(3), Get(4), Get(5), Get(6), Get(7), Get(8)));
        }
        if (entries.Count == 0) throw new InvalidDataException("Autorunsc reported no startup entries.");
        return entries;
    }

    internal static StartupSnapshot Save(string folder, List<StartupEntry> entries, string[]? warnings = null)
    {
        var previous = ReadLatest(folder);
        // A newly inaccessible profile can otherwise look like dozens of removed entries.
        var baseline = previous is null || !(previous.Warnings ?? []).SequenceEqual(warnings ?? []);
        var changes = baseline ? [] : Compare(previous!.Entries, entries);
        var snapshot = new StartupSnapshot(DateTimeOffset.Now, entries, changes, baseline, warnings);
        var staging = Path.Combine(folder, $".scan-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(file, snapshot);
            File.Move(staging, Path.Combine(folder, SnapshotFile), true);
        }
        finally { File.Delete(staging); }
        return snapshot;
    }

    internal static List<StartupChange> Compare(IEnumerable<StartupEntry> previous, IEnumerable<StartupEntry> current)
    {
        static (string, string, string, string) Key(StartupEntry e) =>
            (e.Category.ToUpperInvariant(), e.Account.ToUpperInvariant(), e.Location.ToUpperInvariant(), e.Name.ToUpperInvariant());
        var old = previous.ToLookup(Key);
        var now = current.ToLookup(Key);
        var changes = new List<StartupChange>();
        foreach (var key in old.Select(g => g.Key).Union(now.Select(g => g.Key)))
        {
            var before = old[key].ToList();
            var after = now[key].ToList();
            // Some task names occur more than once, with multiple actions. Match unchanged actions first.
            before.RemoveAll(row => after.Remove(row));
            for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
            {
                var a = i < before.Count ? before[i] : null;
                var b = i < after.Count ? after[i] : null;
                var entry = (a ?? b)!;
                string Detail(StartupEntry e) => $"{e.Enabled} · {e.DisplayPath} · {e.Publisher} · {e.Command}";
                var detail = a is null ? Detail(b!) : b is null ? Detail(a) : $"{Detail(a)} → {Detail(b)}";
                changes.Add(new(a is null ? "Added" : b is null ? "Removed" : "Changed", entry.Name,
                    entry.DisplayAccount, $"{entry.Category} · {entry.Location}\n{detail}"));
            }
        }
        return changes;
    }
}
