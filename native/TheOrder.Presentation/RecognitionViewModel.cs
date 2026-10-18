using System.Collections.Immutable;
using TheOrder.Application;
using TheOrder.Domain;

namespace TheOrder.Presentation;

public sealed record RecognitionReviewCard(Guid Id, string GameId, string Title, string Status, string Summary);
public sealed record RecognitionCandidateCard(string Id, string Title, string Summary, string Evidence, string Conflicts, bool CanApply);

public sealed partial class RecognitionViewModel : ObservableState, IAsyncDisposable
{
    private readonly OfflineLibraryClient offline;
    private readonly RecognitionQueue queue;
    private readonly RecognitionReviewService reviews;
    private readonly IRecognitionCatalog catalog;
    private readonly IRemoteCoverRegistry covers;
    private readonly OnlineAuthContext auth;
    private readonly WorkspaceViewModel workspace;
    private readonly IMediaIdentityDecisions identityDecisions;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Task> pending = [];
    private CancellationTokenSource? batch, details;
    private CancellationTokenSource? automaticStop;
    private readonly Queue<string> automaticTargets = new();
    private readonly HashSet<string> automaticIds = new(StringComparer.Ordinal);
    private Task? automatic;
    private bool automaticProgress;
    private int automaticTotal, automaticCompleted, automaticReview, automaticFailed, automaticCancelled;
    private long detailsGeneration, candidateSelectionGeneration, authGeneration;
    private int dirty = 1;
    private bool disposed, busy;
    private Guid? selectedId;
    private string manualId = "", selectedCandidate = "";
    public RecognitionViewModel(OfflineLibraryClient offline, RecognitionQueue queue, RecognitionReviewService reviews,
        IRecognitionCatalog catalog, IRemoteCoverRegistry covers, OnlineAuthContext auth, WorkspaceViewModel workspace, IMediaIdentityDecisions? identityDecisions = null)
    {
        this.offline = offline; this.queue = queue; this.reviews = reviews; this.catalog = catalog; this.covers = covers; this.auth = auth; this.workspace = workspace;
        this.identityDecisions = identityDecisions ?? new SessionMediaIdentityDecisions();
        authGeneration = auth.Generation; reviews.Changed += OnChanged; queue.Changed += OnChanged;
    }
    private bool useOnline = true, includePe = true;
    public bool UseOnline { get => useOnline; set => Set(ref useOnline, value); }
    // Match the Electron default: bounded, read-only VERSIONINFO evidence, never execution.
    public bool IncludePe { get => includePe; set => Set(ref includePe, value); }
    public string ManualId { get => manualId; set { if (Set(ref manualId, value)) Changed(nameof(CanSaveManualPending)); } }
    public bool Busy => busy;
    public string Message { get; private set; } = UiText.Get("导入后自动识别。排序分严格大于 0.9、身份与版本一致且唯一领先时自动应用；歧义或证据不足的结果在作品详情中核对。");
    public string ProgressText { get; private set; } = UiText.Get("尚未开始识别。");
    // Pending imports have not entered RecognitionQueue yet. Keep the whole batch visible,
    // including detail verification, commit and artwork preparation after recognition ends.
    public RecognitionProgress Progress => automaticProgress
        ? new(automaticTotal, automaticTargets.Count, Math.Max(0, automaticIds.Count - automaticTargets.Count),
            automaticReview, automaticCompleted, automaticFailed, automaticCancelled, 0)
        : queue.Progress;
    public bool HasRecognitionWork => automaticIds.Count > 0 || queue.Progress is { Queued: > 0 } or { Processing: > 0 };
    public IReadOnlyList<RecognitionReviewCard> Reviews { get; private set; } = Array.Empty<RecognitionReviewCard>();
    public IReadOnlyList<RecognitionCandidateCard> Candidates { get; private set; } = Array.Empty<RecognitionCandidateCard>();
    public Guid? SelectedId => selectedId;
    public RecognitionReviewCard? SelectedReviewItem => Reviews.FirstOrDefault(r => r.Id == selectedId);
    public RecognitionCandidateCard? SelectedCandidateItem => Candidates.FirstOrDefault(c => c.Id == selectedCandidate);
    public RecognitionReview? SelectedReview => selectedId is { } id ? reviews.Snapshot.FirstOrDefault(r => r.Id == id) : null;
    public string SelectedCandidateId => selectedCandidate;
    public string? DisplayedCandidateId { get; private set; }
    public string? SelectedGameId => selectedId is { } id ? reviews.Snapshot.FirstOrDefault(r => r.Id == id)?.GameId : null;
    public string SelectedTitle { get; private set; } = UiText.Get("选择一项核对建议");
    public string LocalFacts { get; private set; } = "";
    public string SelectedMessage { get; private set; } = "";
    public string RemoteFacts { get; private set; } = UiText.Get("选择候选的“查看详情”，按需读取完整资料。");
    public string? RemoteCoverKey { get; private set; }
    public IReadOnlyList<CandidateDetailLink> RemoteLinks { get; private set; } = Array.Empty<CandidateDetailLink>();
    public string? PersistenceError => (reviews.PersistenceError ?? reviews.CapacityError) is { } error ? RecognitionDisplayText.Message(error) : null;
    public bool HasSelection => selectedId is not null;
    public bool CanApply => !busy && workspace.CanEdit && selectedId is { } id && reviews.Snapshot.FirstOrDefault(r => r.Id == id)?.Status == RecognitionReviewStatus.NeedsReview && Candidates.Any(c => c.Id == selectedCandidate && c.CanApply);
    public bool CanConfirmSelection => !busy && workspace.CanEdit && Candidates.Any(c => c.Id == selectedCandidate) &&
        SelectedReview?.Result?.Candidates.FirstOrDefault(c => c.Work.Id == selectedCandidate)?.Vetoed != true;
    public bool CanLinkSelectedManually => !busy && workspace.CanEdit && HasSelection && Candidates.Any(c => c.Id == selectedCandidate);
    public bool SelectedCandidateHasConflict => SelectedReview?.Result?.Candidates.FirstOrDefault(c => c.Work.Id == selectedCandidate)?.Vetoed == true;
    public string ConfirmSelectionText => SelectedReview?.Status == RecognitionReviewStatus.NeedsReview ? UiText.Get("确认应用所选候选") : UiText.Get("复核并应用所选候选");
    public ClientResult? LastDecision { get; private set; }
    public RecognitionJobState? LastJobState => queue.Snapshot.LastOrDefault()?.State;
    private void OnChanged() => Interlocked.Exchange(ref dirty, 1);
    public Task InitializeAsync() => Track(async () => { if (!offline.Capability.Loaded) return; await reviews.InitializeAsync(lifetime.Token); await reviews.RefreshValidityAsync(lifetime.Token); Refresh(); });
    public void RefreshIfDirty()
    {
        if (disposed) return;
        if (authGeneration != auth.Generation)
        {
            authGeneration = auth.Generation; CancelDetails(); RemoteFacts = UiText.Get("详情不可用：凭据已变化，请重新读取。"); RemoteCoverKey = null; DisplayedCandidateId = null; RemoteLinks = [];
            _ = Track(async () => { await reviews.RefreshValidityAsync(lifetime.Token); Refresh(); });
            Interlocked.Exchange(ref dirty, 1);
        }
        if (Interlocked.Exchange(ref dirty, 0) != 0) Refresh();
    }
    public void Refresh()
    {
        var all = reviews.Snapshot;
        var cards = all.Select(r => new RecognitionReviewCard(r.Id, r.GameId, r.Title, r.Status switch { RecognitionReviewStatus.NeedsReview => UiText.Get("待核对"), RecognitionReviewStatus.NeedsRecheck => UiText.Get("需重新识别"), _ => UiText.Get("建议已失效") },
            r.Message is { } message ? RecognitionDisplayText.Message(message) : (r.Result is { } result ? UiText.Format($"{result.Candidates.Length} 个在线候选 · {result.Identity.LocalCandidates.Length} 条本地索引线索") : UiText.Get("旧版历史建议")))).ToArray();
        if (!Reviews.SequenceEqual(cards)) Reviews = cards;
        var p = Progress;
        ProgressText = UiText.Format($"{p.Processed} / {p.Total} 已处理 · 排队 {p.Queued} · 处理中 {p.Processing} · 待核对 {p.NeedsReview} · 完成 {p.Completed} · 失败 {p.Failed} · 取消 {p.Cancelled} · 失效 {p.Stale}");
        var selected = selectedId is { } id ? all.FirstOrDefault(r => r.Id == id) : null;
        if (selected is null)
        {
            if (selectedId is not null) CancelDetails();
            selectedId = null; Candidates = []; LocalFacts = ""; SelectedMessage = ""; SelectedTitle = UiText.Get("选择一项核对建议"); selectedCandidate = "";
            DisplayedCandidateId = null; RemoteCoverKey = null; RemoteLinks = []; RemoteFacts = UiText.Get("选择候选后可查看完整详情。");
        }
        else
        {
            SelectedTitle = selected.Title; SelectedMessage = selected.Status switch { RecognitionReviewStatus.NeedsRecheck => UiText.Get("历史建议不能直接应用，请重新识别。"), RecognitionReviewStatus.Stale => UiText.Get("建议已失效，请重新识别或明确手动关联。"), _ => selected.Message is { } message ? RecognitionDisplayText.Message(message) : UiText.Get("先核对作品与版本，再应用建议。") };
            if (selected.Result is { } result)
            {
                LocalFacts = UiText.Get("识别时本地名称：") + result.Input.Entry.Name + UiText.Get("\n识别时关联：") + (result.Input.Entry.Metadata is { } relation ? relation.Title + " · " + relation.Id : UiText.Get("尚未关联"))
                    + UiText.Get("\n安装：") + string.Join("\n", result.Input.Entry.Installations.Take(4).Select(i => i.Folder))
                    + UiText.Get("\n线索：") + string.Join("；", result.Identity.Evidence.Titles.Take(12).Select(t => t.Value + "（" + SourceName(t.Source) + "）"))
                    + UiText.Get("\n来源编号：") + string.Join("、", result.Identity.References) + "\n" + string.Join("；", result.Identity.Evidence.Warnings.Select(RecognitionDisplayText.Message))
                    + (result.Identity.LocalCandidates.IsEmpty ? "" : UiText.Get("\n本地索引：") + string.Join("；", result.Identity.LocalCandidates.Take(6).Select(c => string.Join(" / ", c.Record.Names.Take(2)) + " · " + string.Join(", ", c.Record.References.Take(3)))));
                var candidates = result.Candidates.Select(c => new RecognitionCandidateCard(c.Work.Id, c.Work.Title + " · " + c.Work.Id,
                    UiText.Format($"排序分 {c.Confidence:0.000} · {(c.VersionNeedsConfirmation ? UiText.Get("版本信息不足，需要确认") : c.Vetoed ? UiText.Get("身份冲突") : c.HighConfidence ? UiText.Get("高置信建议") : UiText.Get("需要核对"))} · {RecognitionDisplayText.Reason(c.Reason, c.HighConfidence)}"),
                    string.Join("\n", c.Evidence.Take(6).Select(e => UiText.Format($"{SourceName(e.Source)}：{e.ObservedTitle} → {e.MatchedTitle} · 相似度 {e.Similarity:0.000}"))) + UiText.Get("\n一致来源：") + string.Join("、", c.MatchedReferences),
                    string.Join("；", c.Conflicts.Select(RecognitionDisplayText.Message)), selected.Status == RecognitionReviewStatus.NeedsReview && !c.Vetoed)).ToArray();
                if (!Candidates.SequenceEqual(candidates)) Candidates = candidates;
                if (result.Candidates.IsEmpty && selected.Status == RecognitionReviewStatus.NeedsReview && selected.Message is null)
                    SelectedMessage = UiText.Get("未获得在线候选。本地线索已保留，可重新识别或核对编号后手动关联。");
            }
            else
            {
                LocalFacts = UiText.Get("历史安装：") + selected.HistoricalFolder + "\n" + (selected.Message is { } message ? RecognitionDisplayText.Message(message) : "");
                Candidates = selected.HistoricalCandidates.Select(c => new RecognitionCandidateCard(c.Id, c.Title, UiText.Format($"历史排序分 {c.Confidence:0.000}"), RecognitionDisplayText.Reason(c.Reason, false), UiText.Get("需要重新识别"), false)).ToArray();
            }
        }
        if (!Candidates.Any(c => c.Id == selectedCandidate)) ChangeCandidate("");
        Notify();
    }
    private static string SourceName(string source) => source switch { "folder" => UiText.Get("目录名"), "custom-name" => UiText.Get("书架名称"), "sidecar" => UiText.Get("旧版侧车资料"), "title-index" => UiText.Get("独立标题索引"), "executable-name" => UiText.Get("程序名"), "archive-name" => UiText.Get("资源文件名"), "product-name" => UiText.Get("程序产品名"), "file-description" => UiText.Get("程序说明"), "manual-title" or "readme-title" => UiText.Get("说明标题"), "config-title" => UiText.Get("配置标题"), "steam-config" or "steam-file" => UiText.Get("Steam 配置"), _ => source };
    public void Select(Guid id)
    {
        if (selectedId == id) return; CancelDetails(); selectedId = id; ChangeCandidate(""); RemoteCoverKey = null; RemoteLinks = []; DisplayedCandidateId = null; RemoteFacts = UiText.Get("选择候选后可查看完整详情。"); LastDecision = null; Refresh();
        manualChoices = null; var game = SelectedGameId; var version = ++manualChoicesVersion;
        if (game is not null) _ = Track(() => LoadManualChoicesAsync(game, version));
    }
    // An embedded review must never retain another game's candidate or detail request.
    public void SelectForGame(string? gameId)
    {
        var target = gameId is null ? null : reviews.Snapshot.FirstOrDefault(r => r.GameId == gameId);
        if (target is not null) { Select(target.Id); return; }
        if (selectedId is null) return;
        CancelDetails(); selectedId = null; manualId = ""; Refresh();
    }
    private bool ChangeCandidate(string id)
    {
        if (selectedCandidate == id) return false;
        CancelDetails(); candidateSelectionGeneration++; selectedCandidate = id;
        DisplayedCandidateId = null; RemoteCoverKey = null; RemoteLinks = [];
        RemoteFacts = id.Length == 0 ? UiText.Get("选择候选后可查看完整详情。") : UiText.Get("正在读取候选详情…");
        return true;
    }
    public void SelectCandidate(string id)
    {
        if (!Candidates.Any(c => c.Id == id) || !ChangeCandidate(id)) return;
        Notify(); _ = OpenDetailsAsync(id);
    }
    public void ReportLocalDetailsFailure() => workspace.ReportUnexpected();
    public void ReportLinkFailure() { Message = UiText.Get("无法打开浏览器，请使用候选详情中的链接重试。"); Notify(); }
    public Task StartAsync(ImmutableArray<string> selected, RecognitionMode mode) => Track(async () =>
    {
        if (batch is not null || !offline.Capability.Loaded) return;
        if (automaticIds.Count == 0) automaticProgress = false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); batch = stop;
        try
        {
            await reviews.InitializeAsync(stop.Token); var targets = await offline.RecognitionTargetsAsync(selected, mode, stop.Token);
            if (targets.IsEmpty) { Message = UiText.Get("没有符合条件的本地安装条目。愿望条目不参与本地识别。"); return; }
            int added = 0, failed = 0; bool online = UseOnline, pe = IncludePe;
            foreach (string id in targets)
            {
                stop.Token.ThrowIfCancellationRequested();
                try { await queue.EnqueueAsync(id, mode, online, pe, stop.Token); added++; }
                catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { failed++; }
            }
            Message = UiText.Format($"已排队 {added} 项") + (failed > 0 ? UiText.Format($"，{failed} 项未能进入队列。") : UiText.Get("。高分建议仍需确认，不会自动更换关联。"));
        }
        finally { batch = null; Refresh(); }
    });
    public void CancelAll()
    {
        batch?.Cancel(); automaticStop?.Cancel(); automaticCancelled += automaticIds.Count; automaticTargets.Clear(); automaticIds.Clear();
        queue.CancelAll(); Message = UiText.Get("已取消识别及本轮可用建议。"); Refresh();
    }

    // Import supplies exact committed IDs. At most two jobs are active; waiting IDs are
    // bounded by the existing 2000-item import limit and do not retain remote payloads.
    public Task StartImportedAsync(ImmutableArray<string> ids)
    {
        if (disposed || ids.IsDefaultOrEmpty || !offline.Capability.Loaded) return Task.CompletedTask;
        if (automatic is not { IsCompleted: false })
        {
            automaticProgress = true;
            automaticTotal = automaticCompleted = automaticReview = automaticFailed = automaticCancelled = 0;
        }
        int deferred = 0;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (automaticIds.Contains(id)) continue;
            if (automaticIds.Count >= 2000) { deferred++; continue; }
            automaticIds.Add(id); automaticTargets.Enqueue(id); automaticTotal++;
        }
        if (automatic is not { IsCompleted: false }) automatic = Track(RunAutomaticImportsAsync);
        Refresh();
        if (deferred > 0) { Message = UiText.Format($"自动识别待处理上限已到，{deferred} 项保留在书架，请在当前批次结束后补全资料。"); Notify(); }
        return Task.CompletedTask;
    }
    // Called only after the library transaction committed and released its mutation gate.
    public Func<string, CancellationToken, Task<bool>>? PrepareImportedArtwork { get; set; }
    public Task WaitForAutomaticRecognitionAsync() => automatic ?? Task.CompletedTask;
    public Task ForgetRemovedAsync(ImmutableArray<string> ids) => Track(async () =>
    {
        foreach (var job in queue.Snapshot.Where(j => ids.Contains(j.GameId))) queue.Cancel(job.Id);
        foreach (var review in reviews.Snapshot.Where(r => ids.Contains(r.GameId))) await reviews.IgnoreAsync(review.Id, lifetime.Token);
        Refresh();
    });
    private async Task RetireSupersededAsync(string game, CancellationToken token)
    {
        foreach (var review in reviews.Snapshot.Where(r => r.GameId == game && r.Status != RecognitionReviewStatus.NeedsReview))
            await reviews.IgnoreAsync(review.Id, token);
    }
    public Task StartUnidentifiedAsync(bool retryPending = false) => Track(async () =>
    {
        // A credential may be saved while its earlier no-key import is still settling.
        // Resume after that batch releases IDs instead of silently losing the retry.
        if (retryPending && automatic is { IsCompleted: false } running) await running;
        await reviews.InitializeAsync(lifetime.Token);
        var awaitingReview = reviews.Snapshot.Select(r => r.GameId).ToHashSet(StringComparer.Ordinal);
        var ids = offline.CurrentSnapshot?.Entries.Where(e => e.Metadata is null && !e.Installations.IsEmpty
            && (retryPending || !awaitingReview.Contains(e.Id))).Select(e => e.Id).ToImmutableArray() ?? [];
        await StartImportedAsync(ids);
    });
    private async Task RunAutomaticImportsAsync()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        automaticStop = stop;
        int applied = 0, review = 0, failed = 0, artworkDeferred = 0;
        try
        {
            await reviews.InitializeAsync(stop.Token);
            while (automaticTargets.Count > 0)
            {
                stop.Token.ThrowIfCancellationRequested();
                while (batch is not null) await Task.Delay(250, stop.Token);
                var jobs = new List<(Guid Job, string Game)>();
                for (int n = 0; n < RecognitionQueue.WorkerCount && automaticTargets.TryDequeue(out var id); n++)
                {
                    try
                    {
                        var input = await offline.CaptureRecognitionAsync(id, stop.Token);
                        if (input.Entry.Metadata is not null || input.Entry.Installations.IsEmpty) { FinishAutomatic(id, RecognitionJobState.Completed); continue; }
                        jobs.Add((await queue.EnqueueAsync(id, RecognitionMode.MissingMetadata, online: UseOnline, includePe: IncludePe, cancellation: stop.Token), id));
                    }
                    catch (Exception e) when (e is InvalidDataException or InvalidOperationException)
                    { failed++; FinishAutomatic(id, RecognitionJobState.Failed); }
                }
                Message = UiText.Format($"正在自动识别 · 已应用 {applied} · 待核对 {review} · 未完成 {failed}。"); Refresh();
                await queue.WhenIdleAsync(stop.Token);
                foreach (var item in jobs)
                {
                    var outcome = RecognitionJobState.Failed;
                    try
                    {
                        var job = queue.Snapshot.FirstOrDefault(j => j.Id == item.Job);
                        if (job?.State != RecognitionJobState.NeedsReview || job.Result is null) { failed++; continue; }
                        if (AutomaticRecognitionPolicy.Select(job.Result) is null) { review++; outcome = RecognitionJobState.NeedsReview; continue; }
                        // Never borrow an active save task or modify an unsaved personal draft.
                        while (busy || !workspace.CanEdit || workspace.IsDirty) await Task.Delay(250, stop.Token);
                        stop.Token.ThrowIfCancellationRequested();
                        busy = true; Notify();
                        try
                        {
                            var decision = await workspace.ExecuteFileMutationAsync(async token =>
                            {
                                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
                                return await reviews.ApplyAutomaticAsync(item.Job, linked.Token);
                            }, workspace.SelectedId == item.Game ? item.Game : null, preserveSelection: true);
                            LastDecision = decision;
                            if (decision.IsCommitted) { applied++; outcome = RecognitionJobState.Completed; }
                            else { review++; outcome = RecognitionJobState.NeedsReview; Message = UiText.Get("自动应用未完成，导入资料保持不变：") + decision.Message; }
                            await reviews.RefreshValidityAsync(stop.Token);
                            if (decision.IsCommitted) await RetireSupersededAsync(item.Game, stop.Token);
                        }
                        finally { busy = false; Refresh(); }
                        if (LastDecision?.IsCommitted == true && PrepareImportedArtwork is { } prepare &&
                            offline.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == item.Game)?.Metadata is { } metadata)
                        {
                            Message = UiText.Format($"正在准备作品图片 · 已应用 {applied} 项。"); Refresh();
                            try { if (!await prepare(metadata.Id, stop.Token)) artworkDeferred++; }
                            catch (OperationCanceledException) { throw; }
                            catch { artworkDeferred++; }
                        }
                    }
                    finally { FinishAutomatic(item.Game, stop.IsCancellationRequested && outcome != RecognitionJobState.Completed ? RecognitionJobState.Cancelled : outcome); Refresh(); }
                }
            }
            Message = UiText.Format($"自动识别完成：已应用 {applied} 项，{review} 项需核对，{failed} 项未完成。") +
                (artworkDeferred > 0 ? UiText.Format($" {artworkDeferred} 项图片准备未完成，可稍后刷新重试。") : "");
        }
        finally
        {
            foreach (var id in automaticIds.ToArray()) FinishAutomatic(id, stop.IsCancellationRequested ? RecognitionJobState.Cancelled : RecognitionJobState.Failed);
            automaticTargets.Clear(); automaticStop = null; Refresh();
        }
    }
    private void FinishAutomatic(string id, RecognitionJobState outcome)
    {
        if (!automaticIds.Remove(id)) return;
        switch (outcome)
        {
            case RecognitionJobState.Completed: automaticCompleted++; break;
            case RecognitionJobState.NeedsReview: automaticReview++; break;
            case RecognitionJobState.Cancelled: automaticCancelled++; break;
            default: automaticFailed++; break;
        }
    }
    public Task RerunAsync() => SelectedGameId is { } id ? StartAsync([id], RecognitionMode.Reidentify) : Task.CompletedTask;
    public Task IgnoreAsync() => selectedId is { } id ? Track(async () => { LastDecision = await reviews.IgnoreAsync(id, lifetime.Token); Message = LastDecision.Message; Refresh(); }) : Task.CompletedTask;
    public Task ApplyAsync() => selectedId is { } id && CanApply ? ApplyCapturedAsync(id, selectedCandidate, SelectedGameId) : Task.CompletedTask;
    private Task ApplyCapturedAsync(Guid id, string candidate, string? game) => Mutate(c => reviews.ApplyAsync(id, candidate, c), game);
    public Task ConfirmSelectionAsync() => Track(async () =>
    {
        if (!CanConfirmSelection || SelectedGameId is not { } game || selectedId is not { } original) return;
        var candidate = selectedCandidate;
        if (CanApply) { await ApplyCapturedAsync(original, candidate, game); return; }
        var authVersion = auth.Generation;
        busy = true; Message = UiText.Get("正在重新识别并复核所选作品；原关联和个人编辑会保留。"); Notify();
        Guid jobId = Guid.Empty;
        try
        {
            jobId = await queue.EnqueueAsync(game, RecognitionMode.Reidentify, UseOnline, IncludePe, lifetime.Token);
            await queue.WhenIdleAsync(lifetime.Token);
            await reviews.RefreshValidityAsync(lifetime.Token);
            var fresh = reviews.Snapshot.FirstOrDefault(r => r.Id == jobId);
            var match = fresh?.Result?.Candidates.FirstOrDefault(c => c.Work.Id == candidate);
            if (auth.Generation != authVersion || selectedId != original && selectedId != jobId ||
                selectedId == original && selectedCandidate != candidate || selectedId == jobId && selectedCandidate.Length > 0 && selectedCandidate != candidate || fresh?.GameId != game ||
                fresh.Status != RecognitionReviewStatus.NeedsReview || match is null || match.Vetoed)
            {
                Message = UiText.Get("所选作品未通过当前复核，或选择已变化。没有应用旧建议；请查看新的识别结果。"); return;
            }
            Select(jobId); SelectCandidate(candidate);
        }
        finally { busy = false; Refresh(); }
        if (CanApply && selectedId == jobId && selectedCandidate == candidate) await ApplyCapturedAsync(jobId, candidate, game);
    });
    public Task ManualLinkAsync()
    {
        if (SelectedGameId is not { } id) return Task.CompletedTask;
        var target = ManualId.Trim(); return Mutate(c => reviews.ManualLinkAsync(id, target, c), id);
    }
    public Task ManualLinkSelectedAsync()
    {
        if (!CanLinkSelectedManually || SelectedGameId is not { } id) return Task.CompletedTask;
        var target = selectedCandidate; return Mutate(c => reviews.ManualLinkAsync(id, target, c), id);
    }
    public Func<string, string?, Task>? ReviewCompleted { get; set; }
    private Task Mutate(Func<CancellationToken, Task<ClientResult>> action, string? gameId) => Track(async () =>
    {
        if (busy) return; busy = true; Notify();
        var remaining = Reviews.Select(r => r.GameId).Where(id => id != gameId).ToArray();
        var index = Reviews.ToList().FindIndex(r => r.GameId == gameId);
        var next = remaining.Skip(Math.Max(0, index)).Concat(remaining).Distinct().ToArray();
        ClientResult? decision = null;
        try
        {
            LastDecision = await workspace.ExecuteFileMutationAsync(async token => { using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); return await action(linked.Token); }, gameId);
            decision = LastDecision;
            Message = LastDecision.Message; await reviews.RefreshValidityAsync(lifetime.Token);
            if (decision.IsCommitted && gameId is not null) await RetireSupersededAsync(gameId, lifetime.Token);
        }
        finally { busy = false; Refresh(); }
        if (LastDecision?.IsCommitted == true && PrepareImportedArtwork is { } prepare &&
            offline.CurrentSnapshot?.Entries.FirstOrDefault(e => e.Id == gameId)?.Metadata is { } metadata)
        {
            try { if (!await prepare(metadata.Id, lifetime.Token)) Message += UiText.Get(" 图片准备未完成，可稍后刷新重试。"); }
            catch (OperationCanceledException) { }
            catch { Message += UiText.Get(" 图片准备未完成，可稍后刷新重试。"); }
            Refresh();
        }
        if (decision?.IsCommitted == true && gameId is not null && ReviewCompleted is { } completed)
            await completed(gameId, next.FirstOrDefault(id => Reviews.Any(r => r.GameId == id)));
    });
    public Task OpenDetailsAsync(string candidateId) => Track(async () =>
    {
        if (selectedId is not { } review || !CatalogWorkReference.TryParse(candidateId, out var parsed)) return;
        candidateId = parsed;
        ChangeCandidate(candidateId); CancelDetails(); details = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = details.Token; long generation = detailsGeneration, selection = candidateSelectionGeneration, epoch = auth.Generation;
        bool Current() => generation == detailsGeneration && selection == candidateSelectionGeneration && selectedId == review && selectedCandidate == candidateId && epoch == auth.Generation && !token.IsCancellationRequested;
        DisplayedCandidateId = null; RemoteCoverKey = null; RemoteLinks = []; RemoteFacts = UiText.Get("正在读取候选详情…"); Notify();
        try
        {
            var result = await catalog.DetailsAsync(candidateId, false, token);
            if (!Current()) return;
            DisplayedCandidateId = candidateId;
            RemoteCoverKey = covers.Register(result.CoverUrl, result.FetchedAt);
            RemoteLinks = CandidateDetailLinks.From(result);
            RemoteFacts = UiText.Format($"{result.Title}\n原名：{result.OriginalTitle}\n别名：{string.Join(" / ", result.Aliases)}\n发行：{result.ReleaseDate}\n开发商：{string.Join("、", result.Developers)}\n编号：{result.Id}\n来源：{string.Join("、", result.References.Select(r => r.Source + ":" + r.Id))}\n{result.Description}");
        }
        catch (OperationCanceledException) { if (generation == detailsGeneration && selection == candidateSelectionGeneration) RemoteFacts = UiText.Get("详情不可用：读取已取消。"); }
        catch (Exception e) { if (Current()) RemoteFacts = UiText.Get("详情不可用：") + (e is OnlineException ? e.Message : UiText.Get("读取失败，请重试。")); }
        finally { if (generation == detailsGeneration) Notify(); }
    });
    private void CancelDetails() { detailsGeneration++; details?.Cancel(); details?.Dispose(); details = null; }
    private Task Track(Func<Task> action)
    {
        if (disposed) return Task.CompletedTask; var task = Run(); lock (pending) pending.Add(task); _ = Observe(task); return task;
        async Task Run()
        {
            try { await action(); }
            catch (OperationCanceledException) { Message = UiText.Get("操作已取消。"); Notify(); }
            catch (Exception e) { Message = e is OnlineException or InvalidDataException or InvalidOperationException ? e.Message : UiText.Get("操作未完成，请重试；已有书架不会因此被替换。"); Notify(); }
        }
    }
    private async Task Observe(Task task) { try { await task; } finally { lock (pending) pending.Remove(task); } }
    private void Notify() { foreach (string name in new[] { nameof(Reviews), nameof(Candidates), nameof(SelectedReviewItem), nameof(SelectedCandidateItem), nameof(SelectedTitle), nameof(SelectedId), nameof(SelectedGameId), nameof(LocalFacts), nameof(SelectedMessage), nameof(RemoteFacts), nameof(RemoteCoverKey), nameof(RemoteLinks), nameof(ProgressText), nameof(Progress), nameof(HasRecognitionWork), nameof(Message), nameof(Busy), nameof(CanApply), nameof(CanConfirmSelection), nameof(CanLinkSelectedManually), nameof(SelectedCandidateHasConflict), nameof(ConfirmSelectionText), nameof(HasSelection), nameof(PersistenceError), nameof(LastDecision), nameof(LastJobState) }) Changed(name); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); CancelDetails();
        Task[] tasks; lock (pending) tasks = pending.ToArray(); await Task.WhenAll(tasks);
        await queue.DisposeAsync(); try { await reviews.FlushAsync(); } catch (Exception e) when (e is IOException or InvalidDataException or DataIsolationException or UnauthorizedAccessException) { }
        reviews.Changed -= OnChanged; queue.Changed -= OnChanged; await reviews.DisposeAsync(); lifetime.Dispose();
    }
}
