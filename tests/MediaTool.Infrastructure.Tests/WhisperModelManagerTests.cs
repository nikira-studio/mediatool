using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class WhisperModelManagerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MediaToolWhisperTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RejectsAnImportedModelWithTheWrongChecksumAndCleansTheTemporaryFile()
    {
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "wrong.bin");
        await File.WriteAllTextAsync(source, "not a whisper model");
        var manager = new WhisperModelManager(new LocalDataPaths(Path.Combine(directory, "data")));

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportAsync(WhisperModelCatalog.BaseEnglish, source, CancellationToken.None));
        Assert.False(File.Exists(manager.GetModelPath(WhisperModelCatalog.BaseEnglish)));
        Assert.False(File.Exists(manager.GetModelPath(WhisperModelCatalog.BaseEnglish) + ".importing"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
