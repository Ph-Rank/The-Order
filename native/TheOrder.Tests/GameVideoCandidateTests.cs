using TheOrder.Application;
using TheOrder.Presentation;
using TheOrder.Domain;
using Xunit;

namespace TheOrder.Tests;

public sealed class GameVideoCandidateTests
{
    private static WorkMetadata Work => new("990001", "中文资料名", "日語の原名", ["Romanized Name", "English Official Title"],
        [new("English Official Title", "official", false, "en"), new("日語の原名", "official", false, "ja")],
        null, null, null, "", "2004-01-01", "day", ["Synthetic company"], [], [], [], [new("vndb", "v999999", null)], null, null, DateTimeOffset.UtcNow.ToString("O"));
    private static GameVideoSource Source => GameVideoTests.Source() with { Title = "A test game", Aliases = ["合成作品"], SteamId = null };
    private static IgdbGame Game(int id, string kind = "Main Game", string name = "A test game") => GameVideoTests.Game(id) with { Name = name, Kind = kind, Aliases = [], ExternalIds = [] };

    [Theory]
    [InlineData("Main Game", true)] [InlineData("Remake", true)] [InlineData("Remaster", true)] [InlineData("Expanded Game", true)]
    [InlineData("Port", false)] [InlineData("Mod", false)] [InlineData("Bundle", false)] [InlineData("Episode", false)] [InlineData("Unknown", false)]
    public void PcShortlistKeepsRelevantKindsWithoutClaimingIdentity(string kind, bool primary) => Assert.Equal(primary, GameVideos.PrimaryCandidate(Source, Game(1, kind)));

    [Theory]
    [InlineData("A test game 2")] [InlineData("A test game: Side Stories")] [InlineData("A test game Complete Collection")]
    public void SimilarTitlesDoNotPromoteSequelsOrCollections(string title) => Assert.False(GameVideos.PrimaryCandidate(Source, Game(1, name: title)));

    [Fact]
    public void UnicodeAliasesMatchButPunctuationOnlyNamesDoNot()
    {
        Assert.True(GameVideos.PrimaryCandidate(Source, Game(1, name: "Ａ　ＴＥＳＴ　ＧＡＭＥ")));
        Assert.True(GameVideos.PrimaryCandidate(Source, Game(1, name: "Other") with { Aliases = ["合成作品"] }));
        Assert.False(GameVideos.PrimaryCandidate(Source with { Title = "☆", Aliases = [] }, Game(1, name: "★")));
    }
    [Theory] [InlineData("PlayStation 4")] [InlineData("Nintendo Switch")] [InlineData("Linux")]
    public void ConsoleOnlyAndNonWindowsVersionsStayOutOfPcShortlist(string platform) => Assert.False(GameVideos.PrimaryCandidate(Source, Game(1) with { Platforms = [platform] }));
    [Fact]
    public void HistoricalPcOriginalsRemainAvailableInTheShortlist() => Assert.True(GameVideos.PrimaryCandidate(Source, Game(1) with { Platforms = ["PC-98"] }));

    [Fact]
    public void ExactSteamEvidenceKeepsTheActualPcPortEvenWhenTitlesDiffer()
    {
        var source = Source with { SteamId = "123" };
        var game = Game(1, "Port", "Different localization title") with { ExternalIds = [new("Steam", "123")] };
        Assert.True(GameVideos.PrimaryCandidate(source, game));
        Assert.False(GameVideos.PrimaryCandidate(source, game with { ExternalIds = [new("Steam", "456")] }));
        Assert.False(GameVideos.PrimaryCandidate(source, game with { Platforms = ["Nintendo Switch"] }));
    }
    [Fact]
    public async Task OriginalAndRemasterRemainSeparateAndOtherResultsAreRetainedWithoutAutomaticBinding()
    {
        var original = Game(1) with { ReleaseDate = "2004-01-01" };
        var remaster = Game(2, "Remaster") with { ReleaseDate = "2024-01-01", ParentGame = 1 };
        var unrelated = Game(3, "Mod"); var sequel = Game(4, name: "A test game 2"); var console = Game(5) with { Platforms = ["PlayStation 4"] };
        var all = new[] { remaster, unrelated, sequel, console, original };
        var provider = new GameVideoTests.Provider((q, _) => Task.FromResult(GameVideoTests.Result(q.Id is { } id ? all.Where(x => x.Id == id).ToArray() : all)));
        using var vm = new GameVideoViewModel(new(provider), new SessionMediaIdentityDecisions()); vm.SetSource(Source); await vm.LoadAsync();
        Assert.Equal(new[] { 1, 2 }, vm.PrimaryCandidates.Select(x => x.Id));
        Assert.Equal(new[] { 3, 4, 5 }.Order(), vm.OtherCandidates.Select(x => x.Id).Order());
        Assert.Equal(5, vm.Candidates.Length); Assert.Null(vm.Game); Assert.Empty(vm.Videos);
        await vm.ConfirmAsync(remaster); Assert.Equal(2, vm.Game!.Id);
    }
    [Fact]
    public async Task RejectionMovesACandidateOutOfTheShortlistWhileAllowingAnExplicitCorrection()
    {
        var game = Game(1); var store = new SessionMediaIdentityDecisions();
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(GameVideoTests.Result(game)));
        using var vm = new GameVideoViewModel(new(provider), store); vm.SetSource(Source); await vm.LoadAsync();
        await vm.RejectGameAsync(game); await vm.LoadAsync(true);
        Assert.Empty(vm.PrimaryCandidates); Assert.Single(vm.OtherCandidates); Assert.True(vm.IsRejected(game)); Assert.Null(vm.Game);
        await vm.ConfirmAsync(game); Assert.Equal(1, vm.Game!.Id);
    }
    [Fact]
    public async Task IncompleteShortlistAndUnknownPlatformRemainManualReviewOnly()
    {
        var game = Game(1); var unknown = Game(2) with { Platforms = [] };
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(GameVideoTests.Result(game, unknown) with { Complete = false }));
        using var vm = new GameVideoViewModel(new(provider), new SessionMediaIdentityDecisions()); vm.SetSource(Source); await vm.LoadAsync();
        Assert.Single(vm.PrimaryCandidates); Assert.Single(vm.OtherCandidates); Assert.False(vm.Complete); Assert.Null(vm.Game);
    }
    [Fact]
    public void SearchUsesProviderEnglishThenOriginalWithoutChangingIdentityOrUsingAPersonalName()
    {
        var source = GameVideoSource.From("local", Work);
        Assert.Equal("English Official Title", source.Title); Assert.Equal("日語の原名", source.Aliases[0]);
        Assert.DoesNotContain("用户自定义的名称", new[] { source.Title }.Concat(source.Aliases));
        var languageChanged = GameVideoSource.From("local", Work with { Title = "Another displayed translation" });
        Assert.Equal(source.Fingerprint, languageChanged.Fingerprint);
    }
    [Fact]
    public void MachineTranslationsAndAbbreviationsCannotDisplaceAStandardTitle()
    {
        var work = Work with { Aliases = ["MACHINE", "SHORT", "Romanized Name"], TitleEvidence = [new("MACHINE", "official", true, "en"), new("SHORT", "abbreviation", false, "en")] };
        var source = GameVideoSource.From("local", work);
        Assert.Equal("Romanized Name", source.Title); Assert.Equal("日語の原名", source.Aliases[0]);
        Assert.DoesNotContain("MACHINE", source.Aliases); Assert.DoesNotContain("SHORT", source.Aliases);
    }
    [Fact]
    public async Task ProviderEnglishAndOriginalAreSearchedWithinTheSameBoundedBudget()
    {
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(GameVideoTests.Result()));
        using var vm = new GameVideoViewModel(new(provider), new SessionMediaIdentityDecisions()); vm.SetSource(GameVideoSource.From("local", Work));
        await vm.LoadAsync(); Assert.Equal("English Official Title", provider.Queries[0].Title); Assert.Equal("日語の原名", provider.Queries[1].Title);
        Assert.True(provider.Queries.Count <= 3); Assert.Null(vm.Game);
    }
    [Fact]
    public async Task InstalledSteamEditionResolvesARemasterDespiteSharedVndbAndOriginalCatalogIds()
    {
        var work = Work with { References = [new("vndb", "v999999", null), new("steam", "111", null), new("igdb", "1", null)] };
        var source = GameVideoSource.From("local", work, ["222"]);
        var remaster = Game(2, "Remaster", "English Official Title") with { ParentGame = 1, ExternalIds = [new("Steam", "222")] };
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(GameVideoTests.Result(remaster)));
        var store = new SessionMediaIdentityDecisions();
        using var vm = new GameVideoViewModel(new(provider), store); vm.SetSource(source); await vm.LoadAsync();
        Assert.Equal("222", source.SteamId); Assert.Equal("222", source.InstallationSteamId);
        Assert.Null(Assert.Single(provider.Queries).Id); Assert.Equal("222", provider.Queries[0].SteamId);
        Assert.Equal(2, vm.Game!.Id); Assert.NotEmpty(vm.Videos);
        Assert.Empty((await store.ReadAsync(source.Context, default)).Decisions); // Never fabricate a human confirmation.
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ASharedCatalogSteamLinkOrIncompleteInstalledMappingNeverAutomaticallyChoosesAVersion(bool installed)
    {
        var source = GameVideoSource.From("local", Work with { References = [new("steam", "222", null)] }, installed ? ["222"] : []);
        var game = Game(2, "Remaster", "English Official Title") with { ExternalIds = [new("Steam", "222")] };
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(GameVideoTests.Result(game) with { Complete = !installed }));
        using var vm = new GameVideoViewModel(new(provider), new SessionMediaIdentityDecisions()); vm.SetSource(source); await vm.LoadAsync();
        Assert.Null(vm.Game); Assert.Empty(vm.Videos);
    }
    [Theory] [InlineData("Main Game")] [InlineData("Mod")] [InlineData("Bundle")]
    public async Task ConflictingOrUnsuitableInstalledEditionCandidatesStayUnconfirmed(string kind)
    {
        var source = Source with { SteamId = "222", InstallationSteamId = "222" };
        var game = Game(1, kind) with { ExternalIds = [new("Steam", "222")] };
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(kind == "Main Game" ? GameVideoTests.Result(game, game with { Id = 2, Videos = [] }) : GameVideoTests.Result(game)));
        using var vm = new GameVideoViewModel(new(provider), new SessionMediaIdentityDecisions()); vm.SetSource(source); await vm.LoadAsync(); Assert.Null(vm.Game);
    }
    [Fact]
    public async Task AnExcludedInstalledEditionCannotBeAutomaticallyRestored()
    {
        var source = Source with { SteamId = "222", InstallationSteamId = "222" };
        var game = Game(1) with { ExternalIds = [new("Steam", "222")] };
        var provider = new GameVideoTests.Provider((_, _) => Task.FromResult(GameVideoTests.Result(game)));
        var store = new SessionMediaIdentityDecisions();
        using (var first = new GameVideoViewModel(new(provider), store)) { first.SetSource(source); await first.LoadAsync(); Assert.NotNull(first.Game); await first.RejectGameAsync(game); }
        using var restart = new GameVideoViewModel(new(provider), store); restart.SetSource(source); await restart.LoadAsync(); Assert.Null(restart.Game);
    }
    [Fact]
    public void MultipleInstalledVersionsDoNotBorrowOneOfTheirSteamIds()
    {
        var source = GameVideoSource.From("local", Work, ["111", "222"]);
        Assert.Null(source.SteamId); Assert.Null(source.InstallationSteamId);
    }
    [Fact]
    public void OnlyTheSelectedInstallationProvidesAutomaticEditionEvidence()
    {
        var entry = SessionTestRig.Entry() with { Name = "用户自定义的名称", Metadata = Work,
            Installations = [new("original", "C:\\synthetic-original", null, []) { SteamAppId = "111" }, new("remaster", "C:\\synthetic-remaster", null, []) { SteamAppId = "222" }],
            PreferredInstallationId = "remaster" };
        var selected = GameVideoSource.FromEntry(entry, Work);
        Assert.Equal("English Official Title", selected.Title); Assert.Equal("222", selected.InstallationSteamId);
        Assert.Null(GameVideoSource.FromEntry(entry with { PreferredInstallationId = null }, Work).InstallationSteamId);
        Assert.Null(GameVideoSource.FromEntry(entry with { Installations = entry.Installations.SetItem(1, entry.Installations[1] with { SteamAppId = null }) }, Work).InstallationSteamId);
    }
}
