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

        if (File.Exists(path))
            Shell.Open(path, _logger);
        else
            StatusText.Text = "That log file is no longer in the kit folder";
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Logs, _logger);

    private void SystemInfo_Click(object sender, RoutedEventArgs e)
    {
        new SystemInfoWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
