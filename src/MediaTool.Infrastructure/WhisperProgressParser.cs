using System.Globalization;
using System.Text.RegularExpressions;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public static partial class WhisperProgressParser
{
    [GeneratedRegex(@"progress\s*=\s*(\d{1,3})%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProgressPattern();

    public static ProgressPercent? Parse(string line)
    {
        var match = ProgressPattern().Match(line);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)) return null;
        return ProgressPercent.FromPercent(percent);
    }

    public static int RecommendedThreadCount(int logicalProcessorCount) =>
        Math.Clamp((Math.Max(1, logicalProcessorCount) + 1) / 2, 1, 12);

    public static string DescribePhase(ProgressPercent progress, TimeSpan stageElapsed)
    {
        if (progress.Value >= 100) return "Finishing transcript";
        if (progress.Value < 1) return "Transcribing locally · estimating time remaining";
        var remaining = TimeSpan.FromTicks((long)(stageElapsed.Ticks * ((100d - progress.Value) / progress.Value)));
        return $"Transcribing locally · {DescribeRemaining(remaining)}";
    }

    private static string DescribeRemaining(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        if (minutes < 60) return $"about {minutes} min remaining";
        var hours = minutes / 60;
        var extraMinutes = minutes % 60;
        return extraMinutes == 0
            ? $"about {hours} hr remaining"
            : $"about {hours} hr {extraMinutes} min remaining";
    }
}
