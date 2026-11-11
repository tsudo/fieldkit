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
                new SfcTask(),
                new DismTask(),
                new OptimizeDrivesTask()
            };

            foreach (var task in tasks)
                task.Logger = _logger;

            var pageIndex = Array.IndexOf(e.Args, "--page");
            var startPage = pageIndex >= 0 && pageIndex + 1 < e.Args.Length ? e.Args[pageIndex + 1] : null;
            var mainWindow = new MainWindow(tasks, _logger, startPage);
            mainWindow.Show();
#if DEBUG
            ScheduleDebugCapture(mainWindow, e.Args);
#endif

            // Registered only once startup has succeeded. Anything that fails
            // before this point belongs to the catch below, which shuts down
            // deliberately — including failures inside the disclaimer dialog,
            // whose whole job is obtaining consent before destructive work.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
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
    /// Maintenance tasks catch their own failures and the run loop reports its
    /// own aborts, so anything reaching here is a UI-layer bug outside a run.
    /// The app stays alive so the user keeps access to the log path rather than
    /// losing it to a silent crash.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = _logger;
        logger?.Log($"Unhandled UI exception: {e.Exception}", "ERROR");

        var logNote = logger is null ? "" : $"\n\nLog: {logger.LogFilePath}";
        MessageBox.Show(
            $"FieldKit hit an unexpected error:\n\n{e.Exception.Message}{logNote}",
            "FieldKit — Unexpected Error",
            MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }

#if DEBUG
    /// <summary>
    /// Debug builds only: `--capture file.png [--capture-delay seconds]` renders the
    /// window's own visual tree to a PNG and exits. It draws from WPF, not from the
    /// screen, so it works with the window in the background and never picks up
    /// anything else on the desktop. Used to review layout changes.
    /// </summary>
    private void ScheduleDebugCapture(Window window, string[] args)
    {
        var index = Array.IndexOf(args, "--capture");
        if (index < 0 || index + 1 >= args.Length) return;

        var path = args[index + 1];
        var delayIndex = Array.IndexOf(args, "--capture-delay");
        var delay = delayIndex >= 0 && delayIndex + 1 < args.Length && int.TryParse(args[delayIndex + 1], out var seconds)
            ? seconds : 3;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (window.Content is FrameworkElement root)
            {
                const double scale = 1.5;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale),
                    96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(path);
                encoder.Save(file);
            }
            Shutdown();
        };
        timer.Start();
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Dispose();
        base.OnExit(e);
    }
}
