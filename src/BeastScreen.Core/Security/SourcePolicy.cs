namespace BeastScreen.Core.Security;

public static class SourcePolicy
{
    private static readonly HashSet<string> Protocols = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "rtsp", "rtsps", "rtmp", "rtmps", "udp", "rtp", "srt" };
    public static bool IsNetwork(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) && Protocols.Contains(uri.Scheme);
    public static void Validate(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 32760 || source.Contains('\0') || source.Contains('\r') || source.Contains('\n')) throw new ArgumentException("Enter a file path or supported media URL.");
        if (source.Contains("://", StringComparison.Ordinal) && !IsNetwork(source)) throw new ArgumentException("This URL protocol is not enabled. Use a direct HTTP(S), RTSP, RTMP, RTP, UDP or SRT media URL.");
        if (!IsNetwork(source) && !Path.IsPathFullyQualified(source)) throw new ArgumentException("Select an absolute media path.");
    }
}
