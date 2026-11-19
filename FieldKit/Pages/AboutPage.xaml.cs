using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FieldKit.Services;

namespace FieldKit.Pages;

public partial class AboutPage : UserControl
{
    private readonly Logger _logger;

    public AboutPage(Logger logger)
    {
        InitializeComponent();
        _logger = logger;
        VersionText.Text = $"Version {SystemInfo.GetDisplayVersion()}";
        LicenseText.Text = AboutContent.License;
        DisclaimerText.Text = AboutContent.Disclaimer;
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        StatusText.Text = "Checking GitHub for the latest stable release…";
        try { StatusText.Text = await AboutContent.CheckReleaseAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            _logger.Log($"FieldKit release check failed: {ex.Message}", "WARN");
            StatusText.Text = "The release check could not finish. Try again or open Releases on GitHub.";
        }
        finally { CheckButton.IsEnabled = true; }
    }

    private void Open(string url)
    {
        if (!Shell.Open(url, _logger)) StatusText.Text = $"Open {url} in your browser.";
    }

    private void Repository_Click(object sender, RoutedEventArgs e) => Open(AboutContent.Repository);
    private void Releases_Click(object sender, RoutedEventArgs e) => Open(AboutContent.Releases);
}
