namespace BeastScreen.Core.Playlists;

public enum RepeatMode { Off, One, All }
public sealed class PlaybackQueue
{
    private readonly List<string> items = [];
    private readonly Stack<int> previous = [];
    public IReadOnlyList<string> Items => items;
    public int Index { get; private set; } = -1;
    public RepeatMode Repeat { get; set; }
    public bool Shuffle { get; set; }
    public string? Current => Index >= 0 && Index < items.Count ? items[Index] : null;
    public void Replace(IEnumerable<string> sources) { items.Clear(); items.AddRange(sources.Take(PlaylistFile.MaxItems)); Index = -1; previous.Clear(); }
    public string? Select(int index) { if (index < 0 || index >= items.Count) return null; if (Index >= 0) previous.Push(Index); Index = index; return Current; }
    public string? Next(bool automatic = false)
    {
        if (items.Count == 0) return null;
        if (automatic && Repeat == RepeatMode.One && Index >= 0) return Current;
        if (Shuffle && items.Count > 1 && Index >= 0) { var next = Random.Shared.Next(items.Count - 1); return Select(next >= Index ? next + 1 : next); }
        var index = Index + 1;
        if (index >= items.Count) { if (Repeat != RepeatMode.All) return null; index = 0; }
        return Select(index);
    }
    public string? Previous() { if (previous.TryPop(out var index)) { Index = index; return Current; } return Index > 0 ? Select(Index - 1) : Current; }
    public void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= items.Count || to >= items.Count) return;
        var value = items[from]; items.RemoveAt(from); items.Insert(to, value);
        if (Index == from) Index = to;
        else if (from < Index && to >= Index) Index--;
        else if (from > Index && to <= Index) Index++;
        previous.Clear();
    }
}
