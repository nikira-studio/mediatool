using MediaTool.Core;

namespace MediaTool.Core.Tests;

public sealed class CompressionPlannerTests
{
    [Fact]
    public void UsesNinetyThreePercentSafeBudgetAndDecimalMegabytes()
    {
        Assert.Equal(18_600_000, CompressionPlanner.GetSafeTarget(FileSize.FromMegabytes(20)).Bytes);
    }

    [Theory]
    [InlineData(2_500, 1080, 30)]
    [InlineData(2_499, 720, 30)]
    [InlineData(1_200, 720, 30)]
    [InlineData(1_199, 480, 30)]
    [InlineData(600, 480, 30)]
    [InlineData(599, 360, 24)]
    public void AutoLadderHonorsEveryThreshold(long kilobits, int height, double fps)
    {
        var rung = CompressionPlanner.GetAutoRung(BitRate.FromKilobitsPerSecond(kilobits));
        Assert.Equal(height, rung.MaximumHeight);
        Assert.Equal(fps, rung.MaximumFramesPerSecond);
    }

    [Theory]
    [InlineData(1920, 1080, 720, 1280, 720)]
    [InlineData(1080, 1920, 720, 404, 720)]
    [InlineData(640, 359, 720, 640, 358)]
    public void ScalingPreservesOrientationUsesEvenDimensionsAndNeverUpscales(int width, int height, int limit, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), CompressionPlanner.ScaleToMaximumHeight(width, height, limit));
    }

    [Fact]
    public void BudgetsEveryRetainedAudioTrackAndCapsAtSourceVideoBitrate()
    {
        var inspection = Inspection(TimeSpan.FromSeconds(60), 1920, 1080, 60, 4_000_000,
            Audio(1, true, 2), Audio(2, false, 2));
        var request = Request(50, AudioTrackMode.All);
        var plan = CompressionPlanner.Create(request, inspection);

        Assert.Equal(2, plan.AudioTracks.Count);
        Assert.All(plan.AudioTracks, track => Assert.Equal(128_000, track.BitRate.BitsPerSecond));
        Assert.Equal(4_000_000, plan.VideoBitRate.BitsPerSecond);
        Assert.Equal(30, plan.FramesPerSecond);
    }

    [Fact]
    public void Uses_a_low_bitrate_plan_instead_of_rejecting_a_reasonable_small_target()
    {
        var inspection = Inspection(TimeSpan.FromHours(1), 1920, 1080, 30, null, Audio(1, true, 2));
        var plan = CompressionPlanner.Create(Request(20, AudioTrackMode.Default), inspection);
        Assert.True(plan.VideoBitRate.BitsPerSecond >= CompressionPlanner.MinimumVideoBitsPerSecond);
        Assert.Equal(16_000, plan.AudioTracks.Single().BitRate.BitsPerSecond);
    }

    [Fact]
    public void Rejects_a_target_that_cannot_fit_minimal_audio_and_video()
    {
        var inspection = Inspection(TimeSpan.FromHours(4), 1920, 1080, 30, null, Audio(1, true, 2));
        var exception = Assert.Throws<CompressionPlanningException>(() => CompressionPlanner.Create(Request(5, AudioTrackMode.Default), inspection));
        Assert.Equal("target_too_small", exception.Code);
    }

    [Fact]
    public void RetryMathUsesMeasuredOutputAndExtraHeadroom()
    {
        var adjusted = CompressionPlanner.GetRetryVideoBitRate(
            BitRate.FromKilobitsPerSecond(1_000), FileSize.FromBytes(9_300_000), FileSize.FromBytes(10_000_000));
        Assert.Equal(911_400, adjusted.BitsPerSecond);
        Assert.Equal(3, CompressionPlanner.MaximumAttempts);
    }

    private static CompressionRequest Request(decimal megabytes, AudioTrackMode mode) => new(
        MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "sample.mp4")), new(null), FileSize.FromMegabytes(megabytes),
        VideoCodec.H264, MaximumResolution.Auto, mode, []);

    private static MediaInspection Inspection(TimeSpan duration, int width, int height, double fps, long? videoBitRate, params MediaStream[] audio) => new(
        MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "sample.mp4")), MediaDuration.FromTimeSpan(duration), FileSize.FromMegabytes(100),
        [new(new(0), MediaStreamKind.Video, "h264", null, null, null, width, height, fps, videoBitRate is null ? null : BitRate.FromBitsPerSecond(videoBitRate.Value), true, false), .. audio],
        null, new Dictionary<string, string>());

    private static MediaStream Audio(int index, bool isDefault, int channels) =>
        new(new(index), MediaStreamKind.Audio, "aac", null, "eng", channels, null, null, null, BitRate.FromKilobitsPerSecond(128), isDefault, false);
}
