using System.Diagnostics;
using BeastScreen.Core.Library;
using Xunit;

namespace BeastScreen.Tests;

public sealed class MediaInspectorTests
{
    private static string Tool => Environment.GetEnvironmentVariable("BEASTSCREEN_TEST_FFMPEG") ?? (OperatingSystem.IsWindows() ? Path.GetFullPath("../../../../../.native/tools/ffmpeg.exe", AppContext.BaseDirectory) : "/usr/bin/ffmpeg");
    private static async Task GenerateAsync(params string[] arguments)
    {
        Assert.True(File.Exists(Tool), "Restore the pinned FFmpeg helper before running integration tests.");
        using var process = new Process { StartInfo = new() { FileName = Tool, UseShellExecute = false, RedirectStandardError = true } };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-y" }.Concat(arguments)) process.StartInfo.ArgumentList.Add(arg);
        process.Start(); var error = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await error);
    }
    [Fact] public async Task ReadsRealEmbeddedAudioTagsWithoutChangingTheOriginal()
    {
        using var temp = new TemporaryDirectory(); var file = temp.File("music 映画.flac");
        await GenerateAsync("-f", "lavfi", "-i", "sine=frequency=440:duration=0.2", "-c:a", "flac", "-metadata", "title=An = unusual title", "-metadata", "artist=Local artist", "-metadata", "album=Offline album", "-metadata", "genre=Test", file);
        var before = await File.ReadAllBytesAsync(file); using var inspector = new MediaInspector(Tool, temp.File("cache")); var tags = await inspector.ReadMetadataAsync(file, CancellationToken.None);
        Assert.Equal("An = unusual title", tags.Title); Assert.Equal("Local artist", tags.Artist); Assert.Equal("Offline album", tags.Album); Assert.Equal("Test", tags.Genre); Assert.Equal(before, await File.ReadAllBytesAsync(file));
    }
    [Fact] public async Task GeneratesAndCachesAnActualVideoFrameForAnExtensionlessSource()
    {
        using var temp = new TemporaryDirectory(); var file = temp.File("video without extension");
        await GenerateAsync("-f", "lavfi", "-i", "testsrc2=size=160x90:duration=0.2", "-c:v", "mpeg4", "-f", "matroska", file);
        using var inspector = new MediaInspector(Tool, temp.File("cache")); var result = await inspector.CreateThumbnailAsync(file, 123, CancellationToken.None); Assert.NotNull(result);
        var bytes = await File.ReadAllBytesAsync(result!); Assert.True(bytes.Length > 100); Assert.Equal(0xff, bytes[0]); Assert.Equal(0xd8, bytes[1]); Assert.Equal(result, await inspector.CreateThumbnailAsync(file, 123, CancellationToken.None));
    }
    [Fact] public async Task CorruptAndMissingFilesFailWithoutLeavingPartialThumbnails()
    {
        using var temp = new TemporaryDirectory(); var file = temp.File("corrupt.mkv"); await File.WriteAllTextAsync(file, "not media"); using var inspector = new MediaInspector(Tool, temp.File("cache"));
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.ReadMetadataAsync(file, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.CreateThumbnailAsync(file, 0, CancellationToken.None)); Assert.Empty(Directory.GetFiles(temp.File("cache")));
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.ReadMetadataAsync(temp.File("missing.mkv"), CancellationToken.None));
    }
    [Fact] public async Task CancellationStopsInspection()
    {
        using var temp = new TemporaryDirectory(); using var inspector = new MediaInspector(Tool, temp.File("cache")); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.CreateThumbnailAsync(temp.File("missing"), 0, cancellation.Token));
    }
}
