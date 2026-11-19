using System.Windows;
using System.Windows.Controls;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit.Pages;

public partial class UpdatesPage : UserControl, IPage
{
    private sealed class AppRow(WingetUpgrade entry) : RowViewModel(entry.Name, entry.Id)
    {
        public string Id => entry.Id;
    }

    private readonly Logger _logger;
    private readonly UpdateService _updates;
    private readonly Action _openHistory;
    private List<AppRow> _apps = [];
    private UpdateScan? _scan;
    private RunSummary? _lastRun;
    private CancellationTokenSource? _cts;

    public UpdatesPage(Logger logger, IReadOnlyList<MaintenanceTask> tasks, Action openHistory)
    {
        InitializeComponent();
        _logger = logger;
        _updates = new UpdateService(logger);
        _openHistory = openHistory;
        OfficeButton.Tag = tasks.OfType<OfficeUpdateTask>().Single();
        StoreButton.Tag = tasks.OfType<StoreUpdateTask>().Single();
        RunLock.Changed += () => Dispatcher.Invoke(UpdateButtons);
    }

    public void OnShown()
    {
        if (_cts is null) RefreshHistory();
        UpdateButtons();
    }

    private void RefreshHistory()
    {
        var history = RunHistory.LoadAll().Runs.Where(run => run.Kind == "Updates").ToList();
        _lastRun = history.FirstOrDefault(run => !run.Preview);
        var lastCheck = history.FirstOrDefault(run => run.Preview);
        if (_scan is { } scan && (_lastRun?.Finished > scan.CheckedAt || lastCheck?.Started > scan.CheckedAt))
            ClearSelection();

        var time = _scan?.CheckedAt ?? lastCheck?.Finished;
        LastCheckText.Text = time is { } checkedAt ? $"Last checked {checkedAt:g}" : "No check recorded yet";
        LastRunText.Text = _lastRun is null ? "No update run recorded yet"
            : $"{_lastRun.Finished:g}  ·  {(_lastRun.Stopped ? "Stopped  ·  " : "")}{_lastRun.CountsText}";
        RunLogButton.IsEnabled = _lastRun is not null;
        var restart = SystemInfo.HasPendingReboot() || _scan?.RestartPending == true;
        RestartText.Text = restart ? "Restart pending" : "No pending Windows restart detected";
        RestartText.SetResourceReference(ForegroundProperty, restart ? "Fk.Warn.Text" : "Fk.Text.Secondary");
        if (_scan is not null) return;

        string PriorStatus(string source)
        {
            var record = history.FirstOrDefault(run => run.Preview && run.Items?.Any(item => item.Name == source) == true);
            if (record is null) return "Ready to check";
            if (_lastRun?.Finished > record.Finished) return "Check again for remaining updates";
            return $"Last check ({record.Finished:g}): {record.Items.First(item => item.Name == source).Summary}";
        }
        WindowsStatus.Text = PriorStatus("Windows Update");
        AppsStatus.Text = PriorStatus("App updates (winget)");
    }

    private void ClearSelection()
    {
        _scan = null;
        _apps = [];
        WindowsList.ItemsSource = null;
        AppList.ItemsSource = null;
        IncludeWindows.IsChecked = false;
        SelectionPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowScan(UpdateScan scan)
    {
        _scan = scan;
        WindowsStatus.Text = Describe(scan.WindowsTitles.Count, scan.WindowsProblem);
        AppsStatus.Text = Describe(scan.Apps.Count, scan.AppsProblem);
        WindowsList.ItemsSource = scan.WindowsTitles;
        IncludeWindows.Visibility = scan.WindowsTitles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _apps = scan.Apps.Select(entry => new AppRow(entry)
        {
            Meta = $"{entry.InstalledVersion} → {entry.AvailableVersion}"
        }).ToList();
        foreach (var row in _apps) row.SelectionChanged += UpdateButtons;
        AppList.ItemsSource = _apps;
        SelectionPanel.Visibility = scan.WindowsTitles.Count + _apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Describe(int count, string? problem)
    {
        if (count == 0) return problem ?? "Up to date";
        var status = count == 1 ? "1 update available" : $"{count} updates available";
        return problem is null ? status : $"{status}. {problem}";
    }

    private void UpdateButtons()
    {
        var idle = !RunLock.IsBusy;
        CheckButton.IsEnabled = idle;
        AllButton.IsEnabled = idle && _scan is { } scan && scan.WindowsTitles.Count + _apps.Count > 0;
        int selected = _apps.Count(row => row.IsSelected) + (IncludeWindows.IsChecked == true ? _scan?.WindowsTitles.Count ?? 0 : 0);
        SelectedButton.Content = selected > 0 ? $"Update selected ({selected})" : "Update selected";
        SelectedButton.IsEnabled = idle && selected > 0;
        SelectionPanel.IsEnabled = idle;
        MoreActions.IsEnabled = idle;
        StopButton.Visibility = _cts is null ? Visibility.Collapsed : Visibility.Visible;
        StopButton.IsEnabled = _cts is { IsCancellationRequested: false };
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Update check", async ct =>
        {
            ClearSelection();
            WindowsStatus.Text = AppsStatus.Text = "Checking…";
            Status("Checking Windows Update and winget…");
            var scan = await _updates.ScanAsync(true, true, ct);
            ShowScan(scan);
            Status(scan.HistoryProblem ?? (scan.WindowsProblem is null && scan.AppsProblem is null
                ? "" : "Check the source results above."));
        });
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is not { } scan) return;
        bool all = sender == AllButton;
        bool windows = scan.WindowsTitles.Count > 0 && (all || IncludeWindows.IsChecked == true);
        var apps = _apps.Where(row => all || row.IsSelected).ToList();
        if (!windows && apps.Count == 0) return;
        var targets = new List<string>();
        if (windows) targets.Add($"{scan.WindowsTitles.Count} Windows update(s)");
        if (apps.Count > 0) targets.Add($"{apps.Count} app update(s) via winget");
        string details = string.Join(" and ", targets);
        if (MessageBox.Show(Window.GetWindow(this),
            $"Install {details}?\n\nFieldKit accepts applicable licence terms. A restart may be needed.",
            "Confirm updates", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            != MessageBoxResult.Yes) return;

        await RunAsync("Updates", async ct =>
        {
            ClearSelection();
            Status($"Installing {details}…");
            var result = await _updates.ApplyAsync(scan, all || windows, apps.Select(row => row.Id).ToArray(), ct,
                includeApps: all);
            Status($"{(result.Stopped ? "Stopped" : "Update run finished")}: {result.CountsText}." +
                   (result.RebootRecommended ? " Restart recommended." : ""));
        });
    }

    private async void Extra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MaintenanceTask task }) return;
        await RunAsync(task.Name, async ct =>
        {
            Status($"Checking {task.Name}…");
            var result = await new RunEngine(_logger).RunAsync([new TaskViewModel(task)], false, task.Name, ct);
            Status(result.Items.First().Summary);
        });
    }

    private async Task RunAsync(string name, Func<CancellationToken, Task> operation)
    {
        if (!RunLock.TryEnter(name, Stop)) return;
        _cts = new CancellationTokenSource();
        UpdateButtons();
        try { await operation(_cts.Token); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { Status($"{name} stopped."); }
        catch (Exception ex)
        {
            Status($"{name} could not finish. {ex.Message}");
            _logger.Log($"[{name}] {ex}", "ERROR");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            try { RefreshHistory(); }
            finally { RunLock.Exit(); }
        }
    }

    private void Stop()
    {
        _cts?.Cancel();
        Status("Stop requested. Waiting for the current operation…");
        UpdateButtons();
    }

    private void Status(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Selection_Click(object sender, RoutedEventArgs e) => UpdateButtons();
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();
    private void History_Click(object sender, RoutedEventArgs e) => _openHistory();

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRun is { } run && !ReportsPage.TryOpenRunLog(run.LogFile, _logger))
            Status("That log file is unavailable in the kit folder.");
    }
}
