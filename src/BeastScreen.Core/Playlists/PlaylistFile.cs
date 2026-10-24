using System.Text;
using BeastScreen.Core.Security;

namespace BeastScreen.Core.Playlists;

public static class PlaylistFile
{
    public const int MaxItems = 10000;
    public const int MaxBytes = 4 * 1024 * 1024;
    public static bool IsPlaylist(string path) => new[] { ".m3u", ".m3u8", ".pls" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> Load(string path)
    {
        var result = new List<string>(); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Read(Path.GetFullPath(path), result, active, 0); return result;
    }
    private static void Read(string path, List<string> result, HashSet<string> active, int depth)
    {
        if (depth > 4 || !active.Add(path)) throw new InvalidDataException("Playlist nesting is too deep or contains a cycle.");
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Playlist exceeds the 4 MiB size limit.");
        var entries = File.ReadAllLines(path, Encoding.UTF8).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (entries.Any(x => x.StartsWith("#EXT-X-", StringComparison.Ordinal))) { result.Add(path); active.Remove(path); return; } // HLS is one stream, not a queue of segments.
        if (Path.GetExtension(path).Equals(".pls", StringComparison.OrdinalIgnoreCase))
            entries = entries.Select(x => x.Trim()).Where(x => x.StartsWith("File", StringComparison.OrdinalIgnoreCase) && x.IndexOf('=') > 4)
                .OrderBy(x => int.TryParse(x.AsSpan(4, x.IndexOf('=') - 4), out var n) ? n : int.MaxValue).Select(x => x[(x.IndexOf('=') + 1)..]).ToList();
        foreach (var line in entries)
        {
            var entry = line.Trim(); if (entry.StartsWith('#')) continue;
            if (entry.Contains("://", StringComparison.Ordinal) && !SourcePolicy.IsNetwork(entry)) throw new ArgumentException("This playlist contains a disabled URL protocol.");
            var source = SourcePolicy.IsNetwork(entry) || Path.IsPathFullyQualified(entry) ? entry : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, entry));
            SourcePolicy.Validate(source);
            if (!SourcePolicy.IsNetwork(source) && IsPlaylist(source)) Read(source, result, active, depth + 1);
            else result.Add(source);
            if (result.Count > MaxItems) throw new InvalidDataException("Playlist exceeds the 10,000 item limit.");
        }
        active.Remove(path);
    }
    public static void Save(string path, IEnumerable<string> sources)
    {
        var items = sources.Take(MaxItems + 1).ToArray(); if (items.Length > MaxItems) throw new InvalidDataException("Too many playlist items.");
        foreach (var item in items) SourcePolicy.Validate(item);
        if (Path.GetExtension(path).Equals(".pls", StringComparison.OrdinalIgnoreCase))
            File.WriteAllLines(path, new[] { "[playlist]", $"NumberOfEntries={items.Length}", "Version=2" }.Concat(items.Select((p, i) => $"File{i + 1}={p}")), new UTF8Encoding(false));
        else File.WriteAllLines(path, new[] { "#EXTM3U" }.Concat(items), new UTF8Encoding(false));
    }
}
