using Microsoft.Data.Sqlite;

namespace BeastScreen.Core.Library;

public sealed class LibraryDatabase
{
    public const int SchemaVersion = 1;
    private readonly string path;
    private readonly string connectionString;
    public LibraryDatabase(string path)
    {
        this.path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(this.path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = this.path, Pooling = false }.ToString();
        Initialize();
    }
    private SqliteConnection Connect()
    {
        var c = new SqliteConnection(connectionString); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"; cmd.ExecuteNonQuery();
        return c;
    }
    private void Initialize()
    {
        using var c = Connect();
        using var version = c.CreateCommand(); version.CommandText = "PRAGMA user_version;";
        var current = Convert.ToInt32(version.ExecuteScalar());
        if (current > SchemaVersion) throw new InvalidOperationException("This library was created by a newer BeastScreen version. Update the app or restore a compatible library backup.");
        if (current == SchemaVersion) return;
        if (new FileInfo(path).Length > 0) Backup(path + $".before-v{SchemaVersion}.bak");
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE media (
              id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE,
              title TEXT NOT NULL, kind TEXT NOT NULL, artist TEXT NOT NULL DEFAULT '',
              album TEXT NOT NULL DEFAULT '', genre TEXT NOT NULL DEFAULT '',
              size INTEGER NOT NULL DEFAULT 0, modified INTEGER NOT NULL DEFAULT 0,
              resume REAL NOT NULL DEFAULT 0, duration REAL NOT NULL DEFAULT 0,
              favourite INTEGER NOT NULL DEFAULT 0, missing INTEGER NOT NULL DEFAULT 0,
              added INTEGER NOT NULL, played INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX media_title ON media(title COLLATE NOCASE);
            CREATE INDEX media_played ON media(played DESC);
            CREATE INDEX media_kind ON media(kind);
            CREATE TABLE folders(path TEXT PRIMARY KEY COLLATE NOCASE);
            CREATE TABLE bookmarks(id INTEGER PRIMARY KEY, path TEXT NOT NULL, position REAL NOT NULL, label TEXT NOT NULL);
            CREATE TABLE playlists(id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE playlist_items(playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE, ordinal INTEGER NOT NULL, path TEXT NOT NULL, PRIMARY KEY(playlist_id,ordinal));
            PRAGMA user_version=1;
            """;
        cmd.ExecuteNonQuery(); tx.Commit();
    }
    public IReadOnlyList<MediaItem> Query(LibraryQuery query)
    {
        var where = query.View switch
        {
            "Videos" => "kind='Video'", "Music" => "kind='Music'", "History" => "played>0",
            "Favourites" => "favourite=1", "Continue watching" => "resume>5 AND (duration=0 OR resume<duration-10)",
            "Recently added" => "1=1", "Missing files" => "missing=1", "Streams" => "kind='Stream'", _ => "1=1"
        };
        var order = query.Sort switch { "added" => "added DESC", "played" => "played DESC", "artist" => "artist COLLATE NOCASE,album COLLATE NOCASE,title COLLATE NOCASE", "album" => "album COLLATE NOCASE,title COLLATE NOCASE", "genre" => "genre COLLATE NOCASE,title COLLATE NOCASE", _ => "title COLLATE NOCASE" };
        using var c = Connect(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT * FROM media WHERE {where} AND (title LIKE $search ESCAPE '!' OR artist LIKE $search ESCAPE '!' OR album LIKE $search ESCAPE '!' OR genre LIKE $search ESCAPE '!') ORDER BY {order},id LIMIT $limit OFFSET $offset;";
        var search = query.Search.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
        cmd.Parameters.AddWithValue("$search", "%" + search + "%");
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500)); cmd.Parameters.AddWithValue("$offset", Math.Max(0, query.Offset));
        using var r = cmd.ExecuteReader(); var result = new List<MediaItem>();
        while (r.Read()) result.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt64(7), r.GetInt64(8), r.GetDouble(9), r.GetDouble(10), r.GetBoolean(11), r.GetBoolean(12), r.GetInt64(13), r.GetInt64(14)));
        return result;
    }
    public void Upsert(string source, string title, string kind, long size = 0, long modified = 0, string artist = "", string album = "", string genre = "")
    {
        using var c = Connect(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO media(path,title,kind,size,modified,artist,album,genre,added) VALUES($path,$title,$kind,$size,$modified,$artist,$album,$genre,$now)
            ON CONFLICT(path) DO UPDATE SET title=excluded.title,kind=excluded.kind,size=excluded.size,modified=excluded.modified,missing=0,
            artist=CASE WHEN excluded.artist='' THEN media.artist ELSE excluded.artist END,
            album=CASE WHEN excluded.album='' THEN media.album ELSE excluded.album END,genre=CASE WHEN excluded.genre='' THEN media.genre ELSE excluded.genre END;
            """;
        foreach (var p in new (string, object)[] { ("$path", source), ("$title", title), ("$kind", kind), ("$size", size), ("$modified", modified), ("$artist", artist), ("$album", album), ("$genre", genre), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()) }) cmd.Parameters.AddWithValue(p.Item1, p.Item2);
        cmd.ExecuteNonQuery();
    }
    public bool IsUnchanged(string source, long size, long modified)
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1 FROM media WHERE path=$p AND size=$s AND modified=$m AND missing=0";
        cmd.Parameters.AddWithValue("$p", source); cmd.Parameters.AddWithValue("$s", size); cmd.Parameters.AddWithValue("$m", modified); return cmd.ExecuteScalar() is not null;
    }
    public (double Resume, double Duration) Progress(string source)
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT resume,duration FROM media WHERE path=$p"; cmd.Parameters.AddWithValue("$p", source);
        using var r = cmd.ExecuteReader(); return r.Read() ? (r.GetDouble(0), r.GetDouble(1)) : (0, 0);
    }
    public void SaveProgress(string source, double position, double duration)
    {
        using var c = Connect(); using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE media SET resume=$r,duration=$d,played=$now WHERE path=$p";
        cmd.Parameters.AddWithValue("$r", Math.Max(0, double.IsFinite(position) ? position : 0)); cmd.Parameters.AddWithValue("$d", Math.Max(0, double.IsFinite(duration) ? duration : 0)); cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); cmd.Parameters.AddWithValue("$p", source); cmd.ExecuteNonQuery();
    }
    public void ToggleFavourite(string source) => Execute("UPDATE media SET favourite=NOT favourite WHERE path=$p", source);
    public void MarkMissing(string source) => Execute("UPDATE media SET missing=1 WHERE path=$p", source);
    private void Execute(string sql, string source)
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.Parameters.AddWithValue("$p", source); cmd.ExecuteNonQuery();
    }
    public void AddFolder(string folder) => Execute("INSERT OR IGNORE INTO folders VALUES($p)", System.IO.Path.GetFullPath(folder));
    public IReadOnlyList<string> Folders()
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT path FROM folders ORDER BY path";
        using var r = cmd.ExecuteReader(); var result = new List<string>(); while (r.Read()) result.Add(r.GetString(0)); return result;
    }
    public void RelocateFolder(string oldFolder, string newFolder)
    {
        var prefix = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(oldFolder)) + System.IO.Path.DirectorySeparatorChar;
        var destination = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(newFolder)) + System.IO.Path.DirectorySeparatorChar;
        using var c = Connect(); using var tx = c.BeginTransaction();
        foreach (var table in new[] { "media", "bookmarks", "playlist_items" })
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET path=$new || substr(path,length($old)+1) WHERE substr(path,1,length($old))=$old COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$old", prefix); cmd.Parameters.AddWithValue("$new", destination); cmd.ExecuteNonQuery();
        }
        using var folder = c.CreateCommand(); folder.Transaction = tx; folder.CommandText = "UPDATE folders SET path=$new WHERE path=$old"; folder.Parameters.AddWithValue("$old", oldFolder); folder.Parameters.AddWithValue("$new", newFolder); folder.ExecuteNonQuery(); tx.Commit();
    }
    public void AddBookmark(string source, double position, string label)
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO bookmarks(path,position,label) VALUES($p,$t,$l)"; cmd.Parameters.AddWithValue("$p", source); cmd.Parameters.AddWithValue("$t", position); cmd.Parameters.AddWithValue("$l", label); cmd.ExecuteNonQuery();
    }
    public IReadOnlyList<Bookmark> Bookmarks(string source)
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,path,position,label FROM bookmarks WHERE path=$p ORDER BY position"; cmd.Parameters.AddWithValue("$p", source);
        using var r = cmd.ExecuteReader(); var result = new List<Bookmark>(); while (r.Read()) result.Add(new(r.GetInt64(0), r.GetString(1), r.GetDouble(2), r.GetString(3))); return result;
    }
    public void SavePlaylist(string name, IReadOnlyList<string> items)
    {
        using var c = Connect(); using var tx = c.BeginTransaction(); using var create = c.CreateCommand(); create.Transaction = tx;
        create.CommandText = "INSERT INTO playlists(name) VALUES($n); SELECT last_insert_rowid();"; create.Parameters.AddWithValue("$n", name); var id = (long)create.ExecuteScalar()!;
        for (var i = 0; i < Math.Min(10000, items.Count); i++)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO playlist_items VALUES($id,$i,$p)"; cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$i", i); cmd.Parameters.AddWithValue("$p", items[i]); cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public IReadOnlyList<(long Id, string Name)> Playlists()
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,name FROM playlists ORDER BY name"; using var r = cmd.ExecuteReader(); var result = new List<(long, string)>(); while (r.Read()) result.Add((r.GetInt64(0), r.GetString(1))); return result;
    }
    public IReadOnlyList<string> LoadPlaylist(long id)
    {
        using var c = Connect(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT path FROM playlist_items WHERE playlist_id=$id ORDER BY ordinal"; cmd.Parameters.AddWithValue("$id", id); using var r = cmd.ExecuteReader(); var result = new List<string>(); while (r.Read()) result.Add(r.GetString(0)); return result;
    }
    public void Backup(string destination)
    {
        using var source = Connect(); using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); target.Open(); source.BackupDatabase(target);
    }
    public void Restore(string backup)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); source.Open();
        using var check = source.CreateCommand(); check.CommandText = "PRAGMA integrity_check;"; if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidDataException("The backup is not a healthy SQLite database.");
        check.CommandText = "PRAGMA user_version;"; if (Convert.ToInt32(check.ExecuteScalar()) != SchemaVersion) throw new InvalidDataException("The backup schema is incompatible with this app version.");
        check.CommandText = "SELECT path,title,resume FROM media LIMIT 0;"; check.ExecuteNonQuery();
        Backup(path + ".before-restore.bak"); using var target = Connect(); source.BackupDatabase(target);
    }
}
