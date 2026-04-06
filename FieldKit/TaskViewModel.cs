using System.ComponentModel;
using FieldKit.Tasks;

namespace FieldKit;

public class TaskViewModel : INotifyPropertyChanged
{
    public MaintenanceTask Task { get; }

    public string Name => Task.Name;
    public string Category => Task.Category;
    public string EstimatedTime => Task.EstimatedTime;
    public string TypeLabel => Task.IsAdvanced ? "Advanced" : "Routine";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged(nameof(Status));
        }
    }

    public TaskViewModel(MaintenanceTask task)
    {
        Task = task;
        _isSelected = task.SelectedByDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
