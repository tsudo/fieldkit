using System.IO;

namespace FieldKit.Services;

public class Logger : IDisposable
{
    private readonly string _logFilePath;
    private StreamWriter? _writer;
    private readonly object _lock = new();
    private bool _disposed;
    private bool _writeFailed;

    public event Action<string, string>? LogWritten; // message, level

    public string LogFilePath => _logFilePath;

    /// <summary>True once a line could not be written to the file (disk full, file removed).</summary>
    public bool WriteFailed => _writeFailed;

    public Logger()
    {
        string logFileName = $"FieldKit-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var candidates = new List<string>();

        // AppPaths.Logs is outside %TEMP% on purpose: Clear Temporary Files used
        // to trip over the open log and delete the logs of earlier runs.
        try { candidates.Add(Path.Combine(AppPaths.Logs, logFileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, logFileName));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                // FileShare.ReadWrite lets "Zip for sharing" read the live log.
                var stream = new FileStream(candidate, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _logFilePath = candidate;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Try the next location. If none works the app still runs and
                // shows output on screen; LogFilePath then names a file that
                // does not exist, which the Reports page tells the user.
            }
        }

        _logFilePath = candidates.Last();
        _writeFailed = true;
    }

    public void Log(string message, string level = "INFO")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var entry = $"[{timestamp}] [{level}] {message}";

        lock (_lock)
        {
            if (!_disposed)
            {
                try { _writer?.WriteLine(entry); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { _writeFailed = true; }
            }
        }

        LogWritten?.Invoke(entry, level);
    }

    public void LogLines(string text, string level = "INFO")
    {
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            Log(line, level);
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
