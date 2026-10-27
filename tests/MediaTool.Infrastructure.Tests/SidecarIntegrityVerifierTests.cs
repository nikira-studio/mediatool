using System.Security.Cryptography;
using System.Text.Json;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class SidecarIntegrityVerifierTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MediaTool.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Accepts_the_camel_case_manifest_used_by_the_packaged_app()
    {
        Directory.CreateDirectory(directory);
        var ffmpeg = Path.Combine(directory, "ffmpeg.exe");
        var ffprobe = Path.Combine(directory, "ffprobe.exe");
        await File.WriteAllTextAsync(ffmpeg, "ffmpeg test binary");
        await File.WriteAllTextAsync(ffprobe, "ffprobe test binary");
        var ffmpegHash = await HashAsync(ffmpeg);
        var ffprobeHash = await HashAsync(ffprobe);
        var manifest = JsonSerializer.Serialize(new { ffmpegSha256 = ffmpegHash, ffprobeSha256 = ffprobeHash });
        await File.WriteAllTextAsync(Path.Combine(directory, "ffmpeg.json"), manifest);

        await new SidecarIntegrityVerifier().VerifyFfmpegAsync(directory);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
