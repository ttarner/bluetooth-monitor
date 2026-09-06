using System.Collections.Concurrent;
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

public static class PopularAnimeThemes
{
    public sealed record CatalogEntry(
        string[] Keywords,
        string[]? ArtistKeywords,
        string Anime,
        string? AnimeWesternTitle,
        string ThemeType,
        string Slug);

    public static readonly IReadOnlyList<CatalogEntry> Catalog =
    [
        new(["残響散歌", "zankyou sanka", "zankyosanka", "zankyou zanka"], ["aimer"], "Demon Slayer: Kimetsu no Yaiba – Entertainment District Arc", "Demon Slayer: Kimetsu no Yaiba – Entertainment District Arc", "Opening", "OP1"),
        new(["朝が来る", "asa ga kuru"], ["aimer"], "Demon Slayer: Kimetsu no Yaiba – Entertainment District Arc", "Demon Slayer: Kimetsu no Yaiba – Entertainment District Arc", "Ending", "ED1"),
        new(["紅蓮華", "gurenge"], ["lisa"], "Demon Slayer: Kimetsu no Yaiba", "Demon Slayer: Kimetsu no Yaiba", "Opening", "OP1"),
        new(["from the edge"], ["fictionjunction", "lisa"], "Demon Slayer: Kimetsu no Yaiba", "Demon Slayer: Kimetsu no Yaiba", "Ending", "ED1"),
        new(["炎", "homura"], ["lisa"], "Demon Slayer: Kimetsu no Yaiba – The Movie: Mugen Train", "Demon Slayer: Kimetsu no Yaiba – The Movie: Mugen Train", "Theme Song", "Theme"),
        new(["明け星", "akeboshi"], ["lisa"], "Demon Slayer: Kimetsu no Yaiba – Mugen Train Arc TV", "Demon Slayer: Kimetsu no Yaiba – Mugen Train Arc TV", "Opening", "OP1"),
        new(["白銀", "shirogane"], ["lisa"], "Demon Slayer: Kimetsu no Yaiba – Mugen Train Arc TV", "Demon Slayer: Kimetsu no Yaiba – Mugen Train Arc TV", "Ending", "ED1"),
        new(["絆ノ奇跡", "kizuna no kiseki"], ["man with a mission", "milet"], "Demon Slayer: Kimetsu no Yaiba – Swordsmith Village Arc", "Demon Slayer: Kimetsu no Yaiba – Swordsmith Village Arc", "Opening", "OP1"),
        new(["コイコガレ", "koi kogare"], ["milet", "man with a mission"], "Demon Slayer: Kimetsu no Yaiba – Swordsmith Village Arc", "Demon Slayer: Kimetsu no Yaiba – Swordsmith Village Arc", "Ending", "ED1"),
        new(["夢幻", "mugen"], ["my first story", "hyde"], "Demon Slayer: Kimetsu no Yaiba – Hashira Training Arc", "Demon Slayer: Kimetsu no Yaiba – Hashira Training Arc", "Opening", "OP1"),
        new(["アイドル", "idol"], ["yoasobi"], "[OSHI NO KO]", "[OSHI NO KO]", "Opening", "OP1"),
        new(["メフィスト", "mephisto"], ["queen bee", "ziyoou-vachi"], "[OSHI NO KO]", "[OSHI NO KO]", "Ending", "ED1"),
        new(["ファタール", "fatal"], ["gemn", "kento nakajima", "kitani tatsuya"], "[OSHI NO KO] Season 2", "[OSHI NO KO] Season 2", "Opening", "OP1"),
        new(["廻廻奇譚", "kaikai kitan"], ["eve"], "Jujutsu Kaisen", "Jujutsu Kaisen", "Opening", "OP1"),
        new(["lost in paradise"], ["ali", "aklo"], "Jujutsu Kaisen", "Jujutsu Kaisen", "Ending", "ED1"),
        new(["vivid vice"], ["who-ya extended"], "Jujutsu Kaisen", "Jujutsu Kaisen", "Opening", "OP2"),
        new(["青のすみか", "ao no sumika", "where our blue is"], ["tatsuya kitani", "kitani tatsuya"], "Jujutsu Kaisen Season 2", "Jujutsu Kaisen Season 2", "Opening", "OP1"),
        new(["specialz"], ["king gnu"], "Jujutsu Kaisen Season 2", "Jujutsu Kaisen Season 2", "Opening", "OP2"),
        new(["一途", "ichizu"], ["king gnu"], "Jujutsu Kaisen 0", "Jujutsu Kaisen 0", "Ending", "ED1"),
        new(["逆夢", "sakayume"], ["king gnu"], "Jujutsu Kaisen 0", "Jujutsu Kaisen 0", "Ending", "ED2"),
        new(["kick back"], ["kenshi yonezu", "yonezu kenshi"], "Chainsaw Man", "Chainsaw Man", "Opening", "OP1"),
        new(["刃渡り2億センチ", "hawatori 2-oku centimeter"], ["maximum the hormone"], "Chainsaw Man", "Chainsaw Man", "Ending", "ED3"),
        new(["ちゅ、多様性。", "chu, tayousei"], ["ano"], "Chainsaw Man", "Chainsaw Man", "Ending", "ED7"),
        new(["ファイトソング", "fight song"], ["eve"], "Chainsaw Man", "Chainsaw Man", "Ending", "ED12"),
        new(["第ゼロ感", "dai zero kan"], ["10-feet"], "THE FIRST SLAM DUNK", "THE FIRST SLAM DUNK", "Ending", "ED"),
        new(["勇者", "yuusha", "the brave"], ["yoasobi"], "Frieren: Beyond Journey's End", "Frieren: Beyond Journey's End", "Opening", "OP1"),
        new(["怪物", "kaibutsu", "monster"], ["yoasobi"], "BEASTARS Season 2", "BEASTARS Season 2", "Opening", "OP1"),
        new(["優しい彗星", "yasashii suisei", "comet"], ["yoasobi"], "BEASTARS Season 2", "BEASTARS Season 2", "Ending", "ED1"),
        new(["anytime anywhere"], ["milet"], "Frieren: Beyond Journey's End", "Frieren: Beyond Journey's End", "Ending", "ED1"),
        new(["晴る", "haru", "sunny"], ["yorushika"], "Frieren: Beyond Journey's End", "Frieren: Beyond Journey's End", "Opening", "OP2"),
        new(["ミックスナッツ", "mixed nuts"], ["official hige dandism"], "SPY x FAMILY", "SPY x FAMILY", "Opening", "OP1"),
        new(["喜劇", "comedy", "kigeki"], ["gen hoshino"], "SPY x FAMILY", "SPY x FAMILY", "Ending", "ED1"),
        new(["souvenir"], ["bump of chicken"], "SPY x FAMILY", "SPY x FAMILY", "Opening", "OP2"),
        new(["クラクラ", "kura kura"], ["ado"], "SPY x FAMILY Season 2", "SPY x FAMILY Season 2", "Opening", "OP1"),
        new(["心臓を捧げよ", "shinzou wo sasageyo"], ["linked horizon"], "Attack on Titan Season 2", "Attack on Titan Season 2", "Opening", "OP1"),
        new(["紅蓮の弓矢", "guren no yumiya"], ["linked horizon"], "Attack on Titan Season 1", "Attack on Titan Season 1", "Opening", "OP1"),
        new(["the rumbling"], ["sim"], "Attack on Titan The Final Season Part 2", "Attack on Titan The Final Season Part 2", "Opening", "OP1"),
        new(["悪魔の子", "akuma no ko"], ["higuchi ai"], "Attack on Titan The Final Season Part 2", "Attack on Titan The Final Season Part 2", "Ending", "ED1"),
        new(["unravel"], ["tk from ling tosite sigure"], "Tokyo Ghoul", "Tokyo Ghoul", "Opening", "OP1"),
        new(["残酷な天使のテーゼ", "a cruel angel's thesis", "zankoku na tenshi no teze"], ["yoko takahashi"], "Neon Genesis Evangelion", "Neon Genesis Evangelion", "Opening", "OP1"),
        new(["シルエット", "silhouette"], ["kana-boon"], "Naruto Shippuden", "Naruto Shippuden", "Opening", "OP16"),
        new(["ブルーバード", "blue bird"], ["ikimonogakari"], "Naruto Shippuden", "Naruto Shippuden", "Opening", "OP3"),
        new(["ピースサイン", "peace sign"], ["kenshi yonezu", "yonezu kenshi"], "My Hero Academia Season 2", "My Hero Academia Season 2", "Opening", "OP1"),
        new(["新時代", "new genesis", "shin jidai"], ["ado"], "One Piece Film: Red", "One Piece Film: Red", "Theme Song", "Theme"),
        new(["ウィーアー!", "we are!"], ["hiroshi kitadani"], "One Piece", "One Piece", "Opening", "OP1"),
        new(["青春コンプレックス", "seishun complex"], ["kessoku band"], "Bocchi the Rock!", "Bocchi the Rock!", "Opening", "OP1"),
        new(["ギターと孤独と蒼い惑星", "guitar to kodoku to aoi hoshi"], ["kessoku band"], "Bocchi the Rock!", "Bocchi the Rock!", "Insert", "Insert"),
        new(["bling-bang-bang-born"], ["creepy nuts"], "Mashle: Magic and Muscles", "Mashle: Magic and Muscles", "Opening", "OP2"),
        new(["オトノケ", "otonoke"], ["creepy nuts"], "Dandadan", "Dandadan", "Opening", "OP1"),
        new(["前前前世", "zenzenzense"], ["radwimps"], "Your Name. (君の名は。)", "Your Name.", "Theme Song", "Theme"),
        new(["スパークル", "sparkle"], ["radwimps"], "Your Name. (君の名は。)", "Your Name.", "Theme Song", "Theme"),
        new(["すずめ", "suzume"], ["radwimps", "toaka"], "Suzume (すずめの戸締まり)", "Suzume", "Theme Song", "Theme"),
        new(["crossing field"], ["lisa"], "Sword Art Online", "Sword Art Online", "Opening", "OP1"),
        new(["again"], ["yui"], "Fullmetal Alchemist: Brotherhood", "Fullmetal Alchemist: Brotherhood", "Opening", "OP1"),
        new(["butterfly"], ["koji wada"], "Digimon Adventure", "Digimon Adventure", "Opening", "OP1"),
        new(["cha-la head-cha-la"], ["hironobu kageyama"], "Dragon Ball Z", "Dragon Ball Z", "Opening", "OP1"),
        new(["dan dan 心魅かれてく", "dan dan kokoro hikareteku"], ["field of view"], "Dragon Ball GT", "Dragon Ball GT", "Opening", "OP1"),
        new(["only my railgun"], ["fripside"], "A Certain Scientific Railgun", "A Certain Scientific Railgun", "Opening", "OP1")
    ];

    public static string? FindMatch(string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var cleanTitle = title.ToLowerInvariant().Trim();
        var romajiTitle = JapaneseTitleRomanizer.Romanize(title).ToLowerInvariant().Trim();
        var cleanArtist = (artist ?? "").ToLowerInvariant().Trim();

        var titleTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cleanTitle };
        if (!string.IsNullOrWhiteSpace(romajiTitle))
            titleTargets.Add(romajiTitle);

        var sepIndex = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (sepIndex > 0)
            titleTargets.Add(cleanTitle[..sepIndex].Trim());

        var parenIndex = cleanTitle.IndexOfAny(['(', '[']);
        if (parenIndex > 0)
            titleTargets.Add(cleanTitle[..parenIndex].Trim());

        foreach (var entry in Catalog)
        {
            var titleMatched = entry.Keywords.Any(kw =>
                titleTargets.Any(target => target.Contains(kw, StringComparison.OrdinalIgnoreCase) || kw.Contains(target, StringComparison.OrdinalIgnoreCase)));

            if (!titleMatched)
                continue;

            if (entry.ArtistKeywords is { Length: > 0 } && !string.IsNullOrWhiteSpace(cleanArtist))
            {
                var artistMatched = entry.ArtistKeywords.Any(akw =>
                    cleanArtist.Contains(akw, StringComparison.OrdinalIgnoreCase) || akw.Contains(cleanArtist, StringComparison.OrdinalIgnoreCase));

                if (!artistMatched)
                    continue;
            }

            var animeName = entry.AnimeWesternTitle ?? entry.Anime;
            var themeType = entry.ThemeType.Equals("Theme Song", StringComparison.OrdinalIgnoreCase) ? "theme song" : entry.ThemeType.ToLowerInvariant();
            return $"Anime {themeType} · {animeName}";
        }

        return null;
    }
}

public sealed class AnimeThemesService : IAnimeThemeService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly HttpClient AnimeThemesClient = CreateHttpClient("https://api.animethemes.moe/");
    private static readonly HttpClient KitsuClient = CreateKitsuClient();
    private static readonly ConcurrentDictionary<string, string> EnrichedTitleCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (DateTimeOffset Expires, string Tag)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private DateTimeOffset _serviceUnavailableUntil;

    public async Task<string> ClassifyAsync(string title, string artist, string? album, CancellationToken cancellationToken = default)
    {
        // 1. Check curated catalog for instant 0ms offline match
        var catalogMatch = PopularAnimeThemes.FindMatch(title, artist);
        if (!string.IsNullOrWhiteSpace(catalogMatch))
            return catalogMatch;

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
                    // Enrich anime name via Kitsu if available
                    var theme = tags.FirstOrDefault(t => t.Type is "OP" or "ED");
                    if (theme is not null && !string.IsNullOrWhiteSpace(theme.AnimeName))
                    {
                        var enrichedName = await EnrichAnimeTitleAsync(theme.AnimeName, cancellationToken);
                        tag = $"Anime {(theme.Type == "OP" ? "opening" : "ending")} · {enrichedName}";
                    }

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
        var clean = Regex.Replace(query, @"[^\w\s]", " ").Trim();
        if (string.IsNullOrWhiteSpace(clean) || clean.Length < 2)
            return [];

        try
        {
            // 1. Try search?q= full search endpoint
            using var searchResponse = await AnimeThemesClient.GetAsync(
                $"search?q={Uri.EscapeDataString(clean)}&include[song]=animethemes.anime&fields[search]=songs,animethemes",
                cancellationToken);

            if (searchResponse.IsSuccessStatusCode)
            {
                using var searchJson = await JsonDocument.ParseAsync(await searchResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (searchJson.RootElement.TryGetProperty("search", out var searchObj) && searchObj.TryGetProperty("songs", out var songs) && songs.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<AnimeThemeEntry>();
                    foreach (var song in songs.EnumerateArray())
                    {
                        if (song.TryGetProperty("animethemes", out var themes) && themes.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var theme in themes.EnumerateArray())
                            {
                                var entry = ParseTheme(theme);
                                if (entry is not null)
                                    list.Add(entry);
                            }
                        }
                    }

                    if (list.Count > 0)
                        return list;
                }
            }

            // 2. Fallback to song?filter[title]= endpoint
            using var filterResponse = await AnimeThemesClient.GetAsync(
                $"song?filter%5Btitle%5D={Uri.EscapeDataString(query)}&include=animethemes.anime",
                cancellationToken);

            if (filterResponse.IsSuccessStatusCode)
            {
                using var filterJson = await JsonDocument.ParseAsync(await filterResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (filterJson.RootElement.TryGetProperty("songs", out var songs) && songs.ValueKind == JsonValueKind.Array)
                {
                    return songs.EnumerateArray()
                        .Where(song => song.TryGetProperty("title", out var songTitle)
                            && string.Equals(songTitle.GetString(), query, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(song => song.TryGetProperty("animethemes", out var themes) && themes.ValueKind == JsonValueKind.Array
                            ? themes.EnumerateArray().Select(ParseTheme)
                            : [])
                        .Where(theme => theme is not null)
                        .Select(theme => theme!)
                        .ToArray();
                }
            }
        }
        catch
        {
            // Non-blocking fallback on network or API failure
        }

        return [];
    }

    private static async Task<string> EnrichAnimeTitleAsync(string animeName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(animeName))
            return animeName;

        if (EnrichedTitleCache.TryGetValue(animeName, out var cached))
            return cached;

        try
        {
            var cleanSearch = Regex.Replace(animeName, @"–|—|-|\(.*?\)|\[.*?\]|Season \d+|Arc", " ").Trim();
            var query = string.IsNullOrWhiteSpace(cleanSearch) ? animeName : cleanSearch;

            using var response = await KitsuClient.GetAsync($"anime?filter%5Btext%5D={Uri.EscapeDataString(query)}&page%5Blimit%5D=1", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                {
                    var first = data[0];
                    if (first.TryGetProperty("attributes", out var attrs))
                    {
                        if (attrs.TryGetProperty("titles", out var titles) && titles.TryGetProperty("en", out var enTitle) && !string.IsNullOrWhiteSpace(enTitle.GetString()))
                        {
                            var result = enTitle.GetString()!;
                            EnrichedTitleCache[animeName] = result;
                            return result;
                        }

                        if (attrs.TryGetProperty("canonicalTitle", out var canonical) && !string.IsNullOrWhiteSpace(canonical.GetString()))
                        {
                            var result = canonical.GetString()!;
                            EnrichedTitleCache[animeName] = result;
                            return result;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback to original anime name if Kitsu fails
        }

        EnrichedTitleCache[animeName] = animeName;
        return animeName;
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
    private static HttpClient CreateHttpClient(string baseAddress)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.animethemes.moe/"), Timeout = TimeSpan.FromSeconds(5) };
        var client = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothMonitor", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static HttpClient CreateKitsuClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://kitsu.io/api/edge/"), Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothMonitor", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        return client;
    }
}

public sealed record AnimeThemeEntry(string Type, string Slug, string AnimeName);

public static class AnimeThemesQueryVariants
{
    private static readonly HttpClient TranslationClient = CreateTranslationClient();

    private static HttpClient CreateTranslationClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothMonitor", "1.0"));
        return client;
    }

    public static IEnumerable<string> Get(string title)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { title };
        var separatorIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
            variants.Add(title[..separatorIndex]);

        var parenIndex = title.IndexOfAny(['(', '[']);
        if (parenIndex > 0)
            variants.Add(title[..parenIndex].Trim());

        foreach (var variant in variants.ToArray())
        {
            // Add romanized version
            var romanizedTitle = JapaneseTitleRomanizer.Romanize(variant);
            var isJapanese = !string.Equals(romanizedTitle, variant, StringComparison.OrdinalIgnoreCase);
            if (isJapanese)
            if (!string.Equals(romanizedTitle, variant, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(romanizedTitle);

            // Add English translation variants using public API
            if (isJapanese)
            {
                foreach (var translated in GetEnglishTranslationsAsync(variant).GetAwaiter().GetResult())
                {
                    if (!variants.Contains(translated, StringComparer.OrdinalIgnoreCase))
                        variants.Add(translated);
                }
                var romParenIndex = romanizedTitle.IndexOfAny(['(', '[']);
                if (romParenIndex > 0)
                    variants.Add(romanizedTitle[..romParenIndex].Trim());
            }
        }

        return variants;
    }

    private static async Task<IEnumerable<string>> GetEnglishTranslationsAsync(string japaneseText)
    {
        var results = new List<string>();
        // Use MyMemory Translation API (free, no key required for limited usage)
        const string apiUrl = "https://api.mymemory.translated.net/get";

        try
        {
            using var response = await TranslationClient.GetAsync(apiUrl + $"?q={Uri.EscapeDataString(japaneseText)}&langpair=ja|en");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var json = JsonDocument.Parse(content);
                if (json.RootElement.TryGetProperty("responseData", out var responseData)
                    && responseData.TryGetProperty("translatedText", out var textProp))
                {
                    var translation = textProp.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(translation))
                        results.Add(translation);
                }
            }
        }
        catch
        {
            // API call failed or timed out, silently ignore (caller will use fallback)
        }

        return results;
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