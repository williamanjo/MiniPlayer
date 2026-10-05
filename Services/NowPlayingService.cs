using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniPlayer.Services;

/// <summary>
/// "Tocando agora" files for OBS / streaming: text files for text sources, the cover as an
/// image source, and a self-refreshing overlay.html for a browser source.
/// </summary>
public sealed class NowPlayingService
{
    public const string DefaultTemplate = "{artist} — {title}";

    public static readonly string DefaultFolder = AppInfo.NowPlayingDir;

    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    readonly SettingsService _settings;
    string? _written;
    string? _writtenScript;
    string? _writtenPage;
    string? _coverHash;
    byte[]? _cover;

    public NowPlayingService(SettingsService settings) => _settings = settings;

    public string Folder => string.IsNullOrWhiteSpace(_settings.Data.NowPlayingFolder) ? DefaultFolder : _settings.Data.NowPlayingFolder!;

    public static string Format(string template, string title, string artist, string source) =>
        template.Replace("{title}", title).Replace("{artist}", artist).Replace("{source}", source);

    public void SetCover(byte[]? bytes, string? hash)
    {
        _cover = bytes;
        _coverHash = hash;
        _written = null; // force a rewrite with the new image
    }

    /// <summary>Called with the current state; writes only when something visible changed.</summary>
    public void Update(MediaSnapshot s)
    {
        if (!_settings.Data.NowPlayingEnabled) return;
        var show = s.HasSession && (s.IsPlaying || !_settings.Data.NowPlayingClearWhenPaused);
        var title = show ? s.Title : "";
        var artist = show ? s.Artist : "";
        var source = show ? s.Source : "";
        var text = show ? Format(_settings.Data.NowPlayingTemplate ?? DefaultTemplate, title, artist, source) : "";

        try
        {
            var state = $"{text}|{title}|{artist}|{(show ? _coverHash : null)}|{Folder}";
            if (state != _written)
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "nowplaying.txt"), text, Utf8);
                File.WriteAllText(Path.Combine(Folder, "title.txt"), title, Utf8);
                File.WriteAllText(Path.Combine(Folder, "artist.txt"), artist, Utf8);
                WriteCover(show ? _cover : null);
                _written = state;
            }

            // overlay.html only changes with its style; the song goes to nowplaying.js
            var page = OverlayHtml.Build(_settings.Data.Overlay);
            var pageState = page + "|" + Folder;
            if (pageState != _writtenPage)
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "overlay.html"), page, Utf8);
                _writtenPage = pageState;
            }

            // position is sent with its timestamp; the page moves the bar on its own in between
            var at = s.LastUpdated == default ? DateTimeOffset.Now : s.LastUpdated;
            var script = OverlayHtml.Script(new OverlayHtml.Data(show, title, artist, source,
                show && _cover is not null ? $"cover.png?v={_coverHash}" : null,
                show && s.IsPlaying, s.Position.TotalSeconds, s.Duration.TotalSeconds, at.ToUnixTimeMilliseconds()));
            if (script + Folder != _writtenScript)
            {
                File.WriteAllText(Path.Combine(Folder, "nowplaying.js"), script, Utf8);
                _writtenScript = script + Folder;
            }
        }
        catch
        {
            _written = _writtenScript = _writtenPage = null; // file locked by OBS for a moment: retry on the next tick
        }
    }

    /// <summary>Forces a rewrite (settings changed).</summary>
    public void Invalidate() => _written = _writtenScript = _writtenPage = null;

    void WriteCover(byte[]? bytes)
    {
        BitmapSource image;
        if (bytes is null)
        {
            // 1×1 transparent PNG so an OBS image source just shows nothing.
            image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        }
        else
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelHeight = 512;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            image = bitmap;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(Folder, "cover.png"));
        encoder.Save(file);
    }
}
