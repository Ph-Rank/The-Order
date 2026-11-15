using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.WinUI.Views;

public sealed partial class CatalogBrowserView
{
    private IBangumiRelationsReader? relationsReader;
    private IRelatedSemanticReader? relationSemantics;
    private IRelatedGameIdentityReader? relationIdentities;
    private BangumiRelatedView? relatedWorks;
    private RelatedMediaDetailView? mediaDetail;
    private WorkMetadata? currentWork;
    private CatalogPresentationEntry? currentPresentation;
    private BangumiRelationIdentity? relatedIdentity;
    private readonly string browseLifetime = Guid.NewGuid().ToString("N");
    private readonly Grid mediaHost = new() { Visibility = Visibility.Collapsed };
    private bool mediaActive, catalogLoading, restoreQueued;
    private NavigationEntry? restoreTarget;
    private double mediaReturnOffset;
    private Func<string> bangumiAccessScope = () => "anonymous";

    public void ConfigureRelatedWorks(IBangumiRelationsReader reader, IBangumiSubjectDetailsReader subjects,
        IRelatedSemanticReader semantics, IRelatedGameIdentityReader? identities, Func<string>? accessScope = null)
    {
        bangumiAccessScope = accessScope ?? (() => "anonymous");
        relationsReader = reader; relationSemantics = semantics; relationIdentities = identities;
        mediaDetail?.Dispose(); mediaDetail = new();
        mediaDetail.Configure(subjects, Images, registry, OpenBangumiSourceAsync);
        mediaHost.Children.Clear(); mediaHost.Children.Add(mediaDetail);
        if (!Children.Contains(mediaHost)) Children.Add(mediaHost);
    }
    public void ConfigureMusic(IMusicBrainzReader music, IMusicBrainzEntityReader? entities = null) => mediaDetail?.ConfigureMusic(music, entities);
    public void ConfigureAniList(IAniListReader reader, IWatchLinkPreferencesStore? watchPreferences = null, IBangumiWatchLinksReader? watchReader = null) => mediaDetail?.ConfigureAniList(reader, watchPreferences, watchReader);
    public void ConfigureEpisodes(IBangumiEpisodesReader reader) => mediaDetail?.ConfigureEpisodes(reader);
    public void ConfigureBooks(IBookBibliographyReader books, IBangumiBookRelationsReader relations) => mediaDetail?.ConfigureBooks(books, relations);

    private void ShowRelated(WorkMetadata work, long auth)
    {
        if (relationsReader is null) return;
        var version = generation;
        var view = new BangumiRelatedView(); relatedWorks = view;
        view.Configure(relationsReader, Images, registry, OpenBangumiSourceAsync,
            identity => generation == version && stop?.IsCancellationRequested == false && authGeneration() == auth
                && !mediaActive && current?.Kind == CatalogEntityKind.Work && current.Id == currentWork?.Id && relatedIdentity == identity);
        if (relationSemantics is not null) view.ConfigureSemantics(relationSemantics);
        view.ConfigureGameIdentities(relationIdentities);
        view.OpenMedia = OpenMediaAsync;
        view.OpenCatalog = card => card.Catalog is not { } entity || currentWork is not { } origin ? Task.CompletedTask
            : OpenAsync(entity, incoming: new(origin.Id, origin.Title, card.Label, card.RawRelation, card.Evidence, card.Url));
        view.PresentationChanged = QueueRestore;
        body.Children.Add(view);
        UpdateRelated(work, auth);
    }

    private void UpdateRelated(WorkMetadata work, long auth)
    {
        if (relatedWorks is null || authGeneration() != auth || mediaActive) return;
        currentWork = work;
        var lifecycle = browseLifetime + ":" + generation;
        BangumiRelationIdentityResolver.TryCreateCatalog(work, lifecycle, out relatedIdentity, out var reason);
        if (relatedIdentity is not null) relatedIdentity = relatedIdentity with { AccessScope = bangumiAccessScope() };
        relatedWorks.SetCatalog(work);
        relatedWorks.SetContext(relatedIdentity, reason);
    }

    private async Task OpenMediaAsync(RelatedWorkCard card)
    {
        if (mediaDetail is null || card.Bangumi is not { } item || currentWork is not { } work || stop?.IsCancellationRequested != false) return;
        restoreTarget = null; mediaReturnOffset = scroll.VerticalOffset; mediaActive = true;
        relatedWorks?.SuspendNavigation(); hero?.ReleaseBeforeLeave(); SuspendGameVideos();
        catalogPage.Visibility = Visibility.Collapsed;
        InteractionMotion.StopPage(catalogPage);
        InteractionMotion.EnterPage(mediaHost);
        mediaDetail.RelationLabel = card.Label; mediaDetail.RelationEvidence = card.Evidence;
        await mediaDetail.OpenAsync(item, work.Title, stop.Token);
    }

    private void CloseMedia()
    {
        if (!mediaActive) return;
        mediaDetail?.Deactivate(); mediaActive = false; mediaHost.Visibility = Visibility.Collapsed;
        InteractionMotion.StopPage(mediaHost);
        catalogPage.Visibility = Visibility.Visible;
        if (currentPresentation is not null) title.Visibility = Visibility.Collapsed;
        relatedWorks?.ResumeNavigation();
        if (current is not null) restoreTarget = new(current, incoming, mediaReturnOffset, relatedWorks?.CaptureBrowsing());
        QueueRestore();
        if (currentPresentation is { } cached) hero?.SetPresentation(cached);
        InteractionMotion.EnterPage(catalogPage);
    }

    private void StopRelated()
    {
        mediaDetail?.Deactivate(); mediaActive = false; mediaHost.Visibility = Visibility.Collapsed;
        InteractionMotion.StopPage(mediaHost);
        catalogPage.Visibility = Visibility.Visible;
        relatedWorks?.Dispose(); relatedWorks = null; currentWork = null; relatedIdentity = null; currentPresentation = null;
    }
    public void InvalidateBangumiAccess()
    {
        CloseMedia(); relatedIdentity = null;
        relatedWorks?.SetContext(null, "番组计划访问方式已变化，请重新打开作品。");
        if (IsLoaded && currentWork is { } work) UpdateRelated(work, authGeneration());
    }

    // Cached relations and identity proofs can arrive after the catalog body is
    // first measured. Restore through that bounded settling period; user input
    // cancels restoration immediately, so later results cannot pull them back.
    private void QueueRestore()
    {
        if (restoreTarget is null || restoreQueued) return;
        restoreQueued = true; var version = generation;
        DispatcherQueue.TryEnqueue(() =>
        {
            restoreQueued = false;
            if (version != generation || stop?.IsCancellationRequested != false || restoreTarget is not { } target) return;
            scroll.UpdateLayout(); scroll.ChangeView(null, target.ScrollOffset, null, true);
            if (catalogLoading || relatedWorks?.IsSettled == false) return;
            restoreTarget = null;
            relatedWorks?.RestoreNavigationFocus();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (version == generation && stop?.IsCancellationRequested == false && !mediaActive)
                    scroll.ChangeView(null, target.ScrollOffset, null, true);
            });
        });
    }

    private static async Task OpenBangumiSourceAsync(string url)
    {
        if (!BangumiRelationIdentityResolver.TryReferenceId(url, out var id) || url != "https://bgm.tv/subject/" + id
            || !await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException("未能打开来源网页。");
    }

    internal void DisposeRelated() { DisposeGameVideos(); StopRelated(); mediaDetail?.Dispose(); mediaDetail = null; }
    internal async Task RelatedFixtureAsync(string action, BangumiRelationKind? kind, string? id)
    {
        switch (action)
        {
            case "catalogEntitySources":
                if (Program.Profile?.Manifest.ProfileKind != NativeProfileKind.Test) throw new InvalidOperationException("Test profile required.");
                foreach (var expander in body.Children.OfType<Expander>().Where(x => Equals(x.Tag, "entity-sources"))) expander.IsExpanded = true;
                break;
            case "releaseExpand": if (workContent is not null) await workContent.ReleasesFixtureAsync("expand"); break;
            case "releaseRefresh": if (workContent is not null) await workContent.ReleasesFixtureAsync("refresh"); break;
            case "releaseInspect": if (workContent is not null) await workContent.ReleasesFixtureAsync("inspect"); break;
            case "releaseFilter": if (workContent is not null) await workContent.ReleasesFixtureAsync("filter", id); break;
            case "releaseRow": if (workContent is not null) await workContent.ReleasesFixtureAsync("row", id); break;
            case "expand": if (relatedWorks is not null) await relatedWorks.ExpandForFixtureAsync(); break;
            case "collapse": relatedWorks?.CollapseForFixture(); break;
            case "refresh": if (relatedWorks is not null) await relatedWorks.RefreshForFixtureAsync(); break;
            case "cancel": relatedWorks?.CancelForFixture(); break;
            case "select" when kind is { } value: relatedWorks?.SelectForFixture(value); break;
            case "all": relatedWorks?.AllForFixture(); break;
            case "open" when id is not null: if (relatedWorks is not null) await relatedWorks.OpenForFixtureAsync(id); break;
            case "openCatalog" when id is not null: if (relatedWorks is not null) await relatedWorks.OpenCatalogForFixtureAsync(id); break;
            case "mediaCancel": mediaDetail?.CancelForFixture(); break;
            case "mediaRefresh": if (mediaDetail is not null) await mediaDetail.RefreshForFixtureAsync(); break;
            case "musicOriginal": case "musicPreview": case "musicConfirm": case "musicEditions": case "musicTracks": case "musicTrack": case "musicEntity": case "musicEntityBack":
                if (mediaActive && mediaDetail is not null) await mediaDetail.MusicFixtureAsync(action, id); break;
            case "aniPreview": case "aniConfirm": case "aniCharacters": case "aniStaff": case "aniRelations": case "aniExplore": case "aniMore": case "aniEpisodes": case "aniEpisodeFilter": case "aniEpisodeExpand": case "aniPV": case "aniPVLoad": case "aniPVStart": case "aniPVCollapse": case "aniPVPlay": case "aniPVStop": case "aniPVProbe": case "watchSources": case "watchShow": case "watchCollapse": case "aniChoose": case "aniReview": case "aniContentOriginal": case "aniSources": case "aniSynopsis":
            case "bookExpand": case "bookCollapse": case "bookRelations": case "bookOpen": case "bookSearch": case "bookPreview": case "bookConfirm": case "bookForget": case "bookReload": case "bookNext": case "bookBack": case "bookCancel": case "bookInspect":
                if (mediaActive && mediaDetail is not null) await mediaDetail.AniListFixtureAsync(action, id); break;
            case "sources": relatedWorks?.SourcesForFixture(); break;
            case "rawEvidence": relatedWorks?.RawEvidenceForFixture(); break;
            case "show": relatedWorks?.StartBringIntoView(); break;
        }
    }
}
