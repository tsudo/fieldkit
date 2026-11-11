using FieldKit.Tasks;

namespace FieldKit;

public sealed class TaskViewModel : RowViewModel
{
    public TaskViewModel(MaintenanceTask task) : base(task.Name, task.Description)
    {
        Task = task;
        IsSelected = task.SelectedByDefault;
        Meta = task.EstimatedTime;
    }

    public MaintenanceTask Task { get; }

    public string Category => Task.Category;

    /// <summary>Puts the row back to its idle look: description on the left, estimate on the right.</summary>
    public void Reset()
    {
        SetState(RunState.Ready, Task.Description);
        RefreshMeta();
    }

    public void RefreshMeta()
    {
        Meta = string.IsNullOrWhiteSpace(Task.ContextInfo)
            ? Task.EstimatedTime
            : $"{Task.ContextInfo}  ·  {Task.EstimatedTime}";
    }
}
