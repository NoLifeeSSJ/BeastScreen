using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BeastScreen.Core.Security;

namespace BeastScreen.Core.Library;

public sealed record LocalMetadata(string Title = "", string Artist = "", string Album = "", string Genre = "");

/// <summary>Local metadata and cover/frame extraction. At most two bounded helper processes.</summary>
public sealed class MediaInspector(string ffmpegPath, string cacheDirectory) : IDisposable
{
    private readonly SemaphoreSlim workers = new(2);
    public bool Available => File.Exists(ffmpegPath);
    public static string CacheKey(string path, long modified) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\n" + modified)));
    public string ThumbnailPath(string path, long modified) => Path.Combine(cacheDirectory, CacheKey(path, modified) + ".jpg");
    public async Task<LocalMetadata> ReadMetadataAsync(string source, CancellationToken cancellationToken)
    {
        if (!Available || SourcePolicy.IsNetwork(source)) return new();
        var output = await RunAsync(["-nostdin", "-hide_banner", "-v", "error", "-max_alloc", "67108864", "-protocol_whitelist", "file", "-i", source, "-f", "ffmetadata", "pipe:1"], cancellationToken);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith('[')) break;
            var equal = line.IndexOf('='); if (equal <= 0 || equal > 40 || line.Length > 8192) continue;
            tags[line[..equal].Trim()] = Unescape(line[(equal + 1)..].TrimEnd('\r'));
        }
        return new(tags.GetValueOrDefault("title", ""), tags.GetValueOrDefault("artist", ""), tags.GetValueOrDefault("album", ""), tags.GetValueOrDefault("genre", ""));
    }
    private static string Unescape(string text)
    {
        var builder = new StringBuilder(); var escaped = false;
        foreach (var c in text) { if (c == '\\' && !escaped) { escaped = true; continue; } builder.Append(c); escaped = false; }
        return builder.ToString();
    }
    public async Task<string?> CreateThumbnailAsync(string source, long modified, CancellationToken cancellationToken)
    {
        if (!Available || SourcePolicy.IsNetwork(source)) return null;
        var target = ThumbnailPath(source, modified); if (File.Exists(target)) return target;
        Directory.CreateDirectory(cacheDirectory); var partial = target + "." + Guid.NewGuid().ToString("N") + ".jpg";
        try
        {
            await RunAsync(["-nostdin", "-hide_banner", "-v", "error", "-max_alloc", "67108864", "-protocol_whitelist", "file", "-i", source, "-map", "0:v:0", "-frames:v", "1", "-vf", "scale=320:-2", "-an", "-sn", "-y", partial], cancellationToken);
            if (!File.Exists(partial) || new FileInfo(partial).Length is <= 0 or > 5 * 1024 * 1024) return null;
            File.Move(partial, target, true); return target;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private async Task<string> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        await workers.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpegPath, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            var output = ReadBoundedAsync(process.StandardOutput, 1024 * 1024, timeout.Token); var error = ReadBoundedAsync(process.StandardError, 65536, timeout.Token);
            // Observe reader failures promptly instead of waiting for a full pipe and the timeout.
            var exit = process.WaitForExitAsync(timeout.Token);
            var all = Task.WhenAll(output, error, exit);
            var failedReader = await Task.WhenAny(output, error, exit);
            if (failedReader.IsFaulted) await failedReader;
            await all;
            if (process.ExitCode != 0) throw new InvalidDataException("The media helper could not inspect this file. It may be corrupt, unsupported or have no image stream.");
            return await output;
        }
        finally { try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } workers.Release(); }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0) { if (builder.Length + count > maximum) throw new InvalidDataException("Media helper output exceeded its safety limit."); builder.Append(buffer, 0, count); }
        return builder.ToString();
    }
    public void Dispose() => workers.Dispose();
}
