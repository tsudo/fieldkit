namespace FieldKit.Services;

/// <summary>
/// One long-running job at a time across all pages. A maintenance run, an app
/// install and a kit build would otherwise interleave their output in one log.
/// </summary>
public static class RunLock
{
    private static string? _owner;

    public static event Action? Changed;

    public static string? Owner => _owner;
    public static bool IsBusy => _owner is not null;

    public static bool TryEnter(string owner)
    {
        if (_owner is not null) return false;
        _owner = owner;
        Changed?.Invoke();
        return true;
    }

    public static void Exit()
    {
        _owner = null;
        Changed?.Invoke();
    }
}
