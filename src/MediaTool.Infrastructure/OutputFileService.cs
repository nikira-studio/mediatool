using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed class OutputFileService : IOutputFileService
{
    public string GetAvailablePath(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var fullDirectory = Path.GetFullPath(directory);
        var cleanFileName = Path.GetFileName(fileName);
        var candidate = Path.Combine(fullDirectory, cleanFileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(cleanFileName);
        var extension = Path.GetExtension(cleanFileName);
        for (var number = 2; number < int.MaxValue; number++)
        {
            candidate = Path.Combine(fullDirectory, $"{stem}-{number}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("No conflict-free output name is available.");
    }

    public string CreateTemporaryPath(string finalPath, JobId jobId)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(finalPath)) ?? throw new ArgumentException("Output path has no directory.", nameof(finalPath));
        return Path.Combine(directory, $".{Path.GetFileName(finalPath)}.{jobId}.partial");
    }

    public void Finalize(string temporaryPath, string finalPath)
    {
        if (!File.Exists(temporaryPath)) throw new FileNotFoundException("The validated temporary output is missing.", temporaryPath);
        if (File.Exists(finalPath) || Directory.Exists(finalPath)) throw new IOException("The destination now exists. Nothing was replaced.");
        File.Move(temporaryPath, finalPath);
    }

    public void DeleteTemporary(string temporaryPath)
    {
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
    }
}
