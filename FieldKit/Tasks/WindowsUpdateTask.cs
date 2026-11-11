using System.Runtime.InteropServices;
using FieldKit.Services;

namespace FieldKit.Tasks;

public sealed class WindowsUpdateTask : MaintenanceTask
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(5);

    public WindowsUpdateTask() : base(
        "Windows Update",
        "Checks for pending Windows updates and opens Settings so you can install them.",
        "Update",
        "1-2 min",
        requiresInternet: true)
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        Log("Asking Windows Update for pending updates. This usually takes about a minute.", "INFO");

        IReadOnlyList<string> titles;
        try
        {
            // The Windows Update Agent search is a blocking COM call with no
            // cancellation. Run it on a worker and stop waiting if the user
            // presses Stop; the search itself finishes harmlessly on its own.
            var search = Task.Run(SearchPendingUpdates);
            var finished = await Task.WhenAny(search, Task.Delay(SearchTimeout, ct));
            if (finished != search)
            {
                ct.ThrowIfCancellationRequested();
                return OpenSettingsFallback(dryRun, "Windows Update did not answer within 5 minutes");
            }

            titles = await search;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log($"Windows Update query failed: {ex.Message}", "WARN");
            return OpenSettingsFallback(dryRun, "Could not query Windows Update");
        }

        if (titles.Count == 0)
            return TaskResult.Ok("Windows is up to date");

        foreach (var title in titles)
            Log($"Pending: {title}", "INFO");

        if (dryRun)
            return TaskResult.Ok($"{titles.Count} update(s) pending");

        var opened = Shell.Open("ms-settings:windowsupdate", Logger);
        return opened
            ? TaskResult.Ok($"{titles.Count} update(s) pending. Settings is open: install them there.")
            : TaskResult.Warn($"{titles.Count} update(s) pending. Open Settings > Windows Update to install them.");
    }

    private TaskResult OpenSettingsFallback(bool dryRun, string reason)
    {
        if (!dryRun)
            Shell.Open("ms-settings:windowsupdate", Logger);
        return TaskResult.Warn($"{reason}. Check Settings > Windows Update.");
    }

    private static IReadOnlyList<string> SearchPendingUpdates()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
                   ?? throw new InvalidOperationException("The Windows Update Agent is not available on this system.");

        dynamic? session = null;
        try
        {
            session = Activator.CreateInstance(type)
                      ?? throw new InvalidOperationException("The Windows Update Agent could not be started.");
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");

            var titles = new List<string>();
            int count = result.Updates.Count;
            for (var i = 0; i < count; i++)
                titles.Add((string)result.Updates.Item(i).Title);
            return titles;
        }
        finally
        {
            if (session is not null)
                Marshal.FinalReleaseComObject(session);
        }
    }
}
