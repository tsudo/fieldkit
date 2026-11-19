using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FieldKit.Services;

namespace FieldKit.Pages;

public partial class AppsPage : UserControl, IPage
{
    private sealed class AppRow(CatalogApp app) : RowViewModel(app.Name, app.Note)
    {
        public string Id => app.Id;
        public string Note => app.Note;
        public string Group => app.Group;
        public bool Essential => app.Bundles.Contains("essentials");
        public bool Technician => app.Bundles.Contains("technician");
        public bool Optional => app.Bundles.Contains("picks");
        public bool? Installed { get; private set; }

        public void ApplyInventory(bool? installed)
        {
            Installed = installed;
            SetState(RunState.Ready, Note);
            if (installed == true) SetPill("Installed", "Neutral");
            if (installed is null) SetPill("Not checked", "Neutral");
            if (installed != false) IsSelected = false;
        }

        public void MarkInstalled()
        {
            Installed = true;
            CanSelect = false;
            IsSelected = false;
        }
    }

    private readonly Logger _logger;
    private readonly WingetService _winget;
    private readonly AppCatalog _catalog = AppCatalog.Load();
    private readonly List<AppRow> _rows;
    private readonly List<GroupViewModel> _pickGroups;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _cts;
    private bool _scanned;
    private bool _scanning;
    private string _progressText = "";

    public AppsPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        _winget = new WingetService(logger);
        _rows = _catalog.Apps.Select(app => new AppRow(app) { CanSelect = false }).ToList();
        _pickGroups = _rows.Where(row => row.Optional).GroupBy(row => row.Group)
            .Select(group => new GroupViewModel(group.Key, group)).ToList();
        foreach (var row in _rows) row.SelectionChanged += UpdateActionButton;
        EssentialsList.ItemsSource = _rows.Where(row => row.Essential);
        TechnicianList.ItemsSource = _rows.Where(row => row.Technician);
        PicksList.ItemsSource = _pickGroups;
        ApplyFilter();

        _clock.Tick += (_, _) =>
        {
            foreach (var row in _rows) row.Tick();
            if (_cts is not null) StatusText.Text = _progressText;
        };
        RunLock.Changed += () => Dispatcher.Invoke(UpdateActionButton);
        UpdateActionButton();
    }

    public void OnShown()
    {
        if (!_scanned && !_scanning) _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (!RunLock.TryEnter("App inventory"))
        {
            StatusText.Text = $"Choose Scan again when {RunLock.Owner} has finished.";
            return;
        }
        _scanning = true;
        _scanned = false;
        Notice.Visibility = Visibility.Collapsed;
        StatusText.Text = "Checking which apps are installed…";
        UpdateActionButton();
        try
        {
            var ids = await _winget.GetInstalledIdsAsync(CancellationToken.None)
                      ?? throw new InvalidOperationException("winget could not report the installed apps.");
            foreach (var row in _rows) row.ApplyInventory(ids.Contains(row.Id));
            _scanned = true;
            StatusText.Text = $"Checked at {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            foreach (var row in _rows) row.ApplyInventory(null);
            _logger.Log($"[Apps] Inventory failed: {ex.Message}", "ERROR");
            NoticeText.Text = $"{ex.Message} Check App Installer, then choose Scan again.";
            Notice.Visibility = Visibility.Visible;
            StatusText.Text = "Inventory unavailable";
        }
        finally
        {
            _scanning = false;
            RunLock.Exit();
            UpdateActionButton();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
    }

    private void Picks_Collapsed(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(row => row.Optional)) row.IsSelected = false;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = SearchBox.Text.Trim();
        bool Matches(string text) => text.Contains(term, StringComparison.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            row.IsVisible = Matches(row.Name) || Matches(row.Id);
            if (!row.IsVisible) row.IsSelected = false;
        }
        foreach (var group in _pickGroups) group.IsVisible = group.Rows.Any(row => row.IsVisible);

        var links = _catalog.Links.Where(link => Matches(link.Name)).ToList();
        FeaturedLinks.ItemsSource = links.Where(link => link.Featured);
        LinkList.ItemsSource = links.Where(link => !link.Featured);
        if (term.Length > 0 && (_pickGroups.Any(group => group.IsVisible) || links.Any(link => !link.Featured)))
            Picks.IsExpanded = true;
    }

    private void UpdateActionButton()
    {
        var idle = _cts is null && !_scanning && !RunLock.IsBusy;
        foreach (var row in _rows) row.CanSelect = idle && _scanned && row.Installed == false;
        var count = _rows.Count(row => row.IsSelected);
        ActionButton.Content = count == 0 ? "Install selected" : $"Install {count}";
        ActionButton.IsEnabled = idle && _scanned && count > 0;
        RescanButton.IsEnabled = idle;
        ClearButton.IsEnabled = idle && count > 0;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync();

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
            Shell.Open(url, _logger);
    }

    // ================================================================
    // INSTALL
    // ================================================================

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.IsSelected && row.CanSelect).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "Tick at least one app first";
            return;
        }

        var lines = selected.Select(row => $"  {row.Name}  [{row.Id}]");
        var message = $"FieldKit will install these apps through winget:\n\n{string.Join("\n", lines)}\n\n" +
                      "Installers run silently, though a few still show their own prompts. winget is told to accept " +
                      "each package's licence and source agreements on this PC owner's behalf. Continue?";
        if (MessageBox.Show(Window.GetWindow(this), message, "Confirm app changes",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!RunLock.TryEnter("App install", () => Stop_Click(this, new RoutedEventArgs())))
        {
            StatusText.Text = $"Waiting: {RunLock.Owner} is running";
            return;
        }

        _cts = new CancellationTokenSource();
        var started = DateTime.Now;
        var items = new List<RunItem>();
        int success = 0, error = 0, skipped = 0;
        var reboot = false;

        SetRunning(true);
        _logger.Log($"[Apps] Starting {selected.Count} app installation(s).", "INFO");

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var row = selected[i];

                if (_cts.IsCancellationRequested)
                {
                    row.SetState(RunState.Skipped, "Stopped before this app");
                    items.Add(new RunItem(row.Name, "Skipped", "Stopped before this app"));
                    skipped++;
                    continue;
                }

                _progressText = $"Installing {row.Name} ({i + 1} of {selected.Count})";
                StatusText.Text = _progressText;
                row.SetState(RunState.Running, row.Note);
                _logger.Log($"[Apps] install {row.Name} [{row.Id}]", "INFO");

                WingetOutcome outcome;
                try
                {
                    outcome = await _winget.InstallAsync(row.Id, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    row.SetState(RunState.Skipped, "Stopped while running. The app may be partly installed.");
                    items.Add(new RunItem(row.Name, "Skipped", "Stopped while running"));
                    skipped++;
                    continue;
                }
                catch (TimeoutException ex)
                {
                    outcome = new WingetOutcome(false, -1, ex.Message);
                }
                catch (Exception ex)
                {
                    // One app failing to start must not strand its row at "Running"
                    // or abandon the apps after it.
                    _logger.Log($"[Apps] {row.Name} could not be started: {ex}", "ERROR");
                    outcome = new WingetOutcome(false, -1, $"Could not start winget: {ex.Message}");
                }

                if (outcome.Succeeded)
                {
                    success++;
                    reboot |= outcome.Message.Contains("Restart", StringComparison.Ordinal);
                    row.SetState(RunState.Success, outcome.Message);
                    row.MarkInstalled();
                }
                else
                {
                    error++;
                    row.SetState(RunState.Error, outcome.Message);
                }

                items.Add(new RunItem(row.Name, outcome.Succeeded ? "Success" : "Error", outcome.Message));
                _logger.Log($"[Apps] {row.Name}: {outcome.Message}", outcome.Succeeded ? "OK" : "ERROR");
            }
        }
        finally
        {
            var summary = new RunSummary("Apps", "App installation", started, DateTime.Now,
                success, 0, error, skipped, false, _cts.IsCancellationRequested, reboot,
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
        _progressText = "Stopping the current installer";
        StatusText.Text = _progressText;
    }

    private void SetRunning(bool running)
    {
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = true;
        SearchBox.IsEnabled = !running;
        Picks.IsEnabled = !running;

        if (running) _clock.Start(); else _clock.Stop();
        UpdateActionButton();
    }
}
