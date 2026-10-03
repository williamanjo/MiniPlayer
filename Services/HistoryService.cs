using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace MiniPlayer.Services;

/// <summary>One listened track.</summary>
/// <param name="Cover">File name in the covers folder, or null.</param>
public sealed record PlayRecord(
    DateTime Start, string Title, string Artist, string Source, int DurationSec, int ListenedSec, string? Cover);

/// <summary>
/// Records what was listened to (JSON lines in %APPDATA%\MiniPlayer\history.jsonl, small cover
/// thumbnails next to it). A track counts after 30 s of listening, or half of it if shorter.
/// </summary>
public sealed class HistoryService
{
    public static readonly string Dir = AppInfo.DataDir;
    public static readonly string CoversDir = Path.Combine(Dir, "covers");
    static readonly string FilePath = Path.Combine(Dir, "history.jsonl");
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly SettingsService _settings;
    string? _key;
    DateTime _start, _lastTick;
    double _listened;
    MediaSnapshot _track = MediaSnapshot.Empty;
    string? _cover;

    public HistoryService(SettingsService settings) => _settings = settings;

    /// <summary>Raised after a record is saved (or the history cleared).</summary>
    public event Action? Changed;

    /// <summary>Called a few times per second with the current playback state.</summary>
    public void Tick(MediaSnapshot s)
    {
        var now = DateTime.Now;
        var key = _settings.Data.RecordHistory && s.HasSession && !string.IsNullOrWhiteSpace(s.Title)
            ? $"{s.Source}|{s.Title}|{s.Artist}"
            : null;

        if (key != _key)
        {
            Finish();
            _key = key;
            _start = now;
            _listened = 0;
            _cover = null;
        }
        else if (key is not null && s.IsPlaying)
        {
            _listened += Math.Clamp((now - _lastTick).TotalSeconds, 0, 2); // ignore sleeps/hangs
        }
        if (key is not null) _track = s;
        _lastTick = now;
    }

    /// <summary>Artwork of the current track (saved once per image, 96 px).</summary>
    public void SetCover(byte[]? bytes, string? hash)
    {
        if (_key is null) return;
        if (bytes is null || hash is null)
        {
            _cover = null; // track without artwork: don't keep the previous one
            return;
        }
        var name = hash[..16] + ".png";
        var path = Path.Combine(CoversDir, name);
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(CoversDir);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelHeight = 96;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(path);
                encoder.Save(file);
            }
            _cover = name;
        }
        catch
        {
            // Unreadable artwork: record without cover.
        }
    }

    /// <summary>Saves the current track if it was listened long enough (track change, app exit).</summary>
    public void Finish()
    {
        if (_key is null) return;
        var duration = (int)_track.Duration.TotalSeconds;
        var needed = duration is > 0 and < 60 ? duration * 0.5 : 30;
        var listened = (int)_listened;
        _key = null;
        if (listened < needed) return;

        var record = new PlayRecord(_start, _track.Title, _track.Artist, _track.Source, duration, listened, _cover);
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(record, JsonOptions) + "\n", Encoding.UTF8);
            Changed?.Invoke();
        }
        catch
        {
            // Disk full / locked: history is best effort.
        }
    }

    /// <summary>All records, newest first.</summary>
    public static List<PlayRecord> Load()
    {
        var list = new List<PlayRecord>();
        if (!File.Exists(FilePath)) return list;
        foreach (var line in File.ReadLines(FilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<PlayRecord>(line, JsonOptions) is { } record) list.Add(record);
            }
            catch
            {
                // skip a damaged line
            }
        }
        list.Reverse();
        return list;
    }

    public void Clear()
    {
        _key = null;
        try
        {
            File.Delete(FilePath);
            if (Directory.Exists(CoversDir)) Directory.Delete(CoversDir, recursive: true);
        }
        catch
        {
            // in use; best effort
        }
        Changed?.Invoke();
    }
}
