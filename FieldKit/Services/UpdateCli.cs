namespace FieldKit.Services;

internal static class UpdateCli
{
    internal static (bool Windows, bool Apps)? Parse(string[] args) =>
        args.Length == 2 ? args[1].ToLowerInvariant() switch
        {
            "windows" => (true, false),
            "apps" => (false, true),
            "both" => (true, true),
            _ => null
        } : null;

    internal static async Task<int> RunAsync(string[] args)
    {
        if (Parse(args) is not { } sources)
        {
            Console.Error.WriteLine("Usage: FieldKit.exe --update Windows|Apps|Both");
            return 2;
        }
        try
        {
            using var logger = Logger.ForProgress();
            logger.LogWritten += (message, _) => Console.WriteLine(message);
            var service = new UpdateService(logger);
            var scan = await service.ScanAsync(sources.Windows, sources.Apps, CancellationToken.None);
            var ids = scan.Apps.Select(app => app.Id).ToList();
            var result = await service.ApplyAsync(scan, sources.Windows, ids, CancellationToken.None, includeApps: sources.Apps);
            Console.WriteLine($"Update log: {result.LogFile}");
            return result.Stopped || result.Error > 0 || result.Warning > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FieldKit update failed: {ex.Message}");
            return 1;
        }
    }
}
