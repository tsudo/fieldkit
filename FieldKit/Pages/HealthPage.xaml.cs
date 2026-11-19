using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FieldKit.Services;

namespace FieldKit.Pages;

public partial class HealthPage : UserControl, IPage
{
    private sealed record Finding(string Title, string State, string Detail, string Action);
    private readonly ObservableCollection<Finding> _findings = [];
    private readonly Logger _logger;
    private readonly Action _openStartup;
    private readonly Action _openUpdates;

    public HealthPage(Logger logger, Action openStartup, Action openUpdates)
    {
        InitializeComponent();
        _logger = logger;
        _openStartup = openStartup;
        _openUpdates = openUpdates;
        FindingList.ItemsSource = _findings;
        Loaded += (_, _) => OnShown();
    }

    public void OnShown()
    {
        if (_findings.Count == 0) _ = RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (!RefreshButton.IsEnabled) return;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Reading this PC’s security and storage status…";
        _findings.Clear();
        var checks = PcHealth.Checks();
        foreach (var check in checks)
            _findings.Add(new Finding(check.Title, "Checking…", "", check.Action));
        try
        {
            await Task.WhenAll(checks.Select(async (check, index) =>
            {
                var reading = await PcHealth.ReadSafelyAsync(check, _logger);
                _findings[index] = new Finding(check.Title, reading.State, reading.Detail, check.Action);
            }));
            StatusText.Text = $"Last checked {DateTime.Now:g}";
        }
        finally { RefreshButton.IsEnabled = true; }
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Finding finding }) return;
        if (finding.Action == "Startup review") { _openStartup(); return; }
        if (finding.Action == "Updates") { _openUpdates(); return; }
        var target = finding.Action switch
        {
            "Security providers" => "windowsdefender://providers",
            "Virus protection" => "windowsdefender://threat",
            "Firewall settings" => "windowsdefender://network",
            "Storage settings" => "ms-settings:storagesense",
            "Disk settings" => "ms-settings:disksandvolumes",
            "Optimize drives" => Path.Combine(Environment.SystemDirectory, "dfrgui.exe"),
            "Device encryption" => "ms-settings:deviceencryption",
            _ => null
        };
        if (target is not null && !Shell.Open(target, _logger))
            StatusText.Text = $"Windows could not open {finding.Action}.";
    }
}
