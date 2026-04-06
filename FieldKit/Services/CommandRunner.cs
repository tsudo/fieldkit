using System.Diagnostics;

namespace FieldKit.Services;

public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        Action<string, string>? log = null,
        CancellationToken cancellationToken = default,
        Func<string, bool>? outputFilter = null)
    {
        var output = new List<string>();
        var error = new List<string>();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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

            output.Add(e.Data);
            if (outputFilter?.Invoke(e.Data) ?? true)
                log?.Invoke(e.Data, "INFO");
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
                return;
            }

            error.Add(e.Data);
            if (outputFilter?.Invoke(e.Data) ?? true)
                log?.Invoke(e.Data, "WARN");
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process: {fileName}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutDone.Task, stderrDone.Task);

        return new CommandResult(process.ExitCode, output, error);
    }
}

public sealed record CommandResult(int ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Error)
{
    public IReadOnlyList<string> AllLines => Output.Concat(Error).ToList();
}
