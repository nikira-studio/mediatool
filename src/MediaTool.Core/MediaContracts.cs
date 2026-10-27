namespace MediaTool.Core;

public enum ToolId { VideoCompress, AudioExtract, Transcribe }
public enum JobState { Queued, Inspecting, Ready, Processing, Retrying, Cancelling, Cancelled, Completed, Skipped, Interrupted, Failed }
public enum MediaStreamKind { Video, Audio, Subtitle, Attachment, Unknown }
public enum VideoCodec { H264, H265 }
public enum MaximumResolution { Auto, P1080, P720, P480, P360 }
public enum AudioTrackMode { Default, Selected, All, None }
public enum OverwritePolicy { Numbered, ConfirmOverwrite }
public enum TranscriptFormat { Text, Srt, WebVtt }

public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public sealed record OutputDestination(string? FolderPath, OverwritePolicy OverwritePolicy = OverwritePolicy.Numbered);

public sealed record MediaStream(
    StreamIndex Index,
    MediaStreamKind Kind,
    string Codec,
    string? Title,
    string? Language,
    int? Channels,
    int? Width,
    int? Height,
    double? FramesPerSecond,
    BitRate? BitRate,
    bool IsDefault,
    bool IsAttachedPicture);

public sealed record MediaInspection(
    MediaInput Input,
    MediaDuration Duration,
    FileSize FileSize,
    IReadOnlyList<MediaStream> Streams,
    BitRate? OverallBitRate,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record EncoderCapability(VideoCodec Codec, bool IsAvailable, string? EncoderName, string? Detail);

public sealed record WhisperModel(string Id, string DisplayName, FileSize DownloadSize, string Sha256, string FileName, bool EnglishOnly);

public interface IMediaRequest
{
    ToolId Tool { get; }
    MediaInput Input { get; }
    OutputDestination Output { get; }
}

public sealed record CompressionRequest(
    MediaInput Input,
    OutputDestination Output,
    FileSize MaximumSize,
    VideoCodec Codec,
    MaximumResolution MaximumResolution,
    AudioTrackMode AudioTrackMode,
    IReadOnlyList<StreamIndex> SelectedAudioTracks) : IMediaRequest
{
    public ToolId Tool => ToolId.VideoCompress;
}

public sealed record AudioExtractionRequest(
    MediaInput Input,
    OutputDestination Output,
    BitRate Mp3BitRate,
    AudioTrackMode AudioTrackMode,
    IReadOnlyList<StreamIndex> SelectedAudioTracks) : IMediaRequest
{
    public ToolId Tool => ToolId.AudioExtract;
}

public sealed record TranscriptionRequest(
    MediaInput Input,
    OutputDestination Output,
    WhisperModel Model,
    string? Language,
    IReadOnlySet<TranscriptFormat> Formats,
    bool IncludeTimestamps = true) : IMediaRequest
{
    public ToolId Tool => ToolId.Transcribe;
}

public sealed record JobProgress(string Phase, ProgressPercent? Percent, TimeSpan Elapsed);

public sealed record JobError(string Code, string Message, string NextAction, string? TechnicalDetail = null);

public sealed record JobOutput(string Path, FileSize Size, IReadOnlyDictionary<string, string> Facts);

public sealed record JobResult(JobState State, IReadOnlyList<JobOutput> Outputs, JobError? Error = null)
{
    public static JobResult Completed(params JobOutput[] outputs) => new(JobState.Completed, outputs);
    public static JobResult Failed(JobError error) => new(JobState.Failed, [], error);
    public static JobResult Cancelled(JobError error) => new(JobState.Cancelled, [], error);
}
