using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class OutputFileServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MediaToolTests", Guid.NewGuid().ToString("N"));
    private readonly OutputFileService service = new();

    public OutputFileServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void AddsConflictSafeNumbersStartingAtTwo()
    {
        File.WriteAllText(Path.Combine(directory, "clip-compressed.mp4"), "existing");
        File.WriteAllText(Path.Combine(directory, "clip-compressed-2.mp4"), "existing");
        Assert.Equal(Path.Combine(directory, "clip-compressed-3.mp4"), service.GetAvailablePath(directory, "clip-compressed.mp4"));
    }

    [Fact]
    public void Repeated_versions_continue_numbering_without_replacing_prior_outputs()
    {
        foreach (var suffix in new[] { string.Empty, "-2", "-3" })
            File.WriteAllText(Path.Combine(directory, $"clip-compressed{suffix}.mp4"), suffix);

        var path = service.GetAvailablePath(directory, "clip-compressed.mp4");

        Assert.Equal(Path.Combine(directory, "clip-compressed-4.mp4"), path);
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(directory, "clip-compressed.mp4")));
    }

    [Fact]
    public void FinalizeNeverReplacesExistingDestination()
    {
        var temporary = Path.Combine(directory, "temporary.partial");
        var final = Path.Combine(directory, "final.mp4");
        File.WriteAllText(temporary, "new");
        File.WriteAllText(final, "old");
        Assert.Throws<IOException>(() => service.Finalize(temporary, final));
        Assert.Equal("old", File.ReadAllText(final));
        Assert.True(File.Exists(temporary));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
