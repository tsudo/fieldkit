using System.Text.Json;
using System.Text.RegularExpressions;

namespace FieldKit.Services;

internal enum DnsProvider { Automatic, Cloudflare, NextDns }
internal sealed record DnsBackup(Guid Adapter, DnsFamily[] Original, DnsFamily[] Target, bool Verified);

// The transaction is independent of IP Helper so failure/rollback paths can be exercised
// on disposable configurations without touching the machine running the tests.
internal sealed class DnsRecovery(string folder, Func<Guid, bool, DnsFamily> read, Action<Guid, DnsFamily> write)
{
    internal static DnsRecovery Open() => new(RecoveryStore.DirectoryPath, DnsAdapterSettings.Read, DnsAdapterSettings.Write);
    internal static bool HasRecord(string root, Guid adapter) =>
        File.Exists(Path.Combine(root, "Recovery", $"dns-{adapter:N}.json"));

    internal static DnsFamily[] Target(DnsAdapter adapter, DnsProvider provider, string profile)
    {
        if (provider == DnsProvider.NextDns && !Regex.IsMatch(profile, @"\A[0-9a-fA-F]{6}\z"))
            throw new ArgumentException("Enter the six hexadecimal characters from your NextDNS profile.");
        // A random label lets the provider confirm it saw this exact template, without
        // sending a machine/user name. The test endpoint masks the original profile ID.
        var template = provider switch
        {
            DnsProvider.NextDns => $"https://dns.nextdns.io/{profile.ToLowerInvariant()}/FieldKit-{Guid.NewGuid():N}",
            DnsProvider.Cloudflare => "https://security.cloudflare-dns.com/dns-query",
            _ => null
        };
        DnsFamily Family(bool v6)
        {
            string[] servers = (provider, v6) switch
            {
                (DnsProvider.Cloudflare, false) => ["1.1.1.2", "1.0.0.2"],
                (DnsProvider.Cloudflare, true) => ["2606:4700:4700::1112", "2606:4700:4700::1002"],
                (DnsProvider.NextDns, false) => ["45.90.28.0", "45.90.30.0"],
                (DnsProvider.NextDns, true) => ["2a07:a8c0::", "2a07:a8c1::"],
                _ => []
            };
            return new(v6, servers, servers.Select((_, i) => new DnsEncryption((uint)i, 2, template)).ToArray());
        }
        return new[] {false,true}.Where(v6 => v6 ? adapter.IPv6 : adapter.IPv4).Select(Family).ToArray();
    }

    private string PathFor(Guid adapter) => Path.Combine(folder, $"dns-{adapter:N}.json");
    private FileStream Lock(Guid adapter) => new(Path.Combine(folder, $"dns-{adapter:N}.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);
    private DnsBackup? Load(Guid adapter)
    {
        if (!File.Exists(PathFor(adapter))) return null;
        var b = JsonSerializer.Deserialize<DnsBackup>(File.ReadAllText(PathFor(adapter)));
        if (b is null || b.Adapter != adapter || b.Original.Length == 0 ||
            !b.Original.Select(x => x.IPv6).SequenceEqual(b.Target.Select(x => x.IPv6)))
            throw new InvalidDataException("The DNS recovery file does not match this adapter.");
        return b;
    }
    private void Save(DnsBackup backup)
    {
        var path = PathFor(backup.Adapter);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(f, backup);
                f.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    private void SetAndReadBack(Guid id, DnsFamily family)
    {
        write(id, family);
        if (!read(id, family.IPv6).Matches(family))
            throw new IOException($"Windows did not retain the requested IPv{(family.IPv6 ? 6 : 4)} DNS settings.");
    }

    internal async Task ApplyAsync(Guid adapter, DnsFamily[] target, Func<Task> verify)
    {
        using var lease = Lock(adapter);
        var before = target.Select(f => read(adapter, f.IPv6)).ToArray();
        var prior = Load(adapter);
        if (prior != null && (!prior.Verified || !Same(prior.Target, before)))
            throw new InvalidOperationException("DNS has changed since the saved setup or a change was interrupted. Review Restore previous settings first.");
        var backup = new DnsBackup(adapter, prior?.Original ?? before, target, false);
        Save(backup);
        var touched = new List<DnsFamily>();
        try
        {
            for (var i = 0; i < target.Length; i++)
            {
                touched.Add(before[i]);
                SetAndReadBack(adapter, target[i]);
            }
            await verify();
            Save(backup with { Verified = true });
        }
        catch (Exception problem)
        {
            var failures = new List<string>();
            foreach (var old in touched.AsEnumerable().Reverse())
            {
                try { SetAndReadBack(adapter, old); }
                catch (Exception rollback) { failures.Add(rollback.Message); }
            }
            if (failures.Count > 0)
                throw new IOException($"DNS change failed: {problem.Message} Rollback needs attention: {string.Join("; ", failures)}. The recovery record is retained.");
            if (prior is null) File.Delete(PathFor(adapter)); else Save(prior);
            throw new InvalidOperationException($"DNS change failed. Previous settings restored. {problem.Message}", problem);
        }
    }

    internal void Restore(Guid adapter, bool overrideDrift)
    {
        using var lease = Lock(adapter);
        var backup = Load(adapter) ?? throw new InvalidOperationException("No DNS changes are saved for this adapter.");
        var current = backup.Target.Select(f => read(adapter, f.IPv6)).ToArray();
        if (!overrideDrift && current.Where((f,i) => !f.Matches(backup.Target[i]) && !f.Matches(backup.Original[i])).Any())
            throw new DnsRestoreConflict();
        foreach (var old in backup.Original) SetAndReadBack(adapter, old);
        File.Delete(PathFor(adapter));
    }

    internal static bool Same(DnsFamily[] a, DnsFamily[] b) =>
        a.Length == b.Length && a.Select((f,i) => f.Matches(b[i])).All(equal => equal);
}

internal sealed class DnsRestoreConflict() : Exception("DNS was edited outside FieldKit. Restoring would replace those later changes.");
