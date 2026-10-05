using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniPlayer.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <param name="Lines">Time-synced lines, or null when only plain lyrics (or nothing) exist.</param>
/// <param name="Found">True when the track exists on LRCLIB (even without sync).</param>
public sealed record LyricsResult(IReadOnlyList<LyricLine>? Lines, bool Found);

/// <summary>Synced lyrics from LRCLIB (https://lrclib.net), a free, keyless lyrics database.</summary>
public static partial class LyricsService
{
    static readonly HttpClient Http = CreateClient();
    static readonly Dictionary<string, LyricsResult> Cache = [];

    static HttpClient CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri("https://lrclib.net/api/"), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MiniPlayer/1.0");
        return http;
    }

    public static async Task<LyricsResult> GetAsync(string title, string artist, TimeSpan duration)
    {
        var (track, singer) = Clean(title, artist);
        var key = $"{singer}|{track}|{(int)duration.TotalSeconds}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        LyricsResult result;
        try
        {
            result = await SearchAsync($"search?track_name={Uri.EscapeDataString(track)}&artist_name={Uri.EscapeDataString(singer)}", duration);
            if (!result.Found || result.Lines is null)
            {
                var loose = await SearchAsync($"search?q={Uri.EscapeDataString($"{singer} {track}")}", duration);
                if (loose.Lines is not null || !result.Found) result = loose;
            }
        }
        catch
        {
            // Offline / timeout: don't cache so it retries on the next track change.
            return new LyricsResult(null, false);
        }

        Cache[key] = result;
        return result;
    }

    static async Task<LyricsResult> SearchAsync(string query, TimeSpan duration)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(query));
        JsonElement? best = null;
        var bestScore = double.MaxValue;
        var found = false;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            found = true;
            if (!item.TryGetProperty("syncedLyrics", out var synced) || synced.ValueKind != JsonValueKind.String) continue;
            // Prefer the version whose length matches what is playing (live/edit cuts differ).
            var score = duration > TimeSpan.Zero && item.TryGetProperty("duration", out var d) && d.TryGetDouble(out var secs)
                ? Math.Abs(secs - duration.TotalSeconds)
                : 0;
            if (score < bestScore)
            {
                best = item;
                bestScore = score;
            }
        }

        if (best is null || bestScore > 15) return new LyricsResult(null, found);
        return new LyricsResult(ParseLrc(best.Value.GetProperty("syncedLyrics").GetString()!), true);
    }

    static List<LyricLine> ParseLrc(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var tags = LrcTag().Matches(raw);
            if (tags.Count == 0) continue;
            var text = raw[(tags[^1].Index + tags[^1].Length)..].Trim();
            foreach (Match tag in tags)
            {
                var time = TimeSpan.FromMinutes(int.Parse(tag.Groups[1].Value))
                         + TimeSpan.FromSeconds(double.Parse(tag.Groups[2].Value, CultureInfo.InvariantCulture));
                lines.Add(new LyricLine(time, text.Length > 0 ? text : "♪"));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>
    /// Turns YouTube-style metadata ("Artist - Song (Official Video)" by "ArtistVEVO") into
    /// a clean track/artist pair.
    /// </summary>
    internal static (string Track, string Artist) Clean(string title, string artist)
    {
        var a = ChannelSuffix().Replace(artist, "").Trim();
        var t = Noise().Replace(title, "").Trim();

        var dash = DashSplit().Match(t);
        if (dash.Success)
        {
            var left = dash.Groups[1].Value.Trim();
            var right = dash.Groups[2].Value.Trim();
            if (a.Length == 0 || left.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(left, StringComparison.OrdinalIgnoreCase))
            {
                a = left;
                t = right;
            }
            else if (VersionSuffix().IsMatch(right))
            {
                t = left;
            }
        }

        t = FeatSuffix().Replace(t, "").Trim();
        return (t, a);
    }

    [GeneratedRegex(@"\[(\d+):(\d+(?:\.\d+)?)\]")]
    private static partial Regex LrcTag();

    [GeneratedRegex(@"\s*(-\s*Topic|VEVO|Official)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffix();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(official|video|audio|lyric|letra|clipe|clip|visuali[sz]er|hd|4k|remaster|legendado|tradu[cç][aã]o|mv)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"^(.+?)\s+[-–—]\s+(.+)$")]
    private static partial Regex DashSplit();

    [GeneratedRegex(@"remaster|live|version|vers[aã]o|edit|mix|ao vivo", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s*[\(\[]?\s*(feat\.?|ft\.?|featuring)\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatSuffix();
}
