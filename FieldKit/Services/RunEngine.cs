using FieldKit.Tasks;

namespace FieldKit.Services;

/// <summary>
/// Runs the selected maintenance steps one after another, keeps each row's state
/// current, and writes the run manifest and summary to the log.
/// </summary>
public sealed class RunEngine
{
    private readonly Logger _logger;

    public RunEngine(Logger logger) => _logger = logger;

    /// <summary>Raised on the calling (UI) thread before each step: index, count, row.</summary>
    public event Action<int, int, TaskViewModel>? StepStarting;

    public async Task<RunSummary> RunAsync(
        IReadOnlyList<TaskViewModel> steps,
        bool preview,
        string title,
        CancellationToken ct)
    {
        var started = DateTime.Now;
        var freeBefore = TryGetSystemDriveFreeGb();
        var items = new List<RunItem>();
        int success = 0, warning = 0, error = 0, skipped = 0;
        var reboot = false;

        _logger.Log($"{title} started. Preview: {(preview ? "on" : "off")}.", "INFO");
        // The manifest runs several WMI queries; keep them off the UI thread.
        var stepNames = steps.Select(step => step.Name).ToList();
        _logger.LogLines(await Task.Run(() => SystemInfo.BuildRunManifest(stepNames, _logger.LogFilePath), CancellationToken.None));

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (ct.IsCancellationRequested)
            {
                step.SetState(RunState.Skipped, "Stopped before this step ran");
                items.Add(new RunItem(step.Name, nameof(TaskResultState.Skipped), "Stopped before this step ran"));
                skipped++;
                continue;
            }

            StepStarting?.Invoke(i, steps.Count, step);
            step.SetState(RunState.Running, step.Task.Description);

            TaskResult result;
            try
            {
                result = await step.Task.ExecuteAsync(preview, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                result = TaskResult.Skipped("Stopped while running");
            }
            catch (OperationCanceledException ex)
            {
                // Cancelled, but not by the Stop button: a timeout inside the step.
                _logger.Log($"[{step.Name}] Gave up waiting: {ex.Message}", "ERROR");
                result = TaskResult.Fail("Timed out");
            }
            catch (TimeoutException ex)
            {
                _logger.Log($"[{step.Name}] {ex.Message}", "ERROR");
                result = TaskResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                // A step failing must not end the run: record it against the step
                // and carry on with the next one.
                _logger.Log($"[{step.Name}] Unexpected failure: {ex}", "ERROR");
                result = TaskResult.Fail($"Unexpected failure: {ex.Message}");
            }

            reboot |= result.RebootRecommended;
            items.Add(new RunItem(step.Name, result.State.ToString(), result.Summary));

            switch (result.State)
            {
                case TaskResultState.Success:
                    success++;
                    step.SetState(RunState.Success, result.Summary);
                    break;
                case TaskResultState.Warning:
                    warning++;
                    step.SetState(RunState.Warning, result.Summary);
                    break;
                case TaskResultState.Skipped:
                    skipped++;
                    step.SetState(RunState.Skipped, result.Summary);
                    break;
                default:
                    error++;
                    step.SetState(RunState.Error, result.Summary);
                    break;
            }

            _logger.Log($"[{step.Name}] {result.Summary}", result.State switch
            {
                TaskResultState.Success => "OK",
                TaskResultState.Error => "ERROR",
                _ => "WARN"
            });
        }

        var summary = new RunSummary(
            "Maintenance", title, started, DateTime.Now,
            success, warning, error, skipped,
            preview, ct.IsCancellationRequested, reboot,
            freeBefore, TryGetSystemDriveFreeGb(),
            _logger.LogFilePath, items);

        _logger.LogLines(summary.ToLogText(), summary.Tone == "Ok" ? "OK" : "WARN");
        RunHistory.Save(summary, _logger);
        return summary;
    }

    private static double? TryGetSystemDriveFreeGb()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        return string.IsNullOrWhiteSpace(root) ? null : SystemInfo.GetFreeSpaceGB(root[0]);
    }
}
