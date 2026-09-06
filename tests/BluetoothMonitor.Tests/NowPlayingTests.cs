using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class NowPlayingTests
{
    [TestMethod]
    public void Classifier_LeavesRegularTrackUnmarked()
    {
        Assert.AreEqual("", NowPlayingMetadataClassifier.Classify("Midnight City", "M83", "Hurry Up, We're Dreaming"));
    }

    [TestMethod]
    public void RomanizerConvertsKanaAndKanjiWithoutNetwork()
    {
        Assert.AreEqual("konnichiha", JapaneseTitleRomanizer.Romanize("こんにちは"));
        Assert.AreEqual("sakura", JapaneseTitleRomanizer.Romanize("桜"));
    }

    [TestMethod]
    public void SnapshotKeepsOriginalTitleAndOptionalRomaji()
    {
        var snapshot = new NowPlayingSnapshot("桜", "Artist", "Player", "", JapaneseTitleRomanizer.Romanize("桜"));
        var viewModel = new BluetoothMonitor.Models.NowPlayingOverlayViewModel();

        viewModel.Apply(snapshot);

        Assert.AreEqual("桜", viewModel.Title);
        Assert.AreEqual("sakura", viewModel.RomajiTitle);
        Assert.IsTrue(viewModel.HasRomajiTitle);
    }

    [TestMethod]
    public void ClassifierMarksOpeningOrEnding()
    {
        Assert.AreEqual("Anime opening / ending", NowPlayingMetadataClassifier.Classify("Anime Opening", "Artist", "Series"));
        Assert.AreEqual("Anime opening / ending", NowPlayingMetadataClassifier.Classify("Song", "Artist", "Series ED"));
    }

    [TestMethod]
    public void ClassifierMarksAnimeSoundtrack()
    {
        Assert.AreEqual("Anime soundtrack", NowPlayingMetadataClassifier.Classify("Battle Theme", "Artist", "Anime Original Soundtrack"));
    }

    [TestMethod]
    public void JikanMatcherIdentifiesOpeningAndEnding()
    {
        Assert.AreEqual(
            "Anime opening · Kimetsu no Yaiba",
            AnimeThemesMatcher.Match([new AnimeThemeEntry("OP", "OP1", "Kimetsu no Yaiba")], "Gurenge", "LiSA"));
        Assert.AreEqual(
            "Anime ending · Anime series",
            AnimeThemesMatcher.Match([new AnimeThemeEntry("ED", "ED1", "Anime series")], "Name", "Artist"));
    }

    [TestMethod]
    public void JikanMatcherDoesNotMatchDifferentArtist()
    {
        Assert.AreEqual(
            "",
            AnimeThemesMatcher.Match([], "Gurenge", "Someone Else"));
    }

    [TestMethod]
    public void JikanMatcherAcceptsTitleSubtitleWhenArtistCreditDiffers()
    {
        Assert.AreEqual(
            "Anime opening · The Apothecary Diaries",
            AnimeThemesMatcher.Match([new AnimeThemeEntry("OP", "OP1", "The Apothecary Diaries")], "革命道中 - On The Way", "AINA THE END"));
    }

    [TestMethod]
    public void AnimeThemesQueryVariantsIncludesRomanizedJapaneseTitle()
    {
        CollectionAssert.Contains(AnimeThemesQueryVariants.Get("革命道中 - On The Way").ToList(), "kawainochimichinaka");
    }

    [TestMethod]
    public void NowPlayingViewModelCanHideAnimeInfoWithoutHidingTrack()
    {
        var viewModel = new BluetoothMonitor.Models.NowPlayingOverlayViewModel();
        viewModel.Apply(new NowPlayingSnapshot("Song", "Artist", "Player", "Anime opening · Series"));

        viewModel.ShowAnimeInfo = false;

        Assert.IsTrue(viewModel.IsVisible);
        Assert.IsFalse(viewModel.HasMediaTag);
    }

    [TestMethod]
    public void PopularAnimeThemes_FindsDirectMatches()
    {
        var matchGurenge = PopularAnimeThemes.FindMatch("Gurenge", "LiSA");
        Assert.AreEqual("Anime opening · Demon Slayer: Kimetsu no Yaiba", matchGurenge);

        var matchZankyosanka = PopularAnimeThemes.FindMatch("残響散歌", "Aimer");
        Assert.AreEqual("Anime opening · Demon Slayer: Kimetsu no Yaiba – Entertainment District Arc", matchZankyosanka);

        var matchKickBack = PopularAnimeThemes.FindMatch("KICK BACK", "Kenshi Yonezu");
        Assert.AreEqual("Anime opening · Chainsaw Man", matchKickBack);

        var matchIdol = PopularAnimeThemes.FindMatch("アイドル", "YOASOBI");
        Assert.AreEqual("Anime opening · [OSHI NO KO]", matchIdol);
    }

    [TestMethod]
    public void PopularAnimeThemes_ReturnsNullWhenUnknown()
    {
        var match = PopularAnimeThemes.FindMatch("Midnight City", "M83");
        Assert.IsNull(match);
    }

    [TestMethod]
    public async Task AnimeThemesService_ResolvesCatalogMatchImmediately()
    {
        var service = new AnimeThemesService();
        var result = await service.ClassifyAsync("Kaibutsu", "YOASOBI", "THE BOOK 2");
        Assert.AreEqual("Anime opening · BEASTARS Season 2", result);
    }

    [TestMethod]
    public void AnimeThemesMatcher_RejectsThemeWhenArtistMismatches()
    {
        var entry = new AnimeThemeEntry("OP", "OP1", "Demon Slayer", "Gurenge", ["LiSA"]);
        var match = AnimeThemesMatcher.Match([entry], "Gurenge", "Someone Else");
        Assert.AreEqual("", match);
    }

    [TestMethod]
    public void AnimeThemesMatcher_RejectsThemeWhenTitleMismatches()
    {
        var entry = new AnimeThemeEntry("OP", "OP1", "Koori Zokusei", "FROZEN MIDNIGHT", ["Takao Sakuma"]);
        var match = AnimeThemesMatcher.Match([entry], "Midnight City", "M83");
        Assert.AreEqual("", match);
    }

    [TestMethod]
    public void AnimeThemesMatcher_AcceptsMatchingTitleAndArtist()
    {
        var entry = new AnimeThemeEntry("OP", "OP1", "Tokyo Ghoul", "unravel", ["TK from Ling tosite sigure"]);
        var match = AnimeThemesMatcher.Match([entry], "unravel", "TK from Ling tosite sigure");
        Assert.AreEqual("Anime opening · Tokyo Ghoul", match);
    }

    [TestMethod]
    public void PopularAnimeThemes_FindsNewlyAddedThemes()
    {
        var matchTaidada = PopularAnimeThemes.FindMatch("taidada", "ZUTOMAYO");
        Assert.AreEqual("Anime ending · Dandadan", matchTaidada);

        var matchChainsawBlood = PopularAnimeThemes.FindMatch("chainsaw blood", "Vaundy");
        Assert.AreEqual("Anime ending · Chainsaw Man", matchChainsawBlood);

        var matchPokemon = PopularAnimeThemes.FindMatch("めざせポケモンマスター", "Rica Matsumoto");
        Assert.AreEqual("Anime opening · Pokémon (Original Series)", matchPokemon);
    }
}