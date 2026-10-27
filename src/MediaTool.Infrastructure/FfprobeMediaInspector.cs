using System.Globalization;
using System.Text.Json;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed class FfprobeMediaInspector(string ffprobePath, IProcessRunner processRunner) : IMediaInspector
{
    private readonly string ffprobePath = Path.GetFullPath(ffprobePath);

    public async Task<MediaInspection> InspectAsync(MediaInput input, CancellationToken cancellationToken)
    {
        if (!File.Exists(input.FullPath)) throw new MediaInspectionException("unreadable_input", "The source file does not exist.");

        var result = await processRunner.RunAsync(new(
            ffprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", input.FullPath]), cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new MediaInspectionException("unreadable_input", "FFprobe could not read the media file.", string.Join(Environment.NewLine, result.StandardError));

        return FfprobeJsonParser.Parse(input, string.Join(Environment.NewLine, result.StandardOutput));
    }
}

public sealed class MediaInspectionException(string code, string message, string? detail = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
}

public static class FfprobeJsonParser
{
    public static MediaInspection Parse(MediaInput input, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            var format = root.TryGetProperty("format", out var formatValue) ? formatValue : default;
            var duration = ParsePositiveDouble(format, "duration") ?? throw new MediaInspectionException("unknown_duration", "The media duration is missing or invalid.");
            var size = ParsePositiveLong(format, "size") ?? new FileInfo(input.FullPath).Length;
            BitRate? overallBitRate = ParsePositiveLong(format, "bit_rate") is { } bits ? BitRate.FromBitsPerSecond(bits) : null;
            var streams = new List<MediaStream>();

            if (root.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamArray.EnumerateArray()) streams.Add(ParseStream(stream));
            }

            var metadata = ReadStringMap(format, "tags");
            return new(input, MediaDuration.FromTimeSpan(TimeSpan.FromSeconds(duration)), FileSize.FromBytes(size), streams, overallBitRate, metadata);
        }
        catch (JsonException exception)
        {
            throw new MediaInspectionException("invalid_probe_output", "FFprobe returned invalid data.", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new MediaInspectionException("invalid_probe_output", "FFprobe returned inconsistent data.", exception.Message);
        }
    }

    private static MediaStream ParseStream(JsonElement stream)
    {
        var index = GetInt(stream, "index") ?? throw new MediaInspectionException("invalid_probe_output", "A media stream has no valid index.");
        var codecType = GetString(stream, "codec_type");
        var disposition = stream.TryGetProperty("disposition", out var dispositionValue) ? dispositionValue : default;
        var tags = ReadStringMap(stream, "tags");
        var attachedPicture = GetInt(disposition, "attached_pic") == 1;
        var kind = attachedPicture ? MediaStreamKind.Attachment : codecType switch
        {
            "video" => MediaStreamKind.Video,
            "audio" => MediaStreamKind.Audio,
            "subtitle" => MediaStreamKind.Subtitle,
            "attachment" => MediaStreamKind.Attachment,
            _ => MediaStreamKind.Unknown
        };

        return new(
            new(index),
            kind,
            GetString(stream, "codec_name") ?? "unknown",
            tags.GetValueOrDefault("title"),
            tags.GetValueOrDefault("language"),
            GetInt(stream, "channels"),
            GetInt(stream, "width"),
            GetInt(stream, "height"),
            ParseFrameRate(GetString(stream, "avg_frame_rate")),
            ParsePositiveLong(stream, "bit_rate") is { } bits ? BitRate.FromBitsPerSecond(bits) : null,
            GetInt(disposition, "default") == 1,
            attachedPicture);
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return value.EnumerateObject()
            .Where(item => item.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static long? ParsePositiveLong(JsonElement parent, string property)
    {
        var raw = GetString(parent, property);
        if (raw is not null && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0) return parsed;
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.TryGetInt64(out parsed) && parsed >= 0) return parsed;
        return null;
    }

    private static double? ParsePositiveDouble(JsonElement parent, string property)
    {
        var raw = GetString(parent, property);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 && double.IsFinite(parsed) ? parsed : null;
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var pieces = value.Split('/', 2);
        if (!double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)) return null;
        var denominator = pieces.Length == 2 && double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1d;
        if (denominator == 0) return null;
        var result = numerator / denominator;
        return result > 0 && double.IsFinite(result) ? result : null;
    }
}
