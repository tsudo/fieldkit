using System.IO;
using System.Text;
using System.Windows;
using FieldKit.Services;
using Microsoft.Win32;

namespace FieldKit;

public partial class SystemInfoWindow : Window
{
    private string _report = "";

    public SystemInfoWindow()
    {
        InitializeComponent();
        ReportBox.Text = "Gathering system information...";
        Loaded += async (_, _) =>
        {
            try
            {
                // Dozens of WMI queries: run them off the UI thread so the window opens at once.
                _report = await Task.Run(SystemInfo.BuildFullReport);
                ReportBox.Text = _report;
            }
            catch (Exception ex)
            {
                ReportBox.Text = $"System information could not be gathered: {ex.Message}";
            }
        };
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"SystemInfo_{Environment.MachineName}_{DateTime.Now:yyyyMMdd}.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt"
        };

        if (_report.Length == 0)
        {
            MessageBox.Show(this, "The report is not ready yet.", "Save to file", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, _report, Encoding.UTF8);
                MessageBox.Show($"Saved to {dlg.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
