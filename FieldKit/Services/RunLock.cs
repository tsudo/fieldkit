namespace FieldKit.Services;

/// <summary>
/// One long-running job at a time across all pages. A maintenance run, an app
/// install and a kit build would otherwise interleave their output in one log.
/// The holder may register how to stop its job, which the window uses when the
/// user closes FieldKit mid-run.
/// </summary>
public static class RunLock
{
    private static string? _owner;
    private static Action? _stop;

    public static event Action? Changed;

    public static string? Owner => _owner;
    public static bool IsBusy => _owner is not null;

    /// <summary>True when the running job registered a way to stop it.</summary>
    public static bool CanStop => _stop is not null;

    public static bool TryEnter(string owner, Action? stop = null)
    {
        if (_owner is not null) return false;
        _owner = owner;
        _stop = stop;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Asks the running job to stop. The lock is released by the job itself when it has.</summary>
    public static void RequestStop() => _stop?.Invoke();

    public static void Exit()
    {
        _owner = null;
        _stop = null;
        Changed?.Invoke();
    }
}
