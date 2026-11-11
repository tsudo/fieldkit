using System.IO;
using System.Text;
using System.Windows;
using FieldKit.Services;
using Microsoft.Win32;

namespace FieldKit;

public partial class SystemInfoWindow : Window
{
    private readonly string _report;

    public SystemInfoWindow()
    {
        InitializeComponent();
        _report = SystemInfo.BuildFullReport();
        ReportBox.Text = _report;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"SystemInfo_{Environment.MachineName}_{DateTime.Now:yyyyMMdd}.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt"
        };

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
