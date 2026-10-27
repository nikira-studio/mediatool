using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class CompressionArgumentBuilderTests
{
    [Fact]
    public void H264EncodeUsesCompatibleMp4SettingsAndMappedAudio()
    {
        var request = Request(VideoCodec.H264, AudioTrackMode.All);
        var inspection = Inspection();
        var plan = CompressionPlanner.Create(request, inspection);

        var arguments = CompressionArgumentBuilder.Build(request, inspection, plan, plan.VideoBitRate, "C:\\temp\\out.partial");

        Assert.Contains("libx264", arguments);
        Assert.Contains("medium", arguments);
        Assert.Contains("-maxrate", arguments);
        Assert.Contains("-bufsize", arguments);
        Assert.Contains("yuv420p", arguments);
        Assert.Contains("+faststart", arguments);
        Assert.Contains("-map", arguments);
        Assert.Contains("0:0", arguments);
        Assert.Contains("0:1", arguments);
        Assert.Contains("0:2", arguments);
        Assert.DoesNotContain("-an", arguments);
    }

    [Fact]
    public void EncodeDoesNotCreateTwoPassAnalysisFiles()
    {
        var request = Request(VideoCodec.H264, AudioTrackMode.Default);
        var inspection = Inspection();
        var plan = CompressionPlanner.Create(request, inspection);

        var arguments = CompressionArgumentBuilder.Build(request, inspection, plan, plan.VideoBitRate, "C:\\temp\\out.partial");

        Assert.DoesNotContain("-pass", arguments);
        Assert.DoesNotContain("-passlogfile", arguments);
        Assert.Contains("+faststart", arguments);
        Assert.Equal("C:\\temp\\out.partial", arguments[^1]);
    }

    [Fact]
    public void H265NeverSilentlyBuildsH264Arguments()
    {
        var request = Request(VideoCodec.H265, AudioTrackMode.None);
        var inspection = Inspection();
        var plan = CompressionPlanner.Create(request, inspection);

        var arguments = CompressionArgumentBuilder.Build(request, inspection, plan, plan.VideoBitRate, "C:\\temp\\out.partial");

        Assert.Contains("libx265", arguments);
        Assert.DoesNotContain("libx264", arguments);
        Assert.Contains("-an", arguments);
    }

    private static CompressionRequest Request(VideoCodec codec, AudioTrackMode audio) => new(
        MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "input video.mp4")), new(null), FileSize.FromMegabytes(20), codec, MaximumResolution.Auto, audio, []);

    private static MediaInspection Inspection() => new(
        MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "input video.mp4")), MediaDuration.FromTimeSpan(TimeSpan.FromSeconds(30)), FileSize.FromMegabytes(100),
        [
            new(new(0), MediaStreamKind.Video, "h264", null, null, null, 1920, 1080, 60, BitRate.FromKilobitsPerSecond(10_000), true, false),
            new(new(1), MediaStreamKind.Audio, "aac", null, "eng", 2, null, null, null, BitRate.FromKilobitsPerSecond(128), true, false),
            new(new(2), MediaStreamKind.Audio, "aac", null, "spa", 2, null, null, null, BitRate.FromKilobitsPerSecond(128), false, false)
        ], null, new Dictionary<string, string>());
}
