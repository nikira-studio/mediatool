using System.Globalization;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed record FfmpegPaths(string Ffmpeg, string Ffprobe)
{
    public static FfmpegPaths FromApplicationDirectory(string applicationDirectory) => new(
        Path.Combine(applicationDirectory, "sidecars", "ffmpeg.exe"),
        Path.Combine(applicationDirectory, "sidecars", "ffprobe.exe"));
}

public sealed class FfmpegCapabilityDetector(FfmpegPaths paths, IProcessRunner runner)
{
    public async Task<IReadOnlyList<EncoderCapability>> DetectAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new(paths.Ffmpeg, ["-hide_banner", "-encoders"]), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException("FFmpeg encoder detection failed.");
        var output = string.Join('\n', result.StandardOutput);
        return
        [
            new(VideoCodec.H264, output.Contains("libx264", StringComparison.Ordinal), "libx264", null),
            new(VideoCodec.H265, output.Contains("libx265", StringComparison.Ordinal), "libx265", null)
        ];
    }
}

public static class CompressionArgumentBuilder
{
    public static IReadOnlyList<string> Build(
        CompressionRequest request,
        MediaInspection inspection,
        CompressionPlan plan,
        BitRate videoBitRate,
        string outputPath)
    {
        var video = inspection.Streams.First(stream => stream.Kind == MediaStreamKind.Video && !stream.IsAttachedPicture);
        var encoder = request.Codec == VideoCodec.H264 ? "libx264" : "libx265";
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-y", "-i", request.Input.FullPath,
            "-map", $"0:{video.Index.Value}",
            "-c:v", encoder,
            "-preset", "medium",
            "-b:v", videoBitRate.BitsPerSecond.ToString(CultureInfo.InvariantCulture),
            "-maxrate", videoBitRate.BitsPerSecond.ToString(CultureInfo.InvariantCulture),
            "-bufsize", checked(videoBitRate.BitsPerSecond * 2).ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-vf", $"scale={plan.Width}:{plan.Height}:flags=lanczos,fps={plan.FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture)}",
            "-progress", "pipe:1"
        };
        if (request.Codec == VideoCodec.H264) arguments.AddRange(["-profile:v", "high"]);

        foreach (var track in plan.AudioTracks) arguments.AddRange(["-map", $"0:{track.Index.Value}"]);
        if (plan.AudioTracks.Count == 0) arguments.Add("-an");
        else
        {
            arguments.AddRange(["-c:a", "aac"]);
            for (var index = 0; index < plan.AudioTracks.Count; index++)
                arguments.AddRange([$"-b:a:{index}", plan.AudioTracks[index].BitRate.BitsPerSecond.ToString(CultureInfo.InvariantCulture)]);
        }
        arguments.AddRange(["-movflags", "+faststart", "-f", "mp4", outputPath]);
        return arguments;
    }
}

public sealed class CompressionJobHandler(
    FfmpegPaths paths,
    IProcessRunner runner,
    IMediaInspector inspector,
    IOutputFileService outputs,
    FfmpegCapabilityDetector capabilities) : IJobHandler
{
    public bool CanHandle(ToolId tool) => tool == ToolId.VideoCompress;

    public async Task<JobResult> ExecuteAsync(MediaJob job, CancellationToken cancellationToken)
    {
        if (job.Request is not CompressionRequest request) throw new ArgumentException("Compression handler received a different request type.", nameof(job));
        string? temporaryPath = null;
        try
        {
            job.TransitionTo(JobState.Inspecting);
            var inspection = await inspector.InspectAsync(request.Input, cancellationToken).ConfigureAwait(false);
            job.TransitionTo(JobState.Ready);
            var safeTarget = CompressionPlanner.GetSafeTarget(request.MaximumSize);
            if (inspection.FileSize.Bytes <= safeTarget.Bytes)
                return new(JobState.Skipped, [], null);

            var available = await capabilities.DetectAsync(cancellationToken).ConfigureAwait(false);
            if (!available.Any(capability => capability.Codec == request.Codec && capability.IsAvailable))
                return JobResult.Failed(new("encoder_unavailable", $"The {request.Codec} encoder is unavailable.", "Choose another available codec."));

            var plan = CompressionPlanner.Create(request, inspection);
            var directory = request.Output.FolderPath ?? Path.GetDirectoryName(request.Input.FullPath)!;
            if (!Directory.Exists(directory)) return JobResult.Failed(new("output_not_writable", "The output folder does not exist.", "Choose another output folder."));
            EnsureDiskSpace(directory, request.MaximumSize);
            var finalPath = outputs.GetAvailablePath(directory, $"{Path.GetFileNameWithoutExtension(request.Input.FullPath)}-compressed.mp4");
            temporaryPath = outputs.CreateTemporaryPath(finalPath, job.Id);
            var videoBitRate = plan.VideoBitRate;

            for (var attempt = 1; attempt <= CompressionPlanner.MaximumAttempts; attempt++)
            {
                job.TransitionTo(JobState.Processing);
                var parser = new FfmpegProgressParser(inspection.Duration);
                var encode = await RunEncodeAsync(request, inspection, plan, videoBitRate, temporaryPath, attempt, parser, job, cancellationToken).ConfigureAwait(false);
                if (encode.ExitCode != 0)
                    return JobResult.Failed(new("encoder_failure", "FFmpeg could not encode the video.", "Open details, then try another file or codec.", DiagnosticsRedactor.Redact(string.Join(Environment.NewLine, encode.StandardError), false)));

                job.Report(new("Checking the finished file", ProgressPercent.FromPercent(AttemptEndPercent(attempt)), DateTimeOffset.UtcNow - (job.StartedAt ?? job.CreatedAt)));
                var measured = FileSize.FromBytes(new FileInfo(temporaryPath).Length);
                if (measured.Bytes <= request.MaximumSize.Bytes)
                {
                    var outputInspection = await inspector.InspectAsync(MediaInput.FromPath(temporaryPath), cancellationToken).ConfigureAwait(false);
                    ValidateOutput(inspection, outputInspection);
                    outputs.Finalize(temporaryPath, finalPath);
                    temporaryPath = null;
                    var facts = new Dictionary<string, string>
                    {
                        ["codec"] = request.Codec.ToString(), ["dimensions"] = $"{plan.Width}x{plan.Height}",
                        ["frameRate"] = plan.FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture), ["attempts"] = attempt.ToString(CultureInfo.InvariantCulture)
                    };
                    return JobResult.Completed(new JobOutput(finalPath, measured, facts));
                }

                outputs.DeleteTemporary(temporaryPath);
                if (attempt == CompressionPlanner.MaximumAttempts)
                    return JobResult.Failed(new("target_not_reached", "The encoded video remained over the requested size.", "Choose a larger target or lower maximum resolution."));
                videoBitRate = CompressionPlanner.GetRetryVideoBitRate(videoBitRate, safeTarget, measured);
                job.TransitionTo(JobState.Retrying);
                var retryMessage = attempt == 1
                    ? "The first result was too large — making a smaller correction"
                    : "Still slightly over the limit — making a final correction";
                job.Report(new(retryMessage, ProgressPercent.FromPercent(AttemptEndPercent(attempt)), DateTimeOffset.UtcNow - (job.StartedAt ?? job.CreatedAt)));
            }
            throw new InvalidOperationException("Compression attempt loop ended unexpectedly.");
        }
        catch (MediaInspectionException exception)
        {
            return JobResult.Failed(new(exception.Code, exception.Message, "Choose a readable media file.", exception.Detail));
        }
        catch (CompressionPlanningException exception)
        {
            return JobResult.Failed(new(exception.Code, exception.Message, "Increase the target size or remove audio tracks."));
        }
        catch (IOException exception)
        {
            return JobResult.Failed(new("output_not_writable", "MediaTool could not write the output.", "Check free space and choose another folder.", DiagnosticsRedactor.Redact(exception.ToString(), false)));
        }
        finally
        {
            if (temporaryPath is not null) outputs.DeleteTemporary(temporaryPath);
        }
    }

    private async Task<ProcessRunResult> RunEncodeAsync(CompressionRequest request, MediaInspection inspection, CompressionPlan plan, BitRate videoBitRate,
        string temporaryPath, int attempt, FfmpegProgressParser parser, MediaJob job, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new(paths.Ffmpeg,
            CompressionArgumentBuilder.Build(request, inspection, plan, videoBitRate, temporaryPath),
            StandardOutputLine: line =>
            {
                if (parser.Parse(line) is not { } encodePercent) return;
                var elapsed = DateTimeOffset.UtcNow - (job.StartedAt ?? job.CreatedAt);
                if (attempt == 1)
                    job.Report(new("Creating compressed video", ProgressPercent.FromPercent(encodePercent.Value * 0.95d), elapsed));
                else
                    job.Report(new($"Final size adjustment — {encodePercent.Value:0}%", null, elapsed));
            }), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static double AttemptEndPercent(int attempt) => attempt switch
    {
        1 => 95d,
        2 => 98d,
        3 => 99d,
        _ => throw new ArgumentOutOfRangeException(nameof(attempt))
    };

    private static void ValidateOutput(MediaInspection source, MediaInspection output)
    {
        if (!output.Streams.Any(stream => stream.Kind == MediaStreamKind.Video && !stream.IsAttachedPicture)) throw new IOException("Encoded output has no video stream.");
        var tolerance = Math.Max(1d, source.Duration.Value.TotalSeconds * 0.02d);
        if (Math.Abs(source.Duration.Value.TotalSeconds - output.Duration.Value.TotalSeconds) > tolerance) throw new IOException("Encoded output duration differs from the source.");
    }

    private static void EnsureDiskSpace(string directory, FileSize target)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory))!;
        if (new DriveInfo(root).AvailableFreeSpace < target.Bytes * 2) throw new IOException("Not enough free disk space for the encode passes.");
    }

}
