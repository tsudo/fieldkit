using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace FieldKit.Services;

public static class CommandRunner
{
    static CommandRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// The console (OEM) code page. Classic Windows tools such as DISM, defrag and
    /// ipconfig write their redirected output in it. Callers pass a different
    /// encoding for tools that do not: winget writes UTF-8, sfc writes UTF-16.
    /// </summary>
    public static Encoding OemEncoding
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.UTF8; }
        }
    }

    public static async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        Action<string, string>? log = null,
        CancellationToken cancellationToken = default,
        Func<string, bool>? outputFilter = null,
        Encoding? outputEncoding = null,
        TimeSpan? timeout = null)
    {
        var output = new List<string>();
        var error = new List<string>();
        var encoding = outputEncoding ?? OemEncoding;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
                return;
            }

            lock (output) output.Add(e.Data);
            if (outputFilter?.Invoke(e.Data) ?? true)
                log?.Invoke(e.Data, ClassifyOutputLine(e.Data, "INFO"));
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            lock (error) error.Add(e.Data);
            if (outputFilter?.Invoke(e.Data) ?? true)
                log?.Invoke(e.Data, ClassifyOutputLine(e.Data, "WARN"));
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {fileName}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit)
            timeoutCts.CancelAfter(limit);

        using var registration = timeoutCts.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                log?.Invoke($"Could not stop {Path.GetFileName(fileName)}: {ex.Message}", "WARN");
            }
        });

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{Path.GetFileName(fileName)} did not finish within {timeout!.Value.TotalMinutes:0} minutes and was stopped.");
        }

        await Task.WhenAll(stdoutDone.Task, stderrDone.Task);

        return new CommandResult(process.ExitCode, output, error);
    }

    /// <summary>
    /// Runs a script in the system-installed Windows PowerShell 5.1. The script is
    /// passed with -EncodedCommand, so no quoting in it can break out of the
    /// argument string.
    /// </summary>
    public static Task<CommandResult> RunPowerShellAsync(
        string script,
        Action<string, string>? log = null,
        CancellationToken cancellationToken = default,
        Func<string, bool>? outputFilter = null,
        TimeSpan? timeout = null)
    {
        var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        // Modules load only from the Windows PowerShell install folder, so a
        // module of the same name in the user's Documents cannot shadow a cmdlet.
        const string preamble =
            "$env:PSModulePath = Join-Path $PSHOME 'Modules'; $ProgressPreference='SilentlyContinue'; ";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(preamble + script));
        return RunAsync(ps,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand {encoded}",
            log, cancellationToken, outputFilter, timeout: timeout);
    }

    public static string ClassifyOutputLine(string line, string defaultLevel)
    {
        if (line.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || line.Contains("installer failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exit code", StringComparison.OrdinalIgnoreCase)
            || line.Contains("non-zero", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("blocked", StringComparison.OrdinalIgnoreCase)
            || line.Contains("currently in use", StringComparison.OrdinalIgnoreCase))
        {
            return "WARN";
        }

        return defaultLevel;
    }
}

public sealed record CommandResult(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Error)
{
    public IReadOnlyList<string> AllLines => Output.Concat(Error).ToList();
}
