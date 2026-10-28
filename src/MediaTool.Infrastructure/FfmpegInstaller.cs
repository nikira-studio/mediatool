using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MediaTool.Infrastructure;

public sealed class FfmpegInstaller(HttpClient? httpClient = null)
{
    public async Task<string> EnsureInstalledAsync(string manifestPath, string root, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidDataException("The FFmpeg download information is invalid.");
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !IsHash(manifest.Sha256) || !IsHash(manifest.FfmpegSha256) || !IsHash(manifest.FfprobeSha256))
            throw new InvalidDataException("The FFmpeg download information is incomplete.");

        var destination = Path.Combine(root, "ffmpeg", manifest.Sha256);
        if (await IsValidAsync(Path.Combine(destination, "ffmpeg.exe"), manifest.FfmpegSha256, cancellationToken) &&
            await IsValidAsync(Path.Combine(destination, "ffprobe.exe"), manifest.FfprobeSha256, cancellationToken)) return destination;

        Directory.CreateDirectory(Path.Combine(root, "ffmpeg"));
        var temporary = Path.Combine(root, "ffmpeg", "setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var archivePath = Path.Combine(temporary, "download.zip");
            using var ownedClient = httpClient is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(30) } : null;
            var client = httpClient ?? ownedClient!;
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (response.Content.Headers.ContentLength is > 0 and var total) progress?.Report(Math.Min(100, received * 100d / total));
                }
            }
            if (!await IsValidAsync(archivePath, manifest.Sha256, cancellationToken)) throw new InvalidDataException("The FFmpeg download did not pass its integrity check.");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
                {
                    var matches = archive.Entries.Where(entry => entry.FullName.EndsWith("/bin/" + name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1) throw new InvalidDataException("The FFmpeg download is missing a required file.");
                    matches[0].ExtractToFile(Path.Combine(temporary, name));
                }
            }
            if (!await IsValidAsync(Path.Combine(temporary, "ffmpeg.exe"), manifest.FfmpegSha256, cancellationToken) ||
                !await IsValidAsync(Path.Combine(temporary, "ffprobe.exe"), manifest.FfprobeSha256, cancellationToken))
                throw new InvalidDataException("The FFmpeg files did not pass their integrity check.");
            Directory.CreateDirectory(destination);
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.Move(Path.Combine(temporary, name), Path.Combine(destination, name), true);
            return destination;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static async Task<bool> IsValidAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record Manifest(string DownloadUrl, string Sha256, string FfmpegSha256, string FfprobeSha256);
}
