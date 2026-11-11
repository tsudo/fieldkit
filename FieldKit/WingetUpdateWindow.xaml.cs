using System.Collections.ObjectModel;
using System.Windows;
using FieldKit.Services;

namespace FieldKit;

public partial class WingetUpdateWindow : Window
{
    private readonly ObservableCollection<WingetPackageCandidate> _candidates;

    public WingetUpdateWindow(IEnumerable<WingetPackageCandidate> candidates)
    {
        InitializeComponent();
        _candidates = new ObservableCollection<WingetPackageCandidate>(candidates);
        UpdatesGrid.ItemsSource = _candidates;
        SummaryText.Text = $"{_candidates.Count} app update(s) found. Select the packages to install, or choose Scan Only to leave the system unchanged.";
    }

    public bool ApplyUpdates { get; private set; }

    public IReadOnlyList<WingetPackageCandidate> SelectedCandidates =>
        _candidates.Where(candidate => candidate.IsSelected).ToList();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates)
            candidate.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in _candidates)
            candidate.IsSelected = false;
    }

    private void ScanOnly_Click(object sender, RoutedEventArgs e)
    {
        ApplyUpdates = false;
        DialogResult = true;
    }

    private void InstallSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedCandidates;
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one app update to install, or choose Scan Only.",
                "No Apps Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = $"Install {selected.Count} selected app update(s)?\n\n" +
                      "Some installers may still show prompts even when silent mode is requested.";
        if (MessageBox.Show(this, message, "Confirm App Updates",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyUpdates = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ApplyUpdates = false;
        DialogResult = false;
    }
}
