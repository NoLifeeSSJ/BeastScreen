namespace BeastScreen.Core.Library;

public sealed class LibraryScanner(LibraryDatabase database, MediaInspector? inspector = null) : IDisposable
{
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".aac", ".m4a", ".flac", ".wav", ".ogg", ".opus", ".alac", ".wma", ".aiff", ".aif", ".ac3", ".eac3", ".dts" };
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".mpeg", ".mpg", ".ts", ".m2ts", ".mts", ".wmv", ".flv", ".ogv", ".vob", ".3gp", ".m4v" };
    private readonly List<FileSystemWatcher> watchers = [];
    public event Action? FoldersChanged;
    public event Action<string>? Skipped;

    public Task ScanAsync(string folder, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected library folder is unavailable.");
        database.AddFolder(root);
        var count = 0;
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0) stack.Push(subdirectory);
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(file);
                    if (!AudioExtensions.Contains(ext) && !VideoExtensions.Contains(ext)) continue;
                    var info = new FileInfo(file);
                    if (!database.IsUnchanged(file, info.Length, info.LastWriteTimeUtc.Ticks))
                    {
                        var metadata = new LocalMetadata();
                        if (inspector?.Available == true)
                            try { metadata = await inspector.ReadMetadataAsync(file, cancellationToken); }
                            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { }
                        database.Upsert(file, metadata.Title.Length > 0 ? metadata.Title : Path.GetFileNameWithoutExtension(file), AudioExtensions.Contains(ext) ? "Music" : "Video", info.Length, info.LastWriteTimeUtc.Ticks, metadata.Artist, metadata.Album, metadata.Genre);
                    }
                    count++;
                    if (count % 25 == 0) progress?.Report(new(count, file, false));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skipped?.Invoke(directory); }
        }
        // Check only this root; never remove records or source media.
        for (var offset = 0; ; offset += 500)
        {
            var page = database.Query(new(Limit: 500, Offset: offset)); if (page.Count == 0) break;
            foreach (var item in page)
                if (item.Path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !File.Exists(item.Path)) database.MarkMissing(item.Path);
        }
        progress?.Report(new(count, root, true));
    }, cancellationToken);

    public void Watch()
    {
        foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
        foreach (var folder in database.Folders().Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName };
            watcher.Changed += OnChange; watcher.Created += OnChange; watcher.Deleted += OnChange; watcher.Renamed += OnChange; watcher.Error += (_, _) => FoldersChanged?.Invoke();
            watcher.EnableRaisingEvents = true; watchers.Add(watcher);
        }
    }
    private void OnChange(object sender, FileSystemEventArgs e) => FoldersChanged?.Invoke();
    public void Dispose() { foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); }
}
