using FieldKit.Services;

namespace FieldKit.Tasks;

public abstract class MaintenanceTask
{
    protected MaintenanceTask(
        string name,
        string description,
        string category,
        string estimatedTime,
        bool selectedByDefault = true,
        bool isAdvanced = false,
        bool requiresInternet = false,
        bool mayRequireReboot = false)
    {
        Name = name;
        Description = description;
        Category = category;
        EstimatedTime = estimatedTime;
        SelectedByDefault = selectedByDefault;
        IsAdvanced = isAdvanced;
        RequiresInternet = requiresInternet;
        MayRequireReboot = mayRequireReboot;
    }

    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string EstimatedTime { get; }
    public bool SelectedByDefault { get; }
    public bool IsAdvanced { get; }
    public bool RequiresInternet { get; }
    public bool MayRequireReboot { get; }
    public string? ContextInfo { get; protected set; }
    public Logger? Logger { get; set; }

    public virtual Task GatherContextAsync(CancellationToken ct = default) => Task.CompletedTask;

    public abstract Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct);

    protected void Log(string message, string level = "INFO") => Logger?.Log($"[{Name}] {message}", level);

    protected Task<CommandResult> RunCommandAsync(
        string fileName,
        string arguments,
        CancellationToken ct,
        Func<string, bool>? outputFilter = null)
        => CommandRunner.RunAsync(fileName, arguments, (message, level) => Log(message, level), ct, outputFilter);

    /// <summary>
    /// Runs a PowerShell command via the system-installed Windows PowerShell 5.1.
    /// SECURITY: All callers must pass hardcoded command strings only.
    /// Never pass user-controlled input — the command is interpolated into
    /// the argument string without escaping. Use -EncodedCommand if user
    /// input is ever needed in the future.
    /// </summary>
    protected Task<CommandResult> RunPowerShellAsync(
        string command,
        CancellationToken ct,
        Func<string, bool>? outputFilter = null)
    {
        var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        return RunCommandAsync(ps, $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"", ct, outputFilter);
    }
}
