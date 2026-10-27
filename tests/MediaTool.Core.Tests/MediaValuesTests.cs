using MediaTool.Core;

namespace MediaTool.Core.Tests;

public sealed class MediaValuesTests
{
    [Fact]
    public void MegabytesUseDecimalBytes()
    {
        Assert.Equal(20_000_000, FileSize.FromMegabytes(20).Bytes);
    }

    [Fact]
    public void MediaInputRejectsRelativePaths()
    {
        Assert.Throws<ArgumentException>(() => MediaInput.FromPath("clip.mp4"));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.5, 50)]
    [InlineData(2, 100)]
    [InlineData(double.NaN, 0)]
    public void ProgressIsClamped(double ratio, double expected)
    {
        Assert.Equal(expected, ProgressPercent.FromRatio(ratio).Value);
    }
}
