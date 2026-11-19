namespace FieldKit.Services;

internal sealed record WindowsPackage(string Id, int Revision, string Title);

/// <summary>Snapshot update identities and revisions so a later install cannot silently expand its scope.</summary>
internal sealed class WindowsUpdates(Func<string, object> create)
{
    public WindowsUpdates() : this(progId => Activator.CreateInstance(Type.GetTypeFromProgID(progId, true)!)!) { }

    private dynamic OpenSession()
    {
        dynamic session = create("Microsoft.Update.Session");
        session.ClientApplicationID = "FieldKit";
        return session;
    }

    private static dynamic Search(dynamic session)
    {
        dynamic result = session.CreateUpdateSearcher().Search(
            "IsInstalled=0 and IsHidden=0 and Type='Software' and IsAssigned=1");
        RequireSuccess("Search", result);
        return result.Updates;
    }

    private static void RequireSuccess(string operation, dynamic result)
    {
        if ((int)result.ResultCode != 2)
            throw new InvalidOperationException($"{operation} returned Windows Update result {result.ResultCode}.");
    }

    private static WindowsPackage Describe(dynamic update) =>
        new((string)update.Identity.UpdateID, (int)update.Identity.RevisionNumber, (string)update.Title);

    public IReadOnlyList<WindowsPackage> Scan()
    {
        dynamic updates = Search(OpenSession());
        var packages = new List<WindowsPackage>();
        for (var i = 0; i < (int)updates.Count; i++)
            packages.Add(Describe(updates.Item(i)));
        return packages;
    }

    public void Install(IReadOnlyList<WindowsPackage> selected, Action<RunItem> record,
        Action restartRequired, Logger log, CancellationToken ct)
    {
        dynamic session = OpenSession();
        dynamic pending = Search(session);
        var indexes = new Dictionary<(string, int), int>();
        for (var i = 0; i < (int)pending.Count; i++)
        {
            WindowsPackage package = Describe(pending.Item(i));
            indexes[(package.Id, package.Revision)] = i;
        }

        bool needsRestart = false;
        foreach (var package in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (needsRestart)
            {
                record(new(package.Title, "Skipped", "Restart Windows, then check again."));
                continue;
            }
            if (!indexes.TryGetValue((package.Id, package.Revision), out var index))
            {
                record(new(package.Title, "Skipped", "This revision is no longer pending."));
                continue;
            }
            try
            {
                dynamic update = pending.Item(index);
                if ((bool)update.InstallationBehavior.CanRequestUserInput)
                {
                    record(new(package.Title, "Warning", "Requires interactive installation in Windows Settings."));
                    continue;
                }
                if (!(bool)update.EulaAccepted) update.AcceptEula();
                dynamic batch = create("Microsoft.Update.UpdateColl");
                batch.Add(update);
                log.Log($"Downloading {package.Title}");
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = batch;
                RequireSuccess("Download", downloader.Download());
                ct.ThrowIfCancellationRequested();
                log.Log($"Installing {package.Title}");
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = batch;
                dynamic result = installer.Install();
                needsRestart = (bool)result.RebootRequired;
                if (needsRestart) restartRequired();
                RequireSuccess("Install", result);
                record(new(package.Title, "Success", needsRestart ? "Installed. Restart required." : "Installed."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                record(new(package.Title, "Error", ex.Message));
            }
        }
    }
}
