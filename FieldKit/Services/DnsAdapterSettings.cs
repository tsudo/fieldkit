using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FieldKit.Services;

internal sealed record DnsEncryption(uint Index, ulong Flags, string? Template);
internal sealed record DnsFamily(bool IPv6, string[] Servers, DnsEncryption[] Encryption)
{
    internal bool Matches(DnsFamily other) => IPv6 == other.IPv6
        && Servers.SequenceEqual(other.Servers, StringComparer.OrdinalIgnoreCase)
        && Encryption.OrderBy(e => e.Index).SequenceEqual(other.Encryption.OrderBy(e => e.Index));
}

internal sealed record DnsAdapter(Guid Id, string Name, bool IPv4, bool IPv6, string Effective, bool Connected = true)
{
    public override string ToString() => Connected ? Name : $"{Name} (disconnected)";
}

// IP Helper's version 3 structure handles encryption on this adapter only. The native ABI
// takes the interface GUID by value; NAME_SERVER and DOH are the only fields we change.
internal static class DnsAdapterSettings
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Settings
    {
        public uint Version;
        public ulong Flags;
        public IntPtr Domain, Servers, Search;
        public uint Register, RegisterName, Llmnr, QueryName;
        public IntPtr ProfileServers;
        public uint DisableUnconstrained;
        public IntPtr Supplemental;
        public uint Count;
        public IntPtr Properties;
        public uint ProfileCount;
        public IntPtr ProfileProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Property { public uint Version, Index, Type; public IntPtr Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Doh { public IntPtr Template; public ulong Flags; }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetInterfaceDnsSettings(Guid adapter, ref Settings settings);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint SetInterfaceDnsSettings(Guid adapter, ref Settings settings);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern void FreeInterfaceDnsSettings(ref Settings settings);

    internal static IReadOnlyList<DnsAdapter> List() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && Guid.TryParse(n.Id, out _))
            .Select(n => new DnsAdapter(Guid.Parse(n.Id), n.Name, n.Supports(NetworkInterfaceComponent.IPv4),
                n.Supports(NetworkInterfaceComponent.IPv6), string.Join(", ", n.GetIPProperties().DnsAddresses),
                n.OperationalStatus == OperationalStatus.Up))
            .Where(a => a.IPv4 || a.IPv6).OrderByDescending(a => a.Connected)
            .ThenByDescending(a => a.Effective.Length > 0).ThenBy(a => a.Name).ToList();

    internal static DnsFamily Read(Guid adapter, bool ipv6)
    {
        // A supported interface may have DHCP addresses in GetIPProperties(). Those are
        // display-only; snapshot the persistent manual list, never substitute a DHCP lease.
        var stack = ipv6 ? "Tcpip6" : "Tcpip";
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{stack}\Parameters\Interfaces\{adapter:B}");
        if (key is null) throw new InvalidOperationException("The interface configuration is unavailable.");
        string[] staticServers = ParseServers(key.GetValue("NameServer") as string, ipv6);
        // The DNS_INTERFACE_SETTINGS3 flags documentation specifies IPv6 selection.
        // Windows returns separate v4 and v6 records (verified using a dual-stack adapter).
        // Refuse mixed-family data or inconsistent registry/native reads before allowing changes.
        var native = new Settings { Version = 3, Flags = ipv6 ? 1u : 0u };
        Check(GetInterfaceDnsSettings(adapter, ref native));
        try
        {
            var servers = ParseServers(Marshal.PtrToStringUni(native.Servers), ipv6);
            if (!servers.SequenceEqual(staticServers, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows reported conflicting DNS settings. FieldKit cannot safely save this adapter for rollback.");
            if (staticServers.Length == 0 && native.Count > 0)
                throw new InvalidOperationException("Automatic DNS has custom encryption properties FieldKit cannot safely restore.");
            var encryption = new List<DnsEncryption>();
            for (var i = 0; i < native.Count; i++)
            {
                var prop = Marshal.PtrToStructure<Property>(native.Properties + i * Marshal.SizeOf<Property>());
                if (prop.Version != 1 || prop.Type != 1 || prop.Value == IntPtr.Zero || prop.Index >= servers.Length)
                    throw new InvalidOperationException("This adapter has DNS properties FieldKit cannot restore.");
                var doh = Marshal.PtrToStructure<Doh>(prop.Value);
                encryption.Add(new(prop.Index, doh.Flags, Marshal.PtrToStringUni(doh.Template)));
            }
            return new(ipv6, staticServers, encryption.ToArray());
        }
        finally { FreeInterfaceDnsSettings(ref native); }
    }

    private static string[] ParseServers(string? text, bool ipv6)
    {
        var addresses = (text ?? "").Split([',',' ',';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return addresses.Select(value =>
        {
            var ip = IPAddress.Parse(value);
            if ((ip.AddressFamily == AddressFamily.InterNetworkV6) != ipv6)
                throw new InvalidOperationException("Windows returned an unexpected DNS address family.");
            return ip.ToString();
        }).ToArray();
    }

    internal static void Write(Guid adapter, DnsFamily family)
    {
        var allocated = new List<IntPtr>();
        IntPtr Keep(IntPtr ptr) { allocated.Add(ptr); return ptr; }
        try
        {
            var s = new Settings
            {
                Version = 3, Flags = 2ul | (family.IPv6 ? 1ul : 0ul),
                Servers = Keep(Marshal.StringToHGlobalUni(string.Join(",", family.Servers)))
            };
            // With DHCP there are no static server indices to attach properties to.
            if (family.Servers.Length > 0) s.Flags |= 0x1000;
            s.Count = (uint)family.Encryption.Length;
            if (s.Count > 0)
                s.Properties = Keep(Marshal.AllocHGlobal(checked(family.Encryption.Length * Marshal.SizeOf<Property>())));
            for (var i = 0; i < family.Encryption.Length; i++)
            {
                var e = family.Encryption[i];
                var doh = new Doh { Flags = e.Flags, Template = e.Template is null ? IntPtr.Zero : Keep(Marshal.StringToHGlobalUni(e.Template)) };
                var value = Keep(Marshal.AllocHGlobal(Marshal.SizeOf<Doh>()));
                Marshal.StructureToPtr(doh, value, false);
                Marshal.StructureToPtr(new Property { Version = 1, Type = 1, Index = e.Index, Value = value },
                    s.Properties + i * Marshal.SizeOf<Property>(), false);
            }
            Check(SetInterfaceDnsSettings(adapter, ref s));
        }
        finally { foreach (var memory in allocated) Marshal.FreeHGlobal(memory); }
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new Win32Exception((int)result);
    }
}
