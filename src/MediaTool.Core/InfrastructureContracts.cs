namespace MediaTool.Core;

public sealed record ProcessRunRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    Action<string>? StandardOutputLine = null,
    Action<string>? StandardErrorLine = null);

public sealed record ProcessRunResult(int ExitCode, IReadOnlyList<string> StandardOutput, IReadOnlyList<string> StandardError, TimeSpan Elapsed);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);
}

public interface IMediaInspector
{
    Task<MediaInspection> InspectAsync(MediaInput input, CancellationToken cancellationToken);
}

public interface IOutputFileService
{
    string GetAvailablePath(string directory, string fileName);
    string CreateTemporaryPath(string finalPath, JobId jobId);
    void Finalize(string temporaryPath, string finalPath);
    void DeleteTemporary(string temporaryPath);
}
