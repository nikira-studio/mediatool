namespace MediaTool.Core;

public sealed record AutoResolutionRung(BitRate MinimumVideoBitRate, int MaximumHeight, double MaximumFramesPerSecond);

public sealed record RetainedAudioTrack(StreamIndex Index, BitRate BitRate, int Channels);

public sealed record CompressionPlan(
    BitRate VideoBitRate,
    IReadOnlyList<RetainedAudioTrack> AudioTracks,
    int Width,
    int Height,
    double FramesPerSecond,
    bool WarnAboutLowQuality,
    FileSize SafeTarget);

public sealed class CompressionPlanningException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class CompressionPlanner
{
    public const decimal SafeBudgetRatio = 0.93m;
    public const int MaximumAttempts = 3;
    public const long MinimumVideoBitsPerSecond = 12_000;
    public static IReadOnlyList<AutoResolutionRung> AutoResolutionLadder { get; } =
    [
        new(BitRate.FromKilobitsPerSecond(2_500), 1080, 30),
        new(BitRate.FromKilobitsPerSecond(1_200), 720, 30),
        new(BitRate.FromKilobitsPerSecond(600), 480, 30),
        new(BitRate.FromKilobitsPerSecond(0), 360, 24)
    ];

    public static FileSize GetSafeTarget(FileSize hardTarget) =>
        FileSize.FromBytes((long)Math.Floor(hardTarget.Bytes * SafeBudgetRatio));

    public static CompressionPlan Create(CompressionRequest request, MediaInspection inspection)
    {
        var video = inspection.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video && !stream.IsAttachedPicture)
            ?? throw new CompressionPlanningException("no_video_stream", "The file has no usable video stream.");
        if (video.Width is null or <= 0 || video.Height is null or <= 0)
            throw new CompressionPlanningException("invalid_video_dimensions", "The video dimensions are missing or invalid.");

        var audioStreams = SelectAudioStreams(request, inspection.Streams);
        var safeTarget = GetSafeTarget(request.MaximumSize);
        var totalBitsPerSecond = (long)Math.Floor(safeTarget.Bytes * 8d / inspection.Duration.Value.TotalSeconds);
        var audioTracks = BudgetAudio(audioStreams, totalBitsPerSecond);
        var audioBitsPerSecond = audioTracks.Sum(track => track.BitRate.BitsPerSecond);
        var videoBitsPerSecond = totalBitsPerSecond - audioBitsPerSecond;

        if (videoBitsPerSecond < MinimumVideoBitsPerSecond)
            throw new CompressionPlanningException("target_too_small", "The target is too small for this video's duration, even at the lowest usable quality. Choose a larger target size or remove audio.");
        videoBitsPerSecond = Math.Min(videoBitsPerSecond, 20_000_000);
        if (video.BitRate is { BitsPerSecond: > 0 } sourceBitRate) videoBitsPerSecond = Math.Min(videoBitsPerSecond, sourceBitRate.BitsPerSecond);

        var videoBitRate = BitRate.FromBitsPerSecond(videoBitsPerSecond);
        var rung = GetAutoRung(videoBitRate);
        var heightLimit = request.MaximumResolution == MaximumResolution.Auto ? rung.MaximumHeight : ToHeight(request.MaximumResolution);
        var frameLimit = request.MaximumResolution == MaximumResolution.Auto ? rung.MaximumFramesPerSecond : 30d;
        var (width, height) = ScaleToMaximumHeight(video.Width.Value, video.Height.Value, heightLimit);
        var sourceFrameRate = video.FramesPerSecond is > 0 ? video.FramesPerSecond.Value : frameLimit;

        return new(videoBitRate, audioTracks, width, height, Math.Min(sourceFrameRate, frameLimit), videoBitsPerSecond < 300_000, safeTarget);
    }

    public static AutoResolutionRung GetAutoRung(BitRate videoBitRate) =>
        AutoResolutionLadder.First(rung => videoBitRate.BitsPerSecond >= rung.MinimumVideoBitRate.BitsPerSecond);

    public static BitRate GetRetryVideoBitRate(BitRate previous, FileSize safeTarget, FileSize actualOutput)
    {
        if (actualOutput.Bytes <= 0) throw new ArgumentOutOfRangeException(nameof(actualOutput));
        var correction = safeTarget.Bytes / (double)actualOutput.Bytes;
        return BitRate.FromBitsPerSecond(Math.Max(MinimumVideoBitsPerSecond, (long)Math.Floor(previous.BitsPerSecond * correction * 0.98d)));
    }

    public static (int Width, int Height) ScaleToMaximumHeight(int width, int height, int maximumHeight)
    {
        if (width <= 0 || height <= 0 || maximumHeight <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= maximumHeight) return (MakeEven(width), MakeEven(height));
        var targetHeight = MakeEven(maximumHeight);
        var targetWidth = MakeEven((int)Math.Floor(width * (targetHeight / (double)height)));
        return (Math.Max(2, targetWidth), Math.Max(2, targetHeight));
    }

    private static IReadOnlyList<MediaStream> SelectAudioStreams(CompressionRequest request, IReadOnlyList<MediaStream> streams)
    {
        var audio = streams.Where(stream => stream.Kind == MediaStreamKind.Audio).ToArray();
        if (request.AudioTrackMode == AudioTrackMode.Default)
        {
            var selected = audio.FirstOrDefault(stream => stream.IsDefault) ?? audio.FirstOrDefault();
            return selected is null ? [] : [selected];
        }
        return request.AudioTrackMode switch
        {
            AudioTrackMode.None => [],
            AudioTrackMode.All => audio,
            AudioTrackMode.Selected => audio.Where(stream => request.SelectedAudioTracks.Contains(stream.Index)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(request.AudioTrackMode))
        };
    }

    private static IReadOnlyList<RetainedAudioTrack> BudgetAudio(IReadOnlyList<MediaStream> streams, long totalBitsPerSecond)
    {
        if (streams.Count == 0) return [];
        var bitrate = totalBitsPerSecond switch
        {
            >= 428_000 => 128_000L,
            >= 300_000 => 96_000L,
            >= 160_000 => 64_000L,
            >= 90_000 => 32_000L,
            _ => 16_000L
        };
        // Keep a small, usable video budget. Multiple selected tracks share the remaining budget.
        bitrate = Math.Min(bitrate, Math.Max(16_000L, (totalBitsPerSecond - MinimumVideoBitsPerSecond) / streams.Count));
        return streams.Select(stream => new RetainedAudioTrack(stream.Index, BitRate.FromBitsPerSecond(bitrate), stream.Channels ?? 2)).ToArray();
    }

    private static int ToHeight(MaximumResolution resolution) => resolution switch
    {
        MaximumResolution.P1080 => 1080,
        MaximumResolution.P720 => 720,
        MaximumResolution.P480 => 480,
        MaximumResolution.P360 => 360,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution))
    };

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;
}
