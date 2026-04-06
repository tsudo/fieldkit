namespace FieldKit;

public enum TaskState
{
    Pending,
    Running,
    Success,
    Warning,
    Error,
    Skipped
}

public enum TaskCategory
{
    Preparation,
    Cleanup,
    Updates,
    Repair
}

public class TaskResult
{
    public TaskState State { get; set; }
    public string Note { get; set; } = "";

    public static TaskResult Ok(string note = "") => new() { State = TaskState.Success, Note = note };
    public static TaskResult Warn(string note) => new() { State = TaskState.Warning, Note = note };
    public static TaskResult Fail(string note) => new() { State = TaskState.Error, Note = note };
    public static TaskResult Skip(string note) => new() { State = TaskState.Skipped, Note = note };
}

public enum TaskDuration
{
    Quick,   // seconds
    Medium,  // 1-5 minutes
    Long     // 10-30 minutes
}

public abstract class MaintenanceTask
{
    public string Name { get; }
    public string Description { get; }
    public TaskDuration Duration { get; }
    public TaskCategory Category { get; set; }

    /// <summary>Brief context shown before execution (e.g. "3 items, 1.2 GB").</summary>
    public string ContextInfo { get; set; } = "";

    public string DurationLabel => Duration switch
    {
        TaskDuration.Quick => "Quick",
        TaskDuration.Medium => "1\u20135 min",
        TaskDuration.Long => "10\u201330 min",
        _ => ""
    };

    private TaskState _state = TaskState.Pending;
    private string _resultNote = "";
    private readonly object _stateLock = new();

    public TaskState State
    {
        get { lock (_stateLock) return _state; }
        set { lock (_stateLock) _state = value; }
    }

    public string ResultNote
    {
        get { lock (_stateLock) return _resultNote; }
        set { lock (_stateLock) _resultNote = value; }
    }

    protected Action<string, string> Log { get; private set; } = (_, _) => { };

    protected MaintenanceTask(string name, string description, TaskDuration duration = TaskDuration.Quick)
    {
        Name = name;
        Description = description;
        Duration = duration;
    }

    public void SetLogger(Action<string, string> log) => Log = log;

    public abstract Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct);

    /// <summary>Quick pre-flight check to populate ContextInfo. Called on app load.</summary>
    public virtual Task GatherContextAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Resolves a system binary to its absolute path under %SystemRoot%\System32.</summary>
    protected static string SystemExe(string fileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), fileName);

    protected static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };

    protected TaskResult RunProcess(string fileName, string arguments, bool dryRun,
        CancellationToken ct = default, int timeoutMs = 600_000)
    {
        if (dryRun)
        {
            Log($"[DryRun] Would run: {fileName} {arguments}", "INFO");
            return TaskResult.Ok("Dry run");
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
                return TaskResult.Fail($"Failed to start {fileName}");

            // Kill process on cancellation
            using var registration = ct.Register(() =>
            {
                try { proc.Kill(true); } catch { }
            });

            // Read stderr asynchronously to prevent pipe buffer deadlock
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (!proc.WaitForExit(timeoutMs))
            {
                try
                {
                    proc.Kill(true);
                    proc.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Log($"Warning: failed to kill {fileName}: {ex.Message}", "WARN");
                }
                return TaskResult.Fail($"{fileName} timed out after {timeoutMs / 1000}s");
            }

            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(stdout))
                Log(stdout.TrimEnd(), "INFO");
            if (!string.IsNullOrWhiteSpace(stderr))
                Log(stderr.TrimEnd(), "WARN");

            return new TaskResult
            {
                State = proc.ExitCode == 0 ? TaskState.Success : TaskState.Warning,
                Note = proc.ExitCode == 0 ? "" : $"Exit code {proc.ExitCode}"
            };
        }
        catch (OperationCanceledException)
        {
            return TaskResult.Warn("Cancelled");
        }
        catch (Exception ex)
        {
            return TaskResult.Fail(ex.Message);
        }
    }
}
