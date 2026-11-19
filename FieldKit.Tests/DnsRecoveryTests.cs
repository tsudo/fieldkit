using System.IO;
using System.Text.Json;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public sealed class DnsRecoveryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("FieldKitDns-").FullName;
    private readonly DnsAdapter _adapter = new(Guid.NewGuid(), "Lab", true, true, "");
    private readonly DnsFamily[] _original =
    [
        new(false, [], []),
        new(true, ["2001:db8::a"], [new DnsEncryption(0, 6, "https://resolver.example/dns-query")])
    ];
    private readonly Dictionary<bool, DnsFamily> _live;
    private DnsRecovery Service => new(_folder, (_,v6) => _live[v6], (_,f) => _live[f.IPv6] = f);
    public DnsRecoveryTests() => _live = _original.ToDictionary(f => f.IPv6);
    public void Dispose() => Directory.Delete(_folder, true);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PresetRestoresDhcpStaticAndExistingEncryption(int provider)
    {
        var target = DnsRecovery.Target(_adapter, (DnsProvider)provider, "abcd12");
        await Service.ApplyAsync(_adapter.Id, target, () => Task.CompletedTask);
        Assert.True(DnsRecovery.Same(target, [_live[false],_live[true]]));
        Service.Restore(_adapter.Id, false);
        Assert.True(DnsRecovery.Same(_original, [_live[false],_live[true]]));
    }

    [Fact]
    public async Task FailedVerificationRestoresPreviousPresetAndKeepsOriginalUndo()
    {
        var cloud = DnsRecovery.Target(_adapter, DnsProvider.Cloudflare, "");
        await Service.ApplyAsync(_adapter.Id, cloud, () => Task.CompletedTask);
        var next = DnsRecovery.Target(_adapter, DnsProvider.NextDns, "abcdef");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ApplyAsync(_adapter.Id, next,
            () => throw new IOException("Profile verification failed")));
        Assert.True(DnsRecovery.Same(cloud, [_live[false],_live[true]]));
        Service.Restore(_adapter.Id, false);
        Assert.True(DnsRecovery.Same(_original, [_live[false],_live[true]]));
    }

    [Fact]
    public async Task PartialSetterFailureRollsBackBothFamilies()
    {
        var calls = 0;
        var service = new DnsRecovery(_folder, (_,v6) => _live[v6], (_,f) =>
        {
            _live[f.IPv6] = f;
            if (++calls == 2) throw new IOException("Setter failed after write");
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(_adapter.Id,
            DnsRecovery.Target(_adapter, DnsProvider.Cloudflare, ""), () => Task.CompletedTask));
        Assert.True(DnsRecovery.Same(_original, [_live[false],_live[true]]));
    }

    [Fact]
    public async Task ExternalChangeRequiresExplicitRestore()
    {
        await Service.ApplyAsync(_adapter.Id, DnsRecovery.Target(_adapter, DnsProvider.Automatic, ""), () => Task.CompletedTask);
        _live[false] = new(false, ["192.0.2.12"], []);
        Assert.Throws<DnsRestoreConflict>(() => Service.Restore(_adapter.Id, false));
        Service.Restore(_adapter.Id, true);
        Assert.True(DnsRecovery.Same(_original, [_live[false],_live[true]]));
    }

    [Fact]
    public void NextDnsVerificationRequiresAssignedProfileAndMatchingLabel()
    {
        Assert.Throws<ArgumentException>(() => DnsRecovery.Target(_adapter, DnsProvider.NextDns, "a/path"));
        using var json = JsonDocument.Parse("""{"profile":"opaque-test-id","deviceName":"FieldKit-correct","status":"ok","protocol":"DOH"}""");
        Assert.True(DnsConnectionTest.ProfileVerified(json.RootElement, "FieldKit-correct"));
        Assert.False(DnsConnectionTest.ProfileVerified(json.RootElement, "FieldKit-other"));
    }
}
