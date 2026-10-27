using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class FfmpegProgressParserTests
{
    [Theory]
    [InlineData("out_time_us=0", 0)]
    [InlineData("out_time_us=5000000", 50)]
    [InlineData("out_time_us=12000000", 100)]
    public void ParsesAndClampsFfmpegProgress(string line, double expected)
    {
        var parser = new FfmpegProgressParser(MediaDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
        Assert.Equal(expected, parser.Parse(line)!.Value.Value);
    }

    [Fact]
    public void IgnoresUnrelatedLines()
    {
        var parser = new FfmpegProgressParser(MediaDuration.FromTimeSpan(TimeSpan.FromSeconds(10)));
        Assert.Null(parser.Parse("progress=continue"));
    }
}
