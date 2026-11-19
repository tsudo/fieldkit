using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FieldKit.Services;

namespace FieldKit.Pages;

public partial class StorageSettingsPage : UserControl, IPage
{
    private readonly Logger _logger;
    public StorageSettingsPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
    }

    public void OnShown()
    {
        Enable.IsEnabled = Undo.IsEnabled = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            Account.Text = $"Account: {identity.Name}";
            using var key = Registry.CurrentUser.OpenSubKey(StorageSenseSettings.PolicyKey);
            using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\StorageSense");
            var managed = policy?.GetValue("AllowStorageSenseGlobal") is not null;
            var enabled = key?.GetValue("01") is 1;
            var cadence = key?.GetValue("04") switch
            {
                0 => "when disk space is low", 1 => "daily", 7 => "weekly", 30 => "monthly",
                null => "Windows default", var other => $"configured value {other}"
            };
            State.Text = $"Current user setting: {(enabled ? "On" : "Off")} · Schedule: {cadence}" +
                         (managed ? " · Managed by policy" : "");
            var saved = File.Exists(Path.Combine(AppPaths.Root, "Recovery", $"storage-sense-{identity.User!.Value}.json"));
            Enable.IsEnabled = !enabled && !managed && !saved;
            Undo.IsEnabled = saved && !managed;
        }
        catch (Exception ex) { Status.Text = $"Storage settings unavailable: {ex.Message}"; }
    }

    private void Change(bool restore)
    {
        if (!RunLock.TryEnter("Storage Sense")) { Status.Text = $"Waiting for {RunLock.Owner}."; return; }
        try
        {
            using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\StorageSense");
            if (policy?.GetValue("AllowStorageSenseGlobal") is not null)
                throw new InvalidOperationException("Storage Sense is managed by policy.");
            var settings = StorageSenseSettings.ForCurrentAccount();
            Status.Text = restore ? settings.Restore() : settings.Enable();
            _logger.Log($"[Storage Sense] {Account.Text}. {Status.Text}");
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            _logger.Log($"[Storage Sense] {ex.Message}", "WARN");
        }
        finally { RunLock.Exit(); OnShown(); }
    }

    private void Enable_Click(object sender, RoutedEventArgs e) => Change(false);
    private void Undo_Click(object sender, RoutedEventArgs e) => Change(true);
    private void Open_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:storagesense", _logger);
}
