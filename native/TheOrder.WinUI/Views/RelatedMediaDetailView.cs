using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// Read-only exploration inside the existing details host. The owning page keeps
// the originating game, scroll position and shell back path; this view owns only
// one anonymous public subject request and its image lifetime.
public sealed class RelatedMediaDetailView : Grid, IDisposable
{
    private readonly TextBlock title = new() { FontSize = 28, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock originalTitle = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = .75 };
    private readonly TextBlock media = new() { TextWrapping = TextWrapping.Wrap, Opacity = .75 };
    private readonly TextBlock relation = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock provenance = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly ProgressRing progress = new() { Width = 18, Height = 18, IsActive = false };
    private readonly Button cancel = new() { Content = "取消读取", MinHeight = 32 };
    private readonly Button retry = new() { Content = "重试读取", MinHeight = 32 };
    private readonly Button refresh = new() { Content = "刷新资料", MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly HyperlinkButton source = new() { Content = "在 Bangumi 查看原站", Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TagFlowPanel facts = new() { HorizontalSpacing = 20, VerticalSpacing = 8, MaximumItemWidth = 400, CenterInlineItems = false };
    private readonly TagFlowPanel actions = new() { HorizontalSpacing = 8, VerticalSpacing = 6 };
    private readonly CoverPresenter picture = new() { Width = 136, Height = 182, PreserveEntireImage = true, ImageCornerRadius = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid heading = new() { ColumnSpacing = 20 };
    private readonly Expander sources = new SmoothExpander() { Header = "资料来源", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Expander sourceEvidence = new SmoothExpander() { Header = "详细证据", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ScrollViewer scroll;
    private BangumiSubjectDetailsViewModel? model;
    private IRemoteCoverRegistry? registry;
    private Func<string, Task>? openLink;
    private string originTitle = "";
    private string? shownCover;
    private long generation;
    private bool active, disposed;
    private MusicBrainzAlbumView? musicView;
    private IBangumiSubjectDetailsReader? subjectReader;
    private IImagePipeline? images;
    private BangumiRelationItem? musicItem;
    private bool musicActive;
    private AniListDetailView? aniListView;
    private bool aniListActive;
    // Display-only refinement from the originating relation projection. The
    // original provider label remains independently visible in provenance.
    public string? RelationLabel { get; set; }
    public string? RelationEvidence { get; set; }

    public RelatedMediaDetailView()
    {
        AutomationProperties.SetAutomationId(this, "RelatedMediaDetail");
        AutomationProperties.SetAutomationId(title, "RelatedMediaDetailTitle");
        AutomationProperties.SetAutomationId(media, "RelatedMediaDetailType");
        AutomationProperties.SetAutomationId(relation, "RelatedMediaDetailRelation");
        AutomationProperties.SetAutomationId(summary, "RelatedMediaDetailSummary");
        AutomationProperties.SetAutomationId(status, "RelatedMediaDetailStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(cancel, "RelatedMediaDetailCancel");
        AutomationProperties.SetAutomationId(retry, "RelatedMediaDetailRetry");
        AutomationProperties.SetAutomationId(refresh, "RelatedMediaDetailRefresh");
        AutomationProperties.SetAutomationId(source, "RelatedMediaDetailSource");
        AutomationProperties.SetAutomationId(sources, "RelatedMediaDetailSources");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        picture.ImageCornerRadius = 7;
        var coverFrame = new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CatalogCreditCardStyle"], Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = picture };
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(coverFrame);
        var labels = new StackPanel { Spacing = 10, Children = { media, title, originalTitle, facts } };
        SetColumn(labels, 1); heading.Children.Add(labels);
        actions.Children.Add(progress); actions.Children.Add(cancel); actions.Children.Add(retry);
        sourceEvidence.Content = provenance;
        sources.Content = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "作品与关系资料 · Bangumi", TextWrapping = TextWrapping.Wrap }, source, refresh, sourceEvidence } };
        var body = new StackPanel { Spacing = 24, Children =
        {
            heading,
            relation,
            new StackPanel { Spacing = 8, Children = { status, actions } },
            new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "简介", Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SectionTitleStyle"] }, summary } },
            sources
        } };
        scroll = new ScrollViewer
        {
            Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Children.Add(scroll);
        cancel.Click += (_, _) => { model?.Cancel(); retry.Focus(FocusState.Programmatic); };
        retry.Click += async (_, _) => await RefreshAsync();
        refresh.Click += async (_, _) => await RefreshAsync();
        source.Click += async (_, _) => await OpenSourceAsync();
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 520;
            picture.Width = narrow ? 104 : 136; picture.Height = narrow ? 142 : 182;
            heading.ColumnSpacing = narrow ? 14 : 20; title.FontSize = narrow ? 24 : 28;
        };
        // A deferred old Unloaded event must not cancel a newly reattached page.
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
        Synchronize();
    }

    public void Configure(IBangumiSubjectDetailsReader reader, IImagePipeline images,
        IRemoteCoverRegistry? coverRegistry, Func<string, Task> openExternal)
    {
        if (disposed) return;
        Deactivate();
        if (model is not null) { model.PropertyChanged -= OnState; model.Dispose(); }
        registry = coverRegistry; picture.Pipeline = images; this.images = images; subjectReader = reader; openLink = openExternal;
        model = new(reader); model.PropertyChanged += OnState;
    }

    public void ConfigureMusic(IMusicBrainzReader music, IMusicBrainzEntityReader? entities = null)
    {
        if (disposed || subjectReader is null || images is null || openLink is null) return;
        musicView?.Dispose(); if (musicView is not null) Children.Remove(musicView);
        musicView = new() { Visibility = Visibility.Collapsed };
        musicView.Configure(subjectReader, music, images, registry, openLink, entities); Children.Add(musicView);
    }

    public void ConfigureAniList(IAniListReader reader, IWatchLinkPreferencesStore? watchPreferences = null, IBangumiWatchLinksReader? watchReader = null)
    {
        if (disposed || subjectReader is null || images is null || openLink is null) return;
        aniListView?.Dispose(); if (aniListView is not null) Children.Remove(aniListView);
        aniListView = new() { Visibility = Visibility.Collapsed };
        aniListView.Configure(subjectReader, reader, images, registry, openLink, watchPreferences, watchReader); Children.Add(aniListView);
    }

    public async Task OpenAsync(BangumiRelationItem item, string originTitle, CancellationToken cancellation = default)
    {
        if (disposed || model is null) return;
        generation++; active = true; this.originTitle = originTitle;
        musicView?.Deactivate(); musicItem = null; musicActive = false;
        aniListView?.Deactivate(); aniListActive = false;
        if (aniListView is not null) aniListView.Visibility = Visibility.Collapsed;
        if (musicView is not null) musicView.Visibility = Visibility.Collapsed;
        if (item.Kind is BangumiRelationKind.Anime or BangumiRelationKind.Book && aniListView is not null)
        {
            model.Deactivate(); aniListActive = true; scroll.Visibility = Visibility.Collapsed; aniListView.Visibility = Visibility.Visible;
            await aniListView.OpenAsync(item, originTitle, RelationLabel, RelationEvidence, cancellation); return;
        }
        if (item.Kind == BangumiRelationKind.Music && musicView is not null)
        {
            model.Deactivate(); musicActive = true; musicItem = item; scroll.Visibility = Visibility.Collapsed; musicView.Visibility = Visibility.Visible;
            await musicView.OpenAsync(item, originTitle, RelationLabel, RelationEvidence, cancellation); return;
        }
        scroll.Visibility = Visibility.Visible; if (musicView is not null) musicView.Visibility = Visibility.Collapsed;
        sources.IsExpanded = sourceEvidence.IsExpanded = false; scroll.ChangeView(null, 0, null);
        shownCover = null; picture.SourceKey = null;
        await model.OpenAsync(item, cancellation);
    }

    public void ConfigureEpisodes(IBangumiEpisodesReader reader) => aniListView?.ConfigureEpisodes(reader);
    public void ConfigureBooks(IBookBibliographyReader books, IBangumiBookRelationsReader relations)
        => aniListView?.ConfigureBooks(books, relations);

    private void OnState(object? sender, PropertyChangedEventArgs args)
    {
        if (!disposed && active) { generation++; Synchronize(); }
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private static string KindName(BangumiRelationKind kind) => kind switch
    {
        BangumiRelationKind.Anime => "动画", BangumiRelationKind.Music => "音乐",
        BangumiRelationKind.Book => "书籍", BangumiRelationKind.Game => "游戏", _ => "作品"
    };

    private void Synchronize()
    {
        var item = model?.Item;
        var details = model?.Result?.Details;
        title.Text = details?.Title ?? item?.Title ?? "关联作品";
        originalTitle.Text = details?.OriginalTitle ?? item?.OriginalTitle ?? "";
        originalTitle.Visibility = Visible(originalTitle.Text.Length > 0 && originalTitle.Text != title.Text);
        media.Text = item is null ? "Bangumi" : KindName(item.Kind) + " · Bangumi";
        var rawRelation = string.IsNullOrWhiteSpace(item?.Relation) ? "未标明关系类型" : item.Relation;
        var displayRelation = string.IsNullOrWhiteSpace(RelationLabel) ? rawRelation : RelationLabel;
        relation.Text = originTitle.Length == 0 ? "关联 · " + displayRelation : $"与《{originTitle}》的关联 · {displayRelation}";
        relation.Visibility = Visible(item is not null);
        summary.Text = details is null
            ? model?.State is BangumiSubjectDetailsState.Failed or BangumiSubjectDetailsState.Cancelled
                ? "暂未取得简介，可重试或查看原站。" : "完整资料读取后显示简介。"
            : string.IsNullOrWhiteSpace(details.Summary) ? "原站暂未提供简介。" : details.Summary;
        summary.Opacity = details is not null && details.Summary.Length > 0 ? 1 : .75;
        facts.Children.Clear();
        if (details is not null)
        {
            if (!string.IsNullOrWhiteSpace(details.ReleaseDate)) AddFact("发行日期", details.ReleaseDate);
            if (!string.IsNullOrWhiteSpace(details.Platform)) AddFact("类型 / 平台", details.Platform);
            if (details.EpisodeCount is { } count) AddFact(details.Kind == BangumiRelationKind.Anime ? "集数" : "章节数", count.ToString(CultureInfo.InvariantCulture));
            if (details.Score is { } score && details.RatingCount is { } votes)
                AddFact("Bangumi 评分", score.ToString("0.0", CultureInfo.InvariantCulture) + $"（{votes} 人）");
        }
        facts.Visibility = Visible(facts.Children.Count > 0);
        var cover = details?.CoverUrl ?? item?.CoverUrl;
        if (cover != shownCover)
        {
            shownCover = cover;
            picture.SourceKey = registry?.Register(cover, details?.FetchedAtUtc.ToString("O", CultureInfo.InvariantCulture) ?? "bangumi-relation");
        }
        status.Text = model?.Message ?? "";
        status.Visibility = Visible(status.Text.Length > 0);
        progress.IsActive = model?.IsBusy == true; progress.Visibility = Visible(progress.IsActive);
        cancel.Visibility = Visible(model?.CanCancel == true); cancel.IsEnabled = model?.CanCancel == true;
        var needsRetry = model?.State is BangumiSubjectDetailsState.Failed or BangumiSubjectDetailsState.Cancelled || model?.Result?.Warning is not null;
        retry.Visibility = Visible(needsRetry && model?.CanLoad == true);
        retry.IsEnabled = model?.CanLoad == true;
        actions.Visibility = Visible(progress.IsActive || retry.Visibility == Visibility.Visible);
        // Collapse the entire status container as well, so a successful read does
        // not leave an empty section's spacing above the summary.
        if (status.Parent is StackPanel statusPanel)
            statusPanel.Visibility = Visible(status.Visibility == Visibility.Visible || actions.Visibility == Visibility.Visible);
        refresh.IsEnabled = model?.CanLoad == true;
        source.IsEnabled = item is not null;
        var fetched = details is null ? "" : "\n资料读取时间 · " + details.FetchedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var relationEvidence = string.IsNullOrWhiteSpace(RelationEvidence) ? "" : "\n关系说明 · " + RelationEvidence;
        provenance.Text = item is null ? "Bangumi 公开作品资料" : $"来源 · Bangumi\n作品编号 · {item.Id}\n原始关系标注 · {rawRelation}" + relationEvidence + fetched;
    }

    private void AddFact(string name, string value) => facts.Children.Add(new TextBlock
    {
        Text = name + " · " + value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
    });

    private Task RefreshAsync() => active && !disposed ? aniListActive ? aniListView?.RefreshAsync() ?? Task.CompletedTask : musicActive ? musicView?.RefreshAsync() ?? Task.CompletedTask
        : model?.RefreshAsync() ?? Task.CompletedTask : Task.CompletedTask;
    private async Task OpenSourceAsync()
    {
        var requestModel = model; var item = requestModel?.Item; var version = generation;
        if (!active || disposed || item is null || openLink is null) return;
        try { await openLink(item.Url); }
        catch
        {
            if (!disposed && active && version == generation && ReferenceEquals(model, requestModel) && model?.Item == item)
            {
                status.Text = "未能打开 Bangumi 页面，请稍后重试。"; status.Visibility = Visibility.Visible;
                if (status.Parent is StackPanel panel) panel.Visibility = Visibility.Visible;
            }
        }
    }

    public void Deactivate()
    {
        active = false; generation++; model?.Deactivate();
        musicView?.Deactivate();
        aniListView?.Deactivate();
        picture.SourceKey = null; shownCover = null; progress.IsActive = false;
    }
    internal Task RefreshForFixtureAsync() => RefreshAsync();
    internal void CancelForFixture() { if (aniListActive) aniListView?.Cancel(); else if (musicActive) musicView?.Cancel(); else model?.Cancel(); }
    internal bool GoBackMedia() => aniListActive && aniListView?.GoBack() == true || musicActive && musicView?.GoBack() == true;
    internal Task AniListFixtureAsync(string action, string? id) => aniListActive ? aniListView?.FixtureAsync(action, id) ?? Task.CompletedTask : Task.CompletedTask;
    internal Task MusicFixtureAsync(string action, string? id)
    {
        if (!musicActive || musicView is null) return Task.CompletedTask;
        return action switch
        {
            "musicOriginal" => ShowMusicOriginal(),
            "musicPreview" when id is not null => musicView.PreviewForFixture(id),
            "musicConfirm" => musicView.ConfirmForFixture(),
            "musicEditions" => musicView.EditionsForFixture(),
            "musicTrack" => ShowMusicTrack(id),
            "musicEntity" when MusicBrainzReference.TryParse(id, out var reference) && reference is not null => musicView.EntityForFixture(reference),
            "musicEntityBack" => BackMusicEntity(),
            "musicTracks" => ShowMusicTracks(id), _ => Task.CompletedTask
        };
    }
    private Task ShowMusicOriginal() { musicView?.OriginalForFixture(); return Task.CompletedTask; }
    private Task BackMusicEntity() { musicView?.GoBack(); return Task.CompletedTask; }
    private Task ShowMusicTrack(string? id) { if (int.TryParse(id, out var position) && position is > 0 and <= 1000) musicView?.TrackForFixture(position); return Task.CompletedTask; }
    private Task ShowMusicTracks(string? id) { musicView?.TracksForFixture(int.TryParse(id, out var n) ? n : null); return Task.CompletedTask; }
    internal object Evidence => aniListActive ? (object)new { active, state = "AniListMedia", aniList = aniListView?.Evidence } : musicActive ? (object)new
    {
        active, state = "MusicAlbum", subjectId = musicItem?.Id, kind = "Music", title = musicItem?.Title,
        rawRelation = musicItem?.Relation, RelationLabel, RelationEvidence, originTitle, sourceUrl = musicItem?.Url, music = musicView?.Evidence
    } : new
    {
        active, state = model?.State.ToString(), subjectId = model?.Item?.Id,
        kind = model?.Item?.Kind.ToString(), title = title.Text, originalTitle = originalTitle.Text,
        relation = relation.Text, rawRelation = model?.Item?.Relation, RelationLabel, RelationEvidence, originTitle,
        summary = summary.Text, facts = facts.Children.OfType<TextBlock>().Select(x => x.Text).ToArray(),
        sourceUrl = model?.Item?.Url, status = status.Text, fromCache = model?.Result?.FromCache,
        stale = model?.Result?.IsStale, canCancel = model?.CanCancel, canRetry = retry.Visibility == Visibility.Visible,
        picture.HasImage, picture.SourceStatus, width = ActualWidth, coverWidth = picture.ActualWidth,
        titleWidth = title.ActualWidth, scrollOffset = scroll.VerticalOffset
    };
    public void Dispose()
    {
        if (disposed) return;
        Deactivate(); disposed = true;
        if (model is not null) { model.PropertyChanged -= OnState; model.Dispose(); }
        openLink = null; registry = null; picture.Pipeline = null;
        musicView?.Dispose(); subjectReader = null; images = null;
        aniListView?.Dispose();
    }
}
