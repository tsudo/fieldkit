using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FieldKit.Services;

internal static class DnsConnectionTest
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Request
    {
        public uint Version;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        public ushort Type;
        public ulong Options;
        public IntPtr Servers;
        public uint Interface;
        public IntPtr Callback, Context;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Result { public uint Version, Status; public ulong Options; public IntPtr Records, Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RecordHeader
    {
        public IntPtr Next, Name;
        public ushort Type, Length;
        public uint Flags, Ttl, Reserved;
    }
    [DllImport("dnsapi.dll", ExactSpelling = true)]
    private static extern int DnsQueryEx(ref Request request, ref Result result, IntPtr cancel);
    [DllImport("dnsapi.dll", ExactSpelling = true)]
    private static extern void DnsRecordListFree(IntPtr records, int freeType);

    private static IPAddress[] Resolve(string host, bool v6, uint index)
    {
        ushort type = v6 ? (ushort)28 : (ushort)1;
        var request = new Request { Version = 1, Name = host, Type = type, Interface = index,
            Options = 0x8 | 0x40 | 0x800 }; // BYPASS_CACHE, NO_HOSTS_FILE, TREAT_AS_FQDN
        var result = new Result { Version = 1 };
        try
        {
            var status = DnsQueryEx(ref request, ref result, IntPtr.Zero);
            if (status != 0) throw new Win32Exception(status, $"DNS lookup of {host} failed on the selected adapter ({status}).");
            var addresses = new List<IPAddress>();
            for (var ptr = result.Records; ptr != IntPtr.Zero;)
            {
                var header = Marshal.PtrToStructure<RecordHeader>(ptr);
                if (header.Type == type && header.Length == (v6 ? 16 : 4))
                {
                    byte[] bytes = new byte[header.Length];
                    Marshal.Copy(ptr + Marshal.SizeOf<RecordHeader>(), bytes, 0, bytes.Length);
                    addresses.Add(new IPAddress(bytes));
                }
                ptr = header.Next;
            }
            return addresses.ToArray();
        }
        finally { if (result.Records != IntPtr.Zero) DnsRecordListFree(result.Records, 1); }
    }

    internal static async Task Run(Guid adapter, DnsFamily[] target)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().Single(n => Guid.TryParse(n.Id, out var id) && id == adapter);
        var props = nic.GetIPProperties();
        bool ipv6Only = !nic.Supports(NetworkInterfaceComponent.IPv4);
        uint index = (uint)(ipv6Only ? props.GetIPv6Properties().Index : props.GetIPv4Properties().Index);
        var doh = target.SelectMany(f => f.Encryption).Select(e => e.Template).FirstOrDefault(t => t != null);
        bool next = Uri.TryCreate(doh, UriKind.Absolute, out var template) && template.Host == "dns.nextdns.io";
        var host = next ? "test.nextdns.io" : "www.microsoft.com";
        var addresses = await Task.Run(() => Resolve(host, ipv6Only, index)).WaitAsync(TimeSpan.FromSeconds(15));
        if (addresses.Length == 0) throw new IOException($"No address for {host} on this adapter.");

        if (doh == "https://security.cloudflare-dns.com/dns-query")
        {
            var blocked = await Task.Run(() => Resolve("malware.testcategory.com", ipv6Only, index)).WaitAsync(TimeSpan.FromSeconds(15));
            if (blocked.Length == 0 || blocked.Any(ip => !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any)))
                throw new IOException("The Cloudflare malware-filter test did not return its blocked address.");
        }

        // Reuse the selected adapter's DNS answer for TLS and bind to a local address on
        // that same adapter. Neither the default adapter's DNS nor an HTTP proxy can pass this check.
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        handler.ConnectCallback = async (context, ct) =>
        {
            foreach (var ip in addresses)
            {
                var local = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == ip.AddressFamily &&
                    !a.Address.IsIPv6LinkLocal);
                if (local == null) continue;
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(local.Address, 0));
                    await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new IOException("HTTPS is unreachable on the selected adapter.");
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 65_536 };
        using var request = new HttpRequestMessage(next ? HttpMethod.Get : HttpMethod.Head, $"https://{host}/");
        using var response = await client.SendAsync(request);
        if ((int)response.StatusCode >= 400) throw new IOException($"Connectivity check returned {(int)response.StatusCode}.");
        if (next)
        {
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!ProfileVerified(result.RootElement, template!.Segments.Last()))
                throw new IOException("NextDNS did not confirm the configured profile and device label over HTTPS.");
        }
    }

    internal static bool ProfileVerified(JsonElement json, string device)
    {
        string? Value(string key) => json.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() : null;
        return Value("status") == "ok" && string.Equals(Value("protocol"), "DOH", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(Value("profile")) && Value("deviceName") == device;
    }
}
