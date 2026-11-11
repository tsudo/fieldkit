using FieldKit.Services;

namespace FieldKit.Tasks;

public sealed class EmptyRecycleBinTask : MaintenanceTask
{
    internal const string EmptyMarker = "FIELDKIT_BIN_ALREADY_EMPTY";
    internal const string ClearedMarker = "FIELDKIT_BIN_CLEARED";

    public EmptyRecycleBinTask() : base(
        "Empty recycle bin",
        "Permanently removes files currently sitting in the Recycle Bin.",
        "Clean up",
        "Under 1 min")
    {
    }

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log("Would empty the Recycle Bin.", "INFO");
            return TaskResult.Ok("Would empty the Recycle Bin");
        }

        // Count first so "already empty" is a fact from the shell, not a guess
        // from the wording of an error. Namespace 10 is the Recycle Bin.
        const string script =
            "$ErrorActionPreference='Stop'; " +
            "$count = (New-Object -ComObject Shell.Application).NameSpace(10).Items().Count; " +
            $"if ($count -eq 0) {{ '{EmptyMarker}' }} else {{ Clear-RecycleBin -Force; '{ClearedMarker} ' + $count }}";

        var result = await RunPowerShellAsync(script, ct, line =>
            !string.IsNullOrWhiteSpace(line) && !line.StartsWith("FIELDKIT_", StringComparison.Ordinal));

        var differentUser = Shell.RunsAsDifferentUser(out var interactive, out var current);
        return Interpret(result.ExitCode, result.Output, differentUser ? current : null, differentUser ? interactive : null);
    }

    /// <summary>
    /// The Recycle Bin is per user. When a different administrator account approved
    /// the elevation prompt, this step empties that administrator's bin and leaves
    /// the signed-in user's alone, so the result says whose bin it was.
    /// </summary>
    internal static TaskResult Interpret(int exitCode, IReadOnlyList<string> output, string? runningAs, string? signedIn)
    {
        if (exitCode != 0)
            return TaskResult.Warn("Recycle Bin could not be emptied. See the log.");

        var wrongBin = runningAs is not null;
        var whose = wrongBin ? $" for {runningAs}. The bin of {signedIn} was not touched" : "";

        if (output.Any(line => line.Trim() == EmptyMarker))
            return wrongBin ? TaskResult.Warn($"Already empty{whose}") : TaskResult.Ok("Recycle Bin was already empty");

        var cleared = output.Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith(ClearedMarker, StringComparison.Ordinal));
        if (cleared is not null)
        {
            var count = cleared[ClearedMarker.Length..].Trim();
            return wrongBin ? TaskResult.Warn($"Emptied {count} item(s){whose}") : TaskResult.Ok($"Emptied {count} item(s)");
        }

        return TaskResult.Warn("Recycle Bin command finished but did not report a result");
    }
}
