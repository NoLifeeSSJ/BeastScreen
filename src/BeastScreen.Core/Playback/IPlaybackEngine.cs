namespace BeastScreen.Core.Playback;

public enum PlaybackState { Idle, Loading, Playing, Paused, Ended, Error }
public sealed record MediaTrack(int Id, string Type, string Title, string Language, bool Selected)
{
    public string Display => $"{Id}: {Title} {Language}".Trim();
}
public sealed record Chapter(int Index, string Title, double Time);
public sealed record PlaybackSnapshot(
    PlaybackState State, double Position, double Duration, bool Seekable, bool Live,
    double Volume, bool Muted, string Title, string Diagnostics);
public sealed class PlaybackException(string message) : Exception(message);

public interface IPlaybackEngine : IDisposable
{
    event Action<PlaybackSnapshot>? Changed;
    event Action<IReadOnlyList<MediaTrack>, IReadOnlyList<Chapter>>? TracksChanged;
    event Action<string>? Failed;
    event Action? Ended;
    void Initialize(nint windowHandle, IReadOnlyDictionary<string, string>? options = null);
    void Open(string source, double resumeSeconds = 0);
    void Command(params string[] arguments);
    void Set(string property, string value);
    string Get(string property);
}
