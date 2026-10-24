using System.Text.Json;
using System.Windows;
using BeastScreen.Core.Playback;
using BeastScreen.Core.Security;

namespace BeastScreen.Desktop;

public partial class MainWindow
{
    /// <summary>Only invoked explicitly by the controlled CI fixture driver; no server is shipped.</summary>
    public async Task RunNetworkSmokeAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--network-smoke-test");
        var resultIndex = Array.IndexOf(args, "--result");
        var output = resultIndex >= 0 && resultIndex + 1 < args.Length ? args[resultIndex + 1] : Path.Combine(Path.GetTempPath(), "BeastScreen-network-smoke.json");
        var checks = new Dictionary<string, bool>();
        try
        {
            if (index < 0 || index + 1 >= args.Length || !Uri.TryCreate(args[index + 1], UriKind.Absolute, out var server) || server.Scheme != "http" || server.Host != "127.0.0.1") throw new ArgumentException("Network smoke requires the explicitly supplied loopback fixture server.");
            model.ResumeEnabled = false;
            async Task PlayAsync(string relative)
            {
                var url = new Uri(server, relative).AbsoluteUri;
                model.OpenSources([url]);
                await WaitFor(() => model.Engine.Get("path") == url && model.LastSnapshot is { State: PlaybackState.Playing, Position: > 0.2 }, TimeSpan.FromSeconds(25));
            }

            await PlayAsync("recover"); checks["recoveredAfterHttpFailure"] = true;
            if (!model.Seekable) throw new InvalidDataException("The ranged direct-media fixture did not expose seeking.");
            model.Engine.Command("seek", "3", "absolute+exact");
            await WaitFor(() => model.LastSnapshot?.Position >= 2.9, TimeSpan.FromSeconds(5)); checks["httpSeeking"] = true;

            model.OpenSources([new Uri(server, "exhaust").AbsoluteUri]);
            await WaitFor(() => model.Status.StartsWith("Stream reconnection stopped", StringComparison.Ordinal), TimeSpan.FromSeconds(25));
            checks["retryBudgetExhausted"] = true;

            model.OpenSources([new Uri(server, "cancel").AbsoluteUri]);
            await WaitFor(() => model.Status.StartsWith("Reconnecting stream", StringComparison.Ordinal), TimeSpan.FromSeconds(10));
            model.StopCommand.Execute(null);
            await Task.Delay(4500); // The fixture driver separately asserts no additional request occurred.
            checks["stopCancelledRetries"] = model.Status == "Stopped";

            await PlayAsync("radio");
            await WaitFor(() => model.LastSnapshot is { Live: true, Seekable: false } && !model.Seekable, TimeSpan.FromSeconds(10));
            checks["liveControls"] = model.TimeLabel.EndsWith("LIVE", StringComparison.Ordinal);
            await PlayAsync("hls/index.m3u8"); checks["unencryptedHls"] = model.LastSnapshot?.Duration > 7;
            await PlayAsync("dash/index.mpd"); checks["unencryptedDash"] = model.LastSnapshot?.Duration > 7;

            checks["all"] = checks.Values.All(x => x);
            File.WriteAllText(output, JsonSerializer.Serialize(new { checks, limitations = "Controlled loopback HTTP fixtures on Windows Server. No Internet-service, TLS-authentication, RTSP, hardware-output or Windows 11 qualification." }, new JsonSerializerOptions { WriteIndented = true }));
            Close(); Application.Current.Shutdown(checks["all"] ? 0 : 1);
        }
        catch (Exception ex)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { checks, status = model.Status, snapshot = model.LastSnapshot, error = Redaction.Clean(ex.ToString()) }, new JsonSerializerOptions { WriteIndented = true }));
            Close(); Application.Current.Shutdown(1);
        }
    }
}
