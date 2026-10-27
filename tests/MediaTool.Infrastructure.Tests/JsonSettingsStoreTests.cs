using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MediaToolSettingsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SavesReloadsAndResetsIndependentToolDefaults()
    {
        var store = new JsonSettingsStore(new LocalDataPaths(directory));
        var changed = AppSettings.Default with
        {
            Global = AppSettings.Default.Global with { Theme = AppTheme.Dark },
            Compression = AppSettings.Default.Compression with { MaximumMegabytes = 50 },
            AudioExtraction = AppSettings.Default.AudioExtraction with { Mp3KilobitsPerSecond = 320 }
        };

        await store.SaveAsync(changed);
        var loaded = await store.LoadAsync();
        Assert.Equal(AppTheme.Dark, loaded.Global.Theme);
        Assert.Equal(50, loaded.Compression.MaximumMegabytes);
        Assert.Equal(320, loaded.AudioExtraction.Mp3KilobitsPerSecond);
        Assert.Equal("base.en", loaded.Transcription.ModelId);

        Assert.Equal(AppSettings.Default, await store.ResetAsync());
        Assert.Equivalent(AppSettings.Default, await store.LoadAsync(), strict: true);
    }

    [Fact]
    public async Task FallsBackToDefaultsForMalformedSettings()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{broken");
        var loaded = await new JsonSettingsStore(new LocalDataPaths(directory)).LoadAsync();
        Assert.Equal(AppSettings.Default, loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
