using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaTool.Infrastructure;

public sealed class SidecarIntegrityVerifier
{
    public async Task VerifyFfmpegAsync(string sidecarDirectory, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(sidecarDirectory, "ffmpeg.json");
        var ffmpegPath = Path.Combine(sidecarDirectory, "ffmpeg.exe");
        var ffprobePath = Path.Combine(sidecarDirectory, "ffprobe.exe");
        if (!File.Exists(manifestPath) || !File.Exists(ffmpegPath) || !File.Exists(ffprobePath))
            throw new SidecarIntegrityException("The packaged FFmpeg files are missing. Repair or reinstall MediaTool.");

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<FfmpegManifest>(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new SidecarIntegrityException("The FFmpeg integrity manifest is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.FfmpegSha256) || string.IsNullOrWhiteSpace(manifest.FfprobeSha256))
            throw new SidecarIntegrityException("The FFmpeg integrity manifest has no binary hashes.");

        await VerifyFileAsync(ffmpegPath, manifest.FfmpegSha256, cancellationToken).ConfigureAwait(false);
        await VerifyFileAsync(ffprobePath, manifest.FfprobeSha256, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyFileAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash);
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new SidecarIntegrityException($"The packaged {Path.GetFileName(path)} failed its integrity check. Repair or reinstall MediaTool.");
    }

    // The manifest is deliberately camelCase so it is convenient for tooling and readable outside .NET.
    // These explicit names keep its security-critical fields stable regardless of serializer defaults.
    private sealed record FfmpegManifest(
        [property: JsonPropertyName("ffmpegSha256")] string? FfmpegSha256,
        [property: JsonPropertyName("ffprobeSha256")] string? FfprobeSha256);
}

public sealed class SidecarIntegrityException(string message) : Exception(message);
