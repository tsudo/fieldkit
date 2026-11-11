using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FieldKit.Services;

namespace FieldKit.Pages;

public partial class SupportKitPage : UserControl, IPage
{
    private sealed class ToolRow(KitTool tool) : RowViewModel(tool.Name, tool.Note)
    {
        public KitTool Tool { get; } = tool;
    }

    private sealed class ReportRow(KitReport report) : RowViewModel(report.Name, report.Note)
    {
        public KitReport Report { get; } = report;
    }

    private readonly Logger _logger;
    private readonly SupportKitService _kit;
    private readonly List<ToolRow> _tools;
    private readonly List<ReportRow> _reports;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cts;

    public SupportKitPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        _kit = new SupportKitService(logger);

        FolderText.Text = AppPaths.Root;

        _tools = SupportKitService.Tools.Select(tool => new ToolRow(tool)
        {
            IsSelected = tool.Key != "suite",
            Meta = tool.Size
        }).ToList();

        _reports = SupportKitService.Reports.Select(report => new ReportRow(report)
        {
            IsSelected = report.Key != "events"
        }).ToList();

        GroupList.ItemsSource = new[]
        {
            new GroupViewModel("Tools", _tools),
            new GroupViewModel("Snapshot reports", _reports)
        };

        foreach (var row in Rows) row.SelectionChanged += UpdateButtons;
        _clock.Tick += (_, _) => { foreach (var row in Rows) row.Tick(); };
        RunLock.Changed += () => Dispatcher.Invoke(UpdateButtons);

        UpdateButtons();
    }

    private IEnumerable<RowViewModel> Rows => _tools.Cast<RowViewModel>().Concat(_reports);

    public void OnShown()
    {
        if (_cts is null) RefreshPresence();
    }

    private void RefreshPresence()
    {
        foreach (var row in _tools)
        {
            row.SetState(RunState.Ready, row.Tool.Note);
            if (SupportKitService.IsToolPresent(row.Tool))
                row.SetPill("In kit", "Neutral");
        }

        foreach (var row in _reports)
            row.SetState(RunState.Ready, row.Report.Note);
    }

    private void UpdateButtons()
    {
        var count = Rows.Count(row => row.IsSelected);
        BuildButton.Content = count == 1 ? "Build kit (1 item)" : $"Build kit ({count} items)";

        var idle = _cts is null && !RunLock.IsBusy;
        BuildButton.IsEnabled = idle;
        ZipButton.IsEnabled = idle;
        RemoveButton.IsEnabled = idle;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Root, _logger);

    // ================================================================
    // BUILD
    // ================================================================

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        var tools = _tools.Where(row => row.IsSelected).ToList();
        var reports = _reports.Where(row => row.IsSelected).ToList();
        if (tools.Count + reports.Count == 0)
        {
            StatusText.Text = "Tick at least one tool or report first";
            return;
        }

        if (!RunLock.TryEnter("Support kit build", () => Stop_Click(this, new RoutedEventArgs())))
        {
            StatusText.Text = $"Waiting: {RunLock.Owner} is running";
            return;
        }

        _cts = new CancellationTokenSource();
        var started = DateTime.Now;
        var items = new List<RunItem>();
        int success = 0, error = 0, skipped = 0;

        RefreshPresence();
        SetRunning(true);
        _logger.Log($"[Support kit] Building in {AppPaths.Root}: {tools.Count} tool(s), {reports.Count} report(s).", "INFO");

        async Task RunRowAsync(RowViewModel row, Func<CancellationToken, Task<KitOutcome>> work)
        {
            if (_cts!.IsCancellationRequested)
            {
                row.SetState(RunState.Skipped, "Stopped before this item");
                items.Add(new RunItem(row.Name, "Skipped", "Stopped before this item"));
                skipped++;
                return;
            }

            StatusText.Text = $"Working on {row.Name}";
            row.SetState(RunState.Running);

            try
            {
                var outcome = await work(_cts.Token);
                row.SetState(outcome.Succeeded ? RunState.Success : RunState.Error, outcome.Message);
                items.Add(new RunItem(row.Name, outcome.Succeeded ? "Success" : "Error", outcome.Message));
                if (outcome.Succeeded) success++; else error++;
            }
            catch (OperationCanceledException)
            {
                row.SetState(RunState.Skipped, "Stopped; nothing from this item was kept");
                items.Add(new RunItem(row.Name, "Skipped", "Stopped while running"));
                skipped++;
            }
            catch (Exception ex)
            {
                // One item failing must not strand its row at "Running" or
                // abandon the items after it.
                _logger.Log($"[Support kit] {row.Name} failed unexpectedly: {ex}", "ERROR");
                row.SetState(RunState.Error, $"Unexpected failure: {ex.Message}");
                items.Add(new RunItem(row.Name, "Error", ex.Message));
                error++;
            }
        }

        try
        {
            foreach (var row in tools)
                await RunRowAsync(row, ct => _kit.InstallToolAsync(row.Tool, ct));

            if (reports.Count > 0 && !_cts.IsCancellationRequested)
            {
                var folder = SupportKitService.CreateSnapshotFolder();
                foreach (var row in reports)
                    await RunRowAsync(row, ct => _kit.WriteReportAsync(row.Report, folder, ct));
            }

            if (ShortcutCheck.IsChecked == true && !_cts.IsCancellationRequested)
            {
                var shortcut = _kit.CreateDesktopShortcut();
                items.Add(new RunItem("Desktop shortcut", shortcut.Succeeded ? "Success" : "Error", shortcut.Message));
                if (shortcut.Succeeded) success++; else error++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log($"[Support kit] Build stopped: {ex.Message}", "ERROR");
            items.Add(new RunItem("Kit folder", "Error", ex.Message));
            error++;
        }
        finally
        {
            var summary = new RunSummary("Support kit", "Support kit build", started, DateTime.Now,
                success, 0, error, skipped, false, _cts.IsCancellationRequested, false,
                null, null, _logger.LogFilePath, items);
            _logger.LogLines(summary.ToLogText(), summary.Tone == "Ok" ? "OK" : "WARN");
            RunHistory.Save(summary, _logger);

            _cts.Dispose();
            _cts = null;
            SetRunning(false);
            RunLock.Exit();
            StatusText.Text = summary.CountsText;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null) return;
        _cts.Cancel();
        StopButton.IsEnabled = false;
        StatusText.Text = "Stopping";
    }

    private void SetRunning(bool running)
    {
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = true;
        ShortcutCheck.IsEnabled = !running;
        foreach (var row in Rows) row.CanSelect = !running;

        if (running) _clock.Start(); else _clock.Stop();
        UpdateButtons();
    }

    // ================================================================
    // ZIP / REMOVE
    // ================================================================

    private async void Zip_Click(object sender, RoutedEventArgs e)
    {
        var proceed = MessageBox.Show(Window.GetWindow(this),
            "The zip will hold this PC's reports and every FieldKit log: its name, serial number, network addresses, " +
            "account names, installed software, startup commands and recent error messages.\n\nCreate it?",
            "Zip for sharing", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
        if (proceed != MessageBoxResult.Yes) return;

        if (!RunLock.TryEnter("Zip")) return;
        try
        {
            StatusText.Text = "Zipping reports and logs";
            var (outcome, zipPath) = await _kit.ZipForSharingAsync(CancellationToken.None);
            StatusText.Text = outcome.Message;
            if (outcome.Succeeded && zipPath is not null)
            {
                StatusText.Text = $"Saved {Path.GetFileName(zipPath)}. Read it through before sending it to anyone.";
                Shell.Open(AppPaths.Root, _logger);
            }
        }
        finally
        {
            RunLock.Exit();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Remove the support kit from this PC?\n\nThis deletes the tools, reports, zip files and earlier logs in:\n{AppPaths.Root}\n\n" +
            "It also removes the desktop shortcut. This can't be undone.",
            "Remove kit", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (!RunLock.TryEnter("Remove kit"))
        {
            StatusText.Text = $"Waiting: {RunLock.Owner} is running";
            return;
        }

        try
        {
            StatusText.Text = "Removing the kit";
            // Off the UI thread: the full suite is thousands of files.
            var outcome = await Task.Run(_kit.RemoveKit);
            StatusText.Text = outcome.Message;
            RefreshPresence();
        }
        catch (Exception ex)
        {
            _logger.Log($"[Support kit] Remove failed unexpectedly: {ex}", "ERROR");
            StatusText.Text = $"The kit could not be removed: {ex.Message}";
        }
        finally
        {
            RunLock.Exit();
        }
    }
}
