using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BeastScreen.Testing;

/// <summary>Disposable, loopback-only media fixtures with ranges, finite faults and live radio.</summary>
public sealed class LoopbackMediaServer : IAsyncDisposable
{
    private readonly string root;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<TcpClient, byte> clients = new();
    private readonly ConcurrentBag<Task> handlers = [];
    private readonly ConcurrentDictionary<string, int> requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> failedResponses = new(StringComparer.Ordinal);
    private readonly Task accepting;
    public Uri Address { get; }

    public LoopbackMediaServer(string root)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        listener.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        accepting = AcceptAsync();
    }

    public string Url(string path) => new Uri(Address, path).AbsoluteUri;
    public int RequestCount(string path) => requests.GetValueOrDefault(path);
    public IReadOnlyDictionary<string, int> RequestCounts => requests.ToDictionary(pair => pair.Key, pair => pair.Value);
    public int FailureCount(string path) => failedResponses.GetValueOrDefault(path);
    public void FailNext(string path, int count) => failures[path] = count;

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                clients.TryAdd(client, 0);
                handlers.Add(ServeAsync(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var cancellationToken = timeout.Token;
            try
            {
                var stream = client.GetStream();
                var header = await ReadHeaderAsync(stream, cancellationToken);
                var lines = header.Split("\r\n", StringSplitOptions.None);
                var request = lines[0].Split(' ', 3);
                if (request.Length != 3 || request[0] is not ("GET" or "HEAD") || !request[1].StartsWith('/')) { await ReplyAsync(stream, 400, "Bad Request", cancellationToken); return; }
                var url = new Uri(Address, request[1]);
                var path = Uri.UnescapeDataString(url.AbsolutePath);
                requests.AddOrUpdate(path, 1, (_, previous) => previous + 1);
                if (TakeFailure(path))
                {
                    failedResponses.AddOrUpdate(path, 1, (_, previous) => previous + 1);
                    await ReplyAsync(stream, 503, "Service Unavailable", cancellationToken); return;
                }
                if (path == "/radio")
                {
                    await WriteAsciiAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: audio/mpeg\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n", cancellationToken);
                    var radio = await File.ReadAllBytesAsync(Path.Combine(root, "radio.mp3"), cancellationToken);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        for (var offset = 0; offset < radio.Length; offset += 1024)
                        {
                            await stream.WriteAsync(radio.AsMemory(offset, Math.Min(1024, radio.Length - offset)), cancellationToken);
                            await Task.Delay(50, cancellationToken);
                        }
                    }
                    return;
                }
                // Fault routes serve the same generated file once the configured failures end.
                var relative = path is "/recover" or "/exhaust" or "/cancel" ? "direct.mkv" : path.TrimStart('/');
                if (relative.Contains(':') || relative.Contains('\\')) { await ReplyAsync(stream, 400, "Bad Request", cancellationToken); return; }
                var file = Path.GetFullPath(Path.Combine(root, relative));
                if (!file.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !File.Exists(file)) { await ReplyAsync(stream, 404, "Not Found", cancellationToken); return; }
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, useAsync: true);
                long start = 0, end = input.Length - 1;
                var range = lines.FirstOrDefault(x => x.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                if (range is not null && !TryRange(range[6..].Trim(), input.Length, out start, out end)) { await ReplyAsync(stream, 416, "Range Not Satisfiable", cancellationToken); return; }
                var length = end - start + 1;
                var type = Path.GetExtension(file).ToLowerInvariant() switch { ".m3u8" => "application/vnd.apple.mpegurl", ".mpd" => "application/dash+xml", ".ts" => "video/mp2t", ".mp3" => "audio/mpeg", _ => "application/octet-stream" };
                var response = $"HTTP/1.1 {(range is null ? "200 OK" : "206 Partial Content")}\r\nContent-Type: {type}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n";
                if (range is not null) response += $"Content-Range: bytes {start}-{end}/{input.Length}\r\n";
                await WriteAsciiAsync(stream, response + "\r\n", cancellationToken);
                if (request[0] == "HEAD") return;
                input.Position = start;
                var buffer = new byte[16384];
                while (length > 0)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), cancellationToken);
                    if (count == 0) throw new EndOfStreamException();
                    await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken); length -= count;
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException or UriFormatException or ArgumentException) { }
            finally { clients.TryRemove(client, out _); }
        }
    }

    private bool TakeFailure(string path)
    {
        while (failures.TryGetValue(path, out var remaining) && remaining > 0)
            if (failures.TryUpdate(path, remaining - 1, remaining)) return true;
        return false;
    }

    private static bool TryRange(string value, long length, out long start, out long end)
    {
        start = 0; end = length - 1;
        if (!value.StartsWith("bytes=", StringComparison.Ordinal) || value.Contains(',')) return false;
        var parts = value[6..].Split('-', 2);
        if (parts.Length != 2) return false;
        if (parts[0] == "")
        {
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0) return false;
            start = Math.Max(0, length - suffix);
        }
        else
        {
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)) return false;
            if (parts[1] != "" && !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)) return false;
        }
        end = Math.Min(end, length - 1); return start >= 0 && start < length && end >= start;
    }

    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384]; var used = 0;
        while (used < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(used, 1), cancellationToken);
            if (count == 0) throw new EndOfStreamException();
            used += count;
            if (used >= 4 && buffer[used - 4] == 13 && buffer[used - 3] == 10 && buffer[used - 2] == 13 && buffer[used - 1] == 10) return Encoding.ASCII.GetString(buffer, 0, used);
        }
        throw new InvalidDataException("Fixture request headers exceed 16 KiB.");
    }
    private static Task WriteAsciiAsync(NetworkStream stream, string value, CancellationToken cancellationToken) => stream.WriteAsync(Encoding.ASCII.GetBytes(value), cancellationToken).AsTask();
    private static Task ReplyAsync(NetworkStream stream, int status, string reason, CancellationToken cancellationToken) => WriteAsciiAsync(stream, $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); listener.Stop();
        foreach (var client in clients.Keys) client.Dispose();
        await accepting;
        await Task.WhenAll(handlers).WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Dispose();
    }
}
