using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class FfprobeJsonParserTests
{
    [Fact]
    public void ParsesStreamsAndTreatsCoverArtAsAttachment()
    {
        const string json = """
        {
          "streams": [
            { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080, "avg_frame_rate": "30000/1001", "disposition": { "default": 1, "attached_pic": 0 } },
            { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "bit_rate": "128000", "tags": { "language": "eng", "title": "Main" }, "disposition": { "default": 1 } },
            { "index": 2, "codec_name": "mjpeg", "codec_type": "video", "disposition": { "attached_pic": 1 } }
          ],
          "format": { "duration": "12.5", "size": "5000000", "bit_rate": "3200000", "tags": { "title": "Sample" } }
        }
        """;

        var result = FfprobeJsonParser.Parse(MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "sample.mp4")), json);

        Assert.Equal(TimeSpan.FromSeconds(12.5), result.Duration.Value);
        Assert.Equal(5_000_000, result.FileSize.Bytes);
        Assert.Equal(MediaStreamKind.Attachment, result.Streams[2].Kind);
        Assert.True(result.Streams[2].IsAttachedPicture);
        Assert.Equal("eng", result.Streams[1].Language);
    }

    [Theory]
    [InlineData("{}", "unknown_duration")]
    [InlineData("{not json}", "invalid_probe_output")]
    public void RejectsMissingAndMalformedData(string json, string errorCode)
    {
        var exception = Assert.Throws<MediaInspectionException>(() =>
            FfprobeJsonParser.Parse(MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "bad.mp4")), json));
        Assert.Equal(errorCode, exception.Code);
    }
}
