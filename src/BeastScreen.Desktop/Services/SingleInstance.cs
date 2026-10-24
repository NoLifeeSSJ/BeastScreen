using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BeastScreen.Desktop.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string name;
    private readonly CancellationTokenSource stopping = new();
    public bool IsPrimary { get; }
    public SingleInstance()
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        name = "BeastScreen-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
        mutex = new Mutex(true, "Local\\" + name, out var created); IsPrimary = created;
    }
    public async Task ForwardAsync(string[] paths)
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(paths.Take(128));
        if (bytes.Length > 65536) throw new InvalidDataException("Too many paths to forward.");
        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), timeout.Token); await pipe.WriteAsync(bytes, timeout.Token);
    }
    public async Task ListenAsync(Action<string[]> receive)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var prefix = new byte[4]; await pipe.ReadExactlyAsync(prefix, timeout.Token); var length = BitConverter.ToInt32(prefix);
                if (length is < 2 or > 65536) continue;
                var data = new byte[length]; await pipe.ReadExactlyAsync(data, timeout.Token);
                var paths = JsonSerializer.Deserialize<string[]>(data); if (paths is { Length: <= 128 }) receive(paths);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { }
        }
    }
    public void Dispose() { stopping.Cancel(); if (IsPrimary) mutex.ReleaseMutex(); mutex.Dispose(); stopping.Dispose(); }
}
