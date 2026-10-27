using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class WhisperProgressParserTests
{
    [Theory]
    [InlineData("whisper_print_progress_callback: progress =   0%", 0)]
    [InlineData("whisper_print_progress_callback: progress =  45%", 45)]
    [InlineData("whisper_print_progress_callback: progress = 100%", 100)]
    public void ParsesWhisperProgress(string line, double expected)
    {
        Assert.Equal(expected, WhisperProgressParser.Parse(line)!.Value.Value);
    }

    [Fact]
    public void IgnoresUnrelatedOutput()
    {
        Assert.Null(WhisperProgressParser.Parse("output_txt: saving output"));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(20, 10)]
    [InlineData(64, 12)]
    public void ChoosesHalfTheLogicalProcessorsWithinSafeLimits(int logicalProcessors, int expected)
    {
        Assert.Equal(expected, WhisperProgressParser.RecommendedThreadCount(logicalProcessors));
    }

    [Fact]
    public void DescribesEstimatedTimeRemaining()
    {
        var progress = WhisperProgressParser.Parse("progress = 25%")!.Value;
        Assert.Equal("Transcribing locally · about 30 min remaining", WhisperProgressParser.DescribePhase(progress, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void DescribesCompletionAsFinalizing()
    {
        var progress = WhisperProgressParser.Parse("progress = 100%")!.Value;
        Assert.Equal("Finishing transcript", WhisperProgressParser.DescribePhase(progress, TimeSpan.FromMinutes(10)));
    }
}
