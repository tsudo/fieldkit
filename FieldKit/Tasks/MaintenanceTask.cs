using System.Text;
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
        Func<string, bool>? outputFilter = null,
        Encoding? outputEncoding = null)
        => CommandRunner.RunAsync(fileName, arguments, (message, level) => Log(message, level), ct, outputFilter, outputEncoding);

    /// <summary>
    /// Runs a script in the system-installed Windows PowerShell 5.1. The script
    /// travels as -EncodedCommand, so its quoting cannot affect the command line.
    /// </summary>
    protected Task<CommandResult> RunPowerShellAsync(
        string script,
        CancellationToken ct,
        Func<string, bool>? outputFilter = null)
        => CommandRunner.RunPowerShellAsync(script, (message, level) => Log(message, level), ct, outputFilter);
}
