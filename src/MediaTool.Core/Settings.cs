namespace MediaTool.Core;

public enum AppTheme { System, Light, Dark }
public enum CompletionAction { StayInApp, OpenResult, ShowInFolder }

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized);

public sealed record GlobalSettings(AppTheme Theme, bool DetailedLogging, WindowPlacement? WindowPlacement)
{
    public static GlobalSettings Default { get; } = new(AppTheme.System, false, null);
}

public sealed record CompressionDefaults(decimal MaximumMegabytes, VideoCodec Codec, MaximumResolution MaximumResolution, AudioTrackMode AudioTrackMode, string? OutputFolderPath = null, CompletionAction CompletionAction = CompletionAction.StayInApp)
{
    public static CompressionDefaults Default { get; } = new(20m, VideoCodec.H264, MaximumResolution.Auto, AudioTrackMode.Default);
}

public sealed record AudioExtractionDefaults(long Mp3KilobitsPerSecond, AudioTrackMode AudioTrackMode, string? OutputFolderPath = null, CompletionAction CompletionAction = CompletionAction.StayInApp)
{
    public static AudioExtractionDefaults Default { get; } = new(192, AudioTrackMode.Default);
}

public sealed record TranscriptionDefaults(string ModelId, IReadOnlyList<TranscriptFormat> Formats, string Language = "auto", string? OutputFolderPath = null, bool IncludeTimestamps = true, CompletionAction CompletionAction = CompletionAction.StayInApp)
{
    public static TranscriptionDefaults Default { get; } = new("base.en", [TranscriptFormat.Text]);
}

public sealed record AppSettings(
    GlobalSettings Global,
    CompressionDefaults Compression,
    AudioExtractionDefaults AudioExtraction,
    TranscriptionDefaults Transcription)
{
    public static AppSettings Default { get; } = new(
        GlobalSettings.Default,
        CompressionDefaults.Default,
        AudioExtractionDefaults.Default,
        TranscriptionDefaults.Default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default);
}

public sealed record JobHistoryRecord(
    JobId JobId,
    ToolId Tool,
    string SourceFileName,
    JobState Result,
    string? OutputPath,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? ErrorCode);

public interface IJobHistoryStore
{
    Task<IReadOnlyList<JobHistoryRecord>> LoadAsync(CancellationToken cancellationToken = default);
    Task AppendAsync(JobHistoryRecord record, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
