namespace FieldKit.Services;

public class Logger : IDisposable
{
    private readonly string _logFilePath;
    private StreamWriter? _writer;
    private readonly object _lock = new();
    private bool _disposed;

    public event Action<string, string>? LogWritten; // message, level

    public string LogFilePath => _logFilePath;

    public Logger()
    {
        string logFileName = $"FieldKit-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var candidates = new List<string>();

        try { candidates.Add(Path.Combine(Path.GetTempPath(), logFileName)); } catch { }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, logFileName));
        candidates.Add(logFileName);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _writer = new StreamWriter(candidate, append: true) { AutoFlush = true };
                _logFilePath = candidate;
                return;
            }
            catch
            {
            }
        }

        _logFilePath = candidates.Last();
    }

    public void Log(string message, string level = "INFO")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var entry = $"[{timestamp}] [{level}] {message}";

        lock (_lock)
        {
            if (!_disposed)
            {
                try { _writer?.WriteLine(entry); } catch { }
            }
        }

        try { LogWritten?.Invoke(entry, level); } catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _disposed = true;
                _writer?.Dispose();
                _writer = null;
            }
        }
        GC.SuppressFinalize(this);
    }
}
