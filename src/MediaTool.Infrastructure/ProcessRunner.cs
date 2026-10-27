using System.Diagnostics;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.ExecutablePath))
            throw new ArgumentException("The executable path must be absolute.", nameof(request));

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory ?? Path.GetDirectoryName(request.ExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new List<string>();
        var stderr = new List<string>();

        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(request.ExecutablePath)}.");
        var stdoutTask = ReadLinesAsync(process.StandardOutput, stdout, request.StandardOutputLine);
        var stderrTask = ReadLinesAsync(process.StandardError, stderr, request.StandardErrorLine);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            return new(process.ExitCode, stdout, stderr, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, List<string> destination, Action<string>? observer)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            destination.Add(line);
            observer?.Invoke(line);
        }
    }
}
