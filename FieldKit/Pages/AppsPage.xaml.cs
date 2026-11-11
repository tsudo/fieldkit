using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FieldKit.Services;

namespace FieldKit.Pages;

public sealed class AppRow : RowViewModel
{
    public AppRow(string name, string note, string id, IReadOnlyList<string> bundles) : base(name, note)
    {
        Id = id;
        Note = note;
        Bundles = bundles;
    }

    public string Id { get; }
    public string Note { get; }
    public IReadOnlyList<string> Bundles { get; }
    public bool IsInstalled { get; private set; }
    public bool HasUpdate { get; private set; }

    /// <summary>True when ticking this row would do something.</summary>
    public bool IsActionable => !IsInstalled || HasUpdate;

    /// <param name="installed">Null when the scan could not tell what is installed.</param>
    public void ApplyScan(bool? installed, WingetUpgrade? upgrade)
    {
        IsInstalled = installed == true || upgrade is not null;
        HasUpdate = upgrade is not null;
        SetState(RunState.Ready, Note);

        if (upgrade is not null)
        {
            Meta = $"{upgrade.InstalledVersion}  →  {upgrade.AvailableVersion}";
            SetPill("Update", "Warn");
        }
        else if (IsInstalled)
        {
            Meta = "";
            SetPill("Installed", "Neutral");
        }
        else
        {
            Meta = "";
            // Unknown is shown as unknown. Installing an app that is already
            // there is harmless: winget reports "already installed".
            if (installed is null) SetPill("Not checked", "Neutral");
        }

        CanSelect = IsActionable;
        if (!IsActionable) IsSelected = false;
    }

    public void MarkCurrent()
    {
        IsInstalled = true;
        HasUpdate = false;
        Meta = "";
        CanSelect = false;
        IsSelected = false;
    }
}

public partial class AppsPage : UserControl, IPage
{
    private const string OtherGroupName = "Other apps with updates";

    private readonly Logger _logger;
    private readonly WingetService _winget;
    private readonly AppCatalog _catalog;
    private readonly ObservableCollection<GroupViewModel> _groups = new();
    private readonly List<AppRow> _catalogRows = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private GroupViewModel? _otherGroup;
    private CancellationTokenSource? _cts;
    private bool _scanned;
    private bool _scanning;
    private bool _applyingBundle;
    private string _progressText = "";

    public AppsPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        _winget = new WingetService(logger);
        _catalog = AppCatalog.Load();

        foreach (var group in _catalog.Apps.GroupBy(app => app.Group))
        {
            var rows = group.Select(app => new AppRow(app.Name, app.Note, app.Id, app.Bundles)).ToList();
            foreach (var row in rows) row.SelectionChanged += OnSelectionChanged;
            _catalogRows.AddRange(rows);
            _groups.Add(new GroupViewModel(group.Key, rows));
        }

        GroupList.ItemsSource = _groups;
        LinkList.ItemsSource = _catalog.Links;

        foreach (var bundle in _catalog.Bundles)
            BundleBar.Children.Add(MakeChip(bundle.Name, bundle.Key));
        BundleBar.Children.Add(MakeChip("All updates", "*updates"));

        _clock.Tick += (_, _) =>
        {
            foreach (var row in AllRows) row.Tick();
            if (_cts is not null) StatusText.Text = _progressText;
        };
        RunLock.Changed += () => Dispatcher.Invoke(UpdateActionButton);

        UpdateActionButton();
    }

    private IEnumerable<AppRow> AllRows => _groups.SelectMany(group => group.Rows).OfType<AppRow>();

    private RadioButton MakeChip(string label, string key)
    {
        var chip = new RadioButton
        {
            Content = label,
            Tag = key,
            GroupName = "AppsBundle",
            Style = (Style)FindResource("Fk.Chip")
        };
        chip.Click += Bundle_Click;
        return chip;
    }

    public void OnShown()
    {
        if (!_scanned && !_scanning)
            _ = ScanAsync();
    }

    // ================================================================
    // SCAN
    // ================================================================

    private async Task ScanAsync()
    {
        if (WingetService.ResolvePath() is null)
        {
            NoticeText.Text =
                "winget was not found for the account FieldKit is running as. Install \"App Installer\" from the " +
                "Microsoft Store, then choose Scan again. Until then this page can't install or update anything.";
            Notice.Visibility = Visibility.Visible;
            StatusText.Text = "winget not available";
            UpdateActionButton();
            return;
        }

        // The scan runs winget, so it takes the same lock as every other job.
        if (!RunLock.TryEnter("App scan"))
        {
            StatusText.Text = $"The scan will run when {RunLock.Owner} has finished. Choose Scan again then.";
            return;
        }

        Notice.Visibility = Visibility.Collapsed;
        _scanning = true;
        StatusText.Text = "Checking what's installed. This takes about 20 seconds.";
        UpdateActionButton();

        try
        {
            var scan = await _winget.ScanAsync(CancellationToken.None);
            ApplyScan(scan);
            _scanned = true;

            if (scan.Problem is null)
            {
                StatusText.Text = $"Scanned at {DateTime.Now:HH:mm}";
            }
            else
            {
                NoticeText.Text = scan.Problem;
                Notice.Visibility = Visibility.Visible;
                StatusText.Text = $"Scanned at {DateTime.Now:HH:mm}, with a problem";
            }
        }
        catch (Exception ex)
        {
            // Everything lands here on purpose. This method is started without
            // being awaited, so an exception that escaped would vanish and leave
            // the page saying "Checking..." for the rest of the session.
            _logger.Log($"[Apps] Scan failed: {ex}", "ERROR");
            NoticeText.Text = $"The scan did not finish: {ex.Message} Choose Scan again to retry.";
            Notice.Visibility = Visibility.Visible;
            StatusText.Text = "Scan failed";
        }
        finally
        {
            _scanning = false;
            RunLock.Exit();
            UpdateActionButton();
        }
    }

    private void ApplyScan(WingetScan scan)
    {
        // winget can list one package twice (two installed versions); keep the first.
        var upgrades = scan.Upgrades
            .GroupBy(upgrade => upgrade.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in _catalogRows)
        {
            upgrades.TryGetValue(row.Id, out var upgrade);
            row.ApplyScan(scan.InstalledIds?.Contains(row.Id), upgrade);
        }

        var catalogIds = _catalogRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = scan.Upgrades
            .Where(upgrade => !catalogIds.Contains(upgrade.Id))
            .OrderBy(upgrade => upgrade.Name, StringComparer.OrdinalIgnoreCase)
            .Select(upgrade =>
            {
                var row = new AppRow(upgrade.Name, upgrade.Id, upgrade.Id, []);
                row.ApplyScan(true, upgrade);
                row.SelectionChanged += OnSelectionChanged;
                return row;
            })
            .ToList();

        if (_otherGroup is not null) _groups.Remove(_otherGroup);
        _otherGroup = others.Count > 0 ? new GroupViewModel(OtherGroupName, others) : null;
        if (_otherGroup is not null) _groups.Add(_otherGroup);

        ApplyFilter();
        UpdateActionButton();
    }

    // ================================================================
    // SELECTION
    // ================================================================

    private void Bundle_Click(object sender, RoutedEventArgs e)
    {
        var key = (string)((RadioButton)sender).Tag;

        // A bundle selects rows across the whole list, so show the whole list:
        // nothing should be selected where the filter is hiding it.
        SearchBox.Text = "";

        _applyingBundle = true;
        foreach (var row in AllRows)
        {
            row.IsSelected = row.IsActionable && (key == "*updates"
                ? row.HasUpdate
                : row.Bundles.Contains(key, StringComparer.OrdinalIgnoreCase));
        }
        _applyingBundle = false;

        UpdateActionButton();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in AllRows) row.IsSelected = false;
    }

    private void OnSelectionChanged()
    {
        if (_applyingBundle) return;
        foreach (var chip in BundleBar.Children.OfType<RadioButton>())
            chip.IsChecked = false;
        UpdateActionButton();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = SearchBox.Text.Trim();
        foreach (var group in _groups)
        {
            foreach (var row in group.Rows)
            {
                row.IsVisible = term.Length == 0
                                || row.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || (row is AppRow app && app.Id.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            group.IsVisible = group.Rows.Any(row => row.IsVisible);
        }
    }

    private void UpdateActionButton()
    {
        var selected = AllRows.Where(row => row.IsSelected).ToList();
        var installs = selected.Count(row => !row.IsInstalled);
        var updates = selected.Count - installs;

        ActionButton.Content = (installs, updates) switch
        {
            (0, 0) => "Install or update",
            ( > 0, 0) => $"Install {installs}",
            (0, > 0) => $"Update {updates}",
            _ => $"Install {installs}, update {updates}"
        };

        var idle = _cts is null && !_scanning && !RunLock.IsBusy;
        ActionButton.IsEnabled = idle && _scanned;
        RescanButton.IsEnabled = idle;
        ClearButton.IsEnabled = idle;
        BundleBar.IsEnabled = idle && _scanned;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync();

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
            Shell.Open(url, _logger);
    }

    // ================================================================
    // INSTALL / UPDATE
    // ================================================================

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        var selected = AllRows.Where(row => row.IsSelected && row.IsActionable).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "Tick at least one app first";
            return;
        }

        var lines = selected.Select(row => $"  {(row.IsInstalled ? "Update" : "Install")}  {row.Name}  [{row.Id}]");
        var message = $"FieldKit will make these changes through winget:\n\n{string.Join("\n", lines)}\n\n" +
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
        _logger.Log($"[Apps] Starting {selected.Count} app change(s).", "INFO");

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var row = selected[i];
                var verb = row.IsInstalled ? "upgrade" : "install";

                if (_cts.IsCancellationRequested)
                {
                    row.SetState(RunState.Skipped, "Stopped before this app");
                    items.Add(new RunItem(row.Name, "Skipped", "Stopped before this app"));
                    skipped++;
                    continue;
                }

                _progressText = $"{(verb == "install" ? "Installing" : "Updating")} {row.Name} ({i + 1} of {selected.Count})";
                StatusText.Text = _progressText;
                row.SetState(RunState.Running, row.Note);
                _logger.Log($"[Apps] {verb} {row.Name} [{row.Id}]", "INFO");

                WingetOutcome outcome;
                try
                {
                    outcome = verb == "install"
                        ? await _winget.InstallAsync(row.Id, _cts.Token)
                        : await _winget.UpgradeAsync(row.Id, _cts.Token);
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
                    row.MarkCurrent();
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
            var summary = new RunSummary("Apps", "App install and update", started, DateTime.Now,
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
        foreach (var row in AllRows)
            row.CanSelect = !running && row.IsActionable;

        if (running) _clock.Start(); else _clock.Stop();
        UpdateActionButton();
    }
}
