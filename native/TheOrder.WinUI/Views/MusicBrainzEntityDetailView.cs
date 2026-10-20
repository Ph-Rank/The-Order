using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

// A bounded, read-only branch of the album navigation. The album stays mounted
// at its original offset; returning through this branch never rematches it.
public sealed class MusicBrainzEntityDetailView : Grid, IDisposable
{
    private const int MaximumHistory = 16;
    private sealed record Frame(MusicBrainzEntityResult Result, double Offset, bool Aliases, bool Relations, bool Evidence);
    private readonly List<Frame> history = [];
    private readonly TextBlock type = Text(13), title = Text(28), subtitle = Text(14), message = Text(14), original = Text(13);
    private readonly TagFlowPanel facts = MusicDetailFormatting.Facts();
    private readonly MusicListeningView listening = new();
    private readonly CreditColumnsPanel links = new();
    private readonly Expander aliases = Section("其他名称"), relations = Section("专业关系"), evidence = Section("资料来源与关系依据");
    private readonly StackPanel evidenceBody = new() { Spacing = 8 };
    private readonly HyperlinkButton back = Link("返回专辑"), external = Link("MusicBrainz 原资料 ↗");
    private readonly Button refresh = new() { Content = "刷新资料" }, retry = new() { Content = "重试" }, cancel = new() { Content = "取消读取" };
    private readonly ProgressRing progress = new() { Width = 20, Height = 20 };
    private readonly ScrollViewer scroll;
    private MusicBrainzEntityViewModel? model;
    private Func<string, Task>? openExternal;
    private MusicBrainzEntityDetail? shown;
    private bool active, disposed;
    private long navigation, restoreVersion;
    public Action? ReturnToAlbum { get; set; }

    public MusicBrainzEntityDetailView()
    {
        AutomationProperties.SetAutomationId(this, "MusicEntityDetail");
        AutomationProperties.SetAutomationId(title, "MusicEntityTitle");
        AutomationProperties.SetAutomationId(back, "MusicEntityBack");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        type.Opacity = subtitle.Opacity = .75;
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8, CenterInlineItems = false };
        actions.Children.Add(progress); actions.Children.Add(cancel); actions.Children.Add(retry); actions.Children.Add(refresh);
        aliases.Content = original; relations.Content = links; relations.IsExpanded = true;
        evidence.Content = evidenceBody;
        var body = new StackPanel { Spacing = 24, MaxWidth = 1160, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { back,
            new StackPanel { Spacing = 8, Children = { type, title, subtitle } }, message, actions, listening, facts, aliases, relations, external, evidence } };
        scroll = new() { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(scroll);
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => restoreVersion++), true);
        scroll.PointerPressed += (_, _) => restoreVersion++;
        scroll.KeyDown += (_, _) => restoreVersion++;
        back.Click += (_, _) => GoBack();
        cancel.Click += (_, _) => model?.Cancel();
        retry.Click += async (_, _) => { if (model is not null) await model.RefreshAsync(); };
        refresh.Click += async (_, _) => { if (model is not null) await model.RefreshAsync(); };
        external.Click += async (_, _) =>
        {
            var current = model; var reference = current?.Reference; var version = navigation; var requestVersion = current?.Version;
            if (reference is null || openExternal is null || !active || disposed) return;
            try { await openExternal(reference.Url); }
            catch { if (active && !disposed && version == navigation && ReferenceEquals(current, model) && current?.Version == requestVersion) ShowMessage("未能打开原资料，请稍后重试。"); }
        };
        SizeChanged += (_, _) => title.FontSize = ActualWidth < 520 ? 24 : 28;
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
    }
    public void Configure(IMusicBrainzEntityReader reader, Func<string, Task> open, IMusicListeningReader? outlets = null)
    {
        if (disposed) return;
        Deactivate();
        listening.Configure(outlets);
        if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); }
        openExternal = open; model = new(reader); model.PropertyChanged += Changed;
    }
    public async Task OpenAsync(MusicBrainzReference reference)
    {
        if (disposed || model is null) return;
        history.Clear(); active = true; navigation++; shown = null;
        ResetSections(); scroll.ChangeView(null, 0, null, true);
        await model.OpenAsync(reference);
    }
    private async Task FollowAsync(MusicBrainzReference reference)
    {
        if (!active || disposed || model is null || model.IsBusy || model.Result is not { } result) return;
        if (reference == result.Detail.Reference) return;
        if (history.Count == MaximumHistory) { ShowMessage("已到达本次浏览深度，请返回上一项后继续探索。"); return; }
        history.Add(new(result, scroll.VerticalOffset, aliases.IsExpanded, relations.IsExpanded, evidence.IsExpanded)); navigation++; shown = null;
        ResetSections(); scroll.ChangeView(null, 0, null, true);
        await model.OpenAsync(reference);
    }
    public bool GoBack()
    {
        if (!active || disposed || model is null) return false;
        navigation++;
        if (history.Count == 0) { Deactivate(); ReturnToAlbum?.Invoke(); return true; }
        var previous = history[^1]; history.RemoveAt(history.Count - 1); shown = null;
        model.Restore(previous.Result);
        aliases.IsExpanded = previous.Aliases; relations.IsExpanded = previous.Relations; evidence.IsExpanded = previous.Evidence;
        scroll.UpdateLayout(); scroll.ChangeView(null, previous.Offset, null, true);
        var version = navigation; var restore = ++restoreVersion;
        DispatcherQueue.TryEnqueue(() => { if (active && !disposed && version == navigation && restore == restoreVersion) { scroll.UpdateLayout(); scroll.ChangeView(null, previous.Offset, null, true); } });
        return true;
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) { if (active && !disposed) Render(); }
    private void Render()
    {
        var detail = model?.Detail;
        listening.SetContext(detail?.Reference.Kind == MusicBrainzEntityKind.Recording ? detail.Reference : null);
        type.Text = Kind(model?.Reference?.Kind) + " · MusicBrainz";
        title.Text = detail?.Title ?? (model?.IsBusy == true ? "正在读取资料…" : "音乐资料");
        subtitle.Text = string.Join(" · ", new[] { MusicDetailFormatting.EntityType(detail?.EntityType), detail?.Disambiguation }.Where(x => !string.IsNullOrWhiteSpace(x)));
        subtitle.Visibility = Visible(subtitle.Text.Length > 0);
        ShowMessage(model?.Message ?? "");
        back.Content = new TextBlock { Text = history.Count == 0 ? "返回专辑" : "返回上一项", IsHitTestVisible = false };
        progress.IsActive = model?.IsBusy == true; progress.Visibility = Visible(progress.IsActive);
        cancel.Visibility = Visible(progress.IsActive); retry.Visibility = Visible(model?.State is MusicBrainzEntityState.Failed or MusicBrainzEntityState.Cancelled);
        refresh.IsEnabled = retry.IsEnabled = model?.CanLoad == true;
        external.IsEnabled = model?.Reference is not null;
        // A null detail must always clear the old entity while loading or after
        // a permanent failure, even when navigation reset the render sentinel.
        if (detail is not null && ReferenceEquals(shown, detail)) return;
        shown = detail; facts.Children.Clear(); links.Children.Clear(); evidenceBody.Children.Clear();
        aliases.Visibility = Visible(detail?.Aliases.Length > 0); original.Text = string.Join("\n", detail?.Aliases ?? []);
        relations.Visibility = Visible(detail?.Relations.Length > 0);
        if (detail is null) return;
        foreach (var fact in detail.Facts)
        {
            if (fact.Label == "艺术家" && detail.ArtistCredits.Length > 0)
            {
                var label = Text(12, "艺术家"); label.Opacity = .72;
                facts.Children.Add(new StackPanel { Spacing = 3, Children = { label, MusicDetailFormatting.Artists(detail.ArtistCredits, FollowAsync) } });
                continue;
            }
            var value = fact.Label == "语言" ? string.Join(" / ", fact.Value.Split(" · ").Select(MusicDetailFormatting.Language)) : fact.Value;
            facts.Children.Add(MusicDetailFormatting.Fact(fact.Label, value));
        }
        relations.Header = "专业关系 · " + detail.Relations.Length;
        foreach (var relation in detail.Relations)
        {
            var target = relation.Target;
            var content = new StackPanel { Spacing = 4, IsHitTestVisible = false };
            var role = Text(12, RelationLabel(detail.Reference.Kind, relation)); role.Opacity = .75; content.Children.Add(role);
            var name = Text(15, relation.Title); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; content.Children.Add(name);
            var note = string.Join(" · ", new[] { Kind(target.Kind), relation.Attributes.Length > 0 ? string.Join(" / ", relation.Attributes.Select(MusicDetailFormatting.Attribute)) : null,
                relation.Begin, relation.End }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var noteText = Text(12, note); noteText.Opacity = .7; content.Children.Add(noteText);
            var button = new Button { Content = content, Padding = new(12, 10, 12, 10), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, CornerRadius = new(8) };
            AutomationProperties.SetName(button, relation.Title + " · " + Kind(target.Kind));
            button.Click += async (_, _) => await FollowAsync(target); links.Children.Add(button);
            evidenceBody.Children.Add(Text(12, relation.RelationType + " · " + relation.Direction + " · " + relation.Title + "\n" + target.Url));
        }
        if (detail.RelationsTruncated) links.Children.Add(Text(13, "本次仅显示部分关系；更多资料可在原站查看。"));
        evidenceBody.Children.Insert(0, Text(13, "来源 · MusicBrainz\n" + detail.Reference.Kind + " · " + detail.Reference.Id));
        if (detail.Relations.Length == 0 && !detail.ArtistCredits.Any(x => x.ArtistId is not null))
            facts.Children.Add(Text(13, "此条目暂未提供可继续浏览的关系。"));
    }
    private static string RelationLabel(MusicBrainzEntityKind from, MusicBrainzEntityRelation relation) => relation.RelationType switch
    {
        "composer" => from == MusicBrainzEntityKind.Artist ? "作曲作品" : "作曲", "lyricist" => from == MusicBrainzEntityKind.Artist ? "作词作品" : "作词",
        "arranger" => "编曲", "performance" => relation.Target.Kind == MusicBrainzEntityKind.Work ? "对应作品" : "相关录音",
        "vocal" => "演唱", "instrument" => "演奏", "producer" => "制作", "member of band" => "成员关系",
        "parts" => "组成关系", "based on" => "改编关系", _ => relation.RelationType
    };
    private static string Kind(MusicBrainzEntityKind? kind) => kind switch { MusicBrainzEntityKind.Recording => "录音", MusicBrainzEntityKind.Work => "音乐作品", MusicBrainzEntityKind.Artist => "音乐人 / 团体", _ => "音乐资料" };
    private void ResetSections() { aliases.IsExpanded = evidence.IsExpanded = false; relations.IsExpanded = true; }
    private void ShowMessage(string value) { message.Text = value; message.Visibility = Visible(value.Length > 0); }
    private static TextBlock Text(double size, string value = "") => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static HyperlinkButton Link(string value) => new() { Content = new TextBlock { Text = value, IsHitTestVisible = false }, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
    private static Expander Section(string title) => new() { Header = title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    internal Task FollowForFixture(MusicBrainzReference reference) => FollowAsync(reference);
    internal void Cancel() => model?.Cancel();
    internal Task RefreshAsync() => model?.RefreshAsync() ?? Task.CompletedTask;
    internal object Evidence => new { active, state = model?.State.ToString(), reference = model?.Reference, title = title.Text, historyDepth = history.Count,
        fromCache = model?.Result?.FromCache, stale = model?.Result?.IsStale, facts = model?.Detail?.Facts, relations = model?.Detail?.Relations,
        artistCredits = model?.Detail?.ArtistCredits, listening = listening.Evidence,
        artistLinks = facts.Children.OfType<StackPanel>().SelectMany(x => x.Children.OfType<TagFlowPanel>())
            .SelectMany(x => x.Children.OfType<HyperlinkButton>()).Select(InteractiveCardEvidence.Hit).ToArray(),
        visualFactCount = facts.Children.Count, visualRelationCount = links.Children.OfType<Button>().Count(),
        message = message.Text, scrollOffset = scroll.VerticalOffset, width = ActualWidth, links = links.Children.OfType<Button>().Select(InteractiveCardEvidence.Hit).ToArray() };
    public void Deactivate() { active = false; navigation++; model?.Deactivate(); listening.Deactivate(); progress.IsActive = false; history.Clear(); shown = null; }
    public void Dispose() { if (disposed) return; Deactivate(); disposed = true; if (model is not null) { model.PropertyChanged -= Changed; model.Dispose(); } listening.Dispose(); openExternal = null; }
}
