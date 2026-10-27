namespace MediaTool.Core;

public readonly record struct FileSize
{
    public long Bytes { get; }

    private FileSize(long bytes) => Bytes = bytes;

    public static FileSize FromBytes(long bytes) => bytes >= 0
        ? new(bytes)
        : throw new ArgumentOutOfRangeException(nameof(bytes));

    public static FileSize FromMegabytes(decimal megabytes) => megabytes >= 0
        ? new(checked((long)(megabytes * 1_000_000m)))
        : throw new ArgumentOutOfRangeException(nameof(megabytes));

    public decimal Megabytes => Bytes / 1_000_000m;
}

public readonly record struct BitRate
{
    public long BitsPerSecond { get; }

    private BitRate(long bitsPerSecond) => BitsPerSecond = bitsPerSecond;

    public static BitRate FromBitsPerSecond(long value) => value >= 0
        ? new(value)
        : throw new ArgumentOutOfRangeException(nameof(value));

    public static BitRate FromKilobitsPerSecond(long value) => value >= 0
        ? new(checked(value * 1_000))
        : throw new ArgumentOutOfRangeException(nameof(value));
}

public readonly record struct MediaDuration
{
    public TimeSpan Value { get; }

    private MediaDuration(TimeSpan value) => Value = value;

    public static MediaDuration FromTimeSpan(TimeSpan value) => value > TimeSpan.Zero
        ? new(value)
        : throw new ArgumentOutOfRangeException(nameof(value), "Media duration must be positive.");
}

public readonly record struct StreamIndex
{
    public int Value { get; }

    public StreamIndex(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
}

public readonly record struct ProgressPercent
{
    public double Value { get; }

    private ProgressPercent(double value) => Value = value;

    public static ProgressPercent FromRatio(double ratio) =>
        new(Math.Clamp(double.IsFinite(ratio) ? ratio * 100d : 0d, 0d, 100d));

    public static ProgressPercent FromPercent(double percent) =>
        new(Math.Clamp(double.IsFinite(percent) ? percent : 0d, 0d, 100d));
}

public sealed record MediaInput
{
    public string FullPath { get; }
    public string FileName => Path.GetFileName(FullPath);

    private MediaInput(string fullPath) => FullPath = fullPath;

    public static MediaInput FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Media paths must be absolute.", nameof(path));
        return new(Path.GetFullPath(path));
    }
}
