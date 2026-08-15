using System.Windows;
using System.Windows.Threading;
using FieldKit.Services;
using FieldKit.Tasks;

namespace FieldKit;

public partial class App : Application
{
    private Logger? _logger;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
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

    /// <summary>
    /// Last-chance handler for exceptions raised on the UI thread after startup.
    /// Individual maintenance tasks already catch their own failures, so anything
    /// reaching here is a UI-layer bug. The app stays alive so the user keeps
    /// access to the log path rather than losing it to a silent crash.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Log($"Unhandled UI exception: {e.Exception}", "ERROR");

        var logNote = _logger is null ? "" : $"\n\nLog: {_logger.LogFilePath}";
        MessageBox.Show(
            $"FieldKit hit an unexpected error:\n\n{e.Exception.Message}{logNote}",
            "FieldKit — Unexpected Error",
            MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Dispose();
        base.OnExit(e);
    }
}
