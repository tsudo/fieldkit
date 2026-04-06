using FieldKit.Tasks;

namespace FieldKit;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!DisclaimerForm.HasAccepted())
        {
            using var disclaimer = new DisclaimerForm();
            if (disclaimer.ShowDialog() != DialogResult.OK || !disclaimer.Accepted)
                return;
        }

        using var logger = new Services.Logger();
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
            task.Logger = logger;

        Application.Run(new MainForm(tasks, logger));
    }
}
