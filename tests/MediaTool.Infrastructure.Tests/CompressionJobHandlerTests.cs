using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class CompressionJobHandlerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MediaToolCompressionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompressesRealVideoToAValidatedMp4UnderItsHardLimit()
    {
        Directory.CreateDirectory(directory);
        var ffmpegDirectory = Path.Combine(FindRepositoryRoot(), ".tools", "ffmpeg");
        var paths = new FfmpegPaths(Path.Combine(ffmpegDirectory, "ffmpeg.exe"), Path.Combine(ffmpegDirectory, "ffprobe.exe"));
        Assert.True(File.Exists(paths.Ffmpeg), "Run scripts/fetch-ffmpeg.ps1 before integration tests.");

        var source = Path.Combine(directory, "source.mp4");
        var runner = new ProcessRunner();
        var generated = await runner.RunAsync(new(paths.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "10", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "1500k", "-c:a", "aac", "-b:a", "128k", source]), CancellationToken.None);
        Assert.Equal(0, generated.ExitCode);

        var inspector = new FfprobeMediaInspector(paths.Ffprobe, runner);
        var request = new CompressionRequest(MediaInput.FromPath(source), new(directory), FileSize.FromMegabytes(1), VideoCodec.H264, MaximumResolution.Auto, AudioTrackMode.Default, []);
        var job = MediaJob.Create(request);
        var reportedPercents = new List<double>();
        var reportedPhases = new List<string>();
        job.Changed += (_, changed) =>
        {
            if (changed.Progress?.Percent is { } percent) reportedPercents.Add(percent.Value);
            if (changed.Progress is { } progress) reportedPhases.Add(progress.Phase);
        };
        var handler = new CompressionJobHandler(paths, runner, inspector, new OutputFileService(), new FfmpegCapabilityDetector(paths, runner));

        var result = await handler.ExecuteAsync(job, CancellationToken.None);

        Assert.Equal(JobState.Completed, result.State);
        var output = Assert.Single(result.Outputs);
        Assert.True(output.Size.Bytes <= request.MaximumSize.Bytes, $"Output was {output.Size.Bytes} bytes.");
        Assert.True(File.Exists(output.Path));
        var inspected = await inspector.InspectAsync(MediaInput.FromPath(output.Path), CancellationToken.None);
        Assert.Contains(inspected.Streams, stream => stream.Kind == MediaStreamKind.Video && stream.Codec == "h264");
        Assert.Contains(inspected.Streams, stream => stream.Kind == MediaStreamKind.Audio);
        Assert.True(Math.Abs(inspected.Duration.Value.TotalSeconds - 10d) < 1d);
        Assert.NotEmpty(reportedPercents);
        Assert.True(reportedPercents.Zip(reportedPercents.Skip(1), (left, right) => right >= left).All(value => value), "Compression progress must never restart or move backward.");
        Assert.All(reportedPercents, percent => Assert.InRange(percent, 0d, 99d));
        Assert.Contains("Creating compressed video", reportedPhases);

        // Reusing the source with a different target must keep the previous version intact.
        var secondRequest = request with { MaximumSize = FileSize.FromMegabytes(2) };
        var secondResult = await handler.ExecuteAsync(MediaJob.Create(secondRequest), CancellationToken.None);
        var secondOutput = Assert.Single(secondResult.Outputs);
        Assert.Equal(JobState.Completed, secondResult.State);
        Assert.Equal(Path.Combine(directory, "source-compressed-2.mp4"), secondOutput.Path);
        Assert.True(File.Exists(output.Path));
        Assert.True(File.Exists(secondOutput.Path));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaTool.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the MediaTool solution root.");
    }
}
