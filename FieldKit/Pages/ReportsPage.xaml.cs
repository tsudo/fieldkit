using System.Windows;
using System.Windows.Controls;
using FieldKit.Services;

namespace FieldKit.Pages;

public sealed record RunRow(string Title, string When, string Counts, string Tone, string LogFile);

public partial class ReportsPage : UserControl, IPage
{
    private readonly Logger _logger;

    public ReportsPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
    }

    public void OnShown()
    {
        var (runs, unreadable) = RunHistory.LoadAll();

        RunList.ItemsSource = runs.Select(run => new RunRow(
            run.Preview ? $"{run.Title} (preview)" : run.Title,
            $"{run.Started:ddd d MMM yyyy, HH:mm}  ·  {RowViewModel.FormatClock(run.Duration)}" +
            (run.Stopped ? "  ·  stopped early" : "") +
            (run.RebootRecommended ? "  ·  restart recommended" : ""),
            run.CountsText,
            run.Tone,
            run.LogFile)).ToList();

        EmptyCard.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = unreadable > 0
            ? $"{runs.Count} run(s) shown; {unreadable} record(s) could not be read"
            : runs.Count == 0 ? "" : $"{runs.Count} run(s)";
    }

    private void OpenRunLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        if (!TryOpenRunLog(path, _logger))
            StatusText.Text = "That log file is unavailable in the kit folder";
    }

    internal static bool TryOpenRunLog(string path, Logger logger)
    {
        // The path was read back from a JSON file. Open it only if it is a log
        // that sits directly in the log folder.
        var logs = Path.GetFullPath(AppPaths.Logs);
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var inLogFolder = string.Equals(Path.GetDirectoryName(full), logs, StringComparison.OrdinalIgnoreCase)
                          && full.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
        return inLogFolder && File.Exists(full) && !AppPaths.IsReparsePoint(full) && Shell.Open(full, logger);
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Logs, _logger);

    private void SystemInfo_Click(object sender, RoutedEventArgs e)
    {
        new SystemInfoWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
