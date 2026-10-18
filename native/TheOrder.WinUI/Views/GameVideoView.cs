using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TheOrder.Application;
using TheOrder.Presentation;

namespace TheOrder.WinUI.Views;

public sealed class GameVideoView : StackPanel, IDisposable
{
    private readonly Expander section = new() { Header = "宣传影像 / PV", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel candidates = new() { Spacing = 8 }, videos = new() { Spacing = 8 };
    private readonly TagFlowPanel identityActions = new() { HorizontalSpacing = 8, VerticalSpacing = 6 };
    private readonly StackPanel otherCandidates = new() { Spacing = 8 };
    private readonly Expander otherVersions = new() { Header = "其他检索结果", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock status = Text(13);
    private readonly TextBox query = new() { PlaceholderText = "原名或别名", MaxLength = 200 };
    private readonly TextBox manual = new() { PlaceholderText = "HTTPS YouTube 视频地址", MaxLength = 500 };
    private readonly Button retry = new() { Content = "刷新影像" }, search = new() { Content = "查找游戏" }, add = new() { Content = "添加用户视频" }, cancel = new() { Content = "取消读取" };
    private readonly AnimeAvailabilityView player = new("Game", "来源见所选影像 · 由 YouTube 原站播放器提供，不代表版权或本地发行版本已核验。");
    private GameVideoViewModel? model;
    private string? sourceKey, playing;
    private bool syncing;
    public GameVideoView()
    {
        AutomationProperties.SetAutomationId(section, "GameVideosExpand"); AutomationProperties.SetAutomationId(status, "GameVideosStatus");
        AutomationProperties.SetAutomationId(otherVersions, "GameVideosOtherCandidates"); otherVersions.Content = otherCandidates;
        AutomationProperties.SetAutomationId(query, "GameVideosQuery"); AutomationProperties.SetAutomationId(search, "GameVideosSearch");
        AutomationProperties.SetAutomationId(retry, "GameVideosRefresh"); AutomationProperties.SetAutomationId(cancel, "GameVideosCancel");
        AutomationProperties.SetAutomationId(manual, "GameVideosManualUrl"); AutomationProperties.SetAutomationId(add, "GameVideosManualAdd");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var searchRow = new Grid { ColumnSpacing = 8 }; searchRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.Children.Add(query); searchRow.Children.Add(search); Grid.SetColumn(search, 1);
        var details = new Expander { Header = "影像选项", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 8, Children = { identityActions, searchRow, manual, add } } };
        var attribution = Text(12, "资料来源：IGDB · YouTube。数据库收录不等于官方发布；作品级影像不保证对应本地版本。"); attribution.Opacity = .7;
        section.Content = new StackPanel { Spacing = 10, Children = { status, candidates, otherVersions, videos, player,
            new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6, Children = { retry, cancel } }, details, attribution } };
        Children.Add(section);
        section.Expanding += async (_, _) => { if (!syncing && model is not null) await model.LoadAsync(); };
        section.Collapsed += (_, _) => { model?.Cancel(); StopPlayer(); };
        retry.Click += async (_, _) => { if (model is not null) await model.LoadAsync(true); };
        search.Click += async (_, _) => { if (model is not null) await model.LoadAsync(true, query.Text.Trim()); };
        add.Click += async (_, _) => { if (model is not null) await model.AddManualAsync(manual.Text.Trim()); };
        cancel.Click += (_, _) => { model?.Cancel(); Render(); };
        Unloaded += (_, _) => Suspend(); Visibility = Visibility.Collapsed;
    }
    public void Configure(GameVideoReader reader, IMediaIdentityDecisions decisions)
    {
        model?.Dispose(); model = new(reader, decisions); model.PropertyChanged += (_, _) => Render();
    }
    public void SetSource(GameVideoSource? source)
    {
        if (sourceKey != source?.Fingerprint)
        {
            StopPlayer(); syncing = true; section.IsExpanded = false; syncing = false; sourceKey = source?.Fingerprint;
            otherVersions.IsExpanded = false;
            query.Text = source?.Title ?? ""; manual.Text = "";
        }
        model?.SetSource(source); Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Render()
    {
        if (model is null) return;
        if (model.Busy || !model.Videos.Any(x => x.VideoId == playing)) StopPlayer();
        status.Text = model.Message; status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        search.IsEnabled = add.IsEnabled = retry.IsEnabled = !model.Busy; cancel.Visibility = model.Busy ? Visibility.Visible : Visibility.Collapsed;
        identityActions.Children.Clear(); candidates.Children.Clear(); otherCandidates.Children.Clear(); videos.Children.Clear(); otherVersions.Visibility = Visibility.Collapsed;
        if (model.Game is { } game)
        {
            var row = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6, Children = { Text(13, game.Name + " · " + GameVideos.CandidateKind(game) + " · " + (game.ReleaseDate ?? "日期未注明")) } };
            var withdraw = Action("撤销游戏对应", "GameVideosWithdraw", () => model.WithdrawAsync()); identityActions.Children.Add(withdraw); candidates.Children.Add(row);
            if (game.ParentGame is { } main) candidates.Children.Add(Text(12, "所属游戏 · IGDB " + main));
        }
        else
        {
            foreach (var item in model.PrimaryCandidates) candidates.Children.Add(Candidate(item));
            var others = model.OtherCandidates;
            if (others.Length > 0)
            {
                otherVersions.Header = "其他检索结果 · " + others.Length;
                otherVersions.Visibility = Visibility.Visible;
                otherCandidates.Children.Add(Text(12, "移植、合集、衍生作品或资料不足的结果在此核对；不会自动采用。"));
                foreach (var item in others) otherCandidates.Children.Add(Candidate(item));
            }
        }
        foreach (var item in model.Videos)
        {
            var title = Text(14, item.Name); var caption = Text(12, item.Classification + (item.UserProvided ? " · 来源：用户提供" : " · 来源：IGDB · YouTube") + (model.DefaultVideo == item.VideoId ? " · 默认" : ""));
            var row = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6 };
            var play = Action("播放", "GameVideoPlay-" + item.VideoId, () => { playing = item.VideoId; player.Show(model.Game?.Id ?? 0, true, new("youtube", item.VideoId)); player.Expand(); return Task.CompletedTask; });
            play.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
            row.Children.Add(play); row.Children.Add(Action("设为默认", "GameVideoDefault-" + item.VideoId, () => model.ChooseVideoAsync(item, false)));
            row.Children.Add(Action(item.UserProvided ? "移除" : "排除此视频", "GameVideoReject-" + item.VideoId, () => item.UserProvided ? model.RemoveManualAsync(item) : model.ChooseVideoAsync(item, true)));
            var original = Action("YouTube ↗", "GameVideoOriginal-" + item.VideoId, async () => { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(item.Url))) status.Text = "无法打开原站，请检查默认浏览器。"; }); row.Children.Add(original);
            ToolTipService.SetToolTip(title, item.UserProvided ? "用户提供，未核验" : "IGDB " + string.Join(" / ", item.Relations.Select(x => x.GameId + ":" + x.Id + " · " + x.Name)));
            videos.Children.Add(new StackPanel { Spacing = 4, Children = { title, caption, row } });
        }
        if (model.Videos.Length > 0 || model.Game?.Videos.Length > 0) identityActions.Children.Add(Action("重置视频选择", "GameVideosReset", () => model.ResetVideoChoicesAsync()));
    }
    private StackPanel Candidate(IgdbGame item)
    {
        var title = Text(14, item.Name); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var detail = Text(12, GameVideos.CandidateKind(item) + " · " + (item.ReleaseDate ?? "日期未注明")
            + (model?.IsRejected(item) == true ? " · 已排除" : "")
            + "\n" + string.Join(" / ", item.Developers) + " · " + string.Join(" / ", item.Platforms)
            + (item.VersionParent is { } parent ? "\n版本所属游戏 · IGDB " + parent : "")
            + (item.ParentGame is { } main ? "\n所属游戏 · IGDB " + main : "")); detail.Opacity = .75;
        var actions = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 6, Children = { Action("使用此游戏", "GameVideosConfirm-" + item.Id, () => model!.ConfirmAsync(item)), Action("排除", "GameVideosReject-" + item.Id, () => model!.RejectGameAsync(item)) } };
        return new StackPanel { Spacing = 5, Margin = new(0, 0, 0, 8), Children = { title, detail, actions } };
    }
    private Button Action(string text, string id, Func<Task> action)
    {
        var button = new Button { Content = text, MinHeight = 32, Padding = new Thickness(12, 5, 12, 5), CornerRadius = new(6), IsEnabled = model?.Busy != true }; AutomationProperties.SetAutomationId(button, id);
        button.Click += async (_, _) => { try { await action(); } catch { status.Text = "操作暂未完成，请重试。"; } }; return button;
    }
    private void StopPlayer() { player.Show(null, false, null); playing = null; }
    public void Suspend() { model?.Cancel(); StopPlayer(); }
    public void Dispose() { Suspend(); model?.Dispose(); player.Dispose(); }
    internal Task ProbeForFixtureAsync() => player.ProbeForFixtureAsync();
    internal object Evidence => new { expanded = section.IsExpanded, source = model?.Source, game = model?.Game?.Id, model?.Busy, model?.Message,
        candidates = model?.Candidates.Select(x => x.Id), videos = model?.Videos, defaultVideo = model?.DefaultVideo, playing, playback = player.Evidence };
    private static TextBlock Text(double size, string text = "") => new() { FontSize = size, Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
}
