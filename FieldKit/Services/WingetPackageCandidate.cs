using System.ComponentModel;

namespace FieldKit.Services;

public sealed class WingetPackageCandidate : INotifyPropertyChanged
{
    private bool _isSelected;

    public WingetPackageCandidate(string name, string id, string installedVersion, string availableVersion, string source)
    {
        Name = name;
        Id = id;
        InstalledVersion = installedVersion;
        AvailableVersion = availableVersion;
        Source = source;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Name { get; }
    public string Id { get; }
    public string InstalledVersion { get; }
    public string AvailableVersion { get; }
    public string Source { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
