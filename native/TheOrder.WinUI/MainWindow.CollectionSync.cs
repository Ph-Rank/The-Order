using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using TheOrder.Application;
using TheOrder.Domain;
using TheOrder.Presentation;
using TheOrder.WinUI.Views;

namespace TheOrder.WinUI;

public sealed partial class MainWindow
{
    private sealed record CollectionTargets(VndbAccount? Vndb, BangumiAccessSnapshot Bangumi, long? NextMoeGeneration)
    {
        public bool Includes(CollectionSite site) => site switch
        {
            CollectionSite.Vndb => Vndb is not null,
            CollectionSite.Bangumi => Bangumi.Authorized,
            CollectionSite.NextMoe => NextMoeGeneration.HasValue,
            _ => false
        };
    }

    private sealed record CollectionWrite(string EntryId, string Name, CollectionUpdate Update, CollectionFields Fields = CollectionFields.All)
    {
        public CollectionProblem Problem(string reason) => new(EntryId, Name, reason, Fields);
    }
    private sealed record CollectionProblem(string EntryId, string Name, string Reason, CollectionFields Fields,
        string? OldId = null, string? SuggestedId = null, bool CatalogAssociation = false);
    private sealed record CollectionWriteBatch(CollectionSite Site, CollectionWrite[] Writes,
        CollectionProblem[] MissingAssociations, CollectionProblem[] MissingFields, string? Unavailable = null);
    private readonly CollectionBindingClient collectionBinding;
    private readonly Func<Task> openNextMoeSyncIssues;
    private Task collectionSyncOperation = Task.CompletedTask;
    private Task collectionSyncPreview = Task.CompletedTask;
    private CancellationTokenSource? collectionSyncCancellation;
    private ContentDialog? collectionSyncDialog;

    private CollectionTargets CaptureCollectionTargets()
    {
        var generation = nextMoeSettings.Access.AuthContext.Generation;
        return new(settingsPage.Vndb.ConnectedAccount, bangumiSettings.Access.Capture(),
            nextMoeSettings.Status is { Mode: NextMoeAccessMode.Account, NeedsLogin: false } ? generation : null);
    }

    private CancellationToken CollectionSyncToken()
    {
        if (collectionSyncCancellation is null || collectionSyncCancellation.IsCancellationRequested)
        {
            collectionSyncCancellation?.Dispose();
            collectionSyncCancellation = CancellationTokenSource.CreateLinkedTokenSource(p1Lifetime.Token);
        }
        return collectionSyncCancellation.Token;
    }

    private void CancelCollectionSync()
    {
        collectionSyncCancellation?.Cancel();
        collectionSyncDialog?.Hide();
    }

    private Task StartCollectionSync(OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter,
        INextMoeCollectionWriter nextMoeWriter) => collectionSyncPreview.IsCompleted
        ? collectionSyncPreview = ShowCollectionSyncAsync(library, bangumiWriter, nextMoeWriter)
        : collectionSyncPreview;

    private async Task WriteCollectionEditsAsync(OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter,
        INextMoeCollectionWriter nextMoeWriter, ImmutableArray<CollectionEdit> edits, bool requested)
    {
        if (commandsBlocked || p1Stopping || requested && !ViewModel.CanEdit) return;
        try
        {
            if (edits.IsDefaultOrEmpty)
            {
                if (requested) statusPage.Post(new("写入状态与评分", "书架中暂无作品。"));
                return;
            }

            var targets = CaptureCollectionTargets();
            var batches = Enum.GetValues<CollectionSite>().Select(site => PrepareCollectionBatch(site, targets, edits)).ToArray();
            await QueueCollectionWrites(library, bangumiWriter, nextMoeWriter, targets, batches);
        }
        catch (Exception)
        {
            if (!p1Stopping)
                statusPage.Post(new("写入状态与评分", "网站写入未完成，可在设置中重新写入。", InfoBarSeverity.Error));
        }
    }

    private CollectionWriteBatch PrepareCollectionBatch(CollectionSite site, CollectionTargets targets, ImmutableArray<CollectionEdit> edits)
    {
        if (!targets.Includes(site))
        {
            var name = CollectionUpdate.SiteName(site);
            var reconnect = site switch
            {
                CollectionSite.Vndb => settingsPage.Vndb.Connected,
                CollectionSite.Bangumi => bangumiSettings.HasAuthorization,
                _ => nextMoeSettings.HasSavedAccount
            };
            var message = reconnect ? $"请重新连接 {name} 账号。" : $"请先连接 {name} 账号。";
            return new(site, [], [], [], "未写入作品。" + message);
        }

        var writes = new List<CollectionWrite>();
        var missingAssociations = new List<CollectionProblem>();
        var missingFields = new List<CollectionProblem>();
        foreach (var edit in edits)
        {
            var entry = edit.Entry;
            if (CollectionUpdate.From(entry, site, edit.Fields) is { } update)
                writes.Add(new(entry.Id, entry.Name, update, edit.Fields));
            else if (CollectionUpdate.WorkIdFor(entry, site) is null)
                missingAssociations.Add(new(entry.Id, entry.Name, "缺少可确认的作品关联。", edit.Fields));
            else
                missingFields.Add(new(entry.Id, entry.Name, "本次修改没有可写入的游玩状态或评分。", edit.Fields));
        }
        return new(site, [.. writes], [.. missingAssociations], [.. missingFields]);
    }

    private Task QueueCollectionWrites(OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter,
        INextMoeCollectionWriter nextMoeWriter, CollectionTargets targets, CollectionWriteBatch[] batches)
        => collectionSyncOperation = WriteCollectionAfterAsync(collectionSyncOperation, library, bangumiWriter, nextMoeWriter,
            targets, batches, CollectionSyncToken());

    private async Task WriteCollectionAfterAsync(Task previous, OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter,
        INextMoeCollectionWriter nextMoeWriter, CollectionTargets targets, CollectionWriteBatch[] batches, CancellationToken queued)
    {
        await previous;
        if (queued.IsCancellationRequested || p1Stopping) return;
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(queued);
        var cancellation = scope.Token;
        var cancel = new StatusAction("取消写入", () => { scope.Cancel(); return Task.CompletedTask; }, "CancelCollectionWrite");
        int processed = 0, completed = 0;
        var total = batches.Sum(batch => batch.Writes.Length);
        try
        {
            if (total > 0) ShowProgress(processed);
            foreach (var batch in batches)
            {
                cancellation.ThrowIfCancellationRequested();
                var site = batch.Site;
                var title = CollectionUpdate.SiteName(site) + "同步";
                if (batch.Unavailable is { } unavailable)
                {
                    statusPage.Post(new(title, unavailable));
                    continue;
                }
                var groupSize = batch.Writes.Length;
                List<CollectionProblem> problems = [.. batch.MissingAssociations, .. batch.MissingFields];
                var nextMoeAccess = site == CollectionSite.NextMoe && groupSize > 0
                    ? await nextMoeSettings.Access.AuthContext.CaptureAsync(cancellation) : default;
                int success = 0, failed = 0, skipped = 0, repaired = 0;
                string? issue = null;
                LibrarySnapshot? checkedSnapshot = null;
                var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
                var duplicates = new HashSet<string>(StringComparer.Ordinal);
                for (int position = 0; position < groupSize; position++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (position > 0 && position % 5 == 0) ShowProgress(processed + position);
                    var write = batch.Writes[position];
                    if (site == CollectionSite.NextMoe && (nextMoeAccess.Generation != targets.NextMoeGeneration
                        || nextMoeAccess.Changed.IsCancellationRequested
                        || nextMoeSettings.Status is not { Mode: NextMoeAccessMode.Account, NeedsLogin: false }))
                    {
                        const string reason = "NextMoe 授权已变化或失效。";
                        issue ??= reason;
                        RecordUnsent(position, reason);
                        break;
                    }

                    var snapshot = library.CurrentSnapshot;
                    if (snapshot is not null && !ReferenceEquals(checkedSnapshot, snapshot))
                    {
                        checkedSnapshot = snapshot;
                        entries = snapshot.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
                        duplicates = snapshot.Entries.Select(entry => CollectionUpdate.WorkIdFor(entry, site)).OfType<string>()
                            .GroupBy(id => id, StringComparer.Ordinal).Where(set => set.Count() > 1).Select(set => set.Key).ToHashSet(StringComparer.Ordinal);
                    }
                    if (duplicates.Contains(write.Update.WorkId))
                    {
                        skipped++;
                        problems.Add(write.Problem($"多部书架作品关联到同一个编号（{write.Update.WorkId}）。"));
                        continue;
                    }
                    if (snapshot is null || !entries.TryGetValue(write.EntryId, out var current)
                        || write.Update.RetainCurrentFields(current) is not { } update)
                    {
                        skipped++;
                        problems.Add(write.Problem("作品、关联或待写入的字段在同步前已变更。"));
                        continue;
                    }

                    try
                    {
                        if (site == CollectionSite.Vndb)
                            await settingsPage.Vndb.WriteAsync(update, targets.Vndb!, cancellation);
                        else if (site == CollectionSite.Bangumi)
                            await bangumiWriter.WriteAsync(update, targets.Bangumi, cancellation);
                        else
                        {
                            var result = await nextMoeWriter.WriteAsync(update, nextMoeAccess, cancellation);
                            var oldId = update.WorkId;
                            var updateCatalog = current.Metadata?.Id == oldId;
                            if (result.MergedInto is { } merged)
                            {
                                var repair = await nextMoeSettings.RepairCollectionMergeAsync(write.EntryId, oldId, merged, nextMoeAccess, cancellation);
                                if (repair.UpdatedId is { } newId)
                                {
                                    repaired++;
                                    oldId = newId;
                                    var latest = library.CurrentSnapshot?.Entries.FirstOrDefault(entry => entry.Id == write.EntryId);
                                    if (latest is not null && (update with { WorkId = newId }).RetainCurrentFields(latest) is { } retry)
                                        result = await nextMoeWriter.WriteAsync(retry, nextMoeAccess, cancellation);
                                    else
                                        result = new(false, false, "待写入的字段已变化，请核对最新资料。");
                                }
                                else
                                    result = result with { Error = repair.Error ?? result.Error, MergedInto = repair.SuggestedId };
                            }
                            if (result.Error is { } error)
                            {
                                failed++;
                                var reason = (result.StateSynced ? "游玩状态已写入，评分待确认。 " : "") + error;
                                issue ??= reason;
                                problems.Add(write.Problem(reason) with
                                {
                                    OldId = oldId,
                                    SuggestedId = result.MergedInto,
                                    CatalogAssociation = updateCatalog && result.MergedInto is not null
                                });
                                continue;
                            }
                        }
                        success++;
                        completed++;
                    }
                    catch (CollectionSyncException error)
                    {
                        issue ??= error.Message;
                        failed++;
                        problems.Add(write.Problem(error.Message));
                        if (error.Failure is CollectionSyncFailure.Changed or CollectionSyncFailure.Unauthorized
                            or CollectionSyncFailure.MissingPermission or CollectionSyncFailure.RateLimited
                            or CollectionSyncFailure.Offline or CollectionSyncFailure.Timeout or CollectionSyncFailure.UnsupportedMediaType)
                        {
                            RecordUnsent(position + 1, error.Message);
                            break;
                        }
                    }
                    finally { if (site == CollectionSite.Vndb) settingsPage.Vndb.RefreshState(); }
                }
                processed += groupSize;
                if (groupSize > 0) ShowProgress(processed);
                var message = groupSize > 0 ? $"已写入 {success} 部作品。" : "未写入作品。";
                if (repaired > 0) message += $" 已更新 {repaired} 部作品的 NextMoe 关联。";
                if (failed > 0) message += $" {failed} 部未完成。{issue}";
                if (skipped > 0) message += $" 跳过 {skipped} 部关联不唯一或资料已变化的作品。";
                if (batch.MissingAssociations.Length > 0) message += $" {batch.MissingAssociations.Length} 部缺少可确认的作品关联。";
                if (batch.MissingFields.Length > 0) message += $" {batch.MissingFields.Length} 部没有可写入的状态或评分。";
                var needsReview = skipped > 0 || batch.MissingAssociations.Length > 0;
                var severity = failed > 0 ? InfoBarSeverity.Error : needsReview ? InfoBarSeverity.Warning
                    : success > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
                statusPage.Post(new(title, message, severity, problems.Count > 0
                    ? new("查看未完成作品", () => OpenCollectionProblems(site, problems, library, bangumiWriter, nextMoeWriter), $"CollectionIssues{site}") : null,
                    RequiresAction: needsReview));

                void RecordUnsent(int first, string reason)
                {
                    for (int position = first; position < groupSize; position++)
                    {
                        var write = batch.Writes[position];
                        problems.Add(write.Problem("本轮尚未提交。" + reason));
                        failed++;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!queued.IsCancellationRequested && !p1Stopping)
                statusPage.Post(new("写入状态与评分", $"已停止写入，已完成 {completed} 项。", InfoBarSeverity.Informational));
        }
        catch (Exception)
        {
            if (!p1Stopping)
                statusPage.Post(new("写入状态与评分", "网站写入未完成，可在设置中重新写入。", InfoBarSeverity.Error));
        }
        finally { statusPage.ClearTask("collection-sync"); }

        void ShowProgress(int count) => statusPage.SetTask("collection-sync", "写入状态与评分",
            $"已处理 {count} / {total} 项", 100d * count / total, cancel);
    }

    private async Task<bool> ConfirmCatalogRedirectAsync(LibraryEntry entry, WorkMetadata canonical, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (p1Stopping || Root.XamlRoot is not { } root || ViewModel.SelectedId != entry.Id) return false;
        var preview = Panel(Label(entry.Name), Label($"NextMoe 已将 #{entry.Metadata!.Id} 合并到 #{canonical.Id} · {canonical.Title}"),
            Label("更新在线关联和目录资料，保留书架名称、个人封面、安装与游玩记录。"));
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = Root.ActualTheme,
            Title = "确认更新 NextMoe 关联",
            Content = preview,
            PrimaryButtonText = "更新关联",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        using var cancel = cancellation.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        return await dialog.ShowInShellAsync() == ContentDialogResult.Primary;
    }

    private Task OpenCollectionProblems(CollectionSite site, IReadOnlyList<CollectionProblem> problems,
        OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter, INextMoeCollectionWriter nextMoeWriter)
        => collectionSyncPreview.IsCompleted
            ? collectionSyncPreview = ShowCollectionProblemsAsync(site, problems, library, bangumiWriter, nextMoeWriter)
            : collectionSyncPreview;

    private async Task ShowCollectionProblemsAsync(CollectionSite site, IReadOnlyList<CollectionProblem> problems,
        OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter, INextMoeCollectionWriter nextMoeWriter)
    {
        if (problems.Count == 0 || commandsBlocked || p1Stopping || collectionSyncDialog is not null || Root.XamlRoot is not { } root) return;
        var siteName = CollectionUpdate.SiteName(site);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(CollectionSyncToken());
        var cancellation = scope.Token;
        var list = new ListView
        {
            MaxHeight = Math.Clamp(root.Size.Height / 3, 72, 260),
            SelectionMode = ListViewSelectionMode.Single
        };
        AutomationProperties.SetName(list, "未完成作品");
        AutomationProperties.SetAutomationId(list, "CollectionProblemList");
        foreach (var problem in problems)
        {
            var row = Label($"{problem.Name}\n{problem.Reason}");
            row.Tag = problem;
            list.Items.Add(row);
        }

        var identifier = new TextBox
        {
            Header = $"{siteName} 作品编号或链接",
            PlaceholderText = site == CollectionSite.Vndb ? "例如 v1234" : "例如 1234",
            MaxLength = 2048,
            MinWidth = 0
        };
        AutomationProperties.SetName(identifier, $"{siteName} 作品编号或链接");
        AutomationProperties.SetAutomationId(identifier, "CollectionBindingIdentifier");
        var read = new Button { Content = "读取预览", VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetAutomationId(read, "CollectionBindingRead");
        var fields = new Grid { ColumnSpacing = 8 };
        fields.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(read, 1);
        fields.Children.Add(identifier);
        fields.Children.Add(read);
        var notice = Label("");
        AutomationProperties.SetAutomationId(notice, "CollectionBindingNotice");
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        var scroll = new ScrollViewer
        {
            Content = Panel(list, fields, notice),
            MaxHeight = Math.Clamp(root.Size.Height - 200, 148, 500),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = Root.ActualTheme,
            Title = $"{siteName}同步 · 未完成作品",
            Content = scroll,
            PrimaryButtonText = "确认绑定",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.None,
            IsPrimaryButtonEnabled = false
        };
        AutomationProperties.SetAutomationId(dialog, "CollectionBindingDialog");
        CollectionBindingPreview? preview = null;
        CancellationTokenSource? lookup = null;
        CollectionUpdate? retry = null;
        CollectionTargets? targets = null;
        var saving = false;

        void UpdateButtons()
        {
            var selected = list.SelectedItem is TextBlock { Tag: CollectionProblem };
            identifier.IsEnabled = selected && !saving;
            read.IsEnabled = identifier.IsEnabled && lookup is null && !string.IsNullOrWhiteSpace(identifier.Text);
            dialog.IsPrimaryButtonEnabled = selected && preview is not null && !saving;
            dialog.PrimaryButtonText = retry is not null ? "绑定并重试"
                : list.SelectedItem is TextBlock { Tag: CollectionProblem { CatalogAssociation: true } } ? "更新关联" : "确认绑定";
        }

        void Reset()
        {
            lookup?.Cancel();
            lookup = null;
            preview = null;
            retry = null;
            targets = null;
            notice.Text = "";
            UpdateButtons();
        }

        list.SelectionChanged += (_, _) =>
        {
            var problem = (list.SelectedItem as TextBlock)?.Tag as CollectionProblem;
            var entry = library.CurrentSnapshot?.Entries.FirstOrDefault(item => item.Id == problem?.EntryId);
            identifier.Text = entry is null ? "" : problem?.SuggestedId ?? (problem?.CatalogAssociation == true
                ? entry.Metadata?.Id : CollectionUpdate.WorkIdFor(entry, site)) ?? "";
            Reset();
            if (problem is not null && entry is null) notice.Text = "这部作品已从书架移除。";
        };
        identifier.TextChanged += (_, _) => Reset();
        read.Click += async (_, _) => await ReadPreviewAsync();
        identifier.KeyDown += async (_, args) =>
        {
            if (args.Key != VirtualKey.Enter) return;
            args.Handled = true;
            await ReadPreviewAsync();
        };

        async Task ReadPreviewAsync()
        {
            if (!read.IsEnabled || list.SelectedItem is not TextBlock { Tag: CollectionProblem problem }) return;
            Reset();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            lookup = request;
            UpdateButtons();
            notice.Text = "正在读取作品…";
            try
            {
                var found = await collectionBinding.ReadAsync(problem.EntryId, site, identifier.Text,
                    browsingPolicy.Capture().ShowNsfw, request.Token);
                if (request.IsCancellationRequested || lookup != request) return;
                preview = found;
                targets = CaptureCollectionTargets();
                retry = targets.Includes(site) ? found.UpdateFor(problem.Fields) : null;
                var date = found.ReleaseDate is { Length: > 0 } released ? $" · {released}" : "";
                notice.Text = $"{found.Title}（{found.WorkId}）{date}";
                if (retry is not null)
                {
                    var record = retry.Status is null ? "评分" : retry.Rating is null ? "游玩状态" : "游玩状态与评分";
                    notice.Text += $"\n将重试写入{record}。";
                    if (site == CollectionSite.NextMoe && retry.Rating.HasValue) notice.Text += "NextMoe 评分会公开显示。";
                }
            }
            catch (Exception error)
            {
                if (lookup == request && !request.IsCancellationRequested)
                    notice.Text = error is InvalidDataException or OnlineException or BangumiRelationsException
                        ? error.Message : "读取作品失败，请重新读取预览。";
            }
            finally
            {
                if (lookup == request) { lookup = null; UpdateButtons(); }
            }
        }

        dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            args.Cancel = true;
            if (saving || commandsBlocked || cancellation.IsCancellationRequested || preview is not { } selected
                || list.SelectedItem is not TextBlock { Tag: CollectionProblem problem } row) return;
            if (!ViewModel.CanEdit || ViewModel.IsDirty)
            {
                notice.Text = "当前无法保存，请等待书架操作完成后重试。";
                return;
            }

            var deferral = args.GetDeferral();
            var update = retry;
            var target = targets;
            saving = true;
            list.IsEnabled = false;
            UpdateButtons();
            try
            {
                var result = await ViewModel.ExecuteFileMutationAsync(async token =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
                    if (problem.CatalogAssociation && problem.OldId is { } oldId && problem.SuggestedId == selected.WorkId
                        && target?.NextMoeGeneration is { } generation)
                    {
                        var fence = await nextMoeSettings.Access.AuthContext.CaptureAsync(linked.Token);
                        if (fence.Generation != generation) return new(ClientOutcome.Conflict, "NextMoe 授权已变化，请重新确认。");
                        return await nextMoeSettings.ConfirmCatalogMergeAsync(selected, oldId, fence, linked.Token);
                    }
                    return await collectionBinding.BindAsync(selected, linked.Token, problem.CatalogAssociation);
                }, preserveSelection: true);
                if (!result.IsCommitted)
                {
                    if (result.Outcome == ClientOutcome.Conflict) Reset();
                    notice.Text = result.Outcome == ClientOutcome.Conflict ? "作品关联已变化，请重新读取预览。" : result.Message;
                    return;
                }

                if (!p1Stopping && !cancellation.IsCancellationRequested)
                {
                    if (update is not null && target is not null)
                        _ = QueueCollectionWrites(library, bangumiWriter, nextMoeWriter, target,
                            [new(site, [new(problem.EntryId, problem.Name, update, problem.Fields)], [], [])]);
                    else if (problem.CatalogAssociation)
                        _ = nextMoeSettings.AutomaticAsync(force: true);
                    else
                        statusPage.Post(new($"{siteName}同步", $"已保存《{problem.Name}》的同步编号 {selected.WorkId}。", InfoBarSeverity.Success));
                }

                var index = list.SelectedIndex;
                list.Items.Remove(row);
                if (list.Items.Count == 0) args.Cancel = false;
                else list.SelectedIndex = Math.Min(index, list.Items.Count - 1);
            }
            catch (Exception)
            {
                notice.Text = "保存同步编号时发生错误，请核对后重试。";
            }
            finally
            {
                saving = false;
                list.IsEnabled = true;
                UpdateButtons();
                deferral.Complete();
            }
        };

        list.SelectedIndex = 0;
        dialog.Opened += (_, _) => { identifier.Focus(FocusState.Programmatic); identifier.SelectAll(); };
        collectionSyncDialog = dialog;
        try { await dialog.ShowInShellAsync(); }
        finally
        {
            scope.Cancel();
            Reset();
            collectionSyncDialog = null;
        }
    }

    private async Task ShowCollectionSyncAsync(OfflineLibraryClient library, IBangumiCollectionWriter bangumiWriter,
        INextMoeCollectionWriter nextMoeWriter)
    {
        if (commandsBlocked || p1Stopping || ViewModel.SelectedId is not { } id) return;
        var cancellation = CollectionSyncToken();
        try
        {
            if (!await ResolveUnsavedAsync() || ViewModel.SelectedId != id
                || library.CurrentSnapshot?.Entries.FirstOrDefault(entry => entry.Id == id) is not { } entry) return;
            await bangumiSettings.RefreshIfDueAsync();
            cancellation.ThrowIfCancellationRequested();
            if (ViewModel.SelectedId != id || ViewModel.Page != WorkspacePage.Details) return;
            var vn = CollectionUpdate.From(entry, CollectionSite.Vndb);
            var bgm = CollectionUpdate.From(entry, CollectionSite.Bangumi);
            var next = CollectionUpdate.From(entry, CollectionSite.NextMoe);
            var targets = CaptureCollectionTargets();
            var options = new List<(CollectionUpdate Update, CheckBox Check)>();
            var panel = new StackPanel { Spacing = 8 };
            if (vn is null && bgm is null && next is null && entry.Status == GameProgress.Default && entry.PersonalRating is null or 0)
                panel.Children.Add(new TextBlock { Text = "请先设置游玩状态或 1 至 10 分的评分。", TextWrapping = TextWrapping.Wrap });
            else
            {
                panel.Children.Add(new TextBlock { Text = $"将《{entry.Name}》的本机记录写入勾选的网站。", TextWrapping = TextWrapping.Wrap });
                AddSite(vn, CollectionSite.Vndb, "VndbCollectionSync");
                AddSite(bgm, CollectionSite.Bangumi, "BangumiCollectionSync");
                AddSite(next, CollectionSite.NextMoe, "NextMoeCollectionSync");
                if (targets.Includes(CollectionSite.NextMoe) && next?.Rating is not null)
                    panel.Children.Add(new TextBlock { Text = "同步的 NextMoe 评分会公开显示。", TextWrapping = TextWrapping.Wrap });
            }
            var dialog = new ContentDialog
            {
                Title = "同步状态与评分",
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.ActualTheme,
                Content = panel,
                CloseButtonText = "关闭",
                PrimaryButtonText = options.Count == 0 ? "" : "确认同步",
                SecondaryButtonText = vn is not null && !targets.Includes(CollectionSite.Vndb) || bgm is not null && !targets.Includes(CollectionSite.Bangumi)
                    || next is not null && !targets.Includes(CollectionSite.NextMoe) ? "账号设置" : "",
                DefaultButton = ContentDialogButton.Close
            };
            AutomationProperties.SetAutomationId(dialog, "SyncWorkCollection");
            foreach (var option in options)
            {
                option.Check.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
                option.Check.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = options.Any(item => item.Check.IsChecked == true);
            }
            cancellation.ThrowIfCancellationRequested();
            collectionSyncDialog = dialog;
            var answer = await dialog.ShowInShellAsync();
            collectionSyncDialog = null;
            cancellation.ThrowIfCancellationRequested();
            if (answer == ContentDialogResult.Secondary)
            {
                await Navigate(WorkspacePage.Settings);
                if (ViewModel.Page == WorkspacePage.Settings)
                {
                    if (vn is not null && !targets.Includes(CollectionSite.Vndb)) settingsPage.RevealVndb();
                    else if (bgm is not null && !targets.Includes(CollectionSite.Bangumi)) settingsPage.RevealBangumi();
                    else settingsPage.RevealSources();
                }
                return;
            }
            if (answer != ContentDialogResult.Primary || p1Stopping || commandsBlocked) return;
            var batches = options.Where(option => option.Check.IsChecked == true)
                .Select(option => new CollectionWriteBatch(option.Update.Site, [new(id, entry.Name, option.Update)], [], [])).ToArray();
            if (batches.Length > 0)
                await QueueCollectionWrites(library, bangumiWriter, nextMoeWriter, targets, batches);

            void AddSite(CollectionUpdate? update, CollectionSite site, string automationId)
            {
                var name = CollectionUpdate.SiteName(site);
                var authorized = targets.Includes(site);
                if (!authorized || update is null)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = !authorized ? name + " · 请先连接账号" : name + " · 缺少可同步的作品关联或记录",
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }
                var check = new CheckBox
                {
                    Content = new TextBlock { Text = update.Summary(entry.Status, entry.PersonalRating), TextWrapping = TextWrapping.Wrap },
                    IsChecked = true
                };
                AutomationProperties.SetAutomationId(check, automationId);
                panel.Children.Add(check);
                options.Add((update, check));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { statusPage.Post(new("同步状态与评分", "无法打开同步，请稍后重试。", InfoBarSeverity.Error)); }
        finally { collectionSyncDialog = null; }
    }

    private async Task ImportNextMoeRatingsAsync()
    {
        if (commandsBlocked || p1Stopping || !nextMoeSettings.CanImportRatings) return;
        statusPage.SetTask("nextmoe-ratings", "读入 NextMoe 评分", "正在读取评分。",
            cancel: new("取消读入", () => { nextMoeSettings.Cancel(); return Task.CompletedTask; }, "CancelNextMoeRatingImport"));
        try
        {
            if (await nextMoeSettings.ImportRatingsAsync() is { } count)
                statusPage.Post(new("NextMoe", $"已读入 {count} 部书架作品的评分。", InfoBarSeverity.Success));
        }
        finally { statusPage.ClearTask("nextmoe-ratings"); }
    }
}
