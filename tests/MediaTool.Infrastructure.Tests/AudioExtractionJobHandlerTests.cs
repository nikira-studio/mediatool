using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class AudioExtractionJobHandlerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MediaToolAudioTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExtractsAPlayableMp3FromARealVideo()
    {
        Directory.CreateDirectory(directory);
        var root = FindRepositoryRoot();
        var paths = new FfmpegPaths(Path.Combine(root, ".tools", "ffmpeg", "ffmpeg.exe"), Path.Combine(root, ".tools", "ffmpeg", "ffprobe.exe"));
        var runner = new ProcessRunner();
        var source = Path.Combine(directory, "source.mp4");
        var generated = await runner.RunAsync(new(paths.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-c:a", "aac", source]), CancellationToken.None);
        Assert.Equal(0, generated.ExitCode);

        var inspector = new FfprobeMediaInspector(paths.Ffprobe, runner);
        var request = new AudioExtractionRequest(MediaInput.FromPath(source), new(directory), BitRate.FromKilobitsPerSecond(192), AudioTrackMode.Default, []);
        var result = await new AudioExtractionJobHandler(paths, runner, inspector, new OutputFileService()).ExecuteAsync(MediaJob.Create(request), CancellationToken.None);

        Assert.Equal(JobState.Completed, result.State);
        var output = Assert.Single(result.Outputs);
        Assert.EndsWith(".mp3", output.Path, StringComparison.OrdinalIgnoreCase);
        var inspected = await inspector.InspectAsync(MediaInput.FromPath(output.Path), CancellationToken.None);
        Assert.Contains(inspected.Streams, stream => stream.Kind == MediaStreamKind.Audio && stream.Codec == "mp3");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "MediaTool.slnx"))) return current.FullName;
        throw new DirectoryNotFoundException();
    }
}
