using System.Text.RegularExpressions;

namespace BeastScreen.Core.Security;

public static partial class Redaction
{
    [GeneratedRegex(@"(?i)\b(?:https?|rtsps?|rtmps?|srt|udp|rtp)://[^\s<>""']+")]
    private static partial Regex Urls();
    [GeneratedRegex(@"(?im)\b(authorization|proxy-authorization|cookie|set-cookie)\s*[:=]\s*[^\r\n]+")]
    private static partial Regex Headers();
    public static string Clean(string message) => Headers().Replace(Urls().Replace(message, match =>
    {
        if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[redacted URL]";
        return $"{uri.Scheme}://{uri.IdnHost}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}/[redacted]";
    }), "$1: [redacted]");
}
