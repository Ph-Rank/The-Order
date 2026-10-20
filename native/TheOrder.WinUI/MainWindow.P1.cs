using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;

namespace TheOrder.WinUI;
public sealed partial class MainWindow
{
    private readonly LibrarySourcesService sources;
    private readonly WindowsLibrarySourcesWatcher sourceWatcher;
    private readonly LibraryExtrasClient extras;
    private readonly MetadataSourceClient metadataSources;
    private readonly WindowsLocalResources resources;
    private readonly CancellationTokenSource p1Lifetime = new();
    private readonly DispatcherTimer sourceTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly WindowsBackgroundAudio backgroundAudio = new();
    private Task audioOperation = Task.CompletedTask;
    private DateTimeOffset lastAudioCheck;
    private ContentDialog? p1Dialog;
    private Task p1Operation = Task.CompletedTask, sourceOperation = Task.CompletedTask;
    private bool p1Stopping;
    private string? sourceError, recentSignature;
    private bool? recentShellReady;
    private string? recentShellFailure;
    private DateTimeOffset lastFullSourceScan;
    private void ConfigureP1Pages()
    {
        libraryPage.OpenSources = settingsPage.OpenLibrarySources = OpenSourcesAsync;
        importPage.OpenLibrarySources = OpenSourcesAsync;
        importPage.OpenLibrary = () => Navigate(WorkspacePage.Library);
        libraryPage.ReviewDiscovery = async () =>
        {
            if (p1Stopping || commandsBlocked || !ViewModel.CanEdit || !await ResolveUnsavedAsync()) return;
            files.ReviewDiscovered(sources.Candidates);
            await Navigate(WorkspacePage.Import);
        };
        libraryPage.DismissDiscovery = async () => { var pending = sources.Candidates; if (!pending.IsEmpty) await IgnoreDiscoveredAsync(pending); };
        importPage.IgnoreDiscovered = IgnoreDiscoveredAsync;
        libraryPage.OpenLaunchSettings = OpenLaunchSettingsAsync;
        detailsPage.OpenLaunchSettings = async () => { if (ViewModel.SelectedId is { } id) await OpenLaunchSettingsAsync(id); };
        libraryPage.OpenStatistics = () => Navigate(WorkspacePage.Statistics); libraryPage.ConfigureP1();
        detailsPage.OpenGameTools = async () => { if (ViewModel.SelectedId is { } id && await ResolveUnsavedAsync()) await OpenGameToolsAsync(id); };
        installationsPage.ChooseHelper = () => PickFile(".exe"); installationsPage.LocalResources = resources;
        sourceTimer.Tick += async (_, _) => { if (!p1Stopping && sourceOperation.IsCompleted) await (sourceOperation = CheckSourcesAsync()); };
    }
    private async Task InitializeP1Async()
    {
        if (p1Stopping) return;
        try { await sources.InitializeAsync(p1Lifetime.Token); if (p1Stopping) return; sourceWatcher.Refresh(); await sources.ScanAsync(true, p1Lifetime.Token); lastFullSourceScan = DateTimeOffset.UtcNow; }
        catch (OperationCanceledException) { }
        catch (Exception) { sourceError = "库目录配置无法读取，原文件已保留。"; }
        if (!p1Stopping) { ShowSourceState(); sourceTimer.Start(); UpdateRecentGames(); }
    }
    private async Task CheckSourcesAsync()
    {
        try
        {
            sourceWatcher.Refresh(); bool full = DateTimeOffset.UtcNow - lastFullSourceScan >= TimeSpan.FromMinutes(15);
            await sources.ScanAsync(full, p1Lifetime.Token, watchedOnly: true); if (full) lastFullSourceScan = DateTimeOffset.UtcNow;
            if (!p1Stopping) { ShowSourceState(); UpdateRecentGames(); }
        }
        catch (OperationCanceledException) { }
        catch { if (!p1Stopping) { sourceError = "库目录检查未完成，已有书架保留。"; ShowSourceState(); } }
    }
    private void ShowSourceState() => libraryPage.ShowDiscovery(sources.Candidates.Length, sourceError);
    private async Task IgnoreDiscoveredAsync(ImmutableArray<ScanCandidate> candidates)
    {
        if (p1Stopping || commandsBlocked) return;
        try
        {
            await sources.IgnoreAsync(candidates, p1Lifetime.Token); sourceError = null;
            if (ViewModel.Page == WorkspacePage.Import && files.IsSourceDiscovery) files.ReviewDiscovered(sources.Candidates);
        }
        catch { sourceError = "忽略记录未能保存，请重新检查库目录后重试。"; throw; }
        finally { if (!p1Stopping) ShowSourceState(); }
    }
    private async Task OpenLaunchSettingsAsync(string id)
    {
        if (p1Stopping || commandsBlocked || p1Dialog is not null || !ViewModel.CanEdit) return;
        await installationNavigation.OpenAsync(id, ResolveUnsavedAsync, async () =>
        {
            if (files.GameId != id || files.Installations is not { } installations) return;
            await Navigate(WorkspacePage.Installations);
            var selected = installations.Rows.FirstOrDefault(r => r.Preferred) ?? installations.Rows.FirstOrDefault();
            if (selected is not null) files.Edit(selected);
            await installationsPage.ShowLaunchHelpersAsync();
        });
    }
    private void UpdateRecentGames()
    {
        if (Program.Profile is not { } profile || extras.Snapshot is not { } snapshot) return;
        var rows = RecentGameActivation.Recent(snapshot.Entries); var signature = string.Join('|', rows.Select(e => e.Id + ":" + e.LastPlayedAt + ":" + e.Name));
        if (signature == recentSignature) return;
        try { WindowsRecentGames.Update(profile, rows); recentSignature = signature; recentShellReady = true; recentShellFailure = null; }
        catch (Exception e) { recentSignature = signature; recentShellReady = false; recentShellFailure = e.GetType().Name; } // Shell policy failure cannot block the bookshelf.
    }
    internal async Task ActivateRecentGameAsync(RecentGameActivation request)
    {
        if (!await startupFinished.Task) return;
        if (p1Stopping || commandsBlocked || !startupReady || Program.Profile is not { } profile
            || !request.Matches(profile.Manifest.ProfileId, profile.Manifest.StoreId, profile.Manifest.ProfileKind.ToString(), profile.Root)) return;
        if (extras.Snapshot?.Entries.FirstOrDefault(e => e.Id == request.GameId) is not { } entry) { ShowLifecycleError("此最近游玩项目已移除，请从书架选择其他作品。"); return; }
        if (p1Dialog is not null || !await ResolveUnsavedAsync()) return;
        await QuickLaunchAsync(new GameSummary(entry.Id, entry.Name, entry.Status, entry.PersonalRating, entry.Favorite,
            string.Join(" / ", entry.Categories), entry.Installations.Length, entry.EffectivePlaySeconds)
            { LastPlayedAt = entry.LastPlayedAt, HasPlayHistory = entry.PlayCount > 0, CatalogWorkId = entry.Metadata?.Id });
    }
    private Task? p1Disposal;
    private void RefreshBackgroundAudio()
    {
        if (audioSmokeFixture) return; // Only the marked Test audio proof owns this interval.
        if (p1Stopping || !audioOperation.IsCompleted || DateTimeOffset.UtcNow - lastAudioCheck < TimeSpan.FromSeconds(1)) return;
        lastAudioCheck = DateTimeOffset.UtcNow;
        var session = Sessions.Session;
        var native = session?.Session?.Native;
        var installation = native is null ? null : Sessions.Library?.Entries.FirstOrDefault(e => e.Id == native.GameId)
            ?.Installations.FirstOrDefault(i => i.Id == native.InstallationId);
        bool enabled = native?.Lifecycle == SessionLifecycle.Open
            && session?.Tracking is SessionTracking.LaunchScope or SessionTracking.CurrentProcessOnly
            && installation is not null && launchPolicy.BackgroundMute(installation);
        audioOperation = backgroundAudio.UpdateAsync(session?.Current, enabled);
    }
    private Task StopP1Async() => p1Disposal ??= StopCoreP1Async();
    private async Task StopCoreP1Async()
    {
        p1Stopping = true; startupFinished.TrySetResult(false); sourceTimer.Stop(); p1Dialog?.Hide(); p1Lifetime.Cancel();
        statisticsPage?.Dispose();
        await audioOperation; await backgroundAudio.DisposeAsync();
        if (initialization is not null) await initialization; await backgroundInitialization;
        await Task.WhenAll(p1Operation, sourceOperation); sourceWatcher.Dispose(); await sources.DisposeAsync(); p1Lifetime.Dispose();
    }
    private ContentDialog Dialog(string title, UIElement content)
        => new() { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = title, Content = new ScrollViewer { Content = content, MaxHeight = 550, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch }, CloseButtonText = "关闭" };
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Panel(params UIElement[] children) { var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch }; foreach (var child in children) panel.Children.Add(child); return panel; }
    private static Button ActionButton(string text, Func<Task> action, string? id = null, Func<bool>? canExecute = null)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        if (id is not null) AutomationProperties.SetAutomationId(button, id);
        button.IsEnabled = canExecute?.Invoke() ?? true;
        button.Click += async (_, _) => { if (!button.IsEnabled) return; button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = canExecute?.Invoke() ?? true; } }; return button;
    }
    private async Task RunP1Async(TextBlock notice, Func<Task> action, CancellationToken cancellation)
    {
        if (p1Stopping) return;
        if (!p1Operation.IsCompleted) { notice.Text = "上一项操作尚未完成，请稍后再试。"; return; }
        async Task Run()
        {
            try { notice.Text = "正在处理…"; cancellation.ThrowIfCancellationRequested(); await action(); }
            catch (OperationCanceledException) { notice.Text = "已取消，已开始保存的修改会完成保存。"; }
            catch (InvalidDataException e) { notice.Text = e.Message; }
            catch (Exception) { notice.Text = "未能完成，请检查路径或网络后重试。原书架保留。"; }
        }
        await (p1Operation = Run());
    }
    private async Task OpenSourcesAsync()
    {
        if (p1Stopping || p1Dialog is not null || !await ResolveUnsavedAsync()) return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(p1Lifetime.Token);
        var notice = Label(sourceError ?? "");
        notice.Visibility = string.IsNullOrWhiteSpace(notice.Text) ? Visibility.Collapsed : Visibility.Visible;
        notice.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) =>
        { var text = (TextBlock)sender; text.Visibility = string.IsNullOrWhiteSpace(text.Text) ? Visibility.Collapsed : Visibility.Visible; });
        AutomationProperties.SetAutomationId(notice, "LibrarySourcesStatus");
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        var rows = Panel(); var actions = new Views.TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        Button? scanButton = null, ignoreButton = null, importButton = null;
        bool import = false;
        ToggleSwitch SourceToggle(LibrarySource source, string header, Func<LibrarySource, bool> read, Func<bool, Task> save)
        {
            var toggle = new ToggleSwitch { Header = header, IsOn = read(source) };
            bool synchronizing = false;
            toggle.Toggled += async (_, _) =>
            {
                if (synchronizing) return;
                var requested = toggle.IsOn;
                toggle.IsEnabled = false;
                try { await RunP1Async(notice, () => save(requested), cancel.Token); }
                finally
                {
                    // A busy dialog can reject the operation, and persistence can fail.
                    // Always display the committed value without creating another write.
                    synchronizing = true;
                    try
                    {
                        var committed = sources.Sources.FirstOrDefault(s => s.Id == source.Id);
                        toggle.IsOn = committed is not null && read(committed);
                        toggle.IsEnabled = committed is not null && !p1Stopping && !cancel.IsCancellationRequested;
                    }
                    finally { synchronizing = false; }
                }
            };
            return toggle;
        }
        async Task Refresh()
        {
            rows.Children.Clear();
            if (sources.Sources.IsEmpty) rows.Children.Add(Label("还没有添加文件夹。添加后，新发现的游戏会在书架提醒你，由你选择是否导入。"));
            foreach (var source in sources.Sources)
            {
                var scan = sources.Scans.FirstOrDefault(x => x.SourceId == source.Id);
                var watch = SourceToggle(source, "发现新游戏时提醒我", s => s.Watch, async enabled =>
                { await sources.ConfigureAsync(source.Id, enabled, cancel.Token); sourceWatcher.Refresh(); await Refresh(); notice.Text = enabled ? "已开启自动发现。新游戏会先提醒，不会自动加入书架。" : "已暂停此文件夹的自动发现，仍可手动检查。"; });
                watch.OnContent = "开启"; watch.OffContent = "暂停";
                var card = Panel(Label(source.Kind == LibrarySourceKind.Steam ? "Steam 游戏库" : "游戏库文件夹", 16), Label(source.Root.StoredPath), Label(scan?.Message ?? "尚未检查，点击“检查新游戏”。"), watch);
                if (source.Kind == LibrarySourceKind.Steam)
                {
                    var showAll = SourceToggle(source, "显示类型未确认的 Steam 游戏", s => s.ShowUnconfirmedSteam, async enabled =>
                    { await sources.ConfigureDiscoveryAsync(source.Id, enabled, cancellation: cancel.Token); await Refresh(); notice.Text = enabled ? "已显示类型未确认的游戏，请在导入列表中挑选。" : "仅显示视觉小说目录已收录的作品。"; });
                    AutomationProperties.SetAutomationId(showAll, "ShowUnconfirmedSteam:" + source.Id);
                    card.Children.Add(showAll);
                    card.Children.Add(Label("默认只列出视觉小说目录已收录的游戏。找不到某款游戏时，可打开此选项。"));
                }
                if (!source.IgnoredCandidates.IsEmpty) card.Children.Add(ActionButton($"恢复已忽略游戏的提醒（{source.IgnoredCandidates.Length}）", () => RunP1Async(notice, async () =>
                { await sources.ConfigureDiscoveryAsync(source.Id, restoreIgnored: true, cancellation: cancel.Token); await Refresh(); notice.Text = "已恢复提醒，可重新选择要导入的游戏。"; }, cancel.Token), "RestoreIgnored:" + source.Id));
                card.Children.Add(ActionButton("从自动发现中移除此文件夹", async () => await RunP1Async(notice, async () => { await sources.ConfigureAsync(source.Id, null, cancel.Token); sourceWatcher.Refresh(); await Refresh(); notice.Text = "已停止检查此文件夹。书架中的游戏和磁盘文件均保留。"; }, cancel.Token)));
                rows.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SurfaceStyle"], Padding = new Thickness(16), Child = card });
            }
            if (scanButton is not null) scanButton.IsEnabled = !sources.Sources.IsEmpty;
            if (ignoreButton is not null)
            {
                ignoreButton.Visibility = sources.Candidates.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
                ignoreButton.IsEnabled = !sources.Candidates.IsEmpty;
            }
            if (importButton is not null)
            {
                importButton.Content = sources.Candidates.IsEmpty ? "暂无待导入游戏" : $"选择要导入的游戏（{sources.Candidates.Length}）";
                importButton.IsEnabled = !sources.Candidates.IsEmpty;
            }
            ShowSourceState(); await Task.CompletedTask;
        }
        async Task Add(LibrarySourceKind kind) => await RunP1Async(notice, async () =>
        {
            if (await PickFolder() is not { } path) { notice.Text = "已取消选择。"; return; }
            await sources.AddAsync(path, kind, cancel.Token); sourceWatcher.Refresh(); await sources.ScanAsync(true, cancel.Token, waitForTurn: true); sourceError = null; await Refresh(); notice.Text = sources.Candidates.IsEmpty ? "文件夹已添加，本次没有发现可导入的新游戏。" : $"文件夹已添加，发现 {sources.Candidates.Length} 个新游戏。请继续选择要导入的游戏。";
        }, cancel.Token);
        actions.Children.Add(ActionButton("添加游戏库文件夹", () => Add(LibrarySourceKind.Folder), "AddFolderSource"));
        actions.Children.Add(ActionButton("添加 Steam 库", () => Add(LibrarySourceKind.Steam), "AddSteamSource"));
        scanButton = ActionButton("检查新游戏", () => RunP1Async(notice, async () => { await sources.ScanAsync(true, cancel.Token, waitForTurn: true); sourceError = null; await Refresh(); notice.Text = sources.Candidates.IsEmpty ? "检查完成，暂无待导入的新游戏。" : $"发现 {sources.Candidates.Length} 个新游戏，请选择要加入书架的游戏。"; }, cancel.Token), "ScanLibrarySources", () => !sources.Sources.IsEmpty);
        actions.Children.Add(scanButton);
        ignoreButton = ActionButton("不再提醒这批游戏", () => RunP1Async(notice, async () =>
        { var pending = sources.Candidates; if (!pending.IsEmpty) await IgnoreDiscoveredAsync(pending); await Refresh(); notice.Text = "这批游戏将不再提醒。可在对应文件夹中恢复提醒。"; }, cancel.Token), "IgnoreAllSourceCandidates", () => !sources.Candidates.IsEmpty);
        importButton = ActionButton("选择要导入的游戏", () => { import = true; p1Dialog?.Hide(); return Task.CompletedTask; }, "ReviewSourceImport", () => !sources.Candidates.IsEmpty);
        importButton.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        var reviewActions = new Views.TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 }; reviewActions.Children.Add(importButton); reviewActions.Children.Add(ignoreButton);
        var dialog = Dialog("自动发现游戏", Panel(Label("添加常用游戏库，新游戏会先提醒，再由你选择加入书架。"), actions,
            Label("游戏库请选择存放游戏的文件夹；Steam 库请选择包含 steamapps 的文件夹。"), notice, reviewActions, rows)); p1Dialog = dialog;
        dialog.Closing += (_, _) => cancel.Cancel();
        try { await sources.InitializeAsync(cancel.Token); await Refresh(); await dialog.ShowAsync(); }
        catch (Exception) { sourceError = "库目录配置无法读取，原文件已保留。"; ShowLifecycleError(sourceError); }
        finally { p1Dialog = null; }
        if (import && !p1Stopping && !commandsBlocked) { files.ReviewDiscovered(sources.Candidates); await Navigate(WorkspacePage.Import); }
    }
    private async Task OpenStatisticsAsync(string? id)
    {
        if (p1Dialog is not null || extras.Snapshot is not { } snapshot) return;
        var choose = new ComboBox { Header = "作品", HorizontalAlignment = HorizontalAlignment.Stretch }; choose.Items.Add(new ComboBoxItem { Content = "全部作品" });
        foreach (var game in snapshot.Entries.OrderBy(e => e.Name)) choose.Items.Add(new ComboBoxItem { Content = game.Name, Tag = game.Id });
        choose.SelectedIndex = id is null ? 0 : choose.Items.Cast<ComboBoxItem>().ToList().FindIndex(x => (string?)x.Tag == id);
        var period = new ComboBox { Header = "统计区间", SelectedIndex = 0 }; period.Items.Add("最近 30 天 · 每日"); period.Items.Add("最近 12 周 · 每周");
        var result = Panel(); void Refresh()
        {
            result.Children.Clear(); var selected = (choose.SelectedItem as ComboBoxItem)?.Tag as string;
            DrawStatistics(result, snapshot.Entries.Where(x => selected is null || selected == x.Id), period.SelectedIndex == 1);
        }
        choose.SelectionChanged += (_, _) => Refresh(); period.SelectionChanged += (_, _) => Refresh(); Refresh();
        var dialog = Dialog("游玩统计", Panel(choose, period, result)); p1Dialog = dialog; try { await dialog.ShowAsync(); } finally { p1Dialog = null; }
    }
    private static void DrawStatistics(StackPanel target, IEnumerable<LibraryEntry> entries, bool weeks)
    {
        var report = PlaytimeStatistics.Build(entries, TimeZoneInfo.Local); var today = DateOnly.FromDateTime(DateTime.Today); int count = weeks ? 12 : 30;
        var start = weeks ? today.AddDays(-((int)today.DayOfWeek + 6) % 7).AddDays(-77) : today.AddDays(-29);
        var totals = Enumerable.Range(0, count).Select(i => (Date: start.AddDays(i * (weeks ? 7 : 1)), Seconds: report.Days.Where(x => x.Date >= start.AddDays(i * (weeks ? 7 : 1)) && x.Date < start.AddDays((i + 1) * (weeks ? 7 : 1))).Sum(x => x.Seconds))).ToArray();
        target.Children.Add(Label($"累计 {report.TotalSeconds / 3600d:0.##} 小时 · 当前区间 {totals.Sum(x => x.Seconds) / 3600d:0.##} 小时", 16));
        var chart = new Grid { Height = 165, ColumnSpacing = 3 }; var max = Math.Max(1, totals.Max(x => x.Seconds));
        for (int i = 0; i < totals.Length; i++)
        {
            chart.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); var bar = new Border { Height = Math.Max(1, totals[i].Seconds * 140d / max), VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 20), CornerRadius = new(2), Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"] };
            Grid.SetColumn(bar, i); var text = $"{totals[i].Date:MM-dd} · {totals[i].Seconds / 3600d:0.##} 小时"; AutomationProperties.SetName(bar, text); ToolTipService.SetToolTip(bar, text); chart.Children.Add(bar);
        }
        target.Children.Add(chart); target.Children.Add(Label($"{start:MM-dd} — {today:MM-dd}（本地时间）"));
        if (report.UnknownSeconds > 0) target.Children.Add(Label($"另有 {report.UnknownSeconds / 3600d:0.##} 小时缺少可归属日期，已计入累计。"));
        if (report.Days.Any(x => x.Estimated)) target.Children.Add(Label("跨日记录按各天经过时间分配；未保存暂停区间的旧记录为估算。"));
    }
    private async Task OpenGameToolsAsync(string id)
    {
        if (p1Stopping || p1Dialog is not null) return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(p1Lifetime.Token);
        var details = await extras.LoadAsync(id, cancel.Token); var notice = Label(""); var pivot = new Pivot();
        int visibleRecords = 20;
        var time = Panel(); var guides = Panel(); var metadata = Panel(); var layers = Panel(); var records = Panel(); var chart = Panel();
        pivot.Items.Add(new PivotItem { Header = "游玩记录", Content = time }); pivot.Items.Add(new PivotItem { Header = "本地攻略", Content = guides }); pivot.Items.Add(new PivotItem { Header = "补充资料", Content = metadata });
        async Task Commit(Func<LibraryExtrasDetails, CancellationToken, Task<ClientResult>> action)
        {
            var result = await ViewModel.ExecuteFileMutationAsync(async token => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancel.Token); return await action(details, linked.Token); }, id, preserveSelection: true); notice.Text = result.Message;
            if (result.IsCommitted) { details = await extras.LoadAsync(id, CancellationToken.None); RefreshTime(); RefreshGuides(); RefreshLayers(); }
        }
        var date = new CalendarDatePicker { Header = "日期", Date = DateTimeOffset.Now, MaxDate = DateTimeOffset.Now, MinDate = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var hours = new NumberBox { Header = "小时", Minimum = 0, Maximum = 8784, Value = 1, SmallChange = .5, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var mode = new ComboBox { Header = "方式", SelectedIndex = 0 }; mode.Items.Add("补记时长"); mode.Items.Add("将当天时长修正为"); var note = new TextBox { Header = "备注（可选）", MaxLength = 2000 };
        void RefreshTime()
        {
            chart.Children.Clear(); DrawStatistics(chart, [details.Entry], false); records.Children.Clear();
            foreach (var row in details.Entry.ManualPlaytime.Reverse().Take(visibleRecords))
            {
                var line = Label($"{row.Date} · {row.Seconds / 3600d:+0.##;-0.##;0} 小时 · {row.Note}" + (row.Undone ? "（已撤回）" : ""));
                var panel = Panel(line); if (!row.Undone) panel.Children.Add(ActionButton("撤回这条修正", () => RunP1Async(notice, () => Commit((d, token) => extras.UndoAsync(d, row.Id, token)), cancel.Token), "UndoManual:" + row.Id)); records.Children.Add(panel);
            }
            if (visibleRecords < details.Entry.ManualPlaytime.Length)
                records.Children.Add(ActionButton("显示更多记录", () => { visibleRecords += 20; RefreshTime(); return Task.CompletedTask; }));
        }
        time.Children.Add(chart); time.Children.Add(date); time.Children.Add(mode); time.Children.Add(hours); time.Children.Add(note);
        time.Children.Add(ActionButton("保存时长", () => RunP1Async(notice, async () =>
        { if (date.Date is not { } selected || !double.IsFinite(hours.Value)) throw new InvalidDataException("请选择日期和有效时长。"); await Commit((d, token) => extras.TimeAsync(d, DateOnly.FromDateTime(selected.DateTime), checked((long)Math.Round(hours.Value * 3600)), note.Text, mode.SelectedIndex == 1, token)); }, cancel.Token), "SaveManualPlaytime"));
        time.Children.Add(records); RefreshTime();
        void RefreshGuides()
        {
            guides.Children.Clear(); guides.Children.Add(Label("添加已有攻略文件，方便随时打开。这里只保存文件位置，不复制或删除原文件。")); guides.Children.Add(ActionButton("添加攻略文件", () => RunP1Async(notice, async () =>
            { if (await PickFile(LocalDocuments.Extensions.ToArray()) is { } path) { await resources.ValidateGuideAsync(path, cancel.Token); await Commit((d, token) => extras.GuideAsync(d, path, token)); } else notice.Text = "已取消选择。"; }, cancel.Token), "AddLocalGuide"));
            foreach (var path in details.Entry.GuidePaths)
            {
                var actions = new Views.TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
                actions.Children.Add(ActionButton("打开攻略", () => RunP1Async(notice, async () => { await resources.OpenGuideAsync(path); notice.Text = "已交给系统默认程序打开。"; }, cancel.Token)));
                actions.Children.Add(ActionButton("更换攻略文件", () => RunP1Async(notice, async () => { if (await PickFile(LocalDocuments.Extensions.ToArray()) is { } replacement) { await resources.ValidateGuideAsync(replacement, cancel.Token); await Commit((d, token) => extras.ReplaceGuideAsync(d, path, replacement, token)); } else notice.Text = "已取消，攻略文件未更换。"; }, cancel.Token)));
                actions.Children.Add(ActionButton("移除攻略链接", () => RunP1Async(notice, () => Commit((d, token) => extras.GuideAsync(d, null, token, path)), cancel.Token)));
                guides.Children.Add(Panel(Label(path), actions));
            }
        }
        RefreshGuides();
        var provider = new ComboBox { Header = "资料源", SelectedIndex = 0 }; provider.Items.Add("Bangumi"); provider.Items.Add("VNDB"); var identifier = new TextBox { Header = "作品编号或作品链接", MaxLength = 500 };
        var fill = new CheckBox { Content = "仅补全缺失字段", IsChecked = true }; var fields = Panel(); var previewText = Label(""); WorkMetadata? preview = null; string previewProvider = "", previewId = "";
        var apply = ActionButton("确认应用所选字段", () => RunP1Async(notice, async () =>
        { if (preview is null || previewProvider != (provider.SelectedIndex == 0 ? "bangumi" : "vndb") || previewId != DirectMetadataIdentity.Normalize(previewProvider, identifier.Text)) throw new InvalidDataException("编号或资料源已变化，请重新读取预览。"); var selected = fields.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (string)x.Tag).ToImmutableArray(); if (selected.IsEmpty) throw new InvalidDataException("请选择要应用的字段。"); await Commit((d, token) => metadataSources.ApplyAsync(d, previewProvider, previewId, preview, selected, fill.IsChecked == true, token)); }, cancel.Token), "ApplyMetadataSource"); apply.IsEnabled = false;
        void RefreshLayers()
        {
            layers.Children.Clear(); foreach (var layer in details.Entry.MetadataMix?.Layers ?? [])
            {
                layers.Children.Add(Panel(Label($"{layer.Source} · {layer.SourceId} · {string.Join("、", layer.Fields.Select(FieldName))}"),
                    ActionButton("撤回此资料源", () => RunP1Async(notice, () => Commit((d, token) => metadataSources.RemoveAsync(d, layer.Source, token)), cancel.Token), "RemoveSource:" + layer.Source)));
            }
        }
        var read = ActionButton("读取预览", () => RunP1Async(notice, async () =>
        {
            apply.IsEnabled = false; preview = null; fields.Children.Clear(); previewText.Text = "";
            var source = provider.SelectedIndex == 0 ? "bangumi" : "vndb"; var number = DirectMetadataIdentity.Normalize(source, identifier.Text);
            var fetched = await metadataSources.PreviewAsync(source, number, browsingPolicy.Capture().ShowNsfw, cancel.Token);
            if (cancel.IsCancellationRequested || source != (provider.SelectedIndex == 0 ? "bangumi" : "vndb") || number != DirectMetadataIdentity.Normalize(source, identifier.Text)) return;
            preview = fetched; previewProvider = source; previewId = number;
            previewText.Text = $"{fetched.Title}\n{fetched.OriginalTitle}\n发行 {fetched.ReleaseDate ?? "未知"} · {string.Join(" / ", fetched.Developers)}\n\n{fetched.Description[..Math.Min(1000, fetched.Description.Length)]}";
            foreach (var field in LibraryExtras.MetadataFields) fields.Children.Add(new CheckBox { Content = FieldName(field), Tag = field, IsChecked = field != "cover" && field != "characters" });
            apply.IsEnabled = true; notice.Text = "请核对作品、章节与版本后应用。后应用的替换字段优先，可逐个撤回资料源。";
        }, cancel.Token), "PreviewMetadataSource");
        metadata.Children.Add(Label("日常识别使用 KUN 聚合资料即可。这里用于资料缺项、更新滞后或需要人工更正时的备用补充。")); metadata.Children.Add(provider); metadata.Children.Add(identifier); metadata.Children.Add(read); metadata.Children.Add(previewText); metadata.Children.Add(fields); metadata.Children.Add(fill); metadata.Children.Add(apply); metadata.Children.Add(layers); RefreshLayers();
        var dialog = Dialog(details.Entry.Name, Panel(pivot, notice)); p1Dialog = dialog; dialog.Closing += (_, _) => cancel.Cancel();
        try { await dialog.ShowAsync(); } finally { p1Dialog = null; }
    }
    private static string FieldName(string field) => field switch { "description" => "简介", "releaseDate" => "发行日期", "developers" => "制作公司", "tags" => "标签", "ratings" => "来源评分", "characters" => "角色", "cover" => "封面", _ => field };
}
