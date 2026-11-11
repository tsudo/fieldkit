using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit.Pages;

public partial class MaintainPage : UserControl, IPage
{
    private static readonly string[] CategoryOrder = ["Prepare", "Clean up", "Update", "Repair"];

    private readonly Logger _logger;
    private readonly RunEngine _engine;
    private readonly List<TaskViewModel> _rows;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _runCts;
    private DateTime _runStarted;
    private string _progressText = "";
    private bool _applyingPreset;
    private bool _contextLoaded;

    public MaintainPage(IReadOnlyList<MaintenanceTask> tasks, Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        _engine = new RunEngine(logger);
        _rows = tasks.Select(task => new TaskViewModel(task)).ToList();

        foreach (var row in _rows)
            row.SelectionChanged += OnRowSelectionChanged;

        GroupList.ItemsSource = _rows
            .GroupBy(row => row.Category)
            .OrderBy(group => Array.IndexOf(CategoryOrder, group.Key) is var index && index < 0 ? int.MaxValue : index)
            .Select(group => new GroupViewModel(group.Key, group))
            .ToList();

        _engine.StepStarting += (index, count, row) => _progressText = $"Step {index + 1} of {count}: {row.Name}";
        _clock.Tick += (_, _) => OnClockTick();
        RunLock.Changed += () => Dispatcher.Invoke(UpdateRunButton);

        PresetQuick.IsChecked = true;
        UpdateRunButton();
    }

    public void OnShown()
    {
        if (_contextLoaded) return;
        _contextLoaded = true;
        _ = LoadContextAsync();
    }

    private async Task LoadContextAsync()
    {
        foreach (var row in _rows)
        {
            try
            {
                await row.Task.GatherContextAsync();
                row.RefreshMeta();
            }
            catch (Exception ex)
            {
                // A probe only decorates the row; the step itself still runs.
                _logger.Log($"[{row.Name}] Could not read current state: {ex.Message}", "WARN");
            }
        }
    }

    // ================================================================
    // SELECTION
    // ================================================================

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        Func<TaskViewModel, bool> predicate =
            sender == PresetCleanup ? row => row.Category is "Prepare" or "Clean up"
            : sender == PresetUpdates ? row => row.Category == "Update"
            : sender == PresetRepair ? row => row.Category is "Prepare" or "Repair"
            : sender == PresetAll ? _ => true
            : row => row.Task.SelectedByDefault;

        _applyingPreset = true;
        foreach (var row in _rows)
            row.IsSelected = predicate(row);
        _applyingPreset = false;

        UpdateRunButton();
    }

    private void OnRowSelectionChanged()
    {
        if (_applyingPreset) return;

        // A hand-edited selection no longer matches any preset.
        foreach (var chip in PresetBar.Children.OfType<RadioButton>())
            chip.IsChecked = false;
        UpdateRunButton();
    }

    private void Preview_Changed(object sender, RoutedEventArgs e) => UpdateRunButton();

    private string CurrentPresetName =>
        PresetBar.Children.OfType<RadioButton>().FirstOrDefault(chip => chip.IsChecked == true)?.Content as string
        ?? "Custom selection";

    private void UpdateRunButton()
    {
        var count = _rows.Count(row => row.IsSelected);
        var verb = PreviewCheck.IsChecked == true ? "Preview" : "Run";
        RunButton.Content = count == 1 ? $"{verb} 1 step" : $"{verb} {count} steps";
        RunButton.IsEnabled = _runCts is null && !RunLock.IsBusy;

        if (_runCts is null)
            StatusText.Text = RunLock.IsBusy ? $"Waiting: {RunLock.Owner} is running" : "";
    }

    // ================================================================
    // RUN
    // ================================================================

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "Tick at least one step first";
            return;
        }

        if (!RunLock.TryEnter("Maintenance", () => Stop_Click(this, new RoutedEventArgs())))
        {
            StatusText.Text = $"Waiting: {RunLock.Owner} is running";
            return;
        }

        var preview = PreviewCheck.IsChecked == true;
        _runCts = new CancellationTokenSource();
        _runStarted = DateTime.Now;
        _progressText = "Starting";

        foreach (var row in _rows) row.Reset();
        SummaryCard.Visibility = Visibility.Collapsed;
        SetRunning(true);

        try
        {
            var summary = await _engine.RunAsync(selected, preview, CurrentPresetName, _runCts.Token);
            ShowSummary(summary);
        }
        catch (Exception ex)
        {
            // RunEngine catches per-step failures; reaching here means the engine
            // itself broke. Say so rather than leave rows reading "Running".
            _logger.Log($"Run aborted: {ex}", "ERROR");
            foreach (var row in selected.Where(row => row.State == RunState.Running))
                row.SetState(RunState.Error, "The run stopped unexpectedly");
            SummaryTitle.Text = "The run stopped unexpectedly";
            SummaryDetail.Text = $"{ex.Message} Details are in the log.";
            SummaryPillText.Text = "Aborted";
            SummaryPill.Tag = "Error";
            SummaryCard.Visibility = Visibility.Visible;
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            SetRunning(false);
            RunLock.Exit();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_runCts is null) return;
        _runCts.Cancel();
        StopButton.IsEnabled = false;
        _progressText = "Stopping the current step";
        OnClockTick();
    }

    private void SetRunning(bool running)
    {
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = true;
        PresetBar.IsEnabled = !running;
        PreviewCheck.IsEnabled = !running;
        foreach (var row in _rows) row.CanSelect = !running;

        if (running) _clock.Start();
        else
        {
            _clock.Stop();
            StatusText.Text = "";
        }
        UpdateRunButton();
    }

    private void OnClockTick()
    {
        foreach (var row in _rows) row.Tick();
        StatusText.Text = $"{_progressText}  ·  {RowViewModel.FormatClock(DateTime.Now - _runStarted)} elapsed";
    }

    private void ShowSummary(RunSummary summary)
    {
        SummaryTitle.Text = summary.Stopped
            ? $"{summary.Title} stopped early"
            : summary.Preview ? $"{summary.Title} preview finished: nothing was changed" : $"{summary.Title} finished";

        var detail = $"{summary.CountsText}  ·  {RowViewModel.FormatClock(summary.Duration)}";
        if (!summary.Preview && summary.FreeGbBefore is { } before && summary.FreeGbAfter is { } after && after - before >= 0.01)
            detail += $"  ·  {after - before:F2} GB freed";
        if (summary.RebootRecommended)
            detail += "  ·  Restart the PC to finish repairs";
        SummaryDetail.Text = detail;

        SummaryPillText.Text = summary.Tone switch
        {
            "Error" => "Needs attention",
            "Warn" => "Check results",
            _ => "All good"
        };
        SummaryPill.Tag = summary.Tone;
        SummaryCard.Visibility = Visibility.Visible;
    }
}
