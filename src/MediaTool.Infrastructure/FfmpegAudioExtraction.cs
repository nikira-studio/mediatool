using System.Globalization;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public static class AudioExtractionArgumentBuilder
{
    public static IReadOnlyList<string> Build(AudioExtractionRequest request, MediaStream stream, string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-y", "-i", request.Input.FullPath,
            "-map", $"0:{stream.Index.Value}", "-vn", "-c:a", "libmp3lame",
            "-b:a", request.Mp3BitRate.BitsPerSecond.ToString(CultureInfo.InvariantCulture),
            "-map_metadata", "0"
        };
        AddMetadata(arguments, "title", stream.Title);
        AddMetadata(arguments, "language", stream.Language);
        arguments.AddRange(["-progress", "pipe:1", "-f", "mp3", outputPath]);
        return arguments;
    }

    private static void AddMetadata(List<string> arguments, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) arguments.AddRange(["-metadata", $"{key}={value}"]);
    }
}

public sealed class AudioExtractionJobHandler(FfmpegPaths paths, IProcessRunner runner, IMediaInspector inspector, IOutputFileService outputs) : IJobHandler
{
    public bool CanHandle(ToolId tool) => tool == ToolId.AudioExtract;

    public async Task<JobResult> ExecuteAsync(MediaJob job, CancellationToken cancellationToken)
    {
        if (job.Request is not AudioExtractionRequest request) throw new ArgumentException("Audio handler received a different request type.", nameof(job));
        var temporaryPaths = new List<string>();
        try
        {
            job.TransitionTo(JobState.Inspecting);
            var inspection = await inspector.InspectAsync(request.Input, cancellationToken).ConfigureAwait(false);
            var selected = SelectStreams(request, inspection.Streams);
            if (selected.Count == 0) return JobResult.Failed(new("no_audio_stream", "The file has no selected audio stream.", "Choose a file with audio or change the track selection."));
            job.TransitionTo(JobState.Ready);
            var directory = request.Output.FolderPath ?? Path.GetDirectoryName(request.Input.FullPath)!;
            if (!Directory.Exists(directory)) return JobResult.Failed(new("output_not_writable", "The output folder does not exist.", "Choose another output folder."));
            EnsureDiskSpace(directory, selected.Count, inspection.FileSize);
            job.TransitionTo(JobState.Processing);

            var results = new List<JobOutput>();
            for (var index = 0; index < selected.Count; index++)
            {
                var stream = selected[index];
                var fileName = GetOutputFileName(request.Input.FileName, stream, selected.Count > 1);
                var finalPath = outputs.GetAvailablePath(directory, fileName);
                var temporaryPath = outputs.CreateTemporaryPath(finalPath, job.Id);
                temporaryPaths.Add(temporaryPath);
                var parser = new FfmpegProgressParser(inspection.Duration);
                var run = await runner.RunAsync(new(paths.Ffmpeg, AudioExtractionArgumentBuilder.Build(request, stream, temporaryPath), StandardOutputLine: line =>
                {
                    if (parser.Parse(line) is { } percent) job.Report(new("Extracting audio", percent, DateTimeOffset.UtcNow - (job.StartedAt ?? job.CreatedAt)));
                }), cancellationToken).ConfigureAwait(false);
                if (run.ExitCode != 0) return JobResult.Failed(new("encoder_failure", "FFmpeg could not extract the audio.", "Open details, then retry.", DiagnosticsRedactor.Redact(string.Join(Environment.NewLine, run.StandardError), false)));

                var outputInspection = await inspector.InspectAsync(MediaInput.FromPath(temporaryPath), cancellationToken).ConfigureAwait(false);
                if (!outputInspection.Streams.Any(candidate => candidate.Kind == MediaStreamKind.Audio)) throw new IOException("Extracted output has no audio stream.");
                outputs.Finalize(temporaryPath, finalPath);
                temporaryPaths.Remove(temporaryPath);
                results.Add(new(finalPath, FileSize.FromBytes(new FileInfo(finalPath).Length), new Dictionary<string, string>
                {
                    ["track"] = stream.Index.Value.ToString(CultureInfo.InvariantCulture), ["codec"] = "mp3",
                    ["channels"] = (stream.Channels ?? 0).ToString(CultureInfo.InvariantCulture), ["duration"] = inspection.Duration.Value.ToString()
                }));
            }
            return JobResult.Completed(results.ToArray());
        }
        catch (MediaInspectionException exception)
        {
            return JobResult.Failed(new(exception.Code, exception.Message, "Choose a readable media file.", exception.Detail));
        }
        catch (IOException exception)
        {
            return JobResult.Failed(new("output_not_writable", "MediaTool could not write the MP3 output.", "Check free space and choose another folder.", DiagnosticsRedactor.Redact(exception.ToString(), false)));
        }
        finally
        {
            foreach (var path in temporaryPaths) outputs.DeleteTemporary(path);
        }
    }

    private static IReadOnlyList<MediaStream> SelectStreams(AudioExtractionRequest request, IReadOnlyList<MediaStream> streams)
    {
        var audio = streams.Where(stream => stream.Kind == MediaStreamKind.Audio).ToArray();
        if (request.AudioTrackMode == AudioTrackMode.Default)
        {
            var chosen = audio.FirstOrDefault(stream => stream.IsDefault) ?? audio.FirstOrDefault();
            return chosen is null ? [] : [chosen];
        }
        return request.AudioTrackMode switch
        {
            AudioTrackMode.All => audio,
            AudioTrackMode.Selected => audio.Where(stream => request.SelectedAudioTracks.Contains(stream.Index)).ToArray(),
            _ => []
        };
    }

    private static string GetOutputFileName(string inputFileName, MediaStream stream, bool multiple) =>
        multiple ? $"{Path.GetFileNameWithoutExtension(inputFileName)}-track-{stream.Index.Value}{(string.IsNullOrWhiteSpace(stream.Language) ? string.Empty : $"-{stream.Language}")}.mp3" : $"{Path.GetFileNameWithoutExtension(inputFileName)}.mp3";

    private static void EnsureDiskSpace(string directory, int tracks, FileSize source)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory))!;
        var minimum = Math.Max(source.Bytes / 4, 1_000_000) * tracks;
        if (new DriveInfo(root).AvailableFreeSpace < minimum) throw new IOException("Not enough free disk space for extracted audio.");
    }
}
