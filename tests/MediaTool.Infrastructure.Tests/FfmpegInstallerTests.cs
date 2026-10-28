using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class FfmpegInstallerTests
{
    [Fact]
    public async Task DownloadsVerifiesAndReusesInstalledFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MediaToolInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] ffmpeg = [1, 2, 3], ffprobe = [4, 5, 6];
            using var data = new MemoryStream();
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
            {
                using (var entry = zip.CreateEntry("release/bin/ffmpeg.exe").Open()) entry.Write(ffmpeg);
                using (var entry = zip.CreateEntry("release/bin/ffprobe.exe").Open()) entry.Write(ffprobe);
            }
            var bytes = data.ToArray();
            string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
            var manifestPath = Path.Combine(directory, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { downloadUrl = "https://example.test/ffmpeg.zip", sha256 = Hash(bytes), ffmpegSha256 = Hash(ffmpeg), ffprobeSha256 = Hash(ffprobe) }));
            var handler = new FakeHandler(bytes);
            using var client = new HttpClient(handler);
            var installer = new FfmpegInstaller(client);
            var installed = await installer.EnsureInstalledAsync(manifestPath, directory, null, CancellationToken.None);
            Assert.Equal(ffmpeg, await File.ReadAllBytesAsync(Path.Combine(installed, "ffmpeg.exe")));
            Assert.Equal(ffprobe, await File.ReadAllBytesAsync(Path.Combine(installed, "ffprobe.exe")));
            Assert.Equal(installed, await installer.EnsureInstalledAsync(manifestPath, directory, null, CancellationToken.None));
            Assert.Equal(1, handler.RequestCount);
            await File.WriteAllBytesAsync(Path.Combine(installed, "ffmpeg.exe"), [0]);
            await installer.EnsureInstalledAsync(manifestPath, directory, null, CancellationToken.None);
            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(ffmpeg, await File.ReadAllBytesAsync(Path.Combine(installed, "ffmpeg.exe")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RejectsInvalidManifestBeforeDownloading()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"downloadUrl\":\"https://example.test/a.zip\",\"sha256\":\"../bad\"}");
            await Assert.ThrowsAsync<InvalidDataException>(() => new FfmpegInstaller().EnsureInstalledAsync(path, Path.GetTempPath(), null, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    private sealed class FakeHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
}
