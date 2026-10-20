using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;
public sealed class GameVideoTests
{
    internal static GameVideoSource Source(int? explicitId = null) => new("synthetic-game", "990001", "合成游戏", ["Test game"], ["Synthetic company"], "2004-01-01", explicitId is { } id ? [id] : [], "123");
    internal static IgdbGame Game(int id = 1) => new(id, "合成游戏", [], ["Synthetic company"], ["PC (Microsoft Windows)"], "2004-01-01", "Main Game", null, [], [],
        [new("Steam", "123")], [new(101, id, "abcdefghijk", "Launch Trailer"), new(102, id, "abcdefghijk", "Trailer JP"), new(103, id, "12345678901", "Opening")]);
    internal static IgdbResult Result(params IgdbGame[] games) => new(games, true, DateTimeOffset.UtcNow);
    internal sealed class Provider(Func<GameVideoQuery, CancellationToken, Task<IgdbResult>> action) : IGameVideoProvider
    {
        public List<GameVideoQuery> Queries { get; } = [];
        public Task<IgdbResult> QueryAsync(GameVideoQuery q, CancellationToken c) { Queries.Add(q); return action(q, c); }
    }
    private static Provider Fake(params IgdbGame[] games) => new((_, _) => Task.FromResult(Result(games)));
    private sealed class Cache : IGameVideoCache
    {
        public Dictionary<string, IgdbResult> Values { get; } = [];
        public Task<IgdbResult?> ReadAsync(string k, CancellationToken c) => Task.FromResult(Values.GetValueOrDefault(k));
        public Task WriteAsync(string k, IgdbResult v, CancellationToken c) { c.ThrowIfCancellationRequested(); Values[k] = v; return Task.CompletedTask; }
    }
    [Theory]
    [InlineData("https://youtu.be/abcdefghijk", true)]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk&feature=share", true)]
    [InlineData("https://youtube.com.evil.test/watch?v=abcdefghijk", false)]
    [InlineData("https://youtube.com/watch?v=abcdefghijk&v=12345678901", false)]
    [InlineData("http://youtube.com/watch?v=abcdefghijk", false)]
    [InlineData("https://user@youtube.com/watch?v=abcdefghijk", false)]
    [InlineData("https://youtube.com/watch?v=abcdefghijk%22", false)]
    [InlineData("https://youtube.com/results?search_query=game", false)]
    public void ManualUrlsUseStrictVideoIdentity(string url, bool expected) => Assert.Equal(expected, GameVideos.TryYouTube(url, out _));
    [Theory]
    [InlineData("Launch Trailer", "宣传片")]
    [InlineData("Opening Movie", "OP")]
    [InlineData("Gameplay demo", "实机影像")]
    [InlineData("Movie 01", "宣传影像")]
    public void ClassificationUsesNamesWithoutOfficialClaim(string name, string category) => Assert.StartsWith(category, GameVideos.Classification(name));
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task AUniqueTitleResultNeverAutoConfirmsEvenIfComplete(bool complete)
    {
        var p = new Provider((_, _) => Task.FromResult(Result(Game()) with { Complete = complete }));
        using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions()); vm.SetSource(Source()); await vm.LoadAsync();
        Assert.Null(vm.Game); Assert.Empty(vm.Videos); Assert.Single(vm.Candidates); Assert.Equal(complete, vm.Complete);
    }
    [Fact]
    public async Task ExplicitIdUsesExactQueryAndDeduplicatesVideosWithoutDroppingRelations()
    {
        var p = Fake(Game()); using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source(1)); await vm.LoadAsync(); Assert.Equal(1, vm.Game!.Id); Assert.Equal(1, Assert.Single(p.Queries).Id);
        Assert.Equal(2, vm.Videos.Length); Assert.Equal(2, vm.Videos.Single(x => x.VideoId == "abcdefghijk").Relations.Length);
    }
    [Fact]
    public async Task ManualMappingPersistsRejectsAndWithdrawalDoNotAutomaticallyRebind()
    {
        var decisions = new SessionMediaIdentityDecisions(); var source = Source(1); var p = Fake(Game());
        using var vm = new GameVideoViewModel(new(p), decisions); vm.SetSource(source); await vm.LoadAsync();
        await vm.ConfirmAsync(vm.Game!); await vm.WithdrawAsync(); await vm.LoadAsync();
        Assert.Null(vm.Game); Assert.Empty(vm.Videos);
        using var restart = new GameVideoViewModel(new(p), decisions); restart.SetSource(source); await restart.LoadAsync(); Assert.Null(restart.Game);
        await restart.ConfirmAsync(Assert.Single(restart.Candidates)); Assert.Equal(1, restart.Game!.Id);
        await restart.RejectGameAsync(restart.Game); await restart.LoadAsync(true); Assert.Null(restart.Game);
    }
    [Fact]
    public async Task AnExplicitIdCanBeWithdrawnWithoutFirstCreatingAManualConfirmation()
    {
        var store = new SessionMediaIdentityDecisions(); var p = Fake(Game());
        using var vm = new GameVideoViewModel(new(p), store); vm.SetSource(Source(1)); await vm.LoadAsync();
        await vm.WithdrawAsync(); await vm.LoadAsync();
        Assert.Null(vm.Game); Assert.Empty(vm.Videos);
        Assert.Equal(MediaIdentityDecisionKind.Withdrawn, Assert.Single((await store.ReadAsync(Source(1).Context, default)).Decisions).Kind);
    }
    [Fact]
    public async Task MissingSteamMappingFallsBackToAliasesWithinThreeRequestsAndStillNeedsConfirmation()
    {
        var p = new Provider((q, _) => Task.FromResult(q.Title == "Third alias" ? Result(Game()) : Result()));
        using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source() with { Aliases = ["Third alias", "Unused alias"] }); await vm.LoadAsync();
        Assert.Equal(3, p.Queries.Count); Assert.Equal("123", p.Queries[0].SteamId); Assert.Null(p.Queries[1].SteamId);
        Assert.Equal("Third alias", p.Queries[2].Title); Assert.Null(vm.Game); Assert.Single(vm.Candidates);
    }
    [Fact]
    public async Task ATitleHitDoesNotHideAnOriginalFoundThroughAnExistingAlias()
    {
        var p = new Provider((q, _) => Task.FromResult(q.Id is { } id ? Result(Game(id)) : q.Title == "English alias" ? Result(Game(2)) : Result(Game() with { Kind = "Mod" })));
        using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source() with { SteamId = null, Aliases = ["English alias"] }); await vm.LoadAsync();
        Assert.Equal(2, p.Queries.Count); Assert.Equal(2, vm.Candidates.Length); Assert.Null(vm.Game); Assert.Empty(vm.Videos);
        Assert.True(vm.Complete); await vm.ConfirmAsync(vm.Candidates.Single(x => x.Id == 2)); Assert.Equal(2, vm.Game!.Id);
    }
    [Fact]
    public async Task FailedAliasReadRetainsEarlierCandidatesWithoutClaimingCompleteness()
    {
        var p = new Provider((q, _) => q.Title == "Unavailable alias" ? throw new GameVideoException("timeout") : Task.FromResult(Result(Game())));
        using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source() with { SteamId = null, Aliases = ["Unavailable alias"] }); await vm.LoadAsync();
        Assert.Single(vm.Candidates); Assert.False(vm.Complete); Assert.Null(vm.Game); Assert.False(vm.Busy);
    }
    [Fact]
    public async Task AliasUnionHasABoundedCandidateCountAndCannotAssertCompletenessAfterTruncation()
    {
        var p = new Provider((q, _) => Task.FromResult(Result(Enumerable.Range(q.Title == "Another alias" ? 21 : 1, 20).Select(id => Game(id)).ToArray())));
        using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source() with { SteamId = null, Aliases = ["Another alias"] }); await vm.LoadAsync();
        Assert.Equal(20, vm.Candidates.Length); Assert.False(vm.Complete); Assert.Null(vm.Game); Assert.Empty(vm.Videos);
    }
    [Fact]
    public async Task ConflictingExplicitIdsCanBeResolvedOnlyThroughAnExplicitCandidateChoice()
    {
        var p = Fake(Game()); using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source() with { ExplicitIds = [1, 2] }); await vm.LoadAsync(); Assert.Empty(p.Queries);
        await vm.LoadAsync(true, "Test game"); Assert.Null(vm.Game); await vm.ConfirmAsync(Assert.Single(vm.Candidates));
        Assert.Equal(1, vm.Game!.Id);
    }
    [Fact]
    public async Task AnotherConfirmationDuringAnAutomaticReadCannotBeSilentlyReplaced()
    {
        var held = new TaskCompletionSource<IgdbResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SessionMediaIdentityDecisions(); var source = Source(1);
        using var vm = new GameVideoViewModel(new(new Provider((_, _) => held.Task)), store);
        vm.SetSource(source); var load = vm.LoadAsync();
        await store.ApplyAsync(new(new("IGDB", "Game", "2"), MediaIdentityDecisionKind.Confirmed, source.Context, "Remaster", "1",
            DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User, "synthetic competing user choice"), 0, default);
        held.SetResult(Result(Game())); await load;
        Assert.Null(vm.Game); Assert.Empty(vm.Videos); Assert.Contains("另一项操作", vm.Message);
        Assert.Equal("2", (await store.ReadAsync(source.Context, default)).Confirmed!.Target.Id);
    }
    [Fact]
    public async Task CompetingConfirmationsUseExistingRevisionGuardAndKeepTheCommittedChoice()
    {
        var store = new SessionMediaIdentityDecisions();
        var p = new Provider((q, _) => Task.FromResult(q.Id is { } id ? Result(Game(id)) : Result(Game(), Game(2))));
        using var a = new GameVideoViewModel(new(p), store); using var b = new GameVideoViewModel(new(p), store);
        a.SetSource(Source()); b.SetSource(Source()); await a.LoadAsync(); await b.LoadAsync();
        await a.ConfirmAsync(a.Candidates[0]); await b.ConfirmAsync(b.Candidates[1]);
        Assert.Null(b.Game); Assert.Contains("保存失败", b.Message);
        Assert.Equal("1", (await store.ReadAsync(Source().Context, default)).Confirmed!.Target.Id);
        Assert.Equal(1, a.Game!.Id);
    }
    [Fact]
    public async Task ManualDefaultAndExclusionSurviveRestartCacheClearAndMetadataRename()
    {
        var decisions = new SessionMediaIdentityDecisions(); var cache = new Cache(); var p = Fake(Game());
        using (var vm = new GameVideoViewModel(new(p, cache), decisions)) {
            vm.SetSource(Source()); await vm.LoadAsync(); await vm.ConfirmAsync(Assert.Single(vm.Candidates));
            await vm.ChooseVideoAsync(vm.Videos[0], false); await vm.ChooseVideoAsync(vm.Videos[1], true);
        }
        cache.Values.Clear();
        using var reopened = new GameVideoViewModel(new(p, cache), decisions); reopened.SetSource(Source() with { Title = "修正后的标题", Developers = ["New credit"] }); await reopened.LoadAsync();
        Assert.NotNull(reopened.Game); Assert.Single(reopened.Videos); Assert.Equal("abcdefghijk", reopened.DefaultVideo);
        await reopened.ResetVideoChoicesAsync(); Assert.Equal(2, reopened.Videos.Length); Assert.Null(reopened.DefaultVideo);
    }
    [Fact]
    public async Task EditionChangeRequiresReconfirmationAndDoesNotReuseVideoExclusions()
    {
        var store = new SessionMediaIdentityDecisions();
        var original = Game(); var remaster = Game(2) with { Kind = "Remaster", VersionParent = 1 };
        var p = new Provider((q, _) => Task.FromResult(q.Id is { } id ? Result(id == 1 ? original : remaster) : Result(original, remaster)));
        using var vm = new GameVideoViewModel(new(p), store); vm.SetSource(Source()); await vm.LoadAsync();
        await vm.ConfirmAsync(vm.Candidates[0]); Assert.Equal(1, vm.Game!.Id);
        await vm.ChooseVideoAsync(vm.Videos[0], true); Assert.Single(vm.Videos);
        await vm.LoadAsync(true, "remaster"); Assert.Null(vm.Game); Assert.Empty(vm.Videos);
        await vm.ConfirmAsync(vm.Candidates.Single(x => x.Id == 2));
        Assert.Equal(2, vm.Game!.Id); Assert.Equal(1, vm.Game.VersionParent); Assert.Equal(2, vm.Videos.Length);
        Assert.Null(vm.DefaultVideo);
    }

    [Fact]
    public async Task DeletedRemoteIdNeverFallsBackToSameTitle()
    {
        var store = new SessionMediaIdentityDecisions(); var source = Source();
        var p = new Provider((q, _) => Task.FromResult(q.Id is null ? Result(Game()) : Result(Game(q.Id.Value))));
        using(var vm = new GameVideoViewModel(new(p), store)){ vm.SetSource(source); await vm.LoadAsync(); await vm.ConfirmAsync(vm.Candidates[0]); }
        var gone = new Provider((_, _) => Task.FromResult(Result()));
        using var reopened = new GameVideoViewModel(new(gone), store); reopened.SetSource(source); await reopened.LoadAsync();
        Assert.Null(reopened.Game); Assert.Equal(1, Assert.Single(gone.Queries).Id); Assert.Empty(reopened.Videos);
    }
    [Fact]
    public async Task AProviderEditionIdentityChangeCannotReuseHumanConfirmation()
    {
        var store = new SessionMediaIdentityDecisions();
        using (var vm = new GameVideoViewModel(new(Fake(Game())), store))
        { vm.SetSource(Source()); await vm.LoadAsync(); await vm.ConfirmAsync(vm.Candidates[0]); }
        using var changed = new GameVideoViewModel(new(Fake(Game() with { Kind = "Remaster", VersionParent = 2 })), store);
        changed.SetSource(Source()); await changed.LoadAsync();
        Assert.Null(changed.Game); Assert.Empty(changed.Videos); Assert.Single(changed.Candidates);
        Assert.Equal("Main Game", (await store.ReadAsync(Source().Context, default)).Confirmed!.TargetFormat);
    }
    [Fact]
    public async Task ParentGameIsDistinctFromAnEditionParentAndChangesRequireReconfirmation()
    {
        var store = new SessionMediaIdentityDecisions(); var game = Game(2) with { Kind = "Port", ParentGame = 1 };
        var p = new Provider((_, _) => Task.FromResult(Result(game)));
        using var vm = new GameVideoViewModel(new(p), store); vm.SetSource(Source()); await vm.LoadAsync();
        await vm.ConfirmAsync(Assert.Single(vm.Candidates));
        var saved = (await store.ReadAsync(Source().Context, default)).Confirmed!;
        Assert.Contains("parent_game=1", saved.TargetFormat); Assert.Null(saved.TargetParentId);
        Assert.Equal(1, vm.Game!.ParentGame); Assert.Null(vm.Game.VersionParent);
        game = game with { ParentGame = 3 }; await vm.LoadAsync(true);
        Assert.Null(vm.Game); Assert.Empty(vm.Videos); Assert.Contains("parent_game=1", (await store.ReadAsync(Source().Context, default)).Confirmed!.TargetFormat);
    }
    [Fact]
    public void OlderCachedGameWithoutParentGameStillDeserializesWithoutInventingAnEditionParent()
    {
        var json = JsonSerializer.Serialize(Game()).Replace(",\"ParentGame\":null", "");
        var old = JsonSerializer.Deserialize<IgdbGame>(json)!;
        Assert.Null(old.ParentGame); Assert.Null(old.VersionParent); Assert.True(GameVideos.Valid(old));
        Assert.False(GameVideos.Valid(old with { ParentGame = old.Id }));
        Assert.False(GameVideos.Valid(old with { ParentGame = -1 }));
    }
    [Fact]
    public async Task ReidentifyingLocalGameDoesNotReuseOldConfirmation()
    {
        var store = new SessionMediaIdentityDecisions(); var p = Fake(Game());
        using(var vm = new GameVideoViewModel(new(p), store)){vm.SetSource(Source());await vm.LoadAsync();await vm.ConfirmAsync(vm.Candidates[0]);}
        using var other = new GameVideoViewModel(new(p), store); other.SetSource(Source() with { WorkId = "990002" }); await other.LoadAsync();
        Assert.Null(other.Game); Assert.Empty(other.Videos); Assert.Single(other.Candidates);
    }
    [Fact]
    public async Task LateResponseCannotReviveOldGameOrPopulateNewSource()
    {
        var held = new TaskCompletionSource<IgdbResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var p = new Provider((_, _) => held.Task); using var vm = new GameVideoViewModel(new(p), new SessionMediaIdentityDecisions());
        vm.SetSource(Source(1)); var load = vm.LoadAsync(); vm.SetSource(Source(2) with { GameId = "other-game" });
        held.SetResult(Result(Game())); await load;
        Assert.Null(vm.Game); Assert.Empty(vm.Videos); Assert.Empty(vm.Candidates); Assert.Equal("other-game", vm.Source!.GameId);
    }
    [Fact]
    public async Task CancelledResponseCannotWriteCache()
    {
        var held = new TaskCompletionSource<IgdbResult>(); var cache = new Cache(); var reader = new GameVideoReader(new Provider((_, _) => held.Task), cache);
        using var stop = new CancellationTokenSource(); var result = reader.QueryAsync(new("test"), false, stop.Token); stop.Cancel(); held.SetResult(Result(Game()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await result); Assert.Empty(cache.Values);
    }
    [Fact]
    public async Task CachedMetadataStillHasToMatchTheRequestedExactIdentity()
    {
        var q = new GameVideoQuery("test", Id: 1); var cache = new Cache(); cache.Values[GameVideos.Key(q)] = Result(Game(2));
        var p = Fake(Game()); var result = await new GameVideoReader(p, cache).QueryAsync(q, false, default);
        Assert.Equal(1, Assert.Single(result.Result.Games).Id); Assert.Single(p.Queries); Assert.False(result.Stale);
    }
    [Fact]
    public async Task ManualVideoWorksWithoutServiceAndIsAlwaysMarkedUserProvided()
    {
        var store = new SessionMediaIdentityDecisions(); var p = new Provider((_, _) => throw new GameVideoException("unconfigured"));
        using(var vm = new GameVideoViewModel(new(p),store)){vm.SetSource(Source());await vm.LoadAsync();Assert.Contains("尚未配置",vm.Message);await vm.AddManualAsync("https://youtu.be/abcdefghijk");Assert.True(Assert.Single(vm.Videos).UserProvided);}
        using var reopened = new GameVideoViewModel(new(p),store);reopened.SetSource(Source());await reopened.LoadAsync();var video=Assert.Single(reopened.Videos);Assert.Empty(video.Relations);Assert.True(video.UserProvided);
        await reopened.RemoveManualAsync(video);Assert.Empty(reopened.Videos);
    }
    [Theory] [InlineData("authentication")] [InlineData("rate_limited")] [InlineData("timeout")]
    public async Task NetworkFailureUsesValidCacheWithoutWritingFailureAsNoVideos(string code)
    {
        var cache=new Cache();var q=new GameVideoQuery("test");var prior=Result(Game()) with{FetchedAtUtc=DateTimeOffset.UtcNow.AddDays(-1)};cache.Values[GameVideos.Key(q)]=prior;
        var reader=new GameVideoReader(new Provider((_,_)=>throw new GameVideoException(code)),cache);var result=await reader.QueryAsync(q,true,default);
        Assert.True(result.Stale);Assert.Equal(prior,result.Result);Assert.Equal(prior,cache.Values[GameVideos.Key(q)]);
    }
    [Fact]
    public void WrongGameVideoRelationshipCannotBecomeValidMetadata() => Assert.False(GameVideos.Valid(Game() with{Videos=[new(1,999,"abcdefghijk","Trailer")]}));
    [Fact]
    public async Task ProfileRestartAndEvictionPreserveUserDecisionsAndLibraryBytes()
    {
        var root=Path.Combine(Path.GetTempPath(),"the-order-igdb-test-"+Guid.NewGuid().ToString("N"));
        try {
            byte[] before;
            using(var profile=NativeProfileStore.Open(new(NativeProfileKind.Test,root),new NativeLibraryCodec())){
                before=File.ReadAllBytes(Path.Combine(root,"library.json"));var decisions=new IsolatedMediaIdentityDecisions(profile.Configuration());var source=Source();
                await decisions.ApplyAsync(new(new("IGDB","Game","1"),MediaIdentityDecisionKind.Confirmed,source.Context,"Main Game",null,DateTimeOffset.UtcNow,MediaIdentityDecisionOrigin.User,"synthetic test"),0,default);
                var cache=new IsolatedGameVideoCache(profile.Configuration());
                for(int i=1;i<=66;i++)await cache.WriteAsync(GameVideos.Key(new("test",Id:i)),Result(Game(i)),default);
                Assert.True(Directory.GetFiles(Path.Combine(root,IsolatedGameVideoCache.DirectoryName),"*.json").Length<=64);
                var cacheRoot=Path.GetFullPath(Path.Combine(root,IsolatedGameVideoCache.DirectoryName));Assert.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,cacheRoot);Directory.Delete(cacheRoot,true);
            }
            using(var profile=NativeProfileStore.Open(new(NativeProfileKind.Test,root),new NativeLibraryCodec())){
                Assert.Equal("1",(await new IsolatedMediaIdentityDecisions(profile.Configuration()).ReadAsync(Source().Context,default)).Confirmed!.Target.Id);
                Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"library.json")));
            }
        }finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("the-order-igdb-test-"))Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData(PersistenceStage.DuringTempWrite, false)]
    [InlineData(PersistenceStage.BeforeReplace, false)]
    [InlineData(PersistenceStage.AfterReplace, true)]
    public async Task InterruptedGameMappingWriteRecoversExactlyTheCommittedIdentity(PersistenceStage stage, bool committed)
    {
        var root = Path.Combine(Path.GetTempPath(), "the-order-igdb-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] library;
            using (var profile = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec()))
            {
                library = File.ReadAllBytes(Path.Combine(root, "library.json"));
                var enabled = false; var store = new IsolatedMediaIdentityDecisions(profile.Configuration(), current =>
                { if (enabled && current == stage) throw new IOException("synthetic interrupted identity write"); return Task.CompletedTask; });
                var context = Source().Context;
                await store.ReadAsync(context, default);
                var original = new MediaIdentityDecision(new("IGDB", "Game", "1"), MediaIdentityDecisionKind.Confirmed, context,
                    "Main Game", null, DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User, "synthetic original");
                await store.ApplyAsync(original, 0, default); enabled = true;
                var error = await Record.ExceptionAsync(() => store.ApplyAsync(original with { Target = new("IGDB", "Game", "2"), TargetFormat = "Remaster", TargetParentId = "1" }, 1, default));
                if (committed) Assert.Null(error); else Assert.IsType<IOException>(error);
            }
            using (var restarted = NativeProfileStore.Open(new(NativeProfileKind.Test, root), new NativeLibraryCodec()))
            {
                var value = await new IsolatedMediaIdentityDecisions(restarted.Configuration()).ReadAsync(Source().Context, default);
                Assert.Equal(committed ? "2" : "1", value.Confirmed!.Target.Id);
                Assert.Equal(committed ? 2 : 1, value.Revision);
                Assert.Equal(library, File.ReadAllBytes(Path.Combine(root, "library.json")));
                Assert.Empty(Directory.GetFiles(Path.Combine(root, IsolatedMediaIdentityDecisions.DirectoryName), "*.tmp"));
            }
        }
        finally { if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("the-order-igdb-test-")) Directory.Delete(root, true); }
    }
    private sealed class Http(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken c)=>Task.FromResult(reply(q));}
    [Theory] [InlineData(503,"unconfigured")] [InlineData(503,"authentication")] [InlineData(429,"rate_limited")]
    public async Task ServiceErrorsRemainDistinct(int status,string code)
    {
        using var client=new HttpClient(new Http(_=>new((HttpStatusCode)status){Content=JsonContent.Create(new{error=code})}));
        var p=new IgdbGameVideoProvider(client,new("https://fixture.test/"));
        var error=await Assert.ThrowsAsync<GameVideoException>(()=>p.QueryAsync(new("test"),default));Assert.Equal(code,error.Code);
    }
    [Theory] [InlineData("[]")] [InlineData("{\"error\":123}")]
    public async Task MalformedErrorBodiesCannotEscapeProviderFailureIsolation(string body)
    {
        using var client = new HttpClient(new Http(_ => new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(body) }));
        var error = await Assert.ThrowsAsync<GameVideoException>(() => new IgdbGameVideoProvider(client, new("https://fixture.test/")).QueryAsync(new("test"), default));
        Assert.Equal("upstream", error.Code);
    }
    [Fact]
    public async Task HttpAdapterSendsNoAppSecretsAndRejectsWrongGame()
    {
        using var client=new HttpClient(new Http(q=>{Assert.Equal("/v1/game-videos/query",q.RequestUri!.AbsolutePath);Assert.Null(q.Headers.Authorization);Assert.False(q.Headers.Contains("Client-ID"));return new(HttpStatusCode.OK){Content=JsonContent.Create(Result(Game(2)))};}));
        var p=new IgdbGameVideoProvider(client,new("https://fixture.test/"));var error=await Assert.ThrowsAsync<GameVideoException>(()=>new GameVideoReader(p).QueryAsync(new("test",Id:1),false,default));Assert.Equal("invalid_response",error.Code);
    }
    [Fact]
    public async Task NegativeVideoCacheExpiresSoonerWithoutLosingUserIdentity()
    {
        var now=DateTimeOffset.UtcNow; var q=new GameVideoQuery("test",Id:1);var cache=new Cache();
        cache.Values[GameVideos.Key(q)] = new([Game() with {Videos=[]}],true,now.AddMinutes(-31));
        var provider=Fake(Game());
        var result=await new GameVideoReader(provider,cache,()=>now).QueryAsync(q,false,default);
        Assert.Single(provider.Queries); Assert.NotEmpty(Assert.Single(result.Result.Games).Videos);
        Assert.False(result.Stale);
    }
    [Fact]
    public async Task SuccessfulVideoCacheStillAvoidsRedundantRequests()
    {
        var now=DateTimeOffset.UtcNow;var q=new GameVideoQuery("test",Id:1);var cache=new Cache();
        cache.Values[GameVideos.Key(q)] = new([Game()],true,now.AddHours(-2));var provider=Fake(Game());
        var result=await new GameVideoReader(provider,cache,()=>now).QueryAsync(q,false,default);
        Assert.Empty(provider.Queries);Assert.NotEmpty(Assert.Single(result.Result.Games).Videos);
    }
    [Fact]
    public async Task AnonymousVideoTitleRetainsRelationAndNeutralClassification()
    {
        var video=new IgdbVideo(10,1,"abcdefghijk","宣传影像"){OriginalName=null};
        var provider=Fake(Game() with {Videos=[video]});using var model=new GameVideoViewModel(new(provider),new SessionMediaIdentityDecisions());
        model.SetSource(Source(1));await model.LoadAsync();
        var shown=Assert.Single(model.Videos);Assert.Equal("宣传影像",shown.Classification);
        Assert.Equal(10,Assert.Single(shown.Relations).Id);Assert.False(shown.UserProvided);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task UpgradeRetainsPositiveOfflineCacheButNeverReusesLegacyEmptyVideos(bool positive)
    {
        var now=DateTimeOffset.UtcNow;var q=new GameVideoQuery("test",Id:1);var cache=new Cache();
        cache.Values[GameVideos.Key(q).Replace("igdb-v2:","igdb-v1:")] = new([Game() with {Videos=positive?Game().Videos:[]}],true,now.AddMinutes(-1));
        var provider=Fake(Game());var result=await new GameVideoReader(provider,cache,()=>now).QueryAsync(q,false,default);
        Assert.Equal(positive?0:1,provider.Queries.Count);Assert.NotEmpty(Assert.Single(result.Result.Games).Videos);
    }
}
