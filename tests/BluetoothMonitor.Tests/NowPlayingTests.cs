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
}