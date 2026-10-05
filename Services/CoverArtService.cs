using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MiniPlayer.Services;

/// <summary>
/// High-resolution cover art. Browsers hand Windows a tiny thumbnail (often 120×120), which
/// looks blurry in a big player. Looks the track up on Deezer for a 1000×1000 album cover; if it is not a catalog song (videos, channel remixes), finds
/// the YouTube video by its exact title and uses its 1280×720 thumbnail.
/// </summary>
public static class CoverArtService
{
    static readonly HttpClient Http = CreateClient();
    static readonly Dictionary<string, byte[]?> Cache = [];

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // A browser-like agent: YouTube serves its normal search page to it.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MiniPlayer/1.0");
        return http;
    }

    /// <returns>Image bytes, or null when no confident match exists (keep the browser's art).</returns>
    public static async Task<byte[]?> FindAsync(string title, string artist)
    {
        var (track, singer) = LyricsService.Clean(title, artist);
        if (string.IsNullOrWhiteSpace(track) || string.IsNullOrWhiteSpace(singer)) return null;

        var key = $"{Normalize(singer)}|{Normalize(track)}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        byte[]? image = null;
        try
        {
            var url = await FromDeezerAsync(track, singer);
            image = url is not null ? await Http.GetByteArrayAsync(url) : await FromYouTubeAsync(title, artist);
        }
        catch
        {
            return null; // offline or timeout: don't cache, try again next time
        }
        Cache[key] = image;
        return image;
    }

    static async Task<string?> FromDeezerAsync(string track, string artist)
    {
        var primary = Artists(artist)[0];
        // Field search first (precise); the plain one copes with "A e B" style artist credits.
        return await SearchDeezerAsync($"artist:\"{primary}\" track:\"{track}\"", track, artist)
            ?? await SearchDeezerAsync($"{primary} {track}", track, artist);
    }

    static async Task<string?> SearchDeezerAsync(string query, string track, string artist)
    {
        var q = Uri.EscapeDataString(query);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.deezer.com/search?q={q}&limit=10"));
        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        foreach (var item in data.EnumerateArray())
        {
            var t = item.GetProperty("title").GetString() ?? "";
            var a = item.GetProperty("artist").GetProperty("name").GetString() ?? "";
            if (Matches(track, artist, t, a) && item.GetProperty("album").TryGetProperty("cover_xl", out var cover))
                return cover.GetString();
        }
        return null;
    }

    /// <summary>
    /// The video whose title is exactly the one the browser reports (checked through YouTube's
    /// public oEmbed endpoint, no API key), then its largest thumbnail without black bars.
    /// </summary>
    static async Task<byte[]?> FromYouTubeAsync(string title, string artist)
    {
        var query = Uri.EscapeDataString($"{title} {artist}".Trim());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.youtube.com/results?search_query={query}&sp=EgIQAQ%3D%3D");
        request.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.8");
        using var response = await Http.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        var ids = System.Text.RegularExpressions.Regex.Matches(html, @"""videoId"":""([\w-]{11})""")
            .Select(m => m.Groups[1].Value).Distinct().Take(5);
        var wanted = Collapse(title);
        foreach (var id in ids)
        {
            using var oembed = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={id}&format=json"));
            var videoTitle = oembed.RootElement.GetProperty("title").GetString() ?? "";
            var channel = oembed.RootElement.GetProperty("author_name").GetString() ?? "";
            if (!string.Equals(Collapse(videoTitle), wanted, StringComparison.CurrentCultureIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(artist)
                && !Artists(artist).Select(Normalize).Any(a => a.Length > 0 && (Normalize(channel).Contains(a) || a.Contains(Normalize(channel)))))
                continue;

            foreach (var name in new[] { "maxresdefault", "hq720", "mqdefault" })
            {
                using var thumb = await Http.GetAsync($"https://i.ytimg.com/vi/{id}/{name}.jpg");
                if (thumb.IsSuccessStatusCode) return await thumb.Content.ReadAsByteArrayAsync();
            }
            return null;
        }
        return null;
    }

    /// <summary>Trimmed, single spaces (titles differ only in spacing sometimes).</summary>
    static string Collapse(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Same song and artist; "Song (Slowed + Reverb)" or another artist's song do not count.</summary>
    static bool Matches(string track, string artist, string foundTrack, string foundArtist)
    {
        var (cleanFound, _) = LyricsService.Clean(foundTrack, foundArtist);
        if (Normalize(cleanFound) != Normalize(track)) return false;
        var fa = Normalize(foundArtist);
        if (fa.Length == 0) return false;
        // Any credited artist will do: catalogs often list only the main one.
        return Artists(artist).Select(Normalize).Any(a => a.Length > 0 && (fa.Contains(a) || a.Contains(fa)));
    }

    /// <summary>"Aaron Smith e Krono" / "A &amp; B" / "A, B" / "A x B" / "A feat. B" → each artist.</summary>
    static string[] Artists(string artist)
    {
        var parts = System.Text.RegularExpressions.Regex.Split(artist,
            @"\s*(?:,|&|\s(?:e|and|y|x|with|feat\.?|ft\.?|featuring)\s)\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var list = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        return list.Length > 0 ? list : [artist];
    }

    /// <summary>Lowercase letters and digits only, accents removed ("Ação!" → "acao").</summary>
    static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
