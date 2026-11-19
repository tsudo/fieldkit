using System.Management;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using FieldKit.Services;
using Microsoft.Win32;

namespace FieldKit.Pages;

public partial class NetworkPage : UserControl, IPage
{
    private readonly Logger _logger;
    private bool _managed, _busy, _readable;
    private int _generation;

    public NetworkPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        RunLock.Changed += () => Dispatcher.Invoke(Buttons);
    }

    public void OnShown()
    {
        if (_busy) return;
        try
        {
            var previous = (Adapters.SelectedItem as DnsAdapter)?.Id;
            var list = DnsAdapterSettings.List();
            Adapters.ItemsSource = list;
            Adapters.SelectedItem = list.FirstOrDefault(a => a.Id == previous) ?? list.FirstOrDefault();
            if (list.Count == 0) Status.Text = "No configurable network adapters are available.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        Buttons();
    }

    private static string? ManagedReason()
    {
        using var query = new ManagementObjectSearcher("SELECT PartOfDomain FROM Win32_ComputerSystem");
        using var entries = query.Get();
        foreach (ManagementObject pc in entries)
        {
            using (pc) if (pc["PartOfDomain"] as bool? == true) return "Domain-joined PC.";
        }
        foreach (var path in new[] {@"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient",
                 @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig",
                 @"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts"})
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key != null && key.SubKeyCount + key.ValueCount > 0) return "Windows DNS policy or device management is present.";
        }
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            var description = nic.Name + " " + nic.Description;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp ||
                new[] {"vpn", "wireguard", "wintun", "warp", "tailscale", "anyconnect", "globalprotect", "fortinet", "tap-windows"}
                    .Any(name => description.Contains(name, StringComparison.OrdinalIgnoreCase)))
                return "An active VPN or tunnel may depend on its own DNS.";
        }
        return null;
    }

    private static DnsFamily[] Read(DnsAdapter a) => new[] { false, true }
        .Where(v6 => v6 ? a.IPv6 : a.IPv4).Select(v6 => DnsAdapterSettings.Read(a.Id, v6)).ToArray();

    private async void AdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        var gen = ++_generation;
        _readable = false;
        if (ManagedConsent == null) return;
        ManagedConsent.IsChecked = false;
        ManagementText.Text = "";
        ManagedConsent.Visibility = Visibility.Collapsed;
        Buttons();
        if (Adapters.SelectedItem is not DnsAdapter a) { CurrentText.Text = ""; return; }
        CurrentText.Text = $"Current DNS: {a.Effective}. Reading saved settings…";
        try
        {
            var (families, reason) = await Task.Run(() => (Read(a), ManagedReason()));
            if (gen != _generation) return;
            var info = string.Join("\n", families.Select(f => $"IPv{(f.IPv6 ? 6 : 4)}: " +
                (f.Servers.Length == 0 ? "Automatic" : string.Join(", ", f.Servers)) +
                (f.Encryption.Length > 0 ? " · encrypted DNS configured" : "")));
            CurrentText.Text = info + $"\nEffective DNS: {(a.Effective.Length == 0 ? "none reported" : a.Effective)}";
            _managed = reason != null;
            _readable = true;
            ManagementText.Text = reason;
            ManagedConsent.Visibility = _managed ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (gen != _generation) return;
            CurrentText.Text = $"Current DNS: {a.Effective}. Settings unavailable: {ex.Message}";
        }
        Buttons();
    }

    private void Buttons()
    {
        if (Apply == null) return;
        var idle = !_busy && !RunLock.IsBusy;
        Adapters.IsEnabled = Providers.IsEnabled = Refresh.IsEnabled = idle;
        var selected = Adapters.SelectedItem as DnsAdapter;
        var canChange = idle && _readable && selected != null && (!_managed || ManagedConsent.IsChecked == true);
        Test.IsEnabled = idle && _readable && selected?.Connected == true;
        Apply.IsEnabled = canChange && selected!.Connected;
        Restore.IsEnabled = canChange && DnsRecovery.HasRecord(AppPaths.Root, selected!.Id);
    }

    private async Task Run(string name, Func<DnsAdapter, Task<string>> work)
    {
        if (Adapters.SelectedItem is not DnsAdapter adapter || !RunLock.TryEnter(name)) return;
        _busy = true;
        Buttons();
        Status.Text = name + "…";
        try { Status.Text = await work(adapter); _logger.Log($"[Network] {name} completed for {adapter.Name}."); }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            _logger.Log($"[Network] {name} failed for {adapter.Name} ({ex.GetType().Name}).", "WARN");
        }
        finally { _busy = false; RunLock.Exit(); OnShown(); }
    }

    private bool Confirm(string text) => MessageBox.Show(Window.GetWindow(this), text, "FieldKit network settings",
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var provider = (DnsProvider)Providers.SelectedIndex;
        var profile = Profile.Text.Trim();
        var consent = ManagedConsent.IsChecked == true;
        if (Adapters.SelectedItem is not DnsAdapter a || !Confirm($"Apply {((ComboBoxItem)Providers.SelectedItem).Content} to {a.Name}? FieldKit will save the original settings, test connectivity, and roll back if verification fails.")) return;
        await Run("Apply DNS", adapter => Task.Run(async () =>
        {
            if (ManagedReason() != null && !consent) throw new InvalidOperationException("Managed network detected. Review the adapter and authorize first.");
            var target = DnsRecovery.Target(adapter, provider, profile);
            await DnsRecovery.Open().ApplyAsync(adapter.Id, target, () => DnsConnectionTest.Run(adapter.Id, target));
            return $"Verified DNS on {adapter.Name}. Previous settings can be restored.";
        }));
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Adapters.SelectedItem is not DnsAdapter a || !Confirm($"Restore the original DNS settings on {a.Name}?")) return;
        var consent = ManagedConsent.IsChecked == true;
        await Run("Restore DNS", async adapter =>
        {
            if (await Task.Run(ManagedReason) != null && !consent)
                throw new InvalidOperationException("Managed network detected. Review the adapter and authorize first.");
            var recovery = DnsRecovery.Open();
            try { await Task.Run(() => recovery.Restore(adapter.Id, false)); }
            catch (DnsRestoreConflict ex)
            {
                if (!Confirm(ex.Message + " Restore anyway?")) return "Restore canceled.";
                await Task.Run(() => recovery.Restore(adapter.Id, true));
            }
            return "Original DNS settings restored and checked.";
        });
    }

    private async void Test_Click(object sender, RoutedEventArgs e) =>
        await Run("Test DNS", adapter => Task.Run(async () =>
        {
            await DnsConnectionTest.Run(adapter.Id, Read(adapter));
            return "DNS and HTTPS checks passed on the selected adapter.";
        }));

    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilePanel != null) ProfilePanel.Visibility = Providers.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ConsentChanged(object sender, RoutedEventArgs e) => Buttons();
    private void Refresh_Click(object sender, RoutedEventArgs e) => OnShown();
    private void Setup_Click(object sender, RoutedEventArgs e) => Shell.Open("https://my.nextdns.io", _logger);
}
