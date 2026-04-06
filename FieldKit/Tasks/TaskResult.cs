namespace FieldKit.Tasks;

public enum TaskResultState
{
    Success,
    Warning,
    Error,
    Skipped
}

public sealed record TaskResult(TaskResultState State, string Summary, bool RebootRecommended = false)
{
    public static TaskResult Ok(string summary, bool rebootRecommended = false) =>
        new(TaskResultState.Success, summary, rebootRecommended);

    public static TaskResult Warn(string summary, bool rebootRecommended = false) =>
        new(TaskResultState.Warning, summary, rebootRecommended);

    public static TaskResult Fail(string summary) =>
        new(TaskResultState.Error, summary);

    public static TaskResult Skipped(string summary) =>
        new(TaskResultState.Skipped, summary);
}
