using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit;

public partial class MainWindow : Window
{
    private readonly IReadOnlyList<MaintenanceTask> _tasks;
    private readonly Logger _logger;
    private readonly ObservableCollection<TaskViewModel> _taskViewModels = new();
    private CancellationTokenSource? _runCts;

    public MainWindow()
    {
        InitializeComponent();
        _tasks = Array.Empty<MaintenanceTask>();
        _logger = new Logger();
    }

    public MainWindow(IReadOnlyList<MaintenanceTask> tasks, Logger logger) : this()
    {
        _tasks = tasks;
        _logger = logger;

        foreach (var task in _tasks)
            _taskViewModels.Add(new TaskViewModel(task));

        TaskGrid.ItemsSource = _taskViewModels;
        _logger.LogWritten += OnLogWritten;

        Loaded += async (_, _) => await LoadContextAsync();
        Closed += (_, _) =>
        {
            _logger.LogWritten -= OnLogWritten;
            _runCts?.Dispose();
        };

        UpdateSummary();
    }

    private async Task LoadContextAsync()
    {
        foreach (var vm in _taskViewModels)
        {
            try { await vm.Task.GatherContextAsync(); }
            catch (Exception ex) { _logger.Log($"[{vm.Name}] Context probe failed: {ex.Message}", "WARN"); }
        }
        RefreshDetails();
    }

    // ================================================================
    // TOOLBAR HANDLERS
    // ================================================================

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await RunSelectedTasksAsync();

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_logger.LogFilePath))
            {
                MessageBox.Show(this, "No log file has been created yet.", "Open Log",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = _logger.LogFilePath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the log file: {ex.Message}", "Open Log",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new SystemInfoWindow { Owner = this };
        win.ShowDialog();
    }

    // ================================================================
    // PRESETS
    // ================================================================

    private void PresetAll_Click(object sender, RoutedEventArgs e) => ApplyPreset(_ => true);
    private void PresetCleanup_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(vm => vm.Category is "Preparation" or "Cleanup");
    private void PresetUpdates_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(vm => vm.Category == "Updates");
    private void PresetHealth_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(vm => vm.Category is "Preparation" or "Repair & Optimize");
    private void PresetDefaults_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(vm => vm.Task.SelectedByDefault);

    private void ApplyPreset(Func<TaskViewModel, bool> predicate)
    {
        foreach (var vm in _taskViewModels)
            vm.IsSelected = predicate(vm);
        UpdateSummary();
    }

    // ================================================================
    // TASK SELECTION
    // ================================================================

    private void TaskGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDetails();

    private void RefreshDetails()
    {
        var vm = TaskGrid.SelectedItem as TaskViewModel;
        if (vm is null)
        {
            DetailsBox.Text = "Select an operation to see details.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(vm.Name);
        sb.AppendLine();
        sb.AppendLine(vm.Task.Description);
        sb.AppendLine();
        sb.AppendLine($"Area:      {vm.Category}");
        sb.AppendLine($"Time:      {vm.EstimatedTime}");
        sb.AppendLine($"Status:    {vm.Status}");
        sb.AppendLine($"Type:      {vm.TypeLabel}");
        if (vm.Task.RequiresInternet)
            sb.AppendLine("Internet:  Required");
        if (vm.Task.MayRequireReboot)
            sb.AppendLine("Reboot:    May be needed");
        if (!string.IsNullOrWhiteSpace(vm.Task.ContextInfo))
        {
            sb.AppendLine();
            sb.AppendLine(vm.Task.ContextInfo);
        }

        DetailsBox.Text = sb.ToString();
    }

    private void UpdateSummary()
    {
        var selected = _taskViewModels.Count(vm => vm.IsSelected);
        var advanced = _taskViewModels.Count(vm => vm.IsSelected && vm.Task.IsAdvanced);

        SelectedText.Text = $"Selected: {selected}" + (advanced > 0 ? $" ({advanced} advanced)" : "");
    }

    // ================================================================
    // RUN ENGINE
    // ================================================================

    private async Task RunSelectedTasksAsync()
    {
        if (_runCts is not null) return;

        var selected = _taskViewModels.Where(vm => vm.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one operation before running.",
                "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClearLogPlaceholder();
        _runCts = new CancellationTokenSource();
        var dryRun = false;
        var startFree = TryGetSystemDriveFreeSpace();
        var rebootRecommended = false;
        int success = 0, warning = 0, error = 0, skipped = 0;

        SetRunningState(true);
        _logger.Log($"Run started. Preview: {(dryRun ? "ON" : "OFF")}. Log: {_logger.LogFilePath}", "INFO");

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var vm = selected[i];
                StatusText.Text = $"Running {vm.Name} ({i + 1}/{selected.Count})";
                ProgressBar.Value = (double)i / selected.Count * 100;
                vm.Status = "Running";
                RefreshDetails();

                TaskResult result;
                try { result = await vm.Task.ExecuteAsync(dryRun, _runCts.Token); }
                catch (OperationCanceledException) { result = TaskResult.Skipped("Cancelled"); }
                catch (Exception ex)
                {
                    _logger.Log($"[{vm.Name}] Unhandled failure: {ex.Message}", "ERROR");
                    result = TaskResult.Fail(ex.Message);
                }

                rebootRecommended |= result.RebootRecommended;
                switch (result.State)
                {
                    case TaskResultState.Success: success++; vm.Status = "Success"; break;
                    case TaskResultState.Warning: warning++; vm.Status = "Warning"; break;
                    case TaskResultState.Skipped: skipped++; vm.Status = "Skipped"; break;
                    default: error++; vm.Status = "Error"; break;
                }

                if (!string.IsNullOrWhiteSpace(result.Summary))
                    _logger.Log($"[{vm.Name}] {result.Summary}", result.State switch
                    {
                        TaskResultState.Success => "OK",
                        TaskResultState.Warning => "WARN",
                        TaskResultState.Skipped => "WARN",
                        _ => "ERROR"
                    });

                RefreshDetails();
            }
        }
        finally
        {
            ProgressBar.Value = 100;
            StatusText.Text = "Run complete";
            SetRunningState(false);
            _runCts.Dispose();
            _runCts = null;
        }

        ShowCompletionSummary(dryRun, success, warning, error, skipped,
            startFree, TryGetSystemDriveFreeSpace(), rebootRecommended);
    }

    private void ShowCompletionSummary(bool dryRun, int success, int warning, int error, int skipped,
        double? startFree, double? endFree, bool rebootRecommended)
    {
        var sb = new StringBuilder();
        sb.AppendLine(dryRun ? "Preview complete." : "Run complete.");
        sb.AppendLine();
        sb.AppendLine($"Successful: {success}");
        sb.AppendLine($"Warnings: {warning}");
        sb.AppendLine($"Errors: {error}");
        if (skipped > 0) sb.AppendLine($"Skipped: {skipped}");

        if (startFree.HasValue && endFree.HasValue)
        {
            var delta = Math.Round(endFree.Value - startFree.Value, 2);
            sb.AppendLine();
            sb.AppendLine($"System drive: {startFree.Value:F2} GB \u2192 {endFree.Value:F2} GB ({delta:+0.00;-0.00;0.00} GB)");
        }

        if (rebootRecommended)
        {
            sb.AppendLine();
            sb.AppendLine("A reboot is recommended before relying on repair results.");
        }

        sb.AppendLine();
        sb.AppendLine($"Log: {_logger.LogFilePath}");

        MessageBox.Show(this, sb.ToString(), "FieldKit",
            MessageBoxButton.OK, warning > 0 || error > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void SetRunningState(bool running)
    {
        RunButton.IsEnabled = !running;
        TaskGrid.IsEnabled = !running;
    }

    // ================================================================
    // LOG
    // ================================================================

    private void OnLogWritten(string message, string level)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ClearLogPlaceholder();
            LogBox.AppendText(message + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void ClearLogPlaceholder()
    {
        if (LogBox.Opacity < 1.0)
        {
            LogBox.Text = "";
            LogBox.Opacity = 1.0;
        }
    }

    private static double? TryGetSystemDriveFreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace / 1024d / 1024d / 1024d;
        }
        catch { return null; }
    }
}
