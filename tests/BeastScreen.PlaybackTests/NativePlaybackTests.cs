using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using BeastScreen.Core.Playback;
using BeastScreen.Testing;
using Xunit;

namespace BeastScreen.PlaybackTests;

public sealed class NativePlaybackTests
{
    private static string Repository => Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
    private static string Fixtures => Path.Combine(Repository, "artifacts", "fixtures", "extended");
    private static Dictionary<string, string> Options => new() { ["vo"] = "null", ["ao"] = "null", ["hwdec"] = "no", ["gpu-api"] = "auto" };
    private static string PlayerExecutable => Path.Combine(Repository, "src", "BeastScreen.Desktop", "bin", "Release", "net10.0-windows10.0.22000.0", "win-x64", "BeastScreen.exe");

    [Theory]
    [InlineData("h264.mp4")]
    [InlineData("hevc.mkv")]
    [InlineData("av1.mkv")]
    [InlineData("vp9.webm")]
    [InlineData("mpeg2.mpg")]
    public async Task DecodesVideoIntoActualPngFrames(string fixture)
    {
        RequireWindows();
        var output = Path.Combine(Path.GetTempPath(), "BeastScreen-decoded-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        try
        {
            using var engine = new MpvEngine(); var failed = new ConcurrentQueue<string>();
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Failed += message => { failed.Enqueue(message); ended.TrySetException(new InvalidDataException(message)); };
            engine.Ended += () => ended.TrySetResult();
            var options = Options; options["vo"] = "image"; options["vo-image-outdir"] = output; options["vo-image-format"] = "png";
            engine.Initialize(0, options); engine.Open(Path.Combine(Fixtures, fixture));
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var frames = Directory.GetFiles(output, "*.png"); Assert.True(frames.Length >= 2, "The engine did not decode multiple frames."); Assert.Empty(failed);
            var header = await File.ReadAllBytesAsync(frames[0]); Assert.True(header.Length > 100); Assert.Equal(160, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4))); Assert.Equal(96, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
        }
        finally { Directory.Delete(output, recursive: true); }
    }

    [Theory]
    [InlineData("aac.m4a")]
    [InlineData("flac.flac")]
    [InlineData("mp3.mp3")]
    [InlineData("vorbis.ogg")]
    [InlineData("opus.opus")]
    public async Task DecodesAudioIntoPcmWithNullOutput(string fixture)
    {
        RequireWindows(); using var engine = new MpvEngine();
        var decoded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Failed += error => decoded.TrySetException(new InvalidDataException(error));
        engine.Changed += snapshot => { var format = engine.Get("audio-params/format"); if (snapshot.Position > 0.1 && format.Length > 0) decoded.TrySetResult(format); };
        engine.Initialize(0, Options); engine.Open(Path.Combine(Fixtures, fixture));
        Assert.NotEmpty(await decoded.Task.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Theory] [InlineData("missing.mkv")] [InlineData("corrupt.mkv")]
    public async Task InvalidMediaReportsFailureAndNativeClientCanBeDisposed(string fixture)
    {
        RequireWindows(); using var engine = new MpvEngine(); var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Failed += message => failure.TrySetResult(message); engine.Initialize(0, Options); engine.Open(Path.Combine(Fixtures, fixture));
        Assert.Contains("Playback failed", await failure.Task.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [Fact] public async Task ActualPlayerRecoversStopsRetriesCancelsAndPlaysAdaptiveStreams()
    {
        RequireWindows(); await using var server = new LoopbackMediaServer(Fixtures);
        server.FailNext("/recover", 1); server.FailNext("/exhaust", 100); server.FailNext("/cancel", 100);
        var reportPath = Path.Combine(Repository, "artifacts", "test-results", "network-playback.json"); Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        if (File.Exists(reportPath)) File.Delete(reportPath);
        using var player = new Process { StartInfo = new() { FileName = PlayerExecutable, UseShellExecute = false } };
        foreach (var argument in new[] { "--network-smoke-test", server.Address.AbsoluteUri, "--result", reportPath }) player.StartInfo.ArgumentList.Add(argument);
        var started = false;
        try
        {
            player.Start(); started = true; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await player.WaitForExitAsync(timeout.Token);
            Assert.True(File.Exists(reportPath), "The actual player did not write a network test report.");
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            Assert.True(player.ExitCode == 0, result.RootElement + "\nFixture request counts: " + JsonSerializer.Serialize(server.RequestCounts));
            Assert.True(result.RootElement.GetProperty("checks").GetProperty("all").GetBoolean());
            Assert.Equal(1, server.FailureCount("/recover")); Assert.True(server.RequestCount("/recover") >= 2);
            Assert.Equal(4, server.RequestCount("/exhaust"));
            Assert.Equal(1, server.RequestCount("/cancel"));
            Assert.True(server.RequestCount("/hls/index.m3u8") > 0); Assert.True(server.RequestCount("/dash/index.mpd") > 0); Assert.True(server.RequestCount("/radio") > 0);
        }
        finally { if (started && !player.HasExited) player.Kill(entireProcessTree: true); }
    }

    [Fact] public async Task FailedPlayerSmokeWritesFailureAndExitsNonzero()
    {
        RequireWindows();
        var report = Path.Combine(Repository, "artifacts", "test-results", "expected-smoke-failure.json"); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        if (File.Exists(report)) File.Delete(report);
        using var player = new Process { StartInfo = new() { FileName = PlayerExecutable, UseShellExecute = false } };
        foreach (var arg in new[] { "--smoke-test", Path.Combine(Fixtures, Guid.NewGuid().ToString("N") + ".mkv"), "--result", report }) player.StartInfo.ArgumentList.Add(arg);
        var started = false;
        try
        {
            player.Start(); started = true; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await player.WaitForExitAsync(timeout.Token);
            Assert.Equal(1, player.ExitCode);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(report)); Assert.False(result.RootElement.GetProperty("checks").GetProperty("all").GetBoolean());
        }
        finally { if (started && !player.HasExited) player.Kill(entireProcessTree: true); }
    }

    [Fact] public async Task FfmpegDecodesTheControlledDashFixture()
    {
        await using var server = new LoopbackMediaServer(Fixtures);
        var tool = OperatingSystem.IsWindows() ? Path.Combine(Repository, ".native", "tools", "ffmpeg.exe") : "/usr/bin/ffmpeg";
        using var decoder = new Process { StartInfo = new() { FileName = tool, UseShellExecute = false, RedirectStandardError = true } };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", server.Url("dash/index.mpd"), "-map", "0:v:0", "-frames:v", "2", "-an", "-f", "null", "-" }) decoder.StartInfo.ArgumentList.Add(arg);
        var started = false;
        try
        {
            decoder.Start(); started = true; var error = decoder.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await decoder.WaitForExitAsync(timeout.Token);
            Assert.True(decoder.ExitCode == 0, await error);
            Assert.True(server.RequestCount("/dash/index.mpd") > 0);
        }
        finally { if (started && !decoder.HasExited) decoder.Kill(entireProcessTree: true); }
    }

    private static void RequireWindows() => Assert.True(OperatingSystem.IsWindows(), "This suite must execute on Windows with the pinned native DLLs and generated fixtures. Linux only cross-builds it; no tests are silently skipped.");
}
