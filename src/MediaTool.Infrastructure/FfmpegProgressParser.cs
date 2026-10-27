using System.Globalization;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed class FfmpegProgressParser(MediaDuration duration)
{
    public ProgressPercent? Parse(string line)
    {
        if (!line.StartsWith("out_time_us=", StringComparison.Ordinal)) return null;
        var value = line["out_time_us=".Length..];
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds) || microseconds < 0) return null;
        return ProgressPercent.FromRatio(TimeSpan.FromMicroseconds(microseconds).TotalSeconds / duration.Value.TotalSeconds);
    }
}
