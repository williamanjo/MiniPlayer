using System.IO;
using System.Net;
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

        var state = $"{text}|{title}|{artist}|{(show ? _coverHash : null)}|{Folder}";
        if (state == _written) return;
        _written = state;

        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "nowplaying.txt"), text, Utf8);
            File.WriteAllText(Path.Combine(Folder, "title.txt"), title, Utf8);
            File.WriteAllText(Path.Combine(Folder, "artist.txt"), artist, Utf8);
            WriteCover(show ? _cover : null);
            File.WriteAllText(Path.Combine(Folder, "overlay.html"), Overlay(title, artist, show), Utf8);
        }
        catch
        {
            _written = null; // file locked by OBS for a moment: retry on the next tick
        }
    }

    /// <summary>Forces a rewrite (settings changed).</summary>
    public void Invalidate() => _written = null;

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

    /// <summary>Card for an OBS browser source; reloads itself every 2 s to pick up changes.</summary>
    string Overlay(string title, string artist, bool show)
    {
        var t = WebUtility.HtmlEncode(title);
        var a = WebUtility.HtmlEncode(artist);
        var cover = $"cover.png?v={_coverHash ?? "none"}";
        var body = show
            ? $"""<div class="card"><img src="{cover}" alt=""><div class="text"><div class="title">{t}</div><div class="artist">{a}</div></div></div>"""
            : "";
        return $$"""
            <!doctype html>
            <html lang="pt-br">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="refresh" content="2">
            <title>MiniPlayer — tocando agora</title>
            <style>
              html, body { margin: 0; background: transparent; font-family: "Segoe UI", sans-serif; }
              .card { display: inline-flex; align-items: center; gap: 14px; padding: 12px 18px 12px 12px;
                      background: rgba(20, 20, 20, .78); border-radius: 14px; color: #fff; max-width: 560px; }
              img { width: 72px; height: 72px; border-radius: 8px; object-fit: cover; }
              .title { font-size: 22px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
              .artist { font-size: 16px; opacity: .75; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
              .text { min-width: 0; }
            </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }
}
