namespace BeastScreen.Core.Library;

public sealed record MediaItem(long Id, string Path, string Title, string Kind, string Artist, string Album, string Genre,
    long Size, long ModifiedUtc, double Resume, double Duration, bool Favourite, bool Missing, long AddedUtc, long PlayedUtc)
{
    public string Detail => Kind == "Music" ? $"{Artist} · {Album}" : System.IO.Path.GetDirectoryName(Path) ?? Path;
    public string Thumbnail => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeastScreen", "cache", MediaInspector.CacheKey(Path, ModifiedUtc) + ".jpg");
}
public sealed record Bookmark(long Id, string Path, double Position, string Label);
public sealed record LibraryQuery(string Search = "", string View = "Home", string Sort = "title", int Offset = 0, int Limit = 200);
public sealed record ScanProgress(int Files, string Current, bool Finished);
