using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Romanization;
using Windows.Media.Control;

namespace BluetoothMonitor.Services;

public sealed record NowPlayingSnapshot(string Title, string Artist, string AppName, string MediaTag, string RomajiTitle = "");

public interface INowPlayingService
{
    Task<NowPlayingSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
}

public interface IAnimeThemeService
{
    Task<string> ClassifyAsync(string title, string artist, string? album, CancellationToken cancellationToken = default);
}

public sealed class WindowsNowPlayingService : INowPlayingService
{
    private readonly IAnimeThemeService _animeThemeService;

    public WindowsNowPlayingService(IAnimeThemeService? animeThemeService = null)
    {
        _animeThemeService = animeThemeService ?? new AnimeThemesService();
    }

    public async Task<NowPlayingSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        foreach (var session in manager.GetSessions())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playbackStatus = session.GetPlaybackInfo().PlaybackStatus;
            if (playbackStatus is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused))
                continue;

            var properties = await session.TryGetMediaPropertiesAsync();
            if (string.IsNullOrWhiteSpace(properties.Title))
                continue;

            var artist = properties.Artist ?? properties.AlbumArtist ?? "";
            var mediaTag = await _animeThemeService.ClassifyAsync(
                properties.Title,
                artist,
                properties.AlbumTitle,
                cancellationToken);
            var title = properties.Title.Trim();
            return new NowPlayingSnapshot(
                title,
                artist.Trim(),
                session.SourceAppUserModelId,
                mediaTag,
                JapaneseTitleRomanizer.Romanize(title));
        }

        return null;
    }
}

public static class JapaneseTitleRomanizer
{
    private static readonly Japanese.ModifiedHepburn KanaRomanizer = new();
    private static readonly Japanese.KanjiReadings KanjiRomanizer = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Romanize(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        return Cache.GetOrAdd(title, static value =>
        {
            var withKanji = KanjiRomanizer.Process(
                value,
                Japanese.KanjiReadings.ReadingTypes.Kunyomi | Japanese.KanjiReadings.ReadingTypes.Onyomi);
            return KanaRomanizer.Process(withKanji).Trim();
        });
    }
}

public static class NowPlayingMetadataClassifier
{
    public static string Classify(string title, string artist, string? album)
    {
        var text = $"{title} {artist} {album}".ToLowerInvariant();
        var isAnime = text.Contains("anime") || text.Contains("アニメ") || Regex.IsMatch(text, @"\b(op|ed|opening|ending)\b");
        if (!isAnime)
            return "";

        if (Regex.IsMatch(text, @"\b(opening|ending|op|ed)\b"))
            return "Anime opening / ending";
        if (Regex.IsMatch(text, @"\b(ost|soundtrack|original soundtrack|theme song)\b"))
            return "Anime soundtrack";
        return "Anime track";
    }
}

public sealed class AnimeThemesService : IAnimeThemeService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly Dictionary<string, (DateTimeOffset Expires, string Tag)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private DateTimeOffset _serviceUnavailableUntil;

    public async Task<string> ClassifyAsync(string title, string artist, string? album, CancellationToken cancellationToken = default)
    {
        var fallback = NowPlayingMetadataClassifier.Classify(title, artist, album);
        var cacheKey = $"{title}|{artist}|{album}";
        if (_serviceUnavailableUntil > DateTimeOffset.UtcNow)
            return fallback;
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
            return string.IsNullOrWhiteSpace(cached.Tag) ? fallback : cached.Tag;

        if (!await _requestLock.WaitAsync(0, cancellationToken))
            return fallback;

        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached.Expires > DateTimeOffset.UtcNow)
                return string.IsNullOrWhiteSpace(cached.Tag) ? fallback : cached.Tag;

            foreach (var query in AnimeThemesQueryVariants.Get(title))
            {
                var tags = await SearchSongAsync(query, cancellationToken);
                var tag = AnimeThemesMatcher.Match(tags, title, artist);
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    _cache[cacheKey] = (DateTimeOffset.UtcNow.AddHours(12), tag);
                    return tag;
                }
            }

            var result = fallback;
            _cache[cacheKey] = (DateTimeOffset.UtcNow.AddHours(12), result);
            return result;
        }
        catch (HttpRequestException)
        {
            _serviceUnavailableUntil = DateTimeOffset.UtcNow.AddMinutes(10);
            _cache[cacheKey] = (DateTimeOffset.UtcNow.AddMinutes(10), fallback);
            return fallback;
        }
        catch (JsonException)
        {
            _cache[cacheKey] = (DateTimeOffset.UtcNow.AddHours(1), fallback);
            return fallback;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static async Task<IReadOnlyList<AnimeThemeEntry>> SearchSongAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            $"song?filter%5Btitle%5D={Uri.EscapeDataString(query)}&include=animethemes.anime",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var jsonStr = json.RootElement.ToString();
        return json.RootElement.GetProperty("songs").EnumerateArray()
            .Where(song => song.TryGetProperty("title", out var songTitle)
                && string.Equals(songTitle.GetString(), query, StringComparison.OrdinalIgnoreCase))
            .SelectMany(song => song.TryGetProperty("animethemes", out var themes) && themes.ValueKind == JsonValueKind.Array
                ? themes.EnumerateArray().Select(ParseTheme)
                : [])
            .Where(theme => theme is not null)
            .Select(theme => theme!)
            .ToArray();
    }

    private static AnimeThemeEntry? ParseTheme(JsonElement theme)
    {
        if (!theme.TryGetProperty("type", out var type)
            || !theme.TryGetProperty("slug", out var slug)
            || !theme.TryGetProperty("anime", out var anime)
            || !anime.TryGetProperty("name", out var animeName))
            return null;

        return new AnimeThemeEntry(type.GetString() ?? "", slug.GetString() ?? "", animeName.GetString() ?? "");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.animethemes.moe/"), Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothMonitor", "1.0"));
        return client;
    }
}

public sealed record AnimeThemeEntry(string Type, string Slug, string AnimeName);

public static class AnimeThemesQueryVariants
{
    public static IEnumerable<string> Get(string title)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { title };
        var separatorIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
            variants.Add(title[..separatorIndex]);

        foreach (var variant in variants.ToArray())
        {
            // Add romanized version
            var romanizedTitle = JapaneseTitleRomanizer.Romanize(variant);
            if (!string.Equals(romanizedTitle, variant, StringComparison.OrdinalIgnoreCase))
                variants.Add(romanizedTitle);

            // Add English translation variants using public API
            foreach (var translated in GetEnglishTranslationsAsync(variant).Result)
            {
                if (!variants.Contains(translated, StringComparer.OrdinalIgnoreCase))
                    variants.Add(translated);
            }
        }

        return variants;
    }

    private static IEnumerable<string> GetEnglishTranslationsAsync(string japaneseText)
    {
        // Use MyMemory Translation API (free, no key required for limited usage)
        const string apiUrl = "https://api.mymemory.translated.net/get";
        
        using var httpClient = new HttpClient();
        try
        {
            var response = httpClient.GetAsync(apiUrl + $"?q={Uri.EscapeDataString(japaneseText)}&langpair=ja|en")
                .Wait(TimeSpan.FromSeconds(5));
            
            var content = response.Content.ReadAsStringAsync().Result();
            // Parse JSON response: {"responseData":{"translatedText":"...", "detectedSourceLanguage":"..."}}
            var jsonStart = content.IndexOf("{", StringComparison.Ordinal);
            if (jsonStart >= 0)
            {
                var jsonEnd = content.LastIndexOf("}", StringComparison.Ordinal);
                if (jsonEnd > jsonStart)
                {
                    var jsonString = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var startIndex = jsonString.IndexOf("\"translatedText\":\"", StringComparison.Ordinal) + 16;
                    var endIndex = jsonString.IndexOf("\"", StringComparison.Ordinal, startIndex);
                    if (startIndex >= 0 && endIndex > startIndex)
                    {
                        var translation = jsonString.Substring(startIndex, endIndex - startIndex).Trim();
                        if (!string.IsNullOrWhiteSpace(translation))
                            yield return translation;
                    }
                }
            }
        }
        catch
        {
            // API call failed or timed out, silently ignore (caller will use fallback)
        }
    }
}

public static class AnimeThemesMatcher
{
    public static string Match(IEnumerable<AnimeThemeEntry> themes, string title, string artist)
    {
        var theme = themes.FirstOrDefault(item => item.Type is "OP" or "ED");
        return theme is null ? "" : $"Anime {(theme.Type == "OP" ? "opening" : "ending")} · {theme.AnimeName}";
    }
}