using System.Net;
using System.Text;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class ManualMediaExperienceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    private static AniListSource Book() => new("123", AniListMediaKind.Manga, "合成小说", "Synthetic book", "2006-12-29", "小说", null, []) { BookLevel = BookEntityLevel.Series };
    private static AniListMedia Media(string format = "NOVEL") => new(456, null, AniListMediaKind.Manga, "Synthetic book", "合成小说", "", format, "FINISHED", null, "2011-01-04", null, null, 74, 14, []);
    private static AniListDetails Details(AniListMedia? media = null) => new(media ?? Media(), "Synthetic original description", [], [], [], [], [], [], 1, false, false, Now);
    private static AniListBridgeReader Bridge(AniListSource? input = null)
    {
        var source = input ?? Book();
        return new() { Load = (_, _) => Task.FromResult(new BangumiSubjectDetailsResult(new(source.BangumiId,
            source.Kind == AniListMediaKind.Anime ? BangumiRelationKind.Anime : BangumiRelationKind.Book, source.Title, source.OriginalTitle, null,
            "原条目的中文简介", source.Date, source.Platform, source.Episodes, null, null, Now)
            { BookFacts = source.Kind == AniListMediaKind.Manga ? new(source.BookLevel == BookEntityLevel.Series, null, null,
                source.BookLevel == BookEntityLevel.Publication ? ["9780306406157"] : [], [], [], [], [], [], source.BookLevel == BookEntityLevel.Volume ? "1" : null) : null }, false, false, null)) };
    }
    private static BangumiRelationItem Item(AniListSource? source = null) => new((source ?? Book()).BangumiId,
        source?.Kind == AniListMediaKind.Anime ? BangumiRelationKind.Anime : BangumiRelationKind.Book, "合成源条目", "Synthetic source", "相关作品", null);
    private static AniListFakeProvider Provider(AniListMedia? media = null) => new() { Results = [media ?? Media()], Fetch = (_, _, _) => Task.FromResult(Details(media)) };

    [Theory]
    [InlineData("2006-12-29", "2011-01-04")]
    [InlineData("2011-01-04", "2011-01-05")]
    public async Task DeliberateCompatibleChoiceSurvivesDateAndTitleDifferences(string sourceDate, string targetDate)
    {
        var source = new AniListSource("123", AniListMediaKind.Anime, "不同语言标题", "Another spelling", sourceDate, "TV", 12, []);
        var media = Media() with { Kind = AniListMediaKind.Anime, Format = "TV", Episodes = 12, StartDate = targetDate };
        var choices = new SessionMediaIdentityDecisions(); var service = new AniListService(Provider(media), identityDecisions: choices);
        Assert.False(AniListMatching.StrongEvidence(source, media));
        Assert.True(AniListMatching.ManualCompatible(source, media));
        using var vm = new AniListDetailsViewModel(Bridge(source), service);
        await vm.OpenAsync(Item(source)); await vm.PreviewAsync(media.Id); Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync(); Assert.True(vm.HasTrustedBridge); Assert.Equal(AniListDetailState.Ready, vm.State);
        using var restarted = new AniListDetailsViewModel(Bridge(source), new AniListService(Provider(media), identityDecisions: choices));
        await restarted.OpenAsync(Item(source)); Assert.True(restarted.HasTrustedBridge);
        await restarted.RefreshAsync(); Assert.True(restarted.HasTrustedBridge);
        Assert.True((await service.MatchAsync(source, null, false, default)).Binding!.IsUserDecision);
    }

    [Theory]
    [InlineData("NOVEL", "MANGA", BookEntityLevel.Series)]
    [InlineData("MANGA", "MANGA", BookEntityLevel.Volume)]
    [InlineData("NOVEL", "NOVEL", BookEntityLevel.Publication)]
    public async Task DifferentMediaOrPublicationLevelCreatesDirectedRelationshipWithoutIdentityMerge(string from, string to, BookEntityLevel level)
    {
        var source = Book() with { Platform = from == "NOVEL" ? "小说" : "漫画", BookLevel = level };
        var choices = new SessionMediaIdentityDecisions(); var service = new AniListService(Provider(Media(to)), identityDecisions: choices);
        using var vm = new AniListDetailsViewModel(Bridge(source), service);
        await vm.OpenAsync(Item()); if (vm.Candidates.Length == 0) await vm.SearchAsync("Synthetic book");
        await vm.PreviewAsync(456); Assert.False(vm.CanConfirm); Assert.True(vm.CanAddRelated);
        await vm.AddRelatedAsync("用户说明：不同版本的相关资料");
        Assert.Equal(AniListDetailState.Ready, vm.State); Assert.True(vm.RelatedSelection); Assert.False(vm.HasTrustedBridge);
        var context = MediaIdentityContext.From(source);
        Assert.Null((await choices.ReadAsync(context, default)).Confirmed);
        var link = Assert.Single((await choices.ReadLinksAsync(context, default)).Items);
        Assert.Equal(context.Anchor, link.Source.Anchor); Assert.Equal(MediaUserLinkState.Related, link.State); Assert.Equal("RELATED", link.Relation);
        Assert.Equal("Manga", link.Target.Resource); Assert.Contains("用户说明", link.Note);
        using var restarted = new AniListDetailsViewModel(Bridge(source), service); await restarted.OpenAsync(Item());
        await restarted.OpenUserLinkAsync(Assert.Single(restarted.UserLinks!.Items)); Assert.True(restarted.RelatedSelection); Assert.False(restarted.HasTrustedBridge);
        await restarted.RemoveUserLinkAsync(link.Target); Assert.Empty(restarted.UserLinks!.Items);
        Assert.False(restarted.HasTrustedBridge); Assert.Equal(AniListDetailState.Preview, restarted.State);
    }

    [Fact]
    public async Task FailedCandidateReadCanBeBookmarkedButRecoveryNeverSilentlyConfirmsIt()
    {
        var provider = Provider(); var choices = new SessionMediaIdentityDecisions();
        var service = new AniListService(provider, identityDecisions: choices);
        using var vm = new AniListDetailsViewModel(Bridge(), service); await vm.OpenAsync(Item());
        provider.Fetch = (_, _, _) => throw new AniListException(AniListFailure.Timeout, "合成超时");
        await vm.PreviewAsync(456); Assert.True(vm.CanSavePending); Assert.Null(vm.Displayed);
        await vm.SavePendingAsync(); var saved = Assert.Single(vm.UserLinks!.Items); Assert.Equal(MediaUserLinkState.Pending, saved.State);
        Assert.Null((await choices.ReadAsync(MediaIdentityContext.From(Book()), default)).Confirmed);
        provider.Fetch = (_, _, _) => Task.FromResult(Details());
        await vm.OpenUserLinkAsync(saved); Assert.Equal(AniListDetailState.Preview, vm.State); Assert.False(vm.HasTrustedBridge);
        Assert.Null((await choices.ReadAsync(MediaIdentityContext.From(Book()), default)).Confirmed);
        await vm.ConfirmAsync(); Assert.True(vm.HasTrustedBridge);
    }

    [Fact]
    public async Task RejectedTargetCanBeRestoredWithoutDeletingRelatedWorks()
    {
        var decisions = new SessionMediaIdentityDecisions(); var service = new AniListService(Provider(), identityDecisions: decisions);
        await service.RejectAsync(Book(), Media(), default);
        var context = MediaIdentityContext.From(Book()); var link = Link(context, new("AniList", "Manga", "789"));
        await service.ChangeUserLinkAsync(context, link, link.Target, 0, default);
        await service.RestoreAutomaticAsync(context, default);
        Assert.False((await decisions.ReadAsync(context, default)).Rejects(MediaIdentityDecisionRules.Target(Media())));
        Assert.Null((await service.MatchAsync(Book(), null, false, default)).Binding); // books still never auto-bind from dates
        Assert.Single((await service.ReadUserLinksAsync(context, default)).Items);
    }

    [Theory]
    [InlineData("https://www.kungal.com/galgame/86", "86")]
    [InlineData("https://kungal.com/galgame/86/", "86")]
    [InlineData("https://api.nextmoe.dev/v2/catalog/works/86", "86")]
    [InlineData(" 86 ", "86")]
    public void SupportedWorkLinksResolveToIdsWithoutBecomingRequestDestinations(string input, string expected)
    { Assert.True(CatalogWorkReference.TryParse(input, out var id)); Assert.Equal(expected, id); }
    [Theory]
    [InlineData("https://evil.test/galgame/86")]
    [InlineData("http://kungal.com/galgame/86")]
    [InlineData("https://kungal.com.evil.test/galgame/86")]
    [InlineData("https://kungal.com@evil.test/galgame/86")]
    [InlineData("https://www.kungal.com/galgame/86?token=secret")]
    [InlineData("https://www.kungal.com/galgame/86#x")]
    [InlineData("https://www.kungal.com/galgame/86/../87")]
    [InlineData("https://www.kungal.com/galgame/%38%36")]
    [InlineData("https://www.kungal.com/galgame/86/extra")]
    [InlineData("https://www.kungal.com:8443/galgame/86")]
    [InlineData("0")]
    [InlineData("00086")]
    public void UnsafeOrUnsupportedWorkLinksAreRejected(string input) => Assert.False(CatalogWorkReference.TryParse(input, out _));

    [Fact]
    public async Task InvalidAniListUrlDoesNotSendAnArbitrarySearchOrLeaveASavableTarget()
    {
        var provider = Provider(); using var vm = new AniListDetailsViewModel(Bridge(), new AniListService(provider));
        await vm.OpenAsync(Item()); var calls = provider.Searches;
        await vm.SearchAsync("https://anilist.co.evil.test/manga/456"); Assert.Equal(calls, provider.Searches);
        Assert.False(vm.CanSavePending); Assert.Contains("请粘贴", vm.Message);
    }
    [Fact]
    public async Task ManualGameUrlUsesCanonicalCatalogIdAndPreservesPersonalNotes()
    {
        await using var lab = new RecognitionReviewTests.Lab(); await lab.Init();
        await lab.Library.UpdateAsync("a", new() { Notes = "用户后来修改的笔记", Favorite = true });
        string? requested = null;
        lab.Catalog.Detail = (id, _) => { requested = id; return Task.FromResult(RecognitionBoundaryTests.Work(id)); };
        var result = await lab.Reviews.ManualLinkAsync("a", "https://www.kungal.com/galgame/200", default);
        Assert.True(result.IsCommitted); Assert.Equal("200", requested);
        var entry = Assert.Single(lab.Library.Snapshot.Entries); Assert.Equal("200", entry.Metadata!.Id);
        Assert.Equal("用户后来修改的笔记", entry.Notes); Assert.True(entry.Favorite);
    }

    [Fact]
    public async Task LateCandidateReadCannotSaveOrReplaceNewPageState()
    {
        var delayed = new TaskCompletionSource<AniListDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Provider(); provider.Fetch = (_, _, _) => delayed.Task;
        var choices = new SessionMediaIdentityDecisions(); using var vm = new AniListDetailsViewModel(Bridge(), new AniListService(provider, identityDecisions: choices));
        await vm.OpenAsync(Item()); var pending = vm.PreviewAsync(456); vm.Deactivate();
        delayed.SetResult(Details()); await pending;
        Assert.Null(vm.Displayed); Assert.Null((await choices.ReadAsync(MediaIdentityContext.From(Book()), default)).Confirmed);
        Assert.Empty((await choices.ReadLinksAsync(MediaIdentityContext.From(Book()), default)).Items);
    }

    [Theory]
    [InlineData(BangumiAuthorizationFailure.Timeout)] [InlineData(BangumiAuthorizationFailure.Offline)]
    [InlineData(BangumiAuthorizationFailure.Forbidden)] [InlineData(BangumiAuthorizationFailure.RateLimited)] [InlineData(BangumiAuthorizationFailure.Server)]
    public async Task TransientOrPermissionFailurePreservesAuthorizationAndAllowsRetry(BangumiAuthorizationFailure failure)
    {
        var store = new Credential(); var accounts = new Accounts(); var access = new BangumiAccessContext();
        using var vm = new BangumiSettingsViewModel(new(store, accounts, access)); await vm.InitializeAsync(); var original = access.Capture();
        accounts.Failure = failure; await vm.TestAsync(); Assert.False(vm.Busy); Assert.True(vm.CanTest); Assert.True(vm.HasAuthorization);
        Assert.Same(original, access.Capture()); Assert.False(access.Rejected); Assert.DoesNotContain("已失效", vm.Status);
        accounts.Failure = null; await vm.TestAsync(); Assert.Equal("授权有效。", vm.Message); Assert.Equal(0, store.Clears);
    }
    private sealed class Credential : IBangumiCredentialStore
    {
        public int Clears;
        public Task<string?> ReadAsync(CancellationToken cancellation) => Task.FromResult<string?>("Synthetic_Bangumi_Only_123456");
        public Task SaveAsync(string token, CancellationToken cancellation) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellation) { Clears++; return Task.CompletedTask; }
    }
    private sealed class Accounts : IBangumiAccountProvider
    {
        public BangumiAuthorizationFailure? Failure;
        public Task<BangumiAccount> ValidateAsync(string token, CancellationToken cancellation) => Failure is { } failure
            ? Task.FromException<BangumiAccount>(new BangumiAuthorizationException(failure)) : Task.FromResult(new BangumiAccount("1", "synthetic", "合成用户"));
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Slow = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (Slow) await Task.Delay(Timeout.Infinite, cancellation);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }
    [Fact]
    public async Task CatalogTimeoutDoesNotInvalidateNextMoeLoginAndTheSameSessionCanRetry()
    {
        var identity = new NextMoeAuthTests.Fixture(); await identity.SignIn(); var before = identity.Storage.Record;
        using var handler = new Handler(); using var client = new HttpClient(handler);
        var transport = new CatalogTransport(client, identity.Auth, new("https://api.nextmoe.dev/v2/"), timeout: TimeSpan.FromMilliseconds(30));
        var error = await Assert.ThrowsAsync<OnlineException>(() => transport.Operation(b => transport.GetAsync("catalog/works", new Dictionary<string, string>(), b), default));
        Assert.Equal(OnlineFailure.Timeout, error.Failure); Assert.Same(before, identity.Storage.Record); Assert.False(identity.Auth.CurrentStatus!.NeedsLogin);
        handler.Slow = false;
        using var result = await transport.Operation(b => transport.GetAsync("catalog/works", new Dictionary<string, string>(), b), default);
        Assert.False(identity.Auth.CurrentStatus!.NeedsLogin); Assert.True(identity.Auth.CatalogAvailable);
    }
    internal static MediaUserLink Link(MediaIdentityContext source, MediaIdentityReference? target = null) => new(source,
        target ?? new("AniList", "Manga", "456"), MediaUserLinkState.Related, "合成相关作品", "MANGA", "RELATED", "用户明确添加", Now);
}

[Collection("Native profile resource")]
public sealed class MediaUserLinkPersistenceTests
{
    private static MediaIdentityContext Context() => MediaIdentityContext.From(new AniListSource("123", AniListMediaKind.Manga, "合成小说", "Synthetic", null, "小说", null, []) { BookLevel = BookEntityLevel.Series });
    private sealed class Profile : IDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "the-order-user-links-" + Guid.NewGuid().ToString("N"));
        public NativeProfileStore Store;
        public Profile() => Store = NativeProfileStore.Open(new(NativeProfileKind.Test, Root), new NativeLibraryCodec());
        public void Restart() { Store.Dispose(); Store = NativeProfileStore.Open(new(NativeProfileKind.Test, Root), new NativeLibraryCodec()); }
        public void Dispose()
        {
            Store.Dispose(); var absolute = Path.GetFullPath(Root);
            if (!absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("the-order-user-links-", StringComparison.Ordinal)) throw new InvalidOperationException();
            Directory.Delete(absolute, true);
        }
    }
    [Fact]
    public async Task UserLinksSurviveCacheClearingRestartAndTitleEditsWithoutChangingExistingIdentityEnvelope()
    {
        using var profile = new Profile(); var context = Context();
        var library = File.ReadAllBytes(Path.Combine(profile.Root, "library.json"));
        var store = new IsolatedMediaIdentityDecisions(profile.Store.Configuration());
        await store.ApplyAsync(new(new("AniList", "Manga", "789"), MediaIdentityDecisionKind.Confirmed, context, "NOVEL", null, DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User, "既有确认"), 0, default);
        var identityPath = Path.Combine(profile.Root, IsolatedMediaIdentityDecisions.DirectoryName, context.Anchor.Key + ".json"); var identity = File.ReadAllBytes(identityPath);
        var link = ManualMediaExperienceTests.Link(context); await store.ChangeLinkAsync(context, link, link.Target, 0, default);
        var cache = new IsolatedAniListCache(profile.Store.Configuration()); await cache.WriteAsync(new("media:1:456:1", DateTimeOffset.UtcNow, Details: AniListTestData.Details() with { Media = AniListTestData.Media() with { Id = 456, Kind = AniListMediaKind.Manga, Format = "NOVEL" } }), default);
        var remote = Path.Combine(profile.Root, IsolatedAniListCache.DirectoryName); if (Directory.Exists(remote)) Directory.Delete(remote, true);
        Assert.Equal(identity, File.ReadAllBytes(identityPath)); profile.Restart(); store = new(profile.Store.Configuration());
        var changed = context with { Fingerprint = new('B', 64) };
        Assert.Equal(link, Assert.Single((await store.ReadLinksAsync(changed, default)).Items));
        Assert.Equal("789", (await store.ReadAsync(changed, default)).Confirmed!.Target.Id);
        Assert.Equal(library, File.ReadAllBytes(Path.Combine(profile.Root, "library.json")));
    }
    [Fact]
    public async Task StaleWriteAndCancelledWriteDoNotReplaceNewerUserLink()
    {
        using var profile = new Profile(); var source = Context(); var store = new IsolatedMediaIdentityDecisions(profile.Store.Configuration());
        var link = ManualMediaExperienceTests.Link(source); await store.ChangeLinkAsync(source, link, link.Target, 0, default);
        await Assert.ThrowsAsync<IOException>(() => store.ChangeLinkAsync(source, link with { Note = "旧响应" }, link.Target, 0, default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ChangeLinkAsync(source, null, link.Target, 1, cancel.Token));
        Assert.Equal(link, Assert.Single((await store.ReadLinksAsync(source, default)).Items));
    }
    [Fact]
    public async Task InterruptedAtomicSavePreservesPriorLinkAfterRestart()
    {
        using var profile = new Profile(); var source = Context(); var link = ManualMediaExperienceTests.Link(source);
        var store = new IsolatedMediaIdentityDecisions(profile.Store.Configuration()); await store.ChangeLinkAsync(source, link, link.Target, 0, default);
        var broken = new IsolatedMediaIdentityDecisions(profile.Store.Configuration(), stage => stage == PersistenceStage.BeforeReplace ? throw new IOException("合成中断") : Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => broken.ChangeLinkAsync(source, link with { Note = "不会提交" }, link.Target, 1, default));
        profile.Restart(); Assert.Equal(link, Assert.Single((await new IsolatedMediaIdentityDecisions(profile.Store.Configuration()).ReadLinksAsync(source, default)).Items));
    }
    [Fact]
    public async Task ForeignProviderCannotBeSmuggledIntoTheAniListNavigationNamespace()
    {
        using var profile = new Profile(); var source = Context(); var link = ManualMediaExperienceTests.Link(source, new("Bangumi", "Subject", "456"));
        var store = new IsolatedMediaIdentityDecisions(profile.Store.Configuration());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ChangeLinkAsync(source, link, link.Target, 0, default));
        Assert.Empty((await store.ReadLinksAsync(source, default)).Items);
    }
    [Fact]
    public async Task PendingGameChoiceSurvivesRestartIsEntryScopedAndCannotBecomeIdentityProof()
    {
        using var profile = new Profile(); var before = File.ReadAllBytes(Path.Combine(profile.Root, "library.json"));
        var source = MediaIdentityContext.ForLibraryEntry("local-entry-one");
        var target = new MediaIdentityReference("NextMoe", "Work", "200");
        var link = new MediaUserLink(source, target, MediaUserLinkState.Pending, "待核验作品", "GAME", "RELATED", "", DateTimeOffset.UtcNow);
        var store = new IsolatedMediaIdentityDecisions(profile.Store.Configuration()); await store.ChangeLinkAsync(source, link, target, 0, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ApplyAsync(new(target, MediaIdentityDecisionKind.Confirmed,
            source, "GAME", null, DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User, "不能用待核验代替本地库关联"), 0, default));
        profile.Restart(); store = new(profile.Store.Configuration());
        Assert.Equal(link, Assert.Single((await store.ReadLinksAsync(source, default)).Items));
        Assert.Empty((await store.ReadLinksAsync(MediaIdentityContext.ForLibraryEntry("local-entry-two"), default)).Items);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(profile.Root, "library.json")));
        await store.ChangeLinkAsync(source, null, target, 1, default); Assert.Empty((await store.ReadLinksAsync(source, default)).Items);
    }
}
