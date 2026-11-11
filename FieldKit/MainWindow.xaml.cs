using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FieldKit.Pages;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit;

/// <summary>A page hosted by the shell. <see cref="OnShown"/> runs each time it becomes visible.</summary>
public interface IPage
{
    void OnShown();
}

public sealed record LogLine(string Text, string Level);

public partial class MainWindow : Window
{
    private const int MaxLogLines = 4000;

    private readonly Logger _logger;
    private readonly ObservableCollection<LogLine> _logLines = new();
    private readonly Dictionary<RadioButton, Func<UserControl>> _pageFactories;
    private readonly Dictionary<RadioButton, UserControl> _pages = new();

    public MainWindow(IReadOnlyList<MaintenanceTask> tasks, Logger logger, string? startPage = null)
    {
        InitializeComponent();
        _logger = logger;

        VersionText.Text = $"Version {SystemInfo.GetDisplayVersion()}";
        LogList.ItemsSource = _logLines;

        _pageFactories = new Dictionary<RadioButton, Func<UserControl>>
        {
            [NavMaintain] = () => new MaintainPage(tasks, logger),
            [NavApps] = () => new AppsPage(logger),
            [NavKit] = () => new SupportKitPage(logger),
            [NavReports] = () => new ReportsPage(logger)
        };

        _logger.LogWritten += OnLogWritten;
        RunLock.Changed += OnRunLockChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _logger.LogWritten -= OnLogWritten;
            RunLock.Changed -= OnRunLockChanged;
        };

        if (Shell.RunsAsDifferentUser(out var interactive, out var current))
        {
            AccountNoticeText.Text =
                $"FieldKit is running as {current}, but {interactive} is signed in. " +
                "Per-user steps (temp files, Recycle Bin, Storage Sense, winget apps) act on the administrator's profile, not theirs.";
            AccountNotice.Visibility = Visibility.Visible;
            _logger.Log($"Running as {current}; interactive user is {interactive}.", "WARN");
        }

        if (AppPaths.IsTemporaryRoot)
        {
            var existing = AccountNotice.Visibility == Visibility.Visible
                ? AccountNoticeText.Text + Environment.NewLine + Environment.NewLine
                : "";
            AccountNoticeText.Text = existing +
                $"FieldKit could not create its usual folder on the system drive, so this session keeps logs, reports and tools in {AppPaths.Root}. " +
                "They will not be there next time. Check for a folder named FieldKit at the drive root that FieldKit did not create.";
            AccountNotice.Visibility = Visibility.Visible;
            _logger.Log($"Using temporary kit folder {AppPaths.Root}.", "WARN");
        }

        // `FieldKit.exe --page apps` opens straight on a page; anything else opens Maintain.
        var start = startPage?.ToLowerInvariant() switch
        {
            "apps" => NavApps,
            "kit" => NavKit,
            "reports" => NavReports,
            _ => NavMaintain
        };
        start.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton nav || !_pageFactories.TryGetValue(nav, out var factory))
            return;

        if (!_pages.TryGetValue(nav, out var page))
        {
            page = factory();
            _pages[nav] = page;
        }

        PageHost.Content = page;
        (page as IPage)?.OnShown();
    }

    private void OnRunLockChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_closeWhenIdle && !RunLock.IsBusy)
            {
                Close();
                return;
            }
            if (!_closeWhenIdle)
                BusyText.Text = RunLock.IsBusy ? $"{RunLock.Owner} is running" : "";
        });
    }

    private bool _closeWhenIdle;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!RunLock.IsBusy) return;

        // Never let the window go while a job holds the lock: the process would
        // exit, the child (DISM, an installer) would keep running with nobody
        // watching, and the run would leave no summary.
        e.Cancel = true;
        if (_closeWhenIdle) return;

        var answer = MessageBox.Show(this,
            $"{RunLock.Owner} is still running.\n\nStop it and close FieldKit? The current step is ended partway through; " +
            "FieldKit closes as soon as it has stopped and written its summary.",
            "FieldKit", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        // The job may have finished while the question was on screen.
        if (!RunLock.IsBusy)
        {
            e.Cancel = false;
            return;
        }

        _closeWhenIdle = true;
        BusyText.Text = $"Stopping {RunLock.Owner}, then closing";
        RunLock.RequestStop();
    }

    private void OpenKitFolder_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Root, _logger);

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_logger.LogFilePath))
        {
            MessageBox.Show(this,
                $"FieldKit could not create a log file this session.\n\nExpected at: {_logger.LogFilePath}",
                "Open log", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Shell.Open(_logger.LogFilePath, _logger);
    }

    // ================================================================
    // ACTIVITY LOG
    // ================================================================

    private void OnLogWritten(string entry, string level)
    {
        if (IsProgressNoise(entry)) return;

        Dispatcher.BeginInvoke(() =>
        {
            LogPlaceholder.Visibility = Visibility.Collapsed;
            _logLines.Add(new LogLine(entry, level));
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);

            // Follow new output only while the view is already at the end, so
            // scrolling up to read something is not undone by the next line.
            if (_followLog)
                LogList.ScrollIntoView(_logLines[^1]);

            if (_logger.WriteFailed && !_logFailureShown)
            {
                _logFailureShown = true;
                ShowNotice($"FieldKit can no longer write to its log file ({_logger.LogFilePath}). " +
                           "What you see in Activity is the only record of what happens from here on.");
            }
        });
    }

    private bool _followLog = true;
    private bool _logFailureShown;

    private void LogList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Extent changes are new lines arriving; anything else is the user scrolling.
        if (e.ExtentHeightChange == 0)
            _followLog = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1;
    }

    private void ShowNotice(string text)
    {
        AccountNoticeText.Text = AccountNotice.Visibility == Visibility.Visible
            ? AccountNoticeText.Text + Environment.NewLine + Environment.NewLine + text
            : text;
        AccountNotice.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// winget and DISM redraw spinners and progress bars many times a second.
    /// They still go to the log file; on screen they would bury the lines that matter.
    /// </summary>
    internal static bool IsProgressNoise(string entry)
    {
        var close = entry.LastIndexOf("] ", StringComparison.Ordinal);
        var body = (close >= 0 ? entry[(close + 2)..] : entry).Trim();
        if (body.Length == 0) return true;
        if (body.Length == 1 && "-\\|/".Contains(body[0])) return true;
        return body.Contains('█') || body.Contains('▒') || body.Contains('░');
    }
}
