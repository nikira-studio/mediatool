using System.Security.Cryptography;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed record WhisperPaths(string ExecutablePath)
{
    public static WhisperPaths FromApplicationDirectory(string applicationDirectory) => new(Path.Combine(applicationDirectory, "sidecars", "whisper", "whisper-cli.exe"));
}

public static class WhisperModelCatalog
{
    public static WhisperModel TinyEnglish { get; } = new("tiny.en", "Tiny English (fastest)", FileSize.FromBytes(77_700_000), "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f", "ggml-tiny.en.bin", true);
    public static WhisperModel BaseEnglish { get; } = new("base.en", "Base English (recommended)", FileSize.FromBytes(148_000_000), "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002", "ggml-base.en.bin", true);
    public static WhisperModel SmallEnglish { get; } = new("small.en", "Small English (more accurate)", FileSize.FromBytes(487_600_000), "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d", "ggml-small.en.bin", true);
    public static WhisperModel LargeV3Turbo { get; } = new("large-v3-turbo", "Large v3 Turbo (very slow on CPU)", FileSize.FromBytes(1_624_555_275), "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "ggml-large-v3-turbo.bin", false);
    public static IReadOnlyList<WhisperModel> Available { get; } = [TinyEnglish, BaseEnglish, SmallEnglish, LargeV3Turbo];

    public static Uri GetDownloadUri(WhisperModel model) => new($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{model.FileName}");
}

public sealed record ModelDownloadProgress(long BytesReceived, long? TotalBytes, bool IsVerifying = false)
{
    public double? Percentage => TotalBytes is > 0 ? BytesReceived * 100d / TotalBytes.Value : null;
}

public sealed class WhisperModelManager(LocalDataPaths paths)
{
    private readonly SemaphoreSlim downloadGate = new(1, 1);

    public string GetModelPath(WhisperModel model) => Path.Combine(paths.ModelsDirectory, model.FileName);

    public async Task ImportAsync(WhisperModel model, string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected model file does not exist.", sourcePath);
        paths.EnsureDirectories();
        var target = GetModelPath(model);
        var temporary = target + ".importing";
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(model, temporary, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public async Task DownloadAsync(WhisperModel model, Uri source, CancellationToken cancellationToken, IProgress<ModelDownloadProgress>? progress = null)
    {
        await downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureDirectories();
            var target = GetModelPath(model);
            if (File.Exists(target)) return;
            var temporary = target + ".downloading";
            using var client = new HttpClient();
            try
            {
                using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength;
                await using var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                {
                    await using var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
                    var buffer = new byte[1024 * 128];
                    long received = 0;
                    int read;
                    while ((read = await network.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new(received, totalBytes));
                    }
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                progress?.Report(new(new FileInfo(temporary).Length, totalBytes, true));
                await VerifyAsync(model, temporary, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, target, true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }
        finally { downloadGate.Release(); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async Task VerifyAsync(WhisperModel model, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The model checksum does not match the pinned value.");
    }

    public void Delete(WhisperModel model)
    {
        var path = GetModelPath(model);
        if (File.Exists(path)) File.Delete(path);
    }
}

public static class TimedTranscriptFormatter
{
    public static string FromSrt(string srt)
    {
        var blocks = srt.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        foreach (var block in blocks)
        {
            var parts = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var timeIndex = Array.FindIndex(parts, part => part.Contains("-->", StringComparison.Ordinal));
            if (timeIndex < 0 || timeIndex + 1 >= parts.Length) continue;
            var start = parts[timeIndex].Split("-->", StringSplitOptions.TrimEntries)[0].Replace(',', '.');
            var dot = start.IndexOf('.');
            if (dot >= 0) start = start[..dot];
            lines.Add($"[{start}] {string.Join(' ', parts.Skip(timeIndex + 1))}");
        }
        return string.Join(Environment.NewLine + Environment.NewLine, lines) + (lines.Count == 0 ? string.Empty : Environment.NewLine);
    }
}

public sealed class TranscriptionJobHandler(
    FfmpegPaths ffmpeg,
    WhisperPaths whisper,
    LocalDataPaths localData,
    IProcessRunner runner,
    IMediaInspector inspector,
    IOutputFileService outputs) : IJobHandler
{
    public bool CanHandle(ToolId tool) => tool == ToolId.Transcribe;

    public async Task<JobResult> ExecuteAsync(MediaJob job, CancellationToken cancellationToken)
    {
        if (job.Request is not TranscriptionRequest request) throw new ArgumentException("Transcription handler received a different request type.", nameof(job));
        var modelPath = Path.Combine(localData.ModelsDirectory, request.Model.FileName);
        var wavPath = Path.Combine(localData.TemporaryDirectory, $"{job.Id}.wav");
        var generatedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(whisper.ExecutablePath)) return JobResult.Failed(new("engine_missing", "The local transcription engine is missing.", "Repair or reinstall MediaTool."));
            if (!File.Exists(modelPath)) return JobResult.Failed(new("model_missing", $"The {request.Model.DisplayName} model is not installed.", "Download or import the model in Transcribe."));
            localData.EnsureDirectories();
            job.TransitionTo(JobState.Inspecting);
            var inspection = await inspector.InspectAsync(request.Input, cancellationToken).ConfigureAwait(false);
            if (!inspection.Streams.Any(stream => stream.Kind == MediaStreamKind.Audio)) return JobResult.Failed(new("no_audio_stream", "The file has no audio stream.", "Choose media with audible speech."));
            job.TransitionTo(JobState.Ready);
            job.TransitionTo(JobState.Processing);
            job.Report(new("Preparing audio", null, TimeSpan.Zero));
            var convert = await runner.RunAsync(new(ffmpeg.Ffmpeg,
                ["-hide_banner", "-nostdin", "-y", "-i", request.Input.FullPath, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wavPath]), cancellationToken).ConfigureAwait(false);
            if (convert.ExitCode != 0) return JobResult.Failed(new("engine_failure", "MediaTool could not prepare audio for transcription.", "Open details, then retry.", string.Join(Environment.NewLine, convert.StandardError)));

            var directory = request.Output.FolderPath ?? Path.GetDirectoryName(request.Input.FullPath)!;
            if (!Directory.Exists(directory)) return JobResult.Failed(new("output_not_writable", "The output folder does not exist.", "Choose another output folder."));
            var prefix = Path.Combine(directory, $".{job.Id}-transcript");
            var arguments = new List<string>
            {
                "-m", modelPath,
                "-f", wavPath,
                "-of", prefix,
                "-np", "-pp",
                "-t", WhisperProgressParser.RecommendedThreadCount(Environment.ProcessorCount).ToString()
            };
            if (!request.Model.EnglishOnly) arguments.AddRange(["-l", request.Language ?? "auto"]);
            var timestampedText = request.IncludeTimestamps && request.Formats.Contains(TranscriptFormat.Text);
            if (request.Formats.Contains(TranscriptFormat.Text) && !timestampedText) { arguments.Add("-otxt"); generatedPaths.Add(prefix + ".txt"); }
            if (request.Formats.Contains(TranscriptFormat.Srt) || timestampedText) { arguments.Add("-osrt"); generatedPaths.Add(prefix + ".srt"); }
            if (request.Formats.Contains(TranscriptFormat.WebVtt)) { arguments.Add("-ovtt"); generatedPaths.Add(prefix + ".vtt"); }
            var transcriptionStarted = DateTimeOffset.UtcNow;
            job.Report(new("Transcribing locally · estimating time remaining", ProgressPercent.FromPercent(0), transcriptionStarted - (job.StartedAt ?? job.CreatedAt)));
            var transcribed = await runner.RunAsync(new(whisper.ExecutablePath, arguments, StandardErrorLine: line =>
            {
                var progress = WhisperProgressParser.Parse(line);
                if (progress is null) return;
                var now = DateTimeOffset.UtcNow;
                job.Report(new(WhisperProgressParser.DescribePhase(progress.Value, now - transcriptionStarted), progress, now - (job.StartedAt ?? job.CreatedAt)));
            }), cancellationToken).ConfigureAwait(false);
            if (transcribed.ExitCode != 0) return JobResult.Failed(new("engine_failure", "The local transcription engine could not finish.", "Try a shorter file or another model.", string.Join(Environment.NewLine, transcribed.StandardError)));

            if (timestampedText)
            {
                var srtPath = prefix + ".srt";
                if (!File.Exists(srtPath)) return JobResult.Failed(new("engine_failure", "The transcription engine did not create timestamp data.", "Try the job again."));
                var textPath = prefix + ".txt";
                await File.WriteAllTextAsync(textPath, TimedTranscriptFormatter.FromSrt(await File.ReadAllTextAsync(srtPath, cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
                generatedPaths.Add(textPath);
            }

            var results = new List<JobOutput>();
            foreach (var format in new[] { TranscriptFormat.Text, TranscriptFormat.Srt, TranscriptFormat.WebVtt })
            {
                if (!request.Formats.Contains(format)) continue;
                var extension = format switch { TranscriptFormat.Text => ".txt", TranscriptFormat.Srt => ".srt", TranscriptFormat.WebVtt => ".vtt", _ => throw new ArgumentOutOfRangeException() };
                var generated = prefix + extension;
                if (!File.Exists(generated)) return JobResult.Failed(new("engine_failure", "The transcription engine did not create every requested format.", "Try the job again."));
                var final = outputs.GetAvailablePath(directory, $"{Path.GetFileNameWithoutExtension(request.Input.FileName)}-transcript{Path.GetExtension(generated)}");
                outputs.Finalize(generated, final);
                generatedPaths.Remove(generated);
                results.Add(new(final, FileSize.FromBytes(new FileInfo(final).Length), new Dictionary<string, string> { ["format"] = Path.GetExtension(final).TrimStart('.'), ["timestamps"] = request.IncludeTimestamps.ToString() }));
            }
            return JobResult.Completed(results.ToArray());
        }
        catch (MediaInspectionException exception)
        {
            return JobResult.Failed(new(exception.Code, exception.Message, "Choose a readable media file.", exception.Detail));
        }
        finally
        {
            if (File.Exists(wavPath)) File.Delete(wavPath);
            foreach (var generated in generatedPaths) outputs.DeleteTemporary(generated);
        }
    }
}
