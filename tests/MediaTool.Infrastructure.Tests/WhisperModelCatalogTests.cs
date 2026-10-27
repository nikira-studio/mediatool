using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class WhisperModelCatalogTests
{
    [Fact]
    public void Available_models_have_pinned_hashes_and_download_locations()
    {
        Assert.Equal(4, WhisperModelCatalog.Available.Count);
        foreach (var model in WhisperModelCatalog.Available)
        {
            Assert.Equal(64, model.Sha256.Length);
            Assert.Equal($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{model.FileName}", WhisperModelCatalog.GetDownloadUri(model).AbsoluteUri);
        }
        Assert.False(WhisperModelCatalog.LargeV3Turbo.EnglishOnly);
    }
}
