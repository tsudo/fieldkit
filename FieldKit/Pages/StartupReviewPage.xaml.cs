using System.Windows;
using System.Windows.Controls;
using FieldKit.Services;

namespace FieldKit.Pages;

public partial class StartupReviewPage : UserControl, IPage
{
    private readonly Logger _logger;
    private CancellationTokenSource? _cts;
    private bool _accepted;
    private bool _loaded;

    public StartupReviewPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        Loaded += (_, _) => { RunLock.Changed += Buttons; Buttons(); };
        Unloaded += (_, _) => RunLock.Changed -= Buttons;
    }

    public void OnShown()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var latest = StartupReviewService.ReadLatest();
            if (latest is not null) Show(latest);
            else Status.Text = "Scan to save a baseline for comparison on your next visit.";
        }
        catch (Exception ex) { Status.Text = $"Saved startup review unavailable. {ex.Message}"; }
    }

    private void Buttons()
    {
        ScanButton.IsEnabled = DownloadButton.IsEnabled = OpenButton.IsEnabled = !RunLock.IsBusy;
        StopButton.Visibility = _cts is null ? Visibility.Collapsed : Visibility.Visible;
        StopButton.IsEnabled = _cts?.IsCancellationRequested == false;
    }

    private async Task RunAsync(string name, Func<CancellationToken, Task> run)
    {
        if (!RunLock.TryEnter(name, Stop)) return;
        _cts = new CancellationTokenSource();
        Buttons();
        try { await run(_cts.Token); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { Status.Text = $"{name} stopped."; }
        catch (Exception ex) { Status.Text = ex.Message; _logger.Log($"[Startup review] {ex.Message}", "WARN"); }
        finally { _cts.Dispose(); _cts = null; RunLock.Exit(); Buttons(); }
    }

    private void Stop() { _cts?.Cancel(); Buttons(); }
    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Startup scan", async ct =>
        {
            try
            {
                if (StartupReviewService.FindProgram(false) is null)
                {
                    await BasicAsync("Get Autoruns for scheduled tasks, services, drivers and snapshot comparison.", ct);
                    return;
                }
                if (!_accepted)
                {
                    var consent = MessageBox.Show(Window.GetWindow(this),
                        "Use Microsoft Autorunsc to scan startup entries across profiles? Continuing accepts the Sysinternals license for this tool. " +
                        "Read Eula.txt in the kit’s Autoruns or SysinternalsSuite folder. Choose No for a basic scan.",
                        "Sysinternals Autoruns", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                    if (consent == MessageBoxResult.Cancel) return;
                    if (consent == MessageBoxResult.No) { await BasicAsync("Autorunsc was skipped.", ct); return; }
                    _accepted = true;
                }
                Status.Text = "Scanning sign-in entries, services, drivers and scheduled tasks…";
                Show(await Task.Run(() => StartupReviewService.ScanAsync(ct), ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Log($"[Startup review] Full inventory unavailable: {ex.Message}", "WARN");
                await BasicAsync(ex.Message, ct);
            }
        });
    }

    private async Task BasicAsync(string reason, CancellationToken ct)
    {
        BasicText.Text = await Task.Run(SupportKitService.BuildStartupItems, ct);
        ct.ThrowIfCancellationRequested();
        WarningPanel.Visibility = Visibility.Collapsed;
        Tabs.Visibility = Visibility.Collapsed;
        BasicText.Visibility = Visibility.Visible;
        Status.Text = $"Basic inventory: Run keys and startup folders only. {reason} The full baseline is unchanged.";
    }

    private void Show(StartupSnapshot snapshot)
    {
        var logon = snapshot.Entries.Where(row => row.IsLogon).ToList();
        SignIn.ItemsSource = logon;
        Advanced.ItemsSource = snapshot.Entries.Where(row => !row.IsLogon).ToList();
        Changes.ItemsSource = snapshot.Changes;
        SignInTab.Header = $"Sign-in ({logon.Count})";
        AdvancedTab.Header = $"Advanced ({snapshot.Entries.Count - logon.Count})";
        ChangesTab.Header = $"Changes ({snapshot.Changes.Count})";
        BasicText.Visibility = Visibility.Collapsed;
        Tabs.Visibility = Visibility.Visible;
        Status.Text = $"{snapshot.CheckedAt:g} · {snapshot.Summary}";
        var warnings = snapshot.Warnings ?? [];
        WarningPanel.Visibility = warnings.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningPanel.Header = $"Partial coverage · {warnings.Length} warning(s)";
        WarningText.Text = string.Join(Environment.NewLine, warnings);
    }

    private async void Download_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Get Autoruns", async ct =>
        {
            Status.Text = "Downloading Autoruns from Microsoft and checking signatures…";
            var result = await new SupportKitService(_logger).InstallToolAsync(StartupReviewService.Tool, ct);
            Status.Text = result.Succeeded ? "Autoruns is ready. Choose Scan startup." : result.Message;
        });

    private async void Open_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Open Autoruns", async ct =>
        {
            var path = await Task.Run(() => StartupReviewService.VerifyProgram(true), ct);
            ct.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory });
        });

    private void TaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"))
                { UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory });
        }
        catch (Exception ex) { Status.Text = $"Task Manager could not open. {ex.Message}"; }
    }
}
