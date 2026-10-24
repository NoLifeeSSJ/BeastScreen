using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using BeastScreen.Core.Library;
using BeastScreen.Core.Playback;
using BeastScreen.Core.Playlists;
using BeastScreen.Core.Security;
using BeastScreen.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BeastScreen.Desktop.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeastScreen");
    private readonly string settingsPath;
    private readonly Dispatcher dispatcher;
    private readonly LibraryScanner scanner;
    private readonly MediaInspector inspector;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<string> thumbnailsAttempted = [];
    private readonly ReconnectBudget reconnect = new();
    private CancellationTokenSource reconnectCancellation = new();
    private readonly DispatcherTimer progressTimer;
    private readonly DispatcherTimer scanDebounce;
    private CancellationTokenSource? scanCancellation;
    private readonly PlaybackQueue queue = new();
    private bool ready;
    private bool updating;
    private readonly bool smokeTest;
    private double position;
    private double duration;
    private double? loopStart;
    private int page;
    private string? source;
    public IPlaybackEngine Engine { get; }
    public AppSettings Preferences { get; }
    public LibraryDatabase Database { get; }
    public event Action<string>? UiRequested;
    public event Action? FullscreenRequested;
    public ObservableCollection<MediaItem> Library { get; } = [];
    public ObservableCollection<string> Queue { get; } = [];
    public ObservableCollection<MediaTrack> AudioTracks { get; } = [];
    public ObservableCollection<MediaTrack> SubtitleTracks { get; } = [];
    public ObservableCollection<MediaTrack> VideoTracks { get; } = [];
    public ObservableCollection<Chapter> Chapters { get; } = [];
    public ObservableCollection<Bookmark> Bookmarks { get; } = [];
    public ObservableCollection<string> SavedPlaylists { get; } = [];
    public ObservableCollection<string> AudioDevices { get; } = ["auto"];
    public string[] Navigation { get; } = ["Home", "Videos", "Music", "Playlists", "Streams", "History", "Favourites", "Continue watching", "Recently added", "Missing files", "Settings"];
    public string[] Sorts { get; } = ["title", "added", "played", "artist", "album", "genre"];
    public string[] Speeds { get; } = ["0.25", "0.5", "0.75", "1", "1.25", "1.5", "2", "3", "4"];
    [ObservableProperty] private string view = "Home";
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string sort = "title";
    [ObservableProperty] private string title = "Your media. Your screen.";
    [ObservableProperty] private string status = "Open a file, choose a library folder, or paste a direct media URL.";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private string diagnostics = "No media loaded";
    [ObservableProperty] private string timeLabel = "00:00 / —";
    [ObservableProperty] private string playLabel = "Play";
    [ObservableProperty] private bool seekable;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private double seekPosition;
    [ObservableProperty] private double volume = 70;
    [ObservableProperty] private bool muted;
    [ObservableProperty] private bool shuffle;
    [ObservableProperty] private string repeat = "Off";
    [ObservableProperty] private string speed = "1";
    [ObservableProperty] private bool resumeEnabled = true;
    [ObservableProperty] private string audioDevice = "auto";
    [ObservableProperty] private double audioDelay;
    [ObservableProperty] private double subtitleDelay;
    [ObservableProperty] private string aspect = "-1";
    [ObservableProperty] private double zoom;
    [ObservableProperty] private string rotation = "0";
    [ObservableProperty] private bool deinterlace;
    [ObservableProperty] private double brightness;
    [ObservableProperty] private double contrast;
    [ObservableProperty] private double saturation;
    [ObservableProperty] private MediaItem? selectedMedia;
    [ObservableProperty] private int selectedQueueIndex = -1;
    [ObservableProperty] private MediaTrack? selectedAudio;
    [ObservableProperty] private MediaTrack? selectedSubtitle;
    [ObservableProperty] private MediaTrack? selectedVideo;
    [ObservableProperty] private Chapter? selectedChapter;
    [ObservableProperty] private Bookmark? selectedBookmark;
    [ObservableProperty] private string? selectedSavedPlaylist;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsSettings => View == "Settings";
    public bool IsPlaylists => View == "Playlists";
    public bool LibraryEmpty => Library.Count == 0;
    public string PageLabel => $"Page {page + 1} · {Library.Count} items";
    public PlaybackSnapshot? LastSnapshot { get; private set; }

    public PlayerViewModel(Dispatcher dispatcher, bool temporary = false)
    {
        smokeTest = temporary;
        this.dispatcher = dispatcher;
        var directory = temporary ? Path.Combine(Path.GetTempPath(), "BeastScreen-smoke-" + Guid.NewGuid().ToString("N")) : DataDirectory;
        Directory.CreateDirectory(directory); settingsPath = Path.Combine(directory, "settings.json");
        Preferences = AppSettings.Load(settingsPath); volume = Preferences.Volume; resumeEnabled = Preferences.Resume;
        Database = new(Path.Combine(directory, "library.db"));
        inspector = new(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"), Path.Combine(directory, "cache")); scanner = new(Database, inspector);
        scanner.FoldersChanged += () => dispatcher.InvokeAsync(() => { scanDebounce!.Stop(); scanDebounce.Start(); });
        scanner.Skipped += folder => dispatcher.InvokeAsync(() => Status = "Some inaccessible folders were skipped; see your folder permissions.");
        scanDebounce = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => { scanDebounce!.Stop(); if (!IsScanning) await RescanAsync(); }, dispatcher); scanDebounce.Stop();
        Engine = new MpvEngine(diagnostics: temporary);
        Engine.Changed += snapshot => dispatcher.InvokeAsync(() => OnSnapshot(snapshot));
        Engine.Failed += message => dispatcher.InvokeAsync(async () => { Error = message; Status = "Playback could not continue. Check the source and try again."; await ReconnectAsync(); });
        Engine.Ended += () => dispatcher.InvokeAsync(() => { SaveProgress(true); var next = queue.Next(true); if (next is not null) PlaySource(next, restart: true); });
        Engine.TracksChanged += (tracks, chapters) => dispatcher.InvokeAsync(() => UpdateTracks(tracks, chapters));
        progressTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => SaveProgress(), dispatcher); progressTimer.Start();
        RefreshLibrary(); RefreshPlaylists(); scanner.Watch();
    }

    public void Initialize(nint hwnd)
    {
        try
        {
            Engine.Initialize(hwnd, smokeTest ? new Dictionary<string, string> { ["d3d11-warp"] = "yes", ["hwdec"] = "no", ["ao"] = "null" } : null); ready = true; ApplyPreferences(); Status = "Ready for local files and direct streams.";
            if (queue.Items.Count > 0) PlaySource(queue.Select(0)!);
        }
        catch (Exception ex) { Error = Redaction.Clean(ex.Message); Status = "Playback unavailable. Install the native dependency or run Repair."; }
    }
    private void OnSnapshot(PlaybackSnapshot s)
    {
        LastSnapshot = s; updating = true; position = s.Position; duration = s.Duration;
        Seekable = s.Seekable && s.Duration > 0; SeekPosition = s.Duration > 0 ? s.Position / s.Duration * 100 : 0;
        TimeLabel = $"{FormatTime(s.Position)} / {(s.Live ? "LIVE" : s.Duration > 0 ? FormatTime(s.Duration) : "—")}";
        PlayLabel = s.State == PlaybackState.Playing ? "Pause" : "Play"; Diagnostics = s.Diagnostics;
        if (!string.IsNullOrWhiteSpace(s.Title)) Title = s.Title;
        Volume = s.Volume; Muted = s.Muted; updating = false;
        if (s.State is PlaybackState.Playing or PlaybackState.Paused) Status = s.Live ? "Live stream · seeking unavailable" : s.State.ToString();
    }
    private static string FormatTime(double value) => TimeSpan.FromSeconds(Math.Clamp(value, 0, 3600000)).ToString(value >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");

    public void OpenSources(IEnumerable<string> paths)
    {
        Guard(() =>
        {
            var sources = new List<string>();
            foreach (var p in paths.Where(p => !p.StartsWith("--", StringComparison.Ordinal)).Take(PlaylistFile.MaxItems))
            {
                if (PlaylistFile.IsPlaylist(p) && !SourcePolicy.IsNetwork(p)) sources.AddRange(PlaylistFile.Load(p).Take(PlaylistFile.MaxItems - sources.Count));
                else { SourcePolicy.Validate(p); sources.Add(p); }
                if (sources.Count >= PlaylistFile.MaxItems) break;
            }
            if (sources.Count == 0) return;
            SaveProgress(); queue.Replace(sources); RefreshQueue(); if (ready) PlaySource(queue.Select(0)!);
        });
    }
    private void PlaySource(string value, bool restart = false, bool retry = false)
    {
        Guard(() =>
        {
            SaveProgress();
            if (!retry) { reconnectCancellation.Cancel(); reconnectCancellation.Dispose(); reconnectCancellation = new(); reconnect.Reset(); }
            if (!SourcePolicy.IsNetwork(value) && !File.Exists(value)) { Database.MarkMissing(value); throw new FileNotFoundException("The media file is unavailable. Reconnect its drive or relocate the library folder."); }
            var network = SourcePolicy.IsNetwork(value); source = value; Error = "";
            if (!HasPrivateUrl(value)) Database.Upsert(value, network ? Redaction.Clean(value) : Path.GetFileNameWithoutExtension(value), network ? "Stream" : LibraryScanner.AudioExtensions.Contains(Path.GetExtension(value)) ? "Music" : "Video");
            var progress = Database.Progress(value); var resume = !restart && ResumeEnabled && (progress.Duration == 0 || progress.Resume < progress.Duration - 10) ? progress.Resume : 0;
            position = resume; duration = progress.Duration;
            LastSnapshot = null; Seekable = false; SeekPosition = 0; TimeLabel = "Loading…";
            Engine.Open(value, resume); SelectedQueueIndex = queue.Index; RefreshBookmarks();
        });
    }
    private void SaveProgress(bool completed = false)
    {
        if (source is null || HasPrivateUrl(source)) return;
        var unseekable = LastSnapshot is { Seekable: false, State: PlaybackState.Playing or PlaybackState.Paused };
        Guard(() => Database.SaveProgress(source, completed || unseekable ? 0 : position, unseekable ? 0 : duration));
    }
    private static bool HasPrivateUrl(string value) => SourcePolicy.IsNetwork(value) && Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.UserInfo.Length > 0 || url.Query.Length > 0);
    private async Task ReconnectAsync()
    {
        var current = source; if (current is null || !SourcePolicy.IsNetwork(current)) return;
        var delay = reconnect.TakeDelay(); if (delay is null) { Status = "Stream reconnection stopped after three attempts. Check the URL/network, then reopen to retry."; return; }
        var cancellationToken = reconnectCancellation.Token;
        try { Status = $"Reconnecting stream · attempt {reconnect.Attempts}/3. Stop cancels."; await Task.Delay(delay.Value, cancellationToken); if (!cancellationToken.IsCancellationRequested && source == current) PlaySource(current, retry: true); }
        catch (OperationCanceledException) { }
    }
    private void UpdateTracks(IReadOnlyList<MediaTrack> tracks, IReadOnlyList<Chapter> chapters)
    {
        updating = true;
        AudioTracks.Clear(); SubtitleTracks.Clear(); VideoTracks.Clear(); Chapters.Clear();
        SubtitleTracks.Add(new(0, "sub", "Off", "", false));
        foreach (var track in tracks) { if (track.Type == "audio") AudioTracks.Add(track); if (track.Type == "sub") SubtitleTracks.Add(track); if (track.Type == "video") VideoTracks.Add(track); }
        foreach (var chapter in chapters) Chapters.Add(chapter);
        SelectedAudio = AudioTracks.FirstOrDefault(t => t.Selected); SelectedSubtitle = SubtitleTracks.FirstOrDefault(t => t.Selected) ?? SubtitleTracks[0]; SelectedVideo = VideoTracks.FirstOrDefault(t => t.Selected);
        updating = false;
    }

    [RelayCommand] private void Ui(string? action) { if (action is not null) UiRequested?.Invoke(action); }
    [RelayCommand] private void PlayPause() => Command("cycle", "pause");
    [RelayCommand] private void Stop() { reconnectCancellation.Cancel(); SaveProgress(); Command("stop"); source = null; Status = "Stopped"; }
    [RelayCommand] private void Next() { var next = queue.Next(); if (next is not null) PlaySource(next, true); }
    [RelayCommand] private void Previous() { var previous = queue.Previous(); if (previous is not null) PlaySource(previous); }
    [RelayCommand] private void Restart() { if (source is not null) PlaySource(source, true); }
    [RelayCommand] private void Seek(string? direction) { if (LastSnapshot?.Seekable == true) Command("seek", ((direction == "back" ? -1 : 1) * Preferences.SeekSeconds).ToString(CultureInfo.InvariantCulture), "relative+exact"); }
    [RelayCommand] private void Frame() => Command("frame-step");
    [RelayCommand] private void Fullscreen() => FullscreenRequested?.Invoke();
    [RelayCommand] private void Screenshot()
    {
        Guard(() => { Directory.CreateDirectory(Preferences.ScreenshotFolder); Engine.Command("screenshot-to-file", Path.Combine(Preferences.ScreenshotFolder, "BeastScreen-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png"), "subtitles"); Status = "Screenshot saved to " + Preferences.ScreenshotFolder; });
    }
    [RelayCommand] private void Loop()
    {
        if (LastSnapshot?.Seekable != true) { Status = "A–B looping needs a seekable source."; return; }
        if (loopStart is null) { loopStart = position; Set("ab-loop-a", Number(position)); Status = "A set. Choose a later position and press A–B again."; }
        else if (position > loopStart) { Set("ab-loop-b", Number(position)); loopStart = null; Status = "A–B loop enabled. Use Clear loop to stop."; }
    }
    [RelayCommand] private void ClearLoop() { Set("ab-loop-a", "no"); Set("ab-loop-b", "no"); loopStart = null; }
    [RelayCommand] private void CycleRepeat() { queue.Repeat = (RepeatMode)(((int)queue.Repeat + 1) % 3); Repeat = queue.Repeat.ToString(); }
    [RelayCommand] private void PlaySelected() { if (SelectedMedia is not null) OpenSources([SelectedMedia.Path]); }
    [RelayCommand] private void PlayQueued() { var value = queue.Select(SelectedQueueIndex); if (value is not null) PlaySource(value); }
    [RelayCommand] private void MoveQueue(string? direction) { var from = SelectedQueueIndex; var to = from + (direction == "up" ? -1 : 1); queue.Move(from, to); RefreshQueue(); SelectedQueueIndex = Math.Clamp(to, -1, Queue.Count - 1); }
    [RelayCommand] private void Favourite() { if (SelectedMedia is not null) { Database.ToggleFavourite(SelectedMedia.Path); RefreshLibrary(); } }
    [RelayCommand] private void Bookmark()
    {
        if (source is null) return;
        if (HasPrivateUrl(source)) { Error = "Stream URLs containing user information or query parameters are kept in memory only and cannot be saved as bookmarks."; return; }
        if (LastSnapshot?.Seekable != true) { Error = "Bookmarks require a seekable media source."; return; }
        Database.AddBookmark(source, position, FormatTime(position)); RefreshBookmarks(); Status = "Bookmark saved.";
    }
    [RelayCommand] private void GoBookmark() { if (SelectedBookmark is not null) Command("seek", Number(SelectedBookmark.Position), "absolute+exact"); }
    [RelayCommand] private void SaveQueue() { if (Queue.Count == 0) return; if (Queue.Any(HasPrivateUrl)) { Error = "URLs containing user information or query parameters are kept in memory only. Remove private URLs before saving a library playlist. Explicit file export writes the URLs you select."; return; } Database.SavePlaylist("Playlist " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Queue.ToArray()); RefreshPlaylists(); Status = "Playlist saved in your library."; }
    [RelayCommand] private void LoadSavedPlaylist() { var item = Database.Playlists().FirstOrDefault(p => $"{p.Id} · {p.Name}" == SelectedSavedPlaylist); if (item.Id > 0) OpenSources(Database.LoadPlaylist(item.Id)); }
    [RelayCommand] private void NextPage() { if (Library.Count < 200) return; page++; RefreshLibrary(); }
    [RelayCommand] private void PreviousPage() { if (page == 0) return; page--; RefreshLibrary(); }
    [RelayCommand] private void CancelScan() => scanCancellation?.Cancel();
    [RelayCommand] private async Task RescanAsync() { foreach (var folder in Database.Folders()) { await ScanFolderAsync(folder); if (scanCancellation?.IsCancellationRequested == true) break; } }
    public async Task ScanFolderAsync(string folder)
    {
        if (IsScanning) return; IsScanning = true; scanCancellation?.Dispose(); scanCancellation = new();
        try { await scanner.ScanAsync(folder, new Progress<ScanProgress>(p => Status = $"{(p.Finished ? "Scanned" : "Scanning")} {p.Files} media files"), scanCancellation.Token); scanner.Watch(); RefreshLibrary(); }
        catch (OperationCanceledException) { Status = "Library scan cancelled. Previously indexed items were kept."; }
        catch (Exception ex) { Error = Redaction.Clean(ex.Message); }
        finally { IsScanning = false; }
    }
    [RelayCommand] private void SavePreferences() { Guard(() => { Preferences.Resume = ResumeEnabled; Preferences.Volume = Volume; Preferences.Save(settingsPath); if (ready) ApplyPreferences(); Status = "Preferences saved."; }); }
    public void ApplyPreferences()
    {
        Preferences.Validate();
        foreach (var (name, value) in new[] { ("hwdec", Preferences.HardwareDecoding), ("alang", Preferences.AudioLanguage), ("slang", Preferences.SubtitleLanguage), ("sub-font-size", Number(Preferences.SubtitleSize)), ("sub-color", Preferences.SubtitleColour), ("scale", Preferences.Scale), ("tone-mapping", Preferences.ToneMapping), ("volume", Number(Preferences.Volume)), ("audio-spdif", Preferences.Passthrough ? "ac3,eac3,dts,truehd,dts-hd" : "") }) Set(name, value);
        var gains = Preferences.Equalizer.Split(','); var frequencies = new[] { 60, 230, 910, 3600, 14000 };
        var filters = Enumerable.Range(0, 5).Select(i => $"equalizer=f={frequencies[i]}:width_type=o:width=2:g={gains[i]}").ToList();
        if (Preferences.NormalizeVolume) filters.Add("dynaudnorm");
        Set("af", "lavfi=[" + string.Join(',', filters) + "]");
    }
    public void RefreshLibrary()
    {
        Guard(() => { var items = Database.Query(new(Search, View, View is "History" or "Continue watching" ? "played" : View == "Recently added" ? "added" : Sort, page * 200)); Library.Clear(); foreach (var item in items) Library.Add(item); OnPropertyChanged(nameof(PageLabel)); OnPropertyChanged(nameof(LibraryEmpty)); _ = LoadThumbnailsAsync(items); });
    }
    private async Task LoadThumbnailsAsync(IReadOnlyList<MediaItem> items)
    {
        if (!inspector.Available) return;
        foreach (var item in items)
        {
            if (lifetime.IsCancellationRequested) return;
            if (item.Missing || SourcePolicy.IsNetwork(item.Path) || !thumbnailsAttempted.Add(MediaInspector.CacheKey(item.Path, item.ModifiedUtc))) continue;
            try
            {
                var result = await inspector.CreateThumbnailAsync(item.Path, item.ModifiedUtc, lifetime.Token);
                if (result is not null) await dispatcher.InvokeAsync(() => { var index = Library.ToList().FindIndex(i => i.Id == item.Id); if (index >= 0) Library[index] = item with { }; });
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException) { }
        }
    }
    private void RefreshQueue() { Queue.Clear(); foreach (var item in queue.Items) Queue.Add(item); }
    private void RefreshBookmarks() { Bookmarks.Clear(); if (source is not null) foreach (var bookmark in Database.Bookmarks(source)) Bookmarks.Add(bookmark); }
    private void RefreshPlaylists() { SavedPlaylists.Clear(); foreach (var p in Database.Playlists()) SavedPlaylists.Add($"{p.Id} · {p.Name}"); }
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    private void Set(string property, string value) { if (ready && !updating) Guard(() => Engine.Set(property, value)); }
    private void Command(params string[] arguments) { if (ready) Guard(() => Engine.Command(arguments)); }
    public void Guard(Action action) { try { action(); } catch (Exception ex) when (ex is not AccessViolationException) { Error = Redaction.Clean(ex.Message); } }
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnViewChanged(string value) { page = 0; RefreshLibrary(); OnPropertyChanged(nameof(IsSettings)); OnPropertyChanged(nameof(IsPlaylists)); }
    partial void OnSearchChanged(string value) { page = 0; RefreshLibrary(); }
    partial void OnSortChanged(string value) { page = 0; RefreshLibrary(); }
    partial void OnSeekPositionChanged(double value) { if (!updating && Seekable) Command("seek", Number(value), "absolute-percent+exact"); }
    partial void OnVolumeChanged(double value) { Set("volume", Number(value)); Preferences.Volume = value; }
    partial void OnMutedChanged(bool value) => Set("mute", value ? "yes" : "no");
    partial void OnShuffleChanged(bool value) => queue.Shuffle = value;
    partial void OnSpeedChanged(string value) => Set("speed", value);
    partial void OnAudioDeviceChanged(string value) => Set("audio-device", value);
    partial void OnAudioDelayChanged(double value) => Set("audio-delay", Number(value));
    partial void OnSubtitleDelayChanged(double value) => Set("sub-delay", Number(value));
    partial void OnAspectChanged(string value) => Set("video-aspect-override", value);
    partial void OnZoomChanged(double value) => Set("video-zoom", Number(value));
    partial void OnRotationChanged(string value) => Set("video-rotate", value);
    partial void OnDeinterlaceChanged(bool value) => Set("deinterlace", value ? "yes" : "no");
    partial void OnBrightnessChanged(double value) => Set("brightness", Number(value));
    partial void OnContrastChanged(double value) => Set("contrast", Number(value));
    partial void OnSaturationChanged(double value) => Set("saturation", Number(value));
    partial void OnSelectedAudioChanged(MediaTrack? value) { if (value is not null) Set("aid", value.Id.ToString(CultureInfo.InvariantCulture)); }
    partial void OnSelectedSubtitleChanged(MediaTrack? value) { if (value is not null) Set("sid", value.Id == 0 ? "no" : value.Id.ToString(CultureInfo.InvariantCulture)); }
    partial void OnSelectedVideoChanged(MediaTrack? value) { if (value is not null) Set("vid", value.Id.ToString(CultureInfo.InvariantCulture)); }
    partial void OnSelectedChapterChanged(Chapter? value) { if (value is not null) Command("seek", Number(value.Time), "absolute+exact"); }
    public void Dispose()
    {
        SaveProgress(); progressTimer.Stop(); scanDebounce.Stop(); reconnectCancellation.Cancel(); lifetime.Cancel(); scanCancellation?.Cancel(); inspector.CancelWork(); scanner.Dispose();
        Engine.Dispose(); Preferences.Volume = Volume; Preferences.Resume = ResumeEnabled; Guard(() => Preferences.Save(settingsPath)); scanCancellation?.Dispose();
    }
}
