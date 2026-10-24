using System.Text.Json;

namespace BeastScreen.Core.Settings;

public sealed class AppSettings
{
    public double Volume { get; set; } = 70;
    public int SeekSeconds { get; set; } = 10;
    public bool Resume { get; set; } = true;
    public string HardwareDecoding { get; set; } = "auto-safe";
    public string AudioLanguage { get; set; } = "eng";
    public string SubtitleLanguage { get; set; } = "eng";
    public string ScreenshotFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BeastScreen");
    public double SubtitleSize { get; set; } = 42;
    public string SubtitleColour { get; set; } = "#FFFFFFFF";
    public string Scale { get; set; } = "bilinear";
    public string ToneMapping { get; set; } = "auto";
    public bool NormalizeVolume { get; set; }
    public string Equalizer { get; set; } = "0,0,0,0,0";
    public bool Passthrough { get; set; }
    public Dictionary<string, string> Shortcuts { get; set; } = new()
    {
        ["PlayPause"] = "Space", ["SeekBack"] = "Left", ["SeekForward"] = "Right", ["Fullscreen"] = "F11", ["Screenshot"] = "F12", ["Open"] = "Ctrl+O", ["Stop"] = "Ctrl+S", ["Next"] = "Ctrl+Right", ["Previous"] = "Ctrl+Left"
    };
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        try { var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(); value.Validate(); return value; }
        catch (Exception e) when (e is JsonException or ArgumentException) { File.Copy(path, path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); return new(); }
    }
    public void Validate()
    {
        if (!double.IsFinite(Volume) || Volume < 0 || Volume > 100) throw new ArgumentException("Volume must be between 0 and 100.");
        if (SeekSeconds is < 1 or > 600) throw new ArgumentException("Seek interval must be between 1 and 600 seconds.");
        if (HardwareDecoding is not ("auto-safe" or "no")) throw new ArgumentException("Choose automatic hardware decoding or software decoding.");
        if (Scale is not ("bilinear" or "bicubic" or "lanczos" or "ewa_lanczossharp")) throw new ArgumentException("Unknown scaling algorithm.");
        if (ToneMapping is not ("auto" or "bt.2390" or "clip" or "reinhard")) throw new ArgumentException("Unknown tone mapping algorithm.");
        if (SubtitleSize is < 10 or > 100 || !double.IsFinite(SubtitleSize)) throw new ArgumentException("Subtitle size must be between 10 and 100.");
        if (SubtitleColour is null || !System.Text.RegularExpressions.Regex.IsMatch(SubtitleColour, "^#[0-9a-fA-F]{8}$")) throw new ArgumentException("Subtitle colour must use #AARRGGBB.");
        if (Equalizer is null || Equalizer.Split(',').Length != 5 || Equalizer.Split(',').Any(x => !double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value is < -12 or > 12)) throw new ArgumentException("Equalizer needs five comma-separated gains between -12 and 12 dB.");
        if (string.IsNullOrWhiteSpace(ScreenshotFolder) || !Path.IsPathFullyQualified(ScreenshotFolder)) throw new ArgumentException("Choose an absolute screenshot folder.");
    }
    public void Save(string path)
    {
        Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
