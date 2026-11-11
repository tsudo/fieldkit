using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FieldKit;

public enum RunState
{
    Ready,
    Running,
    Success,
    Warning,
    Error,
    Skipped
}

/// <summary>
/// One line in a checklist: a maintenance step, an app, a kit tool or a report.
/// Every page renders these with the same row template from Theme.xaml.
/// </summary>
public class RowViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _canSelect = true;
    private bool _isVisible = true;
    private string _detail = "";
    private string _meta = "";
    private string _pillText = "";
    private string _pillTone = "";
    private RunState _state = RunState.Ready;
    private DateTime? _startedAt;

    public RowViewModel(string name, string detail = "")
    {
        Name = name;
        _detail = detail;
    }

    public string Name { get; }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
                SelectionChanged?.Invoke();
        }
    }

    public bool CanSelect
    {
        get => _canSelect;
        set => Set(ref _canSelect, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    /// <summary>Quiet right-hand text: an estimate, a version, a size.</summary>
    public string Meta
    {
        get => _meta;
        set => Set(ref _meta, value);
    }

    public string PillText
    {
        get => _pillText;
        private set => Set(ref _pillText, value);
    }

    /// <summary>Ok, Warn, Error, Info, Neutral, or empty for no pill.</summary>
    public string PillTone
    {
        get => _pillTone;
        private set => Set(ref _pillTone, value);
    }

    public RunState State => _state;

    public event Action? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPill(string text, string tone)
    {
        PillText = text;
        PillTone = string.IsNullOrEmpty(text) ? "" : tone;
    }

    /// <summary>Moves the row to a run state and shows the matching pill.</summary>
    public void SetState(RunState state, string? resultText = null)
    {
        _state = state;
        OnPropertyChanged(nameof(State));

        switch (state)
        {
            case RunState.Ready:
                _startedAt = null;
                SetPill("", "");
                break;
            case RunState.Running:
                _startedAt = DateTime.Now;
                SetPill("Running", "Info");
                break;
            case RunState.Success:
                SetPill(WithElapsed("Done"), "Ok");
                break;
            case RunState.Warning:
                SetPill(WithElapsed("Check"), "Warn");
                break;
            case RunState.Error:
                SetPill(WithElapsed("Failed"), "Error");
                break;
            case RunState.Skipped:
                SetPill("Skipped", "Neutral");
                break;
        }

        if (resultText is not null)
            Detail = resultText;
    }

    /// <summary>Refreshes the running pill's clock. Called once a second by the page.</summary>
    public void Tick()
    {
        if (_state == RunState.Running && _startedAt is { } started)
            SetPill($"Running {FormatClock(DateTime.Now - started)}", "Info");
    }

    private string WithElapsed(string label)
    {
        if (_startedAt is not { } started) return label;
        var text = $"{label} {FormatClock(DateTime.Now - started)}";
        _startedAt = null;
        return text;
    }

    public static string FormatClock(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GroupViewModel : INotifyPropertyChanged
{
    private bool _isVisible = true;

    public GroupViewModel(string name, IEnumerable<RowViewModel> rows)
    {
        Name = name;
        Rows = new ObservableCollection<RowViewModel>(rows);
    }

    public string Name { get; }
    public ObservableCollection<RowViewModel> Rows { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
