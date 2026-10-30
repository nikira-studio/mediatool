using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class DiagnosticsRedactorTests
{
    [Theory]
    [InlineData(@"Cannot open 'C:\Users\Alice\private meeting.mp4'.", "private meeting.mp4")]
    [InlineData(@"Cannot open '\\server\share\private meeting.mp4'.", "private meeting.mp4")]
    public void RemovesFullPathsFromDiagnosticDetails(string detail, string sensitiveName)
    {
        var redacted = DiagnosticsRedactor.Redact(detail, false);
        Assert.Contains("<path>", redacted);
        Assert.DoesNotContain(sensitiveName, redacted);
    }
}
