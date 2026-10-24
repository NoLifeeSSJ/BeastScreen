using System.Globalization;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using BeastScreen.Core.Security;

namespace BeastScreen.Core.Playback;

/// <summary>One libmpv client, one event worker. No user scripts, config, or shell commands.</summary>
public sealed class MpvEngine : IPlaybackEngine
{
    private readonly bool diagnostics;
    private readonly ConcurrentQueue<string> diagnosticMessages = new();
    public MpvEngine(bool diagnostics = false) => this.diagnostics = diagnostics;
    public IReadOnlyList<string> DiagnosticMessages => diagnosticMessages.ToArray();
    private readonly object gate = new();
    private nint handle;
    private Thread? worker;
    private volatile bool stopping;
    private PlaybackState state;
    private bool networkSource;
    public event Action<PlaybackSnapshot>? Changed;
    public event Action<IReadOnlyList<MediaTrack>, IReadOnlyList<Chapter>>? TracksChanged;
    public event Action<string>? Failed;
    public event Action? Ended;

    public void Initialize(nint windowHandle, IReadOnlyDictionary<string, string>? options = null)
    {
        lock (gate)
        {
            if (handle != 0) throw new InvalidOperationException("Playback is already initialized.");
            Native.EnsureLoaded();
            handle = Native.mpv_create();
            if (handle == 0) throw new PlaybackException("libmpv could not create a player.");
            try
            {
                var defaults = new Dictionary<string, string>
                {
                    ["config"] = "no", ["load-scripts"] = "no", ["ytdl"] = "no",
                    ["input-default-bindings"] = "no", ["input-vo-keyboard"] = "no",
                    ["input-cursor"] = "no", ["osc"] = "no", ["idle"] = "yes",
                    ["terminal"] = "no", ["hwdec"] = "auto-safe", ["vo"] = "gpu-next",
                    ["audio-pitch-correction"] = "yes", ["keep-open"] = "no",
                    ["sub-auto"] = "exact", ["gpu-api"] = "d3d11",
                    ["demuxer-max-bytes"] = "134217728", ["network-timeout"] = "15",
                    ["wid"] = windowHandle.ToInt64().ToString(CultureInfo.InvariantCulture)
                };
                if (options is not null) foreach (var pair in options) defaults[pair.Key] = pair.Value;
                foreach (var pair in defaults) Check(Native.mpv_set_option_string(handle, pair.Key, pair.Value));
                Check(Native.mpv_initialize(handle));
                if (diagnostics) Check(Native.mpv_request_log_messages(handle, "debug"));
                foreach (var name in new[] { "time-pos", "duration", "pause", "volume", "mute", "seekable", "media-title", "track-list", "chapter-list", "hwdec-current" })
                    Check(Native.mpv_observe_property(handle, 0, name, 0));
                stopping = false;
                worker = new Thread(EventLoop) { IsBackground = true, Name = "BeastScreen libmpv events" };
                worker.Start();
            }
            catch { Native.mpv_terminate_destroy(handle); handle = 0; throw; }
        }
    }

    public void Open(string source, double resumeSeconds = 0)
    {
        SourcePolicy.Validate(source);
        networkSource = SourcePolicy.IsNetwork(source);
        Set("start", Math.Max(0, resumeSeconds).ToString(CultureInfo.InvariantCulture));
        state = PlaybackState.Loading;
        Command("loadfile", source, "replace");
    }

    public void Command(params string[] arguments)
    {
        if (arguments.Length == 0) throw new ArgumentException("A command is required.", nameof(arguments));
        lock (gate)
        {
            EnsureReady();
            var pointers = arguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            var array = Marshal.AllocHGlobal((pointers.Length + 1) * IntPtr.Size);
            try
            {
                for (var i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, pointers[i]);
                Marshal.WriteIntPtr(array, pointers.Length * IntPtr.Size, 0);
                Check(Native.mpv_command(handle, array));
            }
            finally { foreach (var pointer in pointers) Marshal.FreeCoTaskMem(pointer); Marshal.FreeHGlobal(array); }
        }
    }

    public void Set(string property, string value)
    {
        lock (gate) { EnsureReady(); Check(Native.mpv_set_property_string(handle, property, value)); }
    }

    public string Get(string property)
    {
        lock (gate)
        {
            if (handle == 0 || stopping) return "";
            var pointer = Native.mpv_get_property_string(handle, property);
            if (pointer == 0) return "";
            try { return Marshal.PtrToStringUTF8(pointer) ?? ""; }
            finally { Native.mpv_free(pointer); }
        }
    }

    private void EventLoop()
    {
        try
        {
            while (!stopping)
            {
                var pointer = Native.mpv_wait_event(handle, 0.2);
                if (stopping || pointer == 0) continue;
                var e = Marshal.PtrToStructure<MpvEvent>(pointer);
                if (e.Id == 1) break; // shutdown
                if (e.Id == 2 && e.Data != 0)
                {
                    var message = Marshal.PtrToStructure<LogEvent>(e.Data);
                    diagnosticMessages.Enqueue(Redaction.Clean($"{ReadText(message.Prefix, 128)} {ReadText(message.Level, 16)}: {ReadText(message.Text, 4096)}"));
                    while (diagnosticMessages.Count > 200) diagnosticMessages.TryDequeue(out _);
                    continue;
                }
                if (e.Id == 7) // end-file
                {
                    var end = Marshal.PtrToStructure<EndFileEvent>(e.Data);
                    if (end.Reason == 4) { state = PlaybackState.Error; Failed?.Invoke($"Playback failed: {Error(end.Error)}. Check the file, network connection, or codec support."); }
                    else if (end.Reason == 0) { state = PlaybackState.Ended; Ended?.Invoke(); }
                    else state = PlaybackState.Idle;
                }
                if (e.Id == 8) { state = PlaybackState.Playing; PublishTracks(); }
                if (e.Id == 22)
                {
                    var p = Marshal.PtrToStructure<PropertyEvent>(e.Data);
                    var name = Marshal.PtrToStringUTF8(p.Name);
                    if (name is "track-list" or "chapter-list") PublishTracks();
                }
                if (e.Id is 7 or 8 or 22) PublishSnapshot();
            }
        }
        catch (Exception ex) when (ex is not AccessViolationException)
        {
            if (!stopping) Failed?.Invoke($"Playback event processing failed: {Redaction.Clean(ex.Message)}");
        }
    }

    private void PublishSnapshot()
    {
        if (state is PlaybackState.Playing or PlaybackState.Paused) state = Get("pause") == "yes" ? PlaybackState.Paused : PlaybackState.Playing;
        var duration = Number("duration");
        var seekable = Get("seekable") == "yes";
        var diagnostics = $"{Get("video-codec")} · {Get("width")}×{Get("height")} · {Get("estimated-vf-fps")} fps · decoder {Get("hwdec-current")} · audio {Get("audio-codec")} {Get("audio-params/channel-count")} ch · dropped {Get("frame-drop-count")}";
        var live = PlaybackTiming.IsLiveLikeNetworkStream(networkSource, seekable, Number("file-size"), state);
        Changed?.Invoke(new(state, Number("time-pos"), duration, seekable, live, Number("volume"), Get("mute") == "yes", Get("media-title"), diagnostics));
    }

    private void PublishTracks()
    {
        var tracks = new List<MediaTrack>();
        for (var i = 0; i < Math.Min(256, Number("track-list/count")); i++)
        {
            var prefix = $"track-list/{i}/";
            tracks.Add(new((int)Number(prefix + "id"), Get(prefix + "type"), Get(prefix + "title"), Get(prefix + "lang"), Get(prefix + "selected") == "yes"));
        }
        var chapters = new List<Chapter>();
        for (var i = 0; i < Math.Min(2048, Number("chapter-list/count")); i++) chapters.Add(new(i, Get($"chapter-list/{i}/title"), Number($"chapter-list/{i}/time")));
        TracksChanged?.Invoke(tracks, chapters);
    }

    private double Number(string property) => double.TryParse(Get(property), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;
    private static string ReadText(nint pointer, int maximum)
    {
        if (pointer == 0) return "";
        var bytes = new byte[maximum]; var length = 0;
        while (length < bytes.Length && (bytes[length] = Marshal.ReadByte(pointer, length)) != 0) length++;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }
    private void EnsureReady() { if (handle == 0 || stopping) throw new PlaybackException("Playback engine is not ready."); }
    private static string Error(int code) => Marshal.PtrToStringUTF8(Native.mpv_error_string(code)) ?? $"mpv error {code}";
    private static void Check(int result) { if (result < 0) throw new PlaybackException(Error(result)); }

    public void Dispose()
    {
        lock (gate) { if (handle == 0) return; stopping = true; Native.mpv_wakeup(handle); }
        if (worker != Thread.CurrentThread) worker?.Join();
        lock (gate) { Native.mpv_terminate_destroy(handle); handle = 0; }
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)] private struct MpvEvent { public int Id; public int Error; public ulong UserData; public nint Data; }
    [StructLayout(LayoutKind.Sequential)] private struct EndFileEvent { public int Reason; public int Error; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyEvent { public nint Name; public int Format; public nint Data; }
    [StructLayout(LayoutKind.Sequential)] private struct LogEvent { public nint Prefix, Level, Text; public int LogLevel; }

    private static class Native
    {
        private const string Library = "beastscreen-mpv";
        static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, _, _) =>
        {
            if (name != Library) return 0;
            var path = Path.Combine(AppContext.BaseDirectory, "native", OperatingSystem.IsWindows() ? "libmpv-2.dll" : "libmpv.so.2");
            if (!File.Exists(path)) throw new DllNotFoundException("The packaged libmpv library is missing. Repair BeastScreen using the installer.");
            if (!OperatingSystem.IsWindows()) return NativeLibrary.Load(path);
            var library = LoadLibraryEx(path, 0, 0x00000100 | 0x00000800); // DLL directory + System32 only
            if (library == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "libmpv or one of its packaged dependencies could not load. Run Repair.");
            return library;
        });
        public static void EnsureLoaded() { }
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string file, nint reserved, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern nint mpv_create();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_initialize(nint ctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_terminate_destroy(nint ctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_wakeup(nint ctx);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern nint mpv_wait_event(nint ctx, double timeout);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_request_log_messages(nint ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_option_string(nint ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_property_string(nint ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern nint mpv_get_property_string(nint ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_observe_property(nint ctx, ulong data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command(nint ctx, nint args);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern nint mpv_error_string(int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free(nint data);
    }
}
