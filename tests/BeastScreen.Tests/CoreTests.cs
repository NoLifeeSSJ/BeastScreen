using System.Diagnostics;
using BeastScreen.Core.Library;
using BeastScreen.Core.Playlists;
using BeastScreen.Core.Security;
using BeastScreen.Core.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BeastScreen.Tests;

public sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BeastScreen-tests-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, true);
}

public sealed class DatabaseTests : IDisposable
{
    private readonly TemporaryDirectory temp = new();
    private LibraryDatabase Create() => new(temp.File("library.sqlite"));
    [Fact] public void ScanUpdatesPreserveResumeAndFavourites()
    {
        var db = Create(); db.Upsert("映画.mkv", "映画", "Video", 10, 1); db.SaveProgress("映画.mkv", 35, 120); db.ToggleFavourite("映画.mkv"); db.MarkMissing("映画.mkv");
        db.Upsert("映画.mkv", "New title", "Video", 11, 2);
        var item = Assert.Single(db.Query(new())); Assert.Equal(35, item.Resume); Assert.True(item.Favourite); Assert.False(item.Missing); Assert.Equal(11, item.Size);
    }
    [Fact] public void SearchIsLiteralAndParametersCannotInjectSql()
    {
        var db = Create(); db.Upsert("a", "100%_real", "Video"); db.Upsert("b", "Other", "Music");
        Assert.Single(db.Query(new(Search: "%_"))); Assert.Empty(db.Query(new(Search: "' OR 1=1 --"))); Assert.Single(db.Query(new(View: "Music")));
    }
    [Fact] public void BackupAndRestoreKeepHistoryBookmarksAndPlaylists()
    {
        var db = Create(); db.Upsert("movie", "Movie", "Video"); db.SaveProgress("movie", 41, 180); db.AddBookmark("movie", 39, "Scene"); db.SavePlaylist("Evening", ["movie", "other"]);
        var backup = temp.File("backup.sqlite"); db.Backup(backup); db.Upsert("new", "New", "Music"); db.Restore(backup);
        Assert.Single(db.Query(new())); Assert.Equal(41, db.Progress("movie").Resume); Assert.Equal(39, Assert.Single(db.Bookmarks("movie")).Position);
        var playlist = Assert.Single(db.Playlists()); Assert.Equal(new[] { "movie", "other" }, db.LoadPlaylist(playlist.Id)); Assert.True(System.IO.File.Exists(temp.File("library.sqlite.before-restore.bak")));
    }
    [Fact] public void NewerSchemaIsRejectedWithoutChangingDatabase()
    {
        var db = Create(); db.Upsert("a", "A", "Video");
        using (var c = new SqliteConnection("Data Source=" + temp.File("library.sqlite") + ";Pooling=False")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery(); }
        Assert.Throws<InvalidOperationException>(() => Create());
        using var check = new SqliteConnection("Data Source=" + temp.File("library.sqlite") + ";Pooling=False"); check.Open(); using var version = check.CreateCommand(); version.CommandText = "PRAGMA user_version"; Assert.Equal(99L, version.ExecuteScalar());
    }
    [Fact] public void IncompatibleRestoreKeepsCurrentLibrary()
    {
        var db = Create(); db.Upsert("a", "Keep me", "Video"); var other = temp.File("bad.sqlite");
        using (var c = new SqliteConnection("Data Source=" + other + ";Pooling=False")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version=500; CREATE TABLE junk(id INTEGER);"; cmd.ExecuteNonQuery(); }
        Assert.Throws<InvalidDataException>(() => db.Restore(other)); Assert.Equal("Keep me", Assert.Single(db.Query(new())).Title);
    }
    [Fact] public void RelocateUsesFolderBoundaryAndUpdatesDependentRecords()
    {
        var db = Create(); var old = temp.File("old"); var next = temp.File("new"); var movie = System.IO.Path.Combine(old, "film.mkv"); var other = temp.File("old-other/film.mkv");
        db.AddFolder(old); db.Upsert(movie, "Film", "Video"); db.Upsert(other, "Other", "Video"); db.AddBookmark(movie, 12, "Here"); db.SavePlaylist("P", [movie]); db.RelocateFolder(old, next);
        var relocated = System.IO.Path.Combine(next, "film.mkv"); Assert.Equal(relocated, db.Query(new(Search: "Film")).Single().Path); Assert.Equal(other, db.Query(new(Search: "Other")).Single().Path); Assert.Single(db.Bookmarks(relocated)); Assert.Equal(relocated, db.LoadPlaylist(db.Playlists().Single().Id).Single());
    }
    [Fact] public void TenThousandRecordsUseBoundedPages()
    {
        var db = Create();
        using (var c = new SqliteConnection("Data Source=" + temp.File("library.sqlite") + ";Pooling=False"))
        {
            c.Open(); using var tx = c.BeginTransaction();
            using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO media(path,title,kind,added) VALUES($p,$t,'Video',0)"; cmd.Parameters.Add("$p", SqliteType.Text); cmd.Parameters.Add("$t", SqliteType.Text);
            for (var i = 0; i < 10000; i++) { cmd.Parameters["$p"].Value = $"/{i}.mkv"; cmd.Parameters["$t"].Value = $"Film {i:D5}"; cmd.ExecuteNonQuery(); } tx.Commit();
        }
        var clock = Stopwatch.StartNew(); var page = db.Query(new(Offset: 9800)); clock.Stop(); Assert.Equal(200, page.Count); Assert.Equal("Film 09800", page[0].Title); Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Query took {clock.Elapsed}"); Assert.Equal(500, db.Query(new(Limit: int.MaxValue)).Count);
    }
    [Fact] public void NonFinitePlaybackProgressCannotCorruptLibrary()
    {
        var db = Create(); db.Upsert("a", "A", "Video"); db.SaveProgress("a", double.NaN, double.PositiveInfinity); Assert.Equal((0d, 0d), db.Progress("a"));
    }
    public void Dispose() => temp.Dispose();
}

public sealed class PlaylistTests : IDisposable
{
    private readonly TemporaryDirectory temp = new();
    [Theory] [InlineData("m3u8")] [InlineData("pls")]
    public void RoundTripUnicodeSpacesAndUrls(string extension)
    {
        var file = temp.File("list." + extension); var items = new[] { temp.File("映画 one.mkv"), "https://example.com/live/index.m3u8?x=y" }; PlaylistFile.Save(file, items); Assert.Equal(items, PlaylistFile.Load(file));
    }
    [Fact] public void RelativeNestedItemsResolveAgainstTheirOwnPlaylist()
    {
        Directory.CreateDirectory(temp.File("sub")); System.IO.File.WriteAllText(temp.File("list.m3u"), "#EXTM3U\nsub/nested.m3u8\n"); System.IO.File.WriteAllText(temp.File("sub/nested.m3u8"), "../movie.mkv\n"); Assert.Equal(temp.File("movie.mkv"), Assert.Single(PlaylistFile.Load(temp.File("list.m3u"))));
    }
    [Fact] public void CyclicPlaylistsFailInsteadOfRecursingForever()
    {
        var file = temp.File("loop.m3u"); System.IO.File.WriteAllText(file, "loop.m3u"); Assert.Throws<InvalidDataException>(() => PlaylistFile.Load(file));
    }
    [Fact] public void OversizedPlaylistIsRejectedBeforeReading()
    {
        var file = temp.File("huge.m3u"); using (var stream = System.IO.File.Create(file)) stream.SetLength(PlaylistFile.MaxBytes + 1); Assert.Throws<InvalidDataException>(() => PlaylistFile.Load(file));
    }
    [Fact] public void ExecutableMpvProtocolsAreRejectedInPlaylists()
    {
        var file = temp.File("unsafe.m3u"); System.IO.File.WriteAllText(file, "edl://!no_clip;https://example.com/video"); Assert.Throws<ArgumentException>(() => PlaylistFile.Load(file));
    }
    [Fact] public void PlsUsesNumericOrder()
    {
        var file = temp.File("radio.pls"); System.IO.File.WriteAllText(file, "[playlist]\nFile10=https://example.com/10\nFile2=https://example.com/2\nTitle2=Radio\n"); Assert.Equal(new[] { "https://example.com/2", "https://example.com/10" }, PlaylistFile.Load(file));
    }
    [Fact] public void LocalHlsIsPassedToTheEngineAsOneStream()
    {
        var file = temp.File("stream.m3u8"); System.IO.File.WriteAllText(file, "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\nsegment1.ts\n#EXT-X-ENDLIST\n"); Assert.Equal(file, Assert.Single(PlaylistFile.Load(file)));
    }
    public void Dispose() => temp.Dispose();
}

public sealed class QueueTests
{
    [Fact] public void NetworkReconnectsHaveAFiniteExponentialBudget()
    {
        var budget = new BeastScreen.Core.Playback.ReconnectBudget(); Assert.Equal(TimeSpan.FromSeconds(1), budget.TakeDelay()); Assert.Equal(TimeSpan.FromSeconds(2), budget.TakeDelay()); Assert.Equal(TimeSpan.FromSeconds(4), budget.TakeDelay()); Assert.Null(budget.TakeDelay()); budget.Reset(); Assert.Equal(TimeSpan.FromSeconds(1), budget.TakeDelay());
    }
    [Fact] public void NaturalEndStopsUnlessRepeatIsEnabled()
    {
        var queue = new PlaybackQueue(); queue.Replace(["a", "b"]); Assert.Equal("a", queue.Next()); Assert.Equal("b", queue.Next()); Assert.Null(queue.Next(true)); queue.Repeat = RepeatMode.All; Assert.Equal("a", queue.Next(true)); queue.Repeat = RepeatMode.One; Assert.Equal("a", queue.Next(true)); Assert.Equal("b", queue.Next());
    }
    [Fact] public void ReorderKeepsCurrentItemAndPreviousWorks()
    {
        var queue = new PlaybackQueue(); queue.Replace(["a", "b", "c"]); queue.Select(1); queue.Move(1, 0); Assert.Equal("b", queue.Current); Assert.Equal(0, queue.Index); Assert.Equal("a", queue.Next()); Assert.Equal("b", queue.Previous());
    }
    [Fact] public void ShuffleNeverChoosesCurrentWhenAlternativesExist()
    {
        var queue = new PlaybackQueue { Shuffle = true }; queue.Replace(["a", "b", "c"]); queue.Select(0);
        for (var i = 0; i < 100; i++) { var before = queue.Current; Assert.NotEqual(before, queue.Next()); }
    }
}

public sealed class SecurityAndSettingsTests
{
    [Theory] [InlineData("edl://foo")] [InlineData("lavfi://movie=/etc/passwd")] [InlineData("file:///etc/passwd")] [InlineData("javascript://alert(1)")] [InlineData("relative.mkv")] [InlineData("https://example.com/x\n--script=bad")]
    public void UnsafeSourcesAreRejected(string value) => Assert.Throws<ArgumentException>(() => SourcePolicy.Validate(value));
    [Theory] [InlineData("https://example.com/video?token=secret")] [InlineData("rtsp://user:secret@example.com/live")]
    public void DirectMediaUrlsAreAccepted(string value) => SourcePolicy.Validate(value);
    [Fact] public void RedactionRemovesUserInfoPathAndSensitiveQuery()
    {
        var value = Redaction.Clean("Failed https://user:password@example.com/private/media?token=secret&key=abc"); Assert.DoesNotContain("password", value); Assert.DoesNotContain("secret", value); Assert.DoesNotContain("private", value); Assert.Contains("example.com", value);
    }
    [Fact] public void SettingsRoundTripAndInvalidJsonIsPreserved()
    {
        using var temp = new TemporaryDirectory(); var file = temp.File("settings.json"); var settings = new AppSettings { ScreenshotFolder = temp.Path, Volume = 42, SeekSeconds = 25 }; settings.Save(file); Assert.Equal(42, AppSettings.Load(file).Volume);
        System.IO.File.WriteAllText(file, "broken JSON"); Assert.Equal(70, AppSettings.Load(file).Volume); Assert.Single(Directory.GetFiles(temp.Path, "*.invalid-*"));
    }
    [Theory] [InlineData("0,0,0,0,NaN")] [InlineData("0,0,0,0,99")] [InlineData("0,0,0")]
    public void UnsafeEqualizerGainsAreRejected(string value)
    {
        var settings = new AppSettings { ScreenshotFolder = System.IO.Path.GetTempPath(), Equalizer = value }; Assert.Throws<ArgumentException>(settings.Validate);
    }
    [Fact] public async Task ScannerOnlyIndexesSelectedFolderAndCancellationPreservesMedia()
    {
        using var temp = new TemporaryDirectory(); var root = temp.File("media"); Directory.CreateDirectory(root); var movie = System.IO.Path.Combine(root, "映画.mkv"); System.IO.File.WriteAllText(movie, "fixture"); System.IO.File.WriteAllText(System.IO.Path.Combine(root, "notes.txt"), "notes");
        var db = new LibraryDatabase(temp.File("library.sqlite")); using var scanner = new LibraryScanner(db); await scanner.ScanAsync(root, null, CancellationToken.None); Assert.Equal(movie, Assert.Single(db.Query(new())).Path);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(root, null, cancelled.Token)); Assert.True(System.IO.File.Exists(movie));
        System.IO.File.Delete(movie); await scanner.ScanAsync(root, null, CancellationToken.None); Assert.True(Assert.Single(db.Query(new())).Missing);
    }
}
