using System.Text.RegularExpressions;

namespace BeastScreen.Core.Security;

public static partial class Redaction
{
    [GeneratedRegex(@"(?i)\b(?:https?|rtsps?|rtmps?)://[^\s<>""']+")]
    private static partial Regex Urls();
    public static string Clean(string message) => Urls().Replace(message, match =>
    {
        if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[redacted URL]";
        return $"{uri.Scheme}://{uri.IdnHost}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}/[redacted]";
    });
}
