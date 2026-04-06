using System.Windows;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit;

public partial class App : Application
{
    private Logger? _logger;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            ThemeMode = ThemeMode.Dark;

            if (!DisclaimerWindow.HasAccepted())
            {
                var disclaimer = new DisclaimerWindow();
                if (disclaimer.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            _logger = new Logger();
            var tasks = new List<MaintenanceTask>
            {
                new CreateRestorePointTask(),
                new ClearTemporaryFilesTask(),
                new EmptyRecycleBinTask(),
                new FlushDnsCacheTask(),
                new StorageSenseTask(),
                new WindowsUpdateTask(),
                new OfficeUpdateTask(),
                new StoreUpdateTask(),
                new WingetUpdateTask(),
                new SfcTask(),
                new DismTask(),
                new OptimizeDrivesTask()
            };

            foreach (var task in tasks)
                task.Logger = _logger;

            var mainWindow = new MainWindow(tasks, _logger);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"FieldKit failed to start:\n\n{ex}", "FieldKit — Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Dispose();
        base.OnExit(e);
    }
}
