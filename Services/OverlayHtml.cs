using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using MiniPlayer.Localization;

namespace MiniPlayer.Services;

/// <summary>
/// Builds overlay.html from an <see cref="OverlayStyle"/>. The page is static; the song comes from
/// nowplaying.js (rewritten by <see cref="NowPlayingService"/>), polled every second, so OBS never
/// reloads the page (no flicker) and the progress bar moves smoothly between updates.
/// </summary>
public static class OverlayHtml
{
    /// <summary>Data handed to the page: what nowplaying.js calls <c>__np(...)</c> with.</summary>
    public sealed record Data(bool Show, string Title, string Artist, string Source, string? Cover,
        bool Playing, double Position, double Duration, long At);

    public static string Script(Data data) => $"window.__np && __np({Json(data)});";

    static string Json(Data d) => JsonSerializer.Serialize(new
    {
        show = d.Show, title = d.Title, artist = d.Artist, source = d.Source, cover = d.Cover,
        playing = d.Playing, position = d.Position, duration = d.Duration, at = d.At,
    });

    /// <param name="preview">Sample data for the editor preview (rendered at once, no polling).</param>
    public static string Build(OverlayStyle s, Data? preview = null)
    {
        s.Normalize();
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.##", inv);

        var align = s.Align is "center" or "right" ? s.Align : "left";
        var justify = align switch { "center" => "center", "right" => "flex-end", _ => "flex-start" };
        var direction = s.Layout switch { "rowReverse" => "row-reverse", "column" => "column", _ => "row" };
        var showCover = s.Layout != "text";
        var background = s.Background2 is { } b2
            ? $"linear-gradient({N(s.BackgroundAngle)}deg, {Css(s.Background)}, {Css(b2)})"
            : Css(s.Background);
        var fill = s.Accent2 is { } a2 ? $"linear-gradient(90deg, {Css(s.Accent)}, {Css(a2)})" : Css(s.Accent);
        var weight = s.TitleWeight switch { "normal" => "400", "bold" => "700", _ => "600" };
        var coverRadius = s.CoverCircle ? "50%" : $"{N(s.CoverRadius)}px";
        var textShadow = s.TextShadow ? "0 1px 3px rgba(0,0,0,.85)" : "none";
        var label = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s.LabelText) ? Loc.T("np_label_default") : s.LabelText);
        var font = JsonSerializer.Serialize(s.FontFamily); // quoted, escaped

        var lines = new StringBuilder();
        foreach (var item in s.Items.Where(i => i.Visible))
        {
            lines.Append(item.Kind switch
            {
                "label" => $"""<div class="label">{label}</div>""",
                "title" => """<div class="title"><span id="title"></span></div>""",
                "artist" => """<div class="artist" id="artist"></div>""",
                "source" => """<div class="source" id="source"></div>""",
                "progress" => """<div class="bar"><div class="fill" id="fill"></div></div>""",
                "time" => """<div class="time"><span id="cur">0:00</span><span id="dur">0:00</span></div>""",
                _ => "",
            });
        }

        var css = $$"""
            html, body { margin: 0; background: transparent; overflow: hidden; }
            body { display: flex; justify-content: {{justify}}; padding: 16px; font-family: {{font}}, "Segoe UI", sans-serif; }
            .card { display: flex; flex-direction: {{direction}}; align-items: center; gap: {{N(s.Gap)}}px;
                    padding: {{N(s.Padding)}}px; background: {{background}}; border-radius: {{N(s.Radius)}}px;
                    border: {{N(s.BorderWidth)}}px solid {{Css(s.BorderColor)}}; max-width: {{N(s.Width)}}px; box-sizing: border-box;
                    box-shadow: 0 6px 24px rgba(0,0,0,{{N(Math.Clamp(s.ShadowOpacity, 0, 1))}});
                    {{(s.BackdropBlur > 0 ? $"backdrop-filter: blur({N(s.BackdropBlur)}px);" : "")}}
                    transition: opacity .35s ease, transform .35s ease; }
            .card.hidden { opacity: 0; }
            .card.enter { opacity: 0; transform: {{(s.Animation == "slide" ? "translateY(14px)" : "none")}}; }
            .cover { width: {{N(s.CoverSize)}}px; height: {{N(s.CoverSize)}}px; flex: none; border-radius: {{coverRadius}};
                     object-fit: cover; display: {{(showCover ? "block" : "none")}}; }
            .cover.spin { animation: spin 12s linear infinite; }
            .cover.spin.paused { animation-play-state: paused; }
            @keyframes spin { to { transform: rotate(360deg); } }
            .text { min-width: 0; flex: 1; text-align: {{(direction == "column" ? align : "left")}}; text-shadow: {{textShadow}};
                    width: {{(direction == "column" ? "100%" : "auto")}}; }
            .text > div { overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
            .label { font-size: {{N(s.LabelSize)}}px; color: {{Css(s.LabelColor ?? s.Accent)}}; text-transform: uppercase;
                     letter-spacing: .08em; font-weight: 700; margin-bottom: 2px; }
            .title { font-size: {{N(s.TitleSize)}}px; color: {{Css(s.TitleColor)}}; font-weight: {{weight}}; }
            .title.marquee { text-overflow: clip; }
            .title.marquee span { display: inline-block; padding-right: 48px; animation: marquee 12s linear infinite; }
            @keyframes marquee { 0%, 15% { transform: translateX(0); } 100% { transform: translateX(-50%); } }
            .artist { font-size: {{N(s.ArtistSize)}}px; color: {{Css(s.ArtistColor)}}; }
            .source { font-size: {{N(s.SourceSize)}}px; color: {{Css(s.SourceColor)}}; }
            .bar { height: {{N(s.ProgressHeight)}}px; background: {{Css(s.TrackColor)}}; border-radius: 99px; margin-top: 8px; overflow: hidden; }
            .fill { height: 100%; width: 0; background: {{fill}}; border-radius: 99px; }
            .time { display: flex; justify-content: space-between; font-size: {{N(s.TimeSize)}}px; color: {{Css(s.TimeColor)}};
                    margin-top: 4px; font-variant-numeric: tabular-nums; }
            """;

        var feed = preview is null
            ? """
              function poll() {
                const s = document.createElement('script');
                s.src = 'nowplaying.js?t=' + Date.now();
                s.onload = s.onerror = () => s.remove();
                document.head.appendChild(s);
              }
              setInterval(poll, 1000); poll();
              """
            : $"__np({Json(preview)});";

        var script = $$"""
            const card = document.getElementById('card'), cover = document.getElementById('cover');
            const $ = id => document.getElementById(id);
            const spin = {{(s.CoverSpin ? "true" : "false")}}, marquee = {{(s.Marquee ? "true" : "false")}};
            let d = null;
            const fmt = t => { t = Math.max(0, Math.floor(t)); const m = Math.floor(t / 60), h = Math.floor(m / 60);
              const ss = String(t % 60).padStart(2, '0'); return h ? h + ':' + String(m % 60).padStart(2, '0') + ':' + ss : m + ':' + ss; };
            function set(id, text) { const e = $(id); if (e) e.textContent = text; }
            window.__np = x => {
              const changed = !d || d.title !== x.title || d.artist !== x.artist;
              d = x;
              card.classList.toggle('hidden', !x.show);
              if (!x.show) return;
              set('artist', x.artist); set('source', x.source); set('dur', fmt(x.duration));
              const title = $('title');
              if (title && changed) {
                title.textContent = x.title;
                const box = title.parentElement;
                box.classList.remove('marquee');
                if (marquee && title.scrollWidth > box.clientWidth) { title.textContent = x.title + '    ' + x.title; box.classList.add('marquee'); }
              }
              if (x.cover && cover.getAttribute('src') !== x.cover) cover.src = x.cover;
              cover.style.visibility = x.cover ? 'visible' : 'hidden';
              cover.classList.toggle('spin', spin);
              cover.classList.toggle('paused', !x.playing);
              if (changed) { card.classList.add('enter'); requestAnimationFrame(() => requestAnimationFrame(() => card.classList.remove('enter'))); }
            };
            function tick() {
              if (d && d.show) {
                const p = Math.min(d.duration || 0, d.position + (d.playing ? (Date.now() - d.at) / 1000 : 0));
                const f = $('fill'); if (f) f.style.width = (d.duration > 0 ? p / d.duration * 100 : 0) + '%';
                set('cur', fmt(p));
              }
              requestAnimationFrame(tick);
            }
            tick();
            {{feed}}
            """;

        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <title>MiniPlayer — now playing</title>
            <style>
            {{css}}
            </style>
            </head>
            <body>
            <div class="card hidden" id="card"><img class="cover" id="cover" alt=""><div class="text">{{lines}}</div></div>
            <script>
            {{script}}
            </script>
            </body>
            </html>
            """;
    }

    /// <summary>"#AARRGGBB" / "#RRGGBB" / named color → CSS rgba().</summary>
    static string Css(string? color)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color ?? "#00000000");
            return string.Create(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{c.A / 255.0:0.###})");
        }
        catch
        {
            return "transparent";
        }
    }
}
