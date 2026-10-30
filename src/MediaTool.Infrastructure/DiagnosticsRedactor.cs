using System.Text.RegularExpressions;

namespace MediaTool.Infrastructure;

public static partial class DiagnosticsRedactor
{
    public static string Redact(string text, bool detailedLogging)
    {
        if (detailedLogging || string.IsNullOrEmpty(text)) return text;
        var result = WindowsPath().Replace(text, "<path>");
        var userName = Environment.UserName;
        return string.IsNullOrWhiteSpace(userName) ? result : result.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^\r\n\""<>|?*]+")]
    private static partial Regex WindowsPath();
}
