using System.IO;
using System.Text;
using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class StartupReviewTests
{
    private const string Header = "Time,Entry Location,Entry,Enabled,Category,Profile,Description,Signer,Company,Image Path,Version,Launch String";

    private static StartupEntry Entry(string name, string account = "PC\\User", string enabled = "enabled") =>
        new("Logon", account, "Run", name, enabled, "(Verified) Example", "Example", "app.exe", "app.exe /start");

    [Fact]
    public void ReadsQuotedCommaNewlineAndEscapedQuoteWithoutSplittingAnEntry()
    {
        var csv = Header + "\r\n0,HKCU,App,enabled,Logon,PC\\User,\"first\r\nsecond\",Signed,\"Company, Inc.\",app.exe,1,\"\"\"app.exe\"\" /start\"";
        var entry = Assert.Single(StartupReviewService.ParseCsv(csv));
        Assert.Equal("Company, Inc.", entry.Company);
        Assert.Equal("\"app.exe\" /start", entry.Command);
        Assert.Equal("PC\\User", entry.Account);
        Assert.True(entry.IsLogon);
    }

    [Fact]
    public void RejectsTruncatedOrUnrecognizedInventories()
    {
        Assert.Throws<InvalidDataException>(() => StartupReviewService.ParseCsv(Header + "\n0,short"));
        Assert.Throws<InvalidDataException>(() => StartupReviewService.ParseCsv("Warning: scan failed"));
        Assert.Throws<InvalidDataException>(() => StartupReviewService.ParseCsv(Header));
    }

    [Fact]
    public void DetectsBothUtf16AndUtf8Output()
    {
        const string value = "Time,Entry,公司";
        Assert.Equal(value, StartupReviewService.Decode(Encoding.Unicode.GetBytes(value)));
        Assert.Equal(value, StartupReviewService.Decode(Encoding.UTF8.GetBytes(value)));
    }

    [Fact]
    public void FirstScanThenDisableAddAndRemovePersistsChangesPerAccount()
    {
        var folder = Directory.CreateTempSubdirectory("fieldkit-startup-").FullName;
        try
        {
            var original = Entry("Example");
            var secondAccount = Entry("Example", "PC\\Other");
            Assert.True(StartupReviewService.Save(folder, [original, secondAccount, Entry("Removed")]).Baseline);
            var current = StartupReviewService.Save(folder, [original with { Enabled = "disabled" }, secondAccount, Entry("Added")]);
            Assert.False(current.Baseline);
            Assert.Equal(3, current.Changes.Count);
            var changed = Assert.Single(current.Changes, c => c.Kind == "Changed");
            Assert.Contains("disabled", changed.Detail);
            Assert.Equal(original.Account, changed.Account);
            Assert.Contains(current.Changes, c => c.Kind == "Added" && c.Name == "Added");
            Assert.Contains(current.Changes, c => c.Kind == "Removed" && c.Name == "Removed");
            var partial = StartupReviewService.Save(folder, [original], ["Could not read PC\\Other"]);
            Assert.True(partial.Baseline);
            Assert.Empty(partial.Changes);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ReorderingDuplicateTaskActionsDoesNotReportAChange()
    {
        var first = Entry("Example") with { Category = "Scheduled Tasks" };
        var second = first with { Command = "app.exe /other" };
        Assert.Empty(StartupReviewService.Compare([first, second], [second, first]));
    }
}
