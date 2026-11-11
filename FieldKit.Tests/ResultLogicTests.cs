using FieldKit.Services;
using FieldKit.Tasks;
using Xunit;

namespace FieldKit.Tests;

public class ResultLogicTests
{
    // ---- Restore point ------------------------------------------------

    [Fact]
    public void Restore_point_created_is_success()
    {
        var result = CreateRestorePointTask.Interpret(0, [CreateRestorePointTask.CreatedMarker], "FieldKit test");
        Assert.Equal(TaskResultState.Success, result.State);
        Assert.Contains("Created", result.Summary);
    }

    [Fact]
    public void Restore_point_skipped_by_windows_is_not_reported_as_created()
    {
        var result = CreateRestorePointTask.Interpret(0, [CreateRestorePointTask.SkippedMarker], "FieldKit test");
        Assert.Equal(TaskResultState.Success, result.State);
        Assert.DoesNotContain("Created", result.Summary);
        Assert.Contains("24 hours", result.Summary);
    }

    [Fact]
    public void Restore_point_output_mentioning_24_does_not_decide_the_result()
    {
        // The old check treated any output containing "24" as "already created".
        // Exit 0 so the output is what gets judged: text containing "24" with
        // no marker must not be read as "created" or "already exists".
        var result = CreateRestorePointTask.Interpret(0, ["Restore point 24 of 24 checked"], "FieldKit test");
        Assert.Equal(TaskResultState.Warning, result.State);
        Assert.Contains("did not report a result", result.Summary);

        Assert.Contains("could not be created",
            CreateRestorePointTask.Interpret(1, ["Error 0x80042424 at line 24"], "FieldKit test").Summary);
    }

    [Fact]
    public void Restore_point_exit_zero_without_marker_is_a_warning()
    {
        Assert.Equal(TaskResultState.Warning, CreateRestorePointTask.Interpret(0, [], "x").State);
    }

    // ---- Clear temp ---------------------------------------------------

    [Fact]
    public void Clear_temp_with_files_in_use_is_still_success()
    {
        var result = ClearTemporaryFilesTask.Summarize(dryRun: false, removed: 40, inUse: 6, kept: 12);
        Assert.Equal(TaskResultState.Success, result.State);
        Assert.Contains("40", result.Summary);
        Assert.Contains("18 kept", result.Summary);
    }

    [Fact]
    public void Clear_temp_warns_only_when_nothing_could_be_removed()
    {
        Assert.Equal(TaskResultState.Warning, ClearTemporaryFilesTask.Summarize(false, removed: 0, inUse: 3, kept: 0).State);
        Assert.Equal(TaskResultState.Success, ClearTemporaryFilesTask.Summarize(false, removed: 0, inUse: 0, kept: 9).State);
    }

    [Fact]
    public void Clear_temp_preview_says_would()
    {
        Assert.StartsWith("Would remove", ClearTemporaryFilesTask.Summarize(true, 5, 0, 0).Summary);
    }

    // ---- Recycle Bin --------------------------------------------------

    [Fact]
    public void Recycle_bin_result_comes_from_the_markers()
    {
        Assert.Equal("Recycle Bin was already empty",
            EmptyRecycleBinTask.Interpret(0, [EmptyRecycleBinTask.EmptyMarker], null, null).Summary);

        var cleared = EmptyRecycleBinTask.Interpret(0, [$"{EmptyRecycleBinTask.ClearedMarker} 12"], null, null);
        Assert.Equal(TaskResultState.Success, cleared.State);
        Assert.Contains("12", cleared.Summary);
    }

    [Fact]
    public void Recycle_bin_failure_mentioning_empty_is_not_called_already_empty()
    {
        // The old check treated any error text containing "empty" as success,
        // which also matched "The directory is not empty".
        var result = EmptyRecycleBinTask.Interpret(1, ["The directory is not empty."], null, null);
        Assert.Equal(TaskResultState.Warning, result.State);
    }

    [Fact]
    public void Recycle_bin_under_another_account_says_whose_bin_it_was()
    {
        var result = EmptyRecycleBinTask.Interpret(0, [$"{EmptyRecycleBinTask.ClearedMarker} 3"], @"PC\Admin", @"PC\Sam");

        Assert.Equal(TaskResultState.Warning, result.State);
        Assert.Contains("Admin", result.Summary);
        Assert.Contains("Sam was not touched", result.Summary);
    }

    // ---- SFC ----------------------------------------------------------

    [Theory]
    [InlineData("Windows Resource Protection did not find any integrity violations.", TaskResultState.Success)]
    [InlineData("Windows Resource Protection found corrupt files and successfully repaired them.", TaskResultState.Warning)]
    [InlineData("Windows Resource Protection found corrupt files but was unable to fix some of them.", TaskResultState.Error)]
    [InlineData("Windows Resource Protection could not perform the requested operation.", TaskResultState.Error)]
    public void Sfc_result_follows_its_output(string output, TaskResultState expected)
    {
        Assert.Equal(expected, SfcTask.Interpret(0, output).State);
    }

    [Fact]
    public void Sfc_unreadable_output_is_never_reported_as_clean()
    {
        // What UTF-16 output looks like when decoded with the wrong encoding.
        var garbled = "W\0i\0n\0d\0o\0w\0s\0 \0R\0e\0s\0o\0u\0r\0c\0e\0";
        Assert.Equal(TaskResultState.Warning, SfcTask.Interpret(0, garbled).State);
    }

    [Fact]
    public void Sfc_repair_recommends_a_restart()
    {
        Assert.True(SfcTask.Interpret(0, "successfully repaired").RebootRecommended);
    }

    // ---- Output handling ---------------------------------------------

    [Theory]
    [InlineData("Installer failed with exit code: 6", "WARN")]
    [InlineData("Access is denied.", "WARN")]
    [InlineData("The file is currently in use.", "WARN")]
    [InlineData("Successfully installed", "INFO")]
    public void Failure_phrases_are_raised_to_warning(string line, string expected)
    {
        Assert.Equal(expected, CommandRunner.ClassifyOutputLine(line, "INFO"));
    }

    [Theory]
    [InlineData("[2026-11-10 10:00:00] [INFO] [Mozilla.Firefox]    - ", true)]
    [InlineData("[2026-11-10 10:00:00] [INFO] [Mozilla.Firefox]   ██████▒▒▒▒  60%", true)]
    [InlineData("[2026-11-10 10:00:00] [INFO] [Mozilla.Firefox] ", true)]
    [InlineData("[2026-11-10 10:00:00] [INFO] [Mozilla.Firefox] Successfully installed", false)]
    [InlineData("[2026-11-10 10:00:00] [OK] [Clear temporary files] Removed 40 item(s)", false)]
    public void Progress_redraws_stay_off_screen(string entry, bool isNoise)
    {
        Assert.Equal(isNoise, MainWindow.IsProgressNoise(entry));
    }

    // ---- Run history, rows and the run lock ---------------------------

    [Fact]
    public void Run_summary_survives_a_json_round_trip()
    {
        var original = new RunSummary("Apps", "App install and update", new DateTime(2026, 11, 10, 9, 0, 0),
            new DateTime(2026, 11, 10, 9, 4, 30), 2, 0, 1, 1, Preview: false, Stopped: true, RebootRecommended: true,
            FreeGbBefore: null, FreeGbAfter: null, LogFile: @"C:\FieldKit\Logs\x.log",
            Items: [new RunItem("Firefox", "Success", "Installed"), new RunItem("VLC", "Error", "The app is running.")]);

        var json = RunHistory.Serialize(original);
        var copy = RunHistory.Deserialize(json)!;

        Assert.DoesNotContain("CountsText", json); // computed values are not stored
        Assert.Equal(original.Started, copy.Started);
        Assert.Equal(original.Duration, copy.Duration);
        Assert.Equal(original.CountsText, copy.CountsText);
        Assert.Equal("Error", copy.Tone);
        Assert.Equal(2, copy.Items.Count);
        Assert.Equal("The app is running.", copy.Items[1].Summary);
        Assert.True(copy.Stopped && copy.RebootRecommended);
    }

    [Fact]
    public void Row_shows_a_pill_for_each_state_and_clears_it_on_reset()
    {
        var row = new RowViewModel("Step", "What it does");

        row.SetState(RunState.Running);
        Assert.Equal("Info", row.PillTone);
        Assert.StartsWith("Running", row.PillText);

        row.SetState(RunState.Error, "It broke");
        Assert.Equal("Error", row.PillTone);
        Assert.StartsWith("Failed", row.PillText);
        Assert.Equal("It broke", row.Detail);

        row.SetState(RunState.Skipped, "Stopped");
        Assert.Equal("Skipped", row.PillText);

        row.SetState(RunState.Ready, "What it does");
        Assert.Equal("", row.PillText);
        Assert.Equal("", row.PillTone);
    }

    [Fact]
    public void Run_lock_admits_one_job_and_forwards_a_stop_request()
    {
        var stopped = 0;
        Assert.True(RunLock.TryEnter("First", () => stopped++));
        try
        {
            Assert.False(RunLock.TryEnter("Second"));
            Assert.Equal("First", RunLock.Owner);

            RunLock.RequestStop();
            Assert.Equal(1, stopped);
        }
        finally
        {
            RunLock.Exit();
        }

        Assert.False(RunLock.IsBusy);
        RunLock.RequestStop(); // nothing registered: must be a no-op
        Assert.Equal(1, stopped);
    }

    // ---- Run summary --------------------------------------------------

    [Fact]
    public void Summary_counts_and_tone_reflect_the_worst_result()
    {
        var summary = new RunSummary("Maintenance", "Cleanup", new DateTime(2026, 11, 10, 9, 0, 0),
            new DateTime(2026, 11, 10, 9, 2, 5), Success: 3, Warning: 1, Error: 0, Skipped: 2,
            Preview: false, Stopped: false, RebootRecommended: false, FreeGbBefore: 10, FreeGbAfter: 11.5,
            LogFile: "x.log", Items: []);

        Assert.Equal("3 done, 1 to check, 2 skipped", summary.CountsText);
        Assert.Equal("Warn", summary.Tone);
        Assert.Contains("+1.50 GB", summary.ToLogText());
        Assert.Equal("2:05", RowViewModel.FormatClock(summary.Duration));
    }
}
