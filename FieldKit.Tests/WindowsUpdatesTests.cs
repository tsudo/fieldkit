using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class WindowsUpdatesTests
{
    // Implements just the late-bound WUA members exercised by the runner. No native
    // update session, downloader or installer is created by this test.
    public sealed record Identity(string UpdateID, int RevisionNumber);
    public sealed record Behavior(bool CanRequestUserInput);
    public sealed class Package(string name, int revision = 1, bool interactive = false)
    {
        public Identity Identity { get; } = new(name, revision);
        public string Title => Identity.UpdateID;
        public Behavior InstallationBehavior { get; } = new(interactive);
        public bool EulaAccepted { get; private set; }
        public void AcceptEula() => EulaAccepted = true;
    }

    public sealed class Packages : List<Package>
    {
        public Package Item(int index) => this[index];
    }

    public sealed record QueryResult(Packages Updates, int ResultCode = 2);
    public sealed record InstallResult(bool RebootRequired, int ResultCode = 2);
    public sealed class Session
    {
        public string ClientApplicationID { get; set; } = "";
        public Packages Pending { get; } = [];
        public Packages Updates { get; set; } = [];
        public List<string> Installed { get; } = [];
        public Session CreateUpdateSearcher() => this;
        public QueryResult Search(string criteria) => new(Pending);
        public Session CreateUpdateDownloader() => this;
        public InstallResult Download() => new(false);
        public Session CreateUpdateInstaller() => this;
        public InstallResult Install()
        {
            Installed.Add(Assert.Single(Updates).Title);
            return new(true);
        }
    }

    [Fact]
    public void ApplyHonorsReviewedRevisionsInteractiveUpdatesAndRestartBoundary()
    {
        var session = new Session();
        session.Pending.AddRange([new("Revised"), new("Interactive", interactive: true), new("Ready"), new("Later")]);
        var wua = new WindowsUpdates(id => id == "Microsoft.Update.Session" ? session : new Packages());
        var snapshot = wua.Scan();
        session.Pending[0] = new("Revised", 2);
        session.Pending.Add(new("New since scan"));
        var items = new List<RunItem>();
        bool reboot = false;
        using var log = Logger.ForProgress();

        wua.Install(snapshot, items.Add, () => reboot = true, log, CancellationToken.None);

        Assert.True(reboot);
        Assert.Equal("Ready", Assert.Single(session.Installed));
        Assert.True(session.Pending[2].EulaAccepted);
        Assert.Equal(["Skipped", "Warning", "Success", "Skipped"], items.Select(item => item.State));
    }
}
