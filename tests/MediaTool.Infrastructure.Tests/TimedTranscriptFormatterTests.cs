using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class TimedTranscriptFormatterTests
{
    [Fact]
    public void Converts_srt_into_readable_timestamped_meeting_notes()
    {
        const string srt = "1\r\n00:00:01,250 --> 00:00:03,000\r\nWelcome to the meeting.\r\n\r\n2\r\n00:01:05,900 --> 00:01:08,000\r\nThis wraps\r\nonto two lines.\r\n";

        var result = TimedTranscriptFormatter.FromSrt(srt);

        Assert.Contains("[00:00:01] Welcome to the meeting.", result);
        Assert.Contains("[00:01:05] This wraps onto two lines.", result);
    }
}
