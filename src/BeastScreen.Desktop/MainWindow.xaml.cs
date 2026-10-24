using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Data;
using BeastScreen.Core.Playback;
using BeastScreen.Core.Playlists;
using BeastScreen.Core.Security;
using BeastScreen.Desktop.ViewModels;
using Microsoft.Win32;

namespace BeastScreen.Desktop;

public partial class MainWindow : Window
{
    private readonly PlayerViewModel model;
    private bool fullscreen;
    private bool mini;
    private bool grid;
    private Rect previousBounds;
    private WindowState previousState;
    public MainWindow(bool temporary = false)
    {
        model = new(Dispatcher, temporary); DataContext = model; InitializeComponent();
        model.UiRequested += OnUiRequested; model.FullscreenRequested += ToggleFullscreen;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.View)) { if (model.View == "Settings") SettingsTab.IsSelected = true; else if (model.View == "Playlists") LibraryTabs.SelectedIndex = 2; else LibraryTabs.SelectedIndex = 0; }
            if (e.PropertyName == nameof(model.Sort))
            {
                var collection = CollectionViewSource.GetDefaultView(model.Library); collection.GroupDescriptions.Clear();
                if (model.Sort is "artist" or "album" or "genre") collection.GroupDescriptions.Add(new PropertyGroupDescription(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(model.Sort)));
            }
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
    }
    private nint WindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0319)
        {
            var command = ((long)lParam >> 16) & 0x7ff;
            if (command == 14) model.PlayPauseCommand.Execute(null); else if (command == 11) model.NextCommand.Execute(null); else if (command == 12) model.PreviousCommand.Execute(null); else if (command == 13) model.StopCommand.Execute(null); else return 0;
            handled = true; return 1;
        }
        return 0;
    }
    public void OpenSources(IEnumerable<string> paths) => model.OpenSources(paths);
    private void OnVideoReady(nint hwnd) => model.Initialize(hwnd);
    private void OnVideoDoubleClick() => ToggleFullscreen();
    private void OnNativeKey(Key key) => HandleKey(key, Keyboard.Modifiers);
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase || e.OriginalSource is ComboBox) return;
        e.Handled = HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
    }
    private bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape && (fullscreen || mini)) { RestoreWindow(); return true; }
        var converter = new KeyGestureConverter();
        foreach (var shortcut in model.Preferences.Shortcuts)
        {
            KeyGesture? gesture;
            try { gesture = converter.ConvertFromInvariantString(shortcut.Value) as KeyGesture; } catch (NotSupportedException) { continue; }
            if (gesture is null || gesture.Key != key || gesture.Modifiers != modifiers) continue;
            switch (shortcut.Key)
            {
                case "PlayPause": model.PlayPauseCommand.Execute(null); break;
                case "SeekBack": model.SeekCommand.Execute("back"); break;
                case "SeekForward": model.SeekCommand.Execute("forward"); break;
                case "Fullscreen": ToggleFullscreen(); break;
                case "Screenshot": model.ScreenshotCommand.Execute(null); break;
                case "Open": OnUiRequested("files"); break;
                case "Stop": model.StopCommand.Execute(null); break;
                case "Next": model.NextCommand.Execute(null); break;
                case "Previous": model.PreviousCommand.Execute(null); break;
                default: return false;
            }
            return true;
        }
        return false;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) model.OpenSources(files);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text) model.OpenSources([text.Trim()]);
    }
    private void PlayLibrary(object sender, MouseButtonEventArgs e) => model.PlaySelectedCommand.Execute(null);
    private void PlayQueued(object sender, MouseButtonEventArgs e) => model.PlayQueuedCommand.Execute(null);
    private void OnClosing(object? sender, CancelEventArgs e) => model.Dispose();
    private void OnTopChanged(object sender, RoutedEventArgs e) => Topmost = ((CheckBox)sender).IsChecked == true;
    private void ToggleSidebar(object sender, RoutedEventArgs e) { var visible = Sidebar.Visibility == Visibility.Visible; Sidebar.Visibility = visible ? Visibility.Collapsed : Visibility.Visible; SidebarColumn.Width = new(visible ? 0 : 180); }
    private void ToggleQueue(object sender, RoutedEventArgs e) { var visible = PlaylistPanel.Visibility == Visibility.Visible; PlaylistPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible; QueueColumn.Width = new(visible ? 0 : 260); }
    private void ToggleGrid(object sender, RoutedEventArgs e)
    {
        grid = !grid;
        var factory = new FrameworkElementFactory(grid ? typeof(WrapPanel) : typeof(VirtualizingStackPanel));
        if (grid) factory.SetValue(WrapPanel.ItemWidthProperty, 220.0);
        MediaList.ItemsPanel = new ItemsPanelTemplate(factory); model.Status = grid ? "Grid view · pages contain at most 200 records" : "Virtualised list view";
    }
    private void EnterCompact(bool asMini)
    {
        previousBounds = new(Left, Top, Width, Height); previousState = WindowState;
        Header.Visibility = Sidebar.Visibility = PlaylistPanel.Visibility = LibraryTabs.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = QueueColumn.Width = new(0); LibraryRow.Height = new(0); WindowState = WindowState.Normal;
        if (asMini) { MinWidth = 480; MinHeight = 300; Width = 540; Height = 390; Topmost = true; }
        else { WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
    }
    private void RestoreWindow()
    {
        fullscreen = mini = false; WindowStyle = WindowStyle.SingleBorderWindow; Topmost = false; MinWidth = 820; MinHeight = 600;
        WindowState = WindowState.Normal; Left = previousBounds.Left; Top = previousBounds.Top; Width = previousBounds.Width; Height = previousBounds.Height; WindowState = previousState;
        Header.Visibility = Sidebar.Visibility = PlaylistPanel.Visibility = LibraryTabs.Visibility = Visibility.Visible; SidebarColumn.Width = new(180); QueueColumn.Width = new(260); LibraryRow.Height = new(310);
    }
    private void ToggleFullscreen() { if (fullscreen || mini) RestoreWindow(); else { fullscreen = true; EnterCompact(false); } }
    private void ToggleMini(object sender, RoutedEventArgs e) { if (mini || fullscreen) RestoreWindow(); else { mini = true; EnterCompact(true); } }
    private void PreviousChapter(object sender, RoutedEventArgs e) => model.Guard(() => model.Engine.Command("add", "chapter", "-1"));
    private void NextChapter(object sender, RoutedEventArgs e) => model.Guard(() => model.Engine.Command("add", "chapter", "1"));
    private string? Prompt(string title, string label, string value = "")
    {
        // Owned top-level dialog is a separate HWND and is not subject to video airspace clipping.
        var window = new Window { Title = title, Owner = this, Width = 560, Height = 210, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(18) }; var input = new TextBox { Text = value }; var button = new Button { Content = "Continue", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        System.Windows.Automation.AutomationProperties.SetName(input, label); stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); stack.Children.Add(input); stack.Children.Add(button); window.Content = stack;
        button.Click += (_, _) => window.DialogResult = true; window.Loaded += (_, _) => input.Focus(); return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private async void OnUiRequested(string action)
    {
        try
        {
            switch (action)
            {
                case "files":
                case "import":
                    var open = new OpenFileDialog { Multiselect = action == "files", Filter = action == "import" ? "Playlists|*.m3u;*.m3u8;*.pls|All files|*.*" : "All media files|*.*" };
                    if (open.ShowDialog(this) == true) model.OpenSources(open.FileNames); break;
                case "folder":
                case "library":
                    var folder = new OpenFolderDialog { Title = action == "library" ? "Select a library folder" : "Open media folder" };
                    if (folder.ShowDialog(this) == true)
                    {
                        if (action == "library") await model.ScanFolderAsync(folder.FolderName);
                        else model.OpenSources(Directory.EnumerateFiles(folder.FolderName).Where(p => BeastScreen.Core.Library.LibraryScanner.AudioExtensions.Contains(Path.GetExtension(p)) || BeastScreen.Core.Library.LibraryScanner.VideoExtensions.Contains(Path.GetExtension(p))).Take(10000));
                    }
                    break;
                case "url": var url = Prompt("Open stream", "Direct media URL (HTTP(S), HLS, DASH, RTSP, radio)"); if (!string.IsNullOrWhiteSpace(url)) model.OpenSources([url]); break;
                case "subtitle": var sub = new OpenFileDialog { Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx;*.sup|All files|*.*" }; if (sub.ShowDialog(this) == true) model.Engine.Command("sub-add", sub.FileName, "select"); break;
                case "export": var export = new SaveFileDialog { Filter = "M3U8 playlist|*.m3u8|PLS playlist|*.pls", FileName = "BeastScreen.m3u8" }; if (export.ShowDialog(this) == true) PlaylistFile.Save(export.FileName, model.Queue); break;
                case "backup": var backup = new SaveFileDialog { Filter = "SQLite library backup|*.sqlite", FileName = "BeastScreen-library.sqlite" }; if (backup.ShowDialog(this) == true) { model.Database.Backup(backup.FileName); model.Status = "Library backup saved. Media files are not copied."; } break;
                case "restore": var restore = new OpenFileDialog { Filter = "SQLite library backup|*.sqlite" }; if (restore.ShowDialog(this) == true && MessageBox.Show(this, "Replace library records from this backup? A backup of your current library will be kept. Media files will not change.", "Restore library", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { model.Database.Restore(restore.FileName); model.RefreshLibrary(); } break;
                case "relocate":
                    var old = Prompt("Relocate library folder", "Existing library folder path", model.Database.Folders().FirstOrDefault() ?? ""); if (old is null) break;
                    var destination = new OpenFolderDialog { Title = "Choose the folder's new location" }; if (destination.ShowDialog(this) == true) { model.Database.RelocateFolder(old, destination.FolderName); await model.ScanFolderAsync(destination.FolderName); } break;
                case "devices": MessageBox.Show(this, model.Engine.Get("audio-device-list"), "Audio devices — copy a device name into Settings"); break;
                case "edition": var edition = Prompt("Edition", "Edition index (0 is the first edition)", model.Engine.Get("edition")); if (int.TryParse(edition, out var ed) && ed >= 0 && ed < 1000) model.Engine.Set("edition", ed.ToString(CultureInfo.InvariantCulture)); break;
                case "crop":
                    var crop = Prompt("Crop", "width:height:x:y in pixels, or blank to clear"); if (crop is not null) { if (crop == "") model.Engine.Set("vf", ""); else if (Regex.IsMatch(crop, @"^\d{1,5}:\d{1,5}:\d{1,5}:\d{1,5}$")) model.Engine.Set("vf", "lavfi=[crop=" + crop + "]"); else throw new ArgumentException("Enter four nonnegative pixel values separated by colons."); } break;
                case "hdr":
                    var hdr = Prompt("HDR output", "auto uses display metadata where supported; no forces SDR", "auto"); if (hdr is "auto" or "no") model.Engine.Set("target-colorspace-hint", hdr == "auto" ? "yes" : "no"); break;
                case "shortcuts":
                    var shortcuts = Prompt("Keyboard shortcuts", "JSON object: command names and WPF key gestures", JsonSerializer.Serialize(model.Preferences.Shortcuts));
                    if (shortcuts is not null) { var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(shortcuts) ?? throw new ArgumentException("Enter a shortcut object."); foreach (var p in parsed) _ = new KeyGestureConverter().ConvertFromInvariantString(p.Value); model.Preferences.Shortcuts = parsed; model.SavePreferencesCommand.Execute(null); } break;
                case "diagnostics": var diagnostic = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "BeastScreen-diagnostics.txt" }; if (diagnostic.ShowDialog(this) == true) File.WriteAllText(diagnostic.FileName, $"BeastScreen {typeof(App).Assembly.GetName().Version}\n{Environment.OSVersion}\n{model.Diagnostics}\nPlayback error present: {model.HasError}\nMedia paths, credentials, URLs and free-form error text omitted.\n"); break;
            }
        }
        catch (Exception ex) { model.Error = Redaction.Clean(ex.Message); }
    }

    public async Task RunSmokeAsync(string[] args)
    {
        var resultIndex = Array.IndexOf(args, "--result"); var output = resultIndex >= 0 && resultIndex + 1 < args.Length ? args[resultIndex + 1] : Path.Combine(Path.GetTempPath(), "BeastScreen-smoke.json");
        var fileIndex = Array.IndexOf(args, "--smoke-test"); var file = fileIndex >= 0 && fileIndex + 1 < args.Length ? args[fileIndex + 1] : "";
        var checks = new Dictionary<string, bool>();
        try
        {
            if (!File.Exists(file)) throw new FileNotFoundException("Generate the test fixture before running the Windows playback smoke test.");
            model.OpenSources([file]);
            await WaitFor(() => model.LastSnapshot?.Position > 0.3, TimeSpan.FromSeconds(20));
            checks["decodedVideo"] = int.TryParse(model.Engine.Get("width"), out var width) && width > 0;
            checks["decodedAudio"] = !string.IsNullOrWhiteSpace(model.Engine.Get("audio-codec"));
            checks["duration"] = model.LastSnapshot?.Duration > 3;
            model.PlayPauseCommand.Execute(null); await WaitFor(() => model.LastSnapshot?.State == PlaybackState.Paused, TimeSpan.FromSeconds(5)); checks["pauseEvent"] = true;
            model.Engine.Command("seek", "2", "absolute+exact"); await WaitFor(() => model.LastSnapshot?.Position >= 1.9, TimeSpan.FromSeconds(5)); checks["seek"] = true;
            if (model.AudioTracks.Count > 1) { model.Engine.Set("aid", model.AudioTracks[1].Id.ToString(CultureInfo.InvariantCulture)); checks["audioTrackSwitch"] = true; }
            else throw new InvalidDataException("Fixture requires at least two audio tracks.");
            var subtitle = Path.ChangeExtension(file, ".srt"); if (!File.Exists(subtitle)) throw new FileNotFoundException("Fixture subtitle is missing.");
            model.Engine.Command("sub-add", subtitle, "select"); await WaitFor(() => model.SubtitleTracks.Count > 1, TimeSpan.FromSeconds(5)); checks["subtitleTrack"] = true;
            Width = 900; Height = 650; await Task.Delay(200); checks["resize"] = true;
            ToggleFullscreen(); await Task.Delay(200); checks["fullscreen"] = fullscreen; RestoreWindow();
            model.Engine.Set("hwdec", "no"); checks["softwareFallbackRequested"] = true;
            var screenshot = Path.Combine(Path.GetDirectoryName(output)!, "smoke-frame.png"); model.Engine.Command("screenshot-to-file", screenshot, "subtitles"); await WaitFor(() => File.Exists(screenshot) && new FileInfo(screenshot).Length > 100, TimeSpan.FromSeconds(5)); checks["renderedScreenshot"] = true;
            checks["all"] = checks.Values.All(x => x);
            File.WriteAllText(output, JsonSerializer.Serialize(new { checks, diagnostics = model.Diagnostics, limitations = "Automated screenshot and decode checks do not prove physical speaker output, subtitle appearance, HDR, keyboard focus, or hardware decoding. These need an interactive Windows test." }, new JsonSerializerOptions { WriteIndented = true }));
            Close(); Application.Current.Shutdown(checks["all"] ? 0 : 1);
        }
        catch (Exception ex) { File.WriteAllText(output, JsonSerializer.Serialize(new { checks, error = Redaction.Clean(ex.ToString()) })); Close(); Application.Current.Shutdown(1); }
    }
    private async Task WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition()) { if (DateTime.UtcNow >= until) throw new TimeoutException("Playback smoke test timed out: " + model.Error); await Task.Delay(100); }
    }
}
