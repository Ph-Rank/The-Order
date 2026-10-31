using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private sealed class Run(PlaygroundConversation conversation, PlaygroundSettings settings, long generation, Guid userId,
        Guid assistantId, Guid turnId, string question, ImmutableArray<PlaygroundAttachment> attachments, CancellationToken cancellation) : IDisposable
    {
        public PlaygroundConversation Conversation { get; } = conversation;
        public PlaygroundSettings Settings { get; } = settings;
        public long Generation { get; } = generation;
        public Guid UserId { get; } = userId;
        public Guid AssistantId { get; } = assistantId;
        public Guid Id { get; } = turnId;
        public string Question { get; } = question;
        public ImmutableArray<PlaygroundAttachment> Attachments { get; } = attachments;
        public CancellationTokenSource Deadline { get; } = new(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        public CancellationTokenSource Cancel { get; } = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StringBuilder Text { get; } = new();
        public Dictionary<string, PlaygroundSource> Sources { get; } = new(StringComparer.Ordinal);
        public HashSet<Guid> ContextMessageIds { get; } = [];
        public List<PlaygroundMemory> Proposed { get; } = [];
        public List<string> Activities { get; } = [];
        public List<PlaygroundWireMessage> Context { get; } = [];
        public PlaygroundContinuation? Continuation { get; set; }
        public PlaygroundConversationTape? ConversationTape { get; set; }
        public PlaygroundConversationTape? PendingTape { get; set; }
        public ImmutableArray<Guid> TapeContextMessageIds { get; set; } = [];
        public string ConversationBinding { get; set; } = "";
        public PlaygroundActiveSession? TapeActivity { get; set; }
        public PlaygroundUsage? Usage { get; set; }
        public PlaygroundCostEstimate? Cost { get; set; }
        public LibrarySnapshot? LibrarySnapshot { get; set; }
        public bool LibraryCaptured { get; set; }
        public string? SpoilerLibraryStamp { get; set; }
        public string? PlaytimeRankingStamp { get; set; }
        public PlaygroundRetention Retention { get; set; }
        public string? Suggestions { get; set; }
        public string? SafePublicQuestion { get; set; }
        public bool PublicSearchAttempted { get; set; }
        public string? PublicSearchPayload { get; set; }
        public bool PublicSearchAnswerReady { get; set; }
        public bool PublicSearchTruncated { get; set; }
        public Dictionary<string, string> PublicFetchPayloads { get; } = new(StringComparer.Ordinal);
        public string Binding { get; set; } = "";
        public long LastTextPreview { get; set; }
        public bool SavedContext => Conversation.Options.UseSavedContext;
        public void Dispose() { Deadline.Dispose(); Cancel.Dispose(); }
    }
    public Task SendAsync(Guid conversationId, string text, CancellationToken cancellation = default)
        => Send(conversationId, text, [], null, cancellation);
    public Task SendAsync(Guid conversationId, string text, ImmutableArray<PlaygroundAttachment> attachments, CancellationToken cancellation = default)
        => Send(conversationId, text, attachments, null, cancellation);
    public Task RetryAsync(Guid conversationId, CancellationToken cancellation = default)
    {
        var messages = Snapshot.Messages.Where(m => m.ConversationId == conversationId).ToArray();
        var reply = messages.LastOrDefault(m => m.Role == "assistant");
        if (reply is null || reply.Status is not (PlaygroundMessageStatus.Failed or PlaygroundMessageStatus.Cancelled or PlaygroundMessageStatus.Interrupted))
            throw new InvalidOperationException("当前没有可重试的中断或失败回复。");
        var question = messages.TakeWhile(m => m.Id != reply.Id).LastOrDefault(m => m.Role == "user")
            ?? throw new InvalidOperationException("原始问题已不存在。");
        if (Suppressed(Snapshot, question)) throw new InvalidOperationException("原问题包含已遗忘的内容，请重新表达后再发送。");
        var bound = question.Sources.Where(x => x.WorkKey is not null).Select(x => x.WorkKey).ToArray();
        if (bound.Length != 0 && (!Snapshot.Settings.Permissions.Library || bound.Any(key => library()?.Entries.Any(e => WorkKeyFor(e) == key) != true)))
            throw new InvalidOperationException("原问题引用的作品关联已变化或无法核对，请重新核对后发送。");
        return Send(conversationId, question.Text, question.Attachments, question.Id, cancellation);
    }
    private async Task Send(Guid conversationId, string text, ImmutableArray<PlaygroundAttachment> attachments, Guid? retry, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(text); ValidateAttachments(attachments);
        text = text.Trim(); if (text.Length > 16000 || text.Length == 0 && attachments.IsEmpty) throw new ArgumentException("请填写消息或添加文件资料；消息最多 16000 个字符。");
        await InitializeAsync(cancellation).ConfigureAwait(false);
        var conversation = Conversation(conversationId); var captured = Snapshot; long epoch;
        lock (gate) { ThrowClosed(); if (configurationUncertain) throw new InvalidOperationException("授权设置尚未安全保存，请先重新保存设置。"); epoch = generation; }
        RequireConnection(captured.Settings, conversation.Options);
        if (!await credentials.HasAsync(conversation.Options.Provider, Endpoint(conversation.Options.Provider, conversation.Options.Endpoint), cancellation).ConfigureAwait(false))
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Authentication, "尚未配置此连接的 API Key，请在 Playground 设置中填写。"));
        var run = new Run(conversation, captured.Settings, epoch, retry ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text, attachments, cancellation);
        run.Binding = string.Join(':', captured.Identity.ProfileId, captured.Identity.StoreId, epoch, conversation.Id, run.Id,
            conversation.Options.Provider, Endpoint(conversation.Options.Provider, conversation.Options.Endpoint), conversation.Options.Model);
        using var timeout = run.Deadline.Token.Register(() => run.Cancel.Cancel());
        lock (gate)
        {
            if (epoch != generation || closing || disposed) { run.Dispose(); throw new OperationCanceledException("设置已变化，尚未发送请求。"); }
            if (active is not null) { run.Dispose(); throw new InvalidOperationException("当前已有一轮回复正在进行或停止中，请稍候。"); }
            active = run;
        }
        try
        {
            PrepareConversationTape(run, captured, retry is not null);
            await Mutate(s =>
            {
                EnsureCurrent(run); var now = clock(); var messages = s.Messages;
                if (retry is null)
                {
                    string? work = MentionedWork(run, text);
                    var bindings = work is null ? ImmutableArray<PlaygroundSource>.Empty : [new("binding_" + run.UserId.ToString("N"), PlaygroundSourceKind.LocalRecord, "用户消息涉及的作品身份")
                        { WorkKey = work, At = now }];
                    messages = messages.Add(new(run.UserId, conversationId, "user", text, now, PlaygroundMessageStatus.Complete, run.Id) { Sources = bindings, Attachments = attachments });
                }
                messages = messages.Add(new(run.AssistantId, conversationId, "assistant", "", now, PlaygroundMessageStatus.Generating, run.Id)
                    { Provider = conversation.Options.Provider, Model = conversation.Options.Model });
                return s with { Messages = messages, Conversations = s.Conversations.Select(c => c.Id == conversationId
                    ? c with { UpdatedAt = now, Title = c.Title is "新会话" or "临时会话" && !c.Temporary
                        ? Bounded(text.Length == 0 ? "资料 · " + attachments[0].Name : text, 38) : c.Title } : c).ToImmutableArray() };
            }, run.Cancel.Token, !conversation.Temporary).ConfigureAwait(false);
            Notify(conversationId, run.Id, "started");
            await Execute(run).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            bool timedOut = run.Deadline.IsCancellationRequested;
            await Finish(run, timedOut ? PlaygroundMessageStatus.Failed : PlaygroundMessageStatus.Cancelled,
                timedOut ? "本轮已超时，未自动重试；可以手动重试。" : "本轮已停止，未自动重新发送。", false).ConfigureAwait(false);
        }
        catch (PlaygroundProviderException error)
        { await Finish(run, PlaygroundMessageStatus.Failed, Bounded(error.Failure.Message, 400), false).ConfigureAwait(false); }
        catch (Exception)
        { await Finish(run, PlaygroundMessageStatus.Failed, "本轮未完成，未自动重发；检查设置后可手动重试。", false).ConfigureAwait(false); }
        finally
        {
            lock (gate) { if (active == run) active = null; }
            run.Finished.TrySetResult(); run.Dispose(); Notify(conversationId, run.Id, "idle");
        }
    }
    private void EnsureCurrent(Run run)
    {
        run.Cancel.Token.ThrowIfCancellationRequested();
        lock (gate) if (disposed || closing || active != run || generation != run.Generation || state.Identity != store.Identity
            || !state.Conversations.Any(c => c.Id == run.Conversation.Id)) throw new OperationCanceledException(run.Cancel.Token);
    }
    private static void RequireConnection(PlaygroundSettings settings, PlaygroundConversationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model)) throw new InvalidOperationException("请先选择或填写模型 ID。");
        _ = Endpoint(options.Provider, options.Endpoint);
        if (!PlaygroundAuthorizationPolicy.CanSend(settings))
            throw new InvalidOperationException("尚未获得全局讨论授权，请先在设置中确认数据去向。");
    }
    private async Task Execute(Run run)
    {
        var options = run.Conversation.Options; var provider = Provider(options.Provider); var capability = provider.Capabilities(options.Model, options.Endpoint);
        if (run.Attachments.Any(IsImageAttachment) && !capability.ImageInput)
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "此模型的图片输入尚未核实或不受支持，未发送图片或模型请求。请改用支持图片的模型。"));
        PreparePublicQuestion(run);
        await PrepareContext(run).ConfigureAwait(false);
        var messages = DiscussionMessages(run, capability.ImageInput); var tools = capability.Tools ? Definitions(run) : [];
        RegisterAdmittedWebSources(run);
        var results = ImmutableArray<PlaygroundWireMessage>.Empty;
        while (true)
        {
            EnsureCurrent(run); var calls = new List<PlaygroundToolCall>(); var callIds = new HashSet<string>(StringComparer.Ordinal); long callCharacters = 0; bool completed = false; string? stopReason = null; PlaygroundUsage? roundUsage = null;
            var request = new PlaygroundProviderRequest(run.Conversation.Id, run.Id, options.Provider, Endpoint(options.Provider, options.Endpoint), options.Model,
                Instructions(run), messages) { Phase = PlaygroundPhase.Discussion, Tools = tools, ToolResults = results,
                Binding = run.Binding, Continuation = run.Continuation, ConversationBinding = run.ConversationBinding, ConversationTape = run.ConversationTape,
                ReasoningEffort = capability.Verified && options.ReasoningEffort is { } effort && capability.ReasoningEfforts.Contains(effort) ? effort : null,
                MaximumOutputTokens = run.Settings.MaximumOutputTokens, MaximumToolCalls = 0 };
            EnsureConversationScope(run); CheckRequestBudget(run, request);
            var chargedAt = clock();
            try
            {
              await foreach (var item in provider.SendAsync(request, run.Cancel.Token).WithCancellation(run.Cancel.Token).ConfigureAwait(false))
              {
                // Billing remains attached to this immutable attempt even if a
                // late terminal usage arrives after Stop. No text/source/tool
                // from that cancelled generation is admitted below.
                if (item.Kind == PlaygroundProviderEventKind.Usage) roundUsage = item.Usage;
                EnsureCurrent(run);
                switch (item.Kind)
                {
                    case PlaygroundProviderEventKind.Text:
                        if (item.Text is { } delta)
                        {
                            if (run.Text.Length + delta.Length > Math.Min(1048576L, (long)run.Settings.MaximumOutputTokens * 16))
                                throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "回复超出本轮输出上限，已停止，未自动重试。"));
                            run.Text.Append(delta);
                            if (options.Spoiler == PlaygroundSpoilerMode.Full)
                            {
                                long now = Stopwatch.GetTimestamp();
                                if (run.LastTextPreview == 0 || Stopwatch.GetElapsedTime(run.LastTextPreview, now).TotalMilliseconds >= 80)
                                { run.LastTextPreview = now; Preview(run); }
                            }
                        }
                        break;
                    case PlaygroundProviderEventKind.ToolCall:
                        if (item.ToolCall is not { } call || call.ArgumentsJson.Length > 16000 || call.Id.Length is < 1 or > 160)
                            throw new PlaygroundProviderException(new(PlaygroundFailureKind.InvalidResponse, "工具请求无效或超出本轮上限。"));
                        if (!callIds.Add(call.Id)) throw new PlaygroundProviderException(new(PlaygroundFailureKind.InvalidResponse, "工具调用身份重复。"));
                        callCharacters += (long)call.Id.Length + call.Name.Length + call.ArgumentsJson.Length;
                        if (callCharacters > run.Settings.MaximumContextCharacters) throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "本次工具请求资料超过上下文容量，已停止且未自动重发。"));
                        calls.Add(call); break;
                    case PlaygroundProviderEventKind.Source: if (item.Source is { } source) RegisterWeb(run, source); break;
                    case PlaygroundProviderEventKind.Usage: roundUsage = item.Usage; break;
                    case PlaygroundProviderEventKind.ConversationTape:
                        if (item.ConversationTape is not { } tape || tape.Binding != run.ConversationBinding)
                            throw new PlaygroundProviderException(new(PlaygroundFailureKind.InvalidResponse, "服务商会话状态不属于当前讨论。"));
                        if (tape.Data.Length is > 0 and <= MaximumTapeCharacters) run.PendingTape = tape;
                        break;
                    case PlaygroundProviderEventKind.Continuation:
                        if (item.Continuation?.Binding != run.Binding) throw new PlaygroundProviderException(new(PlaygroundFailureKind.InvalidResponse, "服务商续接状态不属于本轮。"));
                        if (options.Search == PlaygroundSearchMode.Off && NativeWebState(item.Continuation.Data))
                            throw new PlaygroundProviderException(new(PlaygroundFailureKind.AccessDenied, "联网已关闭，未接纳含网页或搜索内容的续接状态。"));
                        lock (gate) { EnsureCurrent(run); run.Continuation = item.Continuation; } break;
                    case PlaygroundProviderEventKind.Completed: completed = true; stopReason = item.StopReason; break;
                    case PlaygroundProviderEventKind.Refusal: throw new PlaygroundProviderException(new(PlaygroundFailureKind.Refused, "模型拒绝了本轮请求；未自动重试。"));
                    case PlaygroundProviderEventKind.Error: throw new PlaygroundProviderException(item.Failure ?? new(PlaygroundFailureKind.InvalidResponse, "服务商返回异常。"));
                    // Never relay provider reasoning, protocol names or arbitrary activity
                    // payloads into the UI. The host owns its small activity vocabulary.
                }
              }
            }
            finally { RecordCharge(run, options.Provider, options.Model, roundUsage, chargedAt, PlaygroundChargeKind.Discussion); }
            EnsureCurrent(run);
            if (!completed) throw new PlaygroundProviderException(new(PlaygroundFailureKind.InvalidResponse, "回复连接异常结束，未当成完整回复。"));
            if (stopReason is "max_tokens" or "length")
            { await Finish(run, PlaygroundMessageStatus.Interrupted, "回复达到模型输出上限，未视为完整证据；可缩小问题后手动重试。", false).ConfigureAwait(false); return; }
            if (stopReason == "refusal") throw new PlaygroundProviderException(new(PlaygroundFailureKind.Refused, "模型拒绝了本轮请求，未自动重试。"));
            if (calls.Count == 0) { await Finish(run, PlaygroundMessageStatus.Complete, null, true).ConfigureAwait(false); return; }
            if (!capability.Tools) throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "当前模型未核实支持本地工具。"));
            if (run.Continuation is null) throw new PlaygroundProviderException(new(PlaygroundFailureKind.InvalidResponse, "工具回合缺少可验证的续接状态。"));
            var next = ImmutableArray.CreateBuilder<PlaygroundWireMessage>();
            long resultCharacters = 0;
            foreach (var call in calls)
            {
                EnsureCurrent(run); var data = await Tool(run, call).ConfigureAwait(false); resultCharacters += data.Length;
                if (await FinishPublicSearch(run).ConfigureAwait(false)) return;
                if (resultCharacters > run.Settings.MaximumContextCharacters)
                    throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "工具资料超过本轮上下文字符预算，已停止且未自动重发。"));
                next.Add(new("tool", data, call.Id, call.Name));
            }
            results = next.ToImmutable();
        }
    }
    private static void CheckRequestBudget(Run run, PlaygroundProviderRequest request)
    {
        // Images are native input blocks with a separate byte/count budget;
        // base64 is neither prose nor an estimate of visual-token usage.
        PlaygroundAttachmentRules.Validate(request.Messages.SelectMany(m => m.Attachments).ToImmutableArray());
        // This is readable character capacity, not tokens. The bounded native
        // envelope replaces its already-contained messages; JSON escapes and
        // typed opaque signatures do not masquerade as readable prose.
        long characters = request.Instructions.Length
            + (request.Continuation is { } continuation ? NativeEnvelopeCharacters(continuation.Data) : (request.ConversationTape is { } tape ? NativeEnvelopeCharacters(tape.Data) : 0L) + request.Messages.Sum(m => (long)m.Text.Length))
            + request.ToolResults.Sum(m => (long)m.Text.Length)
            + request.Tools.Sum(t => (long)t.Name.Length + t.Description.Length + t.ParametersJson.Length);
        if (characters > run.Settings.MaximumContextCharacters)
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "本轮上下文超过设定的字符预算，已停止，未继续发送模型请求。请缩小问题或提高上下文预算。"));
    }
    private void Activity(Run run, string value)
    {
        EnsureCurrent(run); if (!run.Activities.Contains(value) && run.Activities.Count < 24) run.Activities.Add(value);
        Preview(run, false); Notify(run.Conversation.Id, run.Id, "activity");
    }
    private void Preview(Run run, bool text = true)
    {
        EnsureCurrent(run);
        lock (gate)
        {
            if (active != run || generation != run.Generation || run.Cancel.IsCancellationRequested || closing || disposed) throw new OperationCanceledException(run.Cancel.Token);
            state = state with { Messages = state.Messages.Select(m => m.Id == run.AssistantId
                ? m with { Text = text ? run.Text.ToString() : m.Text, Activities = run.Activities.ToImmutableArray(), Retention = run.Retention } : m).ToImmutableArray() };
        }
        Notify(run.Conversation.Id, run.Id, text ? "delta" : "activity");
    }
    private async Task Finish(Run run, PlaygroundMessageStatus status, string? error, bool remember)
    {
        bool valid; lock (gate) valid = active == run && generation == run.Generation && !run.Cancel.IsCancellationRequested && !closing && !disposed;
        if (status == PlaygroundMessageStatus.Complete && !valid) { status = PlaygroundMessageStatus.Cancelled; error = "本轮授权或状态已变化，迟到回复未被接纳。"; }
        if (status == PlaygroundMessageStatus.Complete && (run.Conversation.Options.Provider == PlaygroundProviderKind.Gemini
            || run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.LibraryStatus || run.PlaytimeRankingStamp is not null) && !LibraryCurrent(run))
        { status = PlaygroundMessageStatus.Interrupted; error = "书架资料已变化，本轮未作为完整证据接纳；可手动重新提问。"; run.PendingTape = null; }
        string text = status == PlaygroundMessageStatus.Complete || run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full
            ? run.Text.ToString() : "";
        if (status == PlaygroundMessageStatus.Complete && string.IsNullOrWhiteSpace(text)) { status = PlaygroundMessageStatus.Failed; error = "服务商没有返回可显示的正文。"; }
        string originalText = text;
        text = SpoilerSafeText(run, text, true, out bool spoilerFiltered);
        if (spoilerFiltered) { run.PendingTape = null; run.Proposed.Clear(); error = "部分未玩或无法可靠关联到已玩作品的剧情内容已省略。"; }
        text = CitationMarker.Replace(text, match => run.Sources.ContainsKey(match.Groups[1].Value) ? match.Value : "[未核验引用]");
        if (text != originalText) foreach (var source in run.Sources.Values.ToArray()) run.Sources[source.Id] = source with { HasExactRange = false };
        var sources = FinalSources(run, text);
        try
        {
            await Mutate(s =>
            {
                // A deletion is final even if an HTTP implementation ignored cancellation.
                if (!s.Messages.Any(m => m.Id == run.AssistantId)) return s;
                bool allowed; lock (gate) allowed = active == run && generation == run.Generation && !run.Cancel.IsCancellationRequested && !closing;
                var accepted = status == PlaygroundMessageStatus.Complete && allowed;
                var messages = s.Messages.Select(m => m.Id == run.AssistantId ? m with { Text = accepted || status != PlaygroundMessageStatus.Complete ? text : "",
                    Status = status == PlaygroundMessageStatus.Complete && !allowed ? PlaygroundMessageStatus.Cancelled : status,
                    Error = accepted ? error : error ?? "本轮已停止。", Sources = accepted ? sources : [], Usage = run.Usage, Cost = run.Cost,
                    ContextMessageIds = accepted ? run.ContextMessageIds.ToImmutableArray() : [],
                    Activities = run.Activities.ToImmutableArray(), Retention = run.Retention, SearchSuggestionsHtml = accepted ? run.Suggestions : null } : m).ToImmutableArray();
                var next = s with { Messages = messages };
                return accepted && remember && !run.Conversation.Temporary && run.Retention == PlaygroundRetention.Portable ? Remember(next, run) : next;
            }, status == PlaygroundMessageStatus.Complete ? run.Cancel.Token : CancellationToken.None, !run.Conversation.Temporary, true,
                status == PlaygroundMessageStatus.Complete ? run : null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (status == PlaygroundMessageStatus.Complete)
        { await Finish(run, PlaygroundMessageStatus.Cancelled, "状态或授权已变化，迟到内容未接纳。", false).ConfigureAwait(false); return; }
        catch (Exception)
        {
            lock (gate) state = state with { Messages = state.Messages.Select(m => m.Id == run.AssistantId
                ? m with { Status = PlaygroundMessageStatus.Interrupted, Error = "回复状态未能安全保存，原始资料已保留；请重启后核对。", Sources = [] } : m).ToImmutableArray() };
        }
        if (status == PlaygroundMessageStatus.Complete) SaveConversationTape(run);
        run.Continuation = null; run.ConversationTape = null; run.PendingTape = null; Notify(run.Conversation.Id, run.Id, "finished");
    }
    private static PlaygroundUsage? AddUsage(PlaygroundUsage? a, PlaygroundUsage? b)
    {
        if (a is null) return b; if (b is null) return a;
        static long? Sum(long? x, long? y) => x is null || y is null ? null : checked(x.Value + y.Value);
        static int? Count(int? x, int? y) => x is null || y is null ? null : checked(x.Value + y.Value);
        return new(Sum(a.InputTokens, b.InputTokens), Sum(a.OutputTokens, b.OutputTokens), Sum(a.ReasoningTokens, b.ReasoningTokens),
            Sum(a.CachedInputTokens, b.CachedInputTokens), Count(a.Searches, b.Searches), Count(a.Fetches, b.Fetches))
        {
            BillingModel = a.BillingModel == b.BillingModel ? a.BillingModel : null,
            ServiceTier = a.ServiceTier == b.ServiceTier ? a.ServiceTier : null,
            ReportedCostUsd = a.ReportedCostUsd is { } x && b.ReportedCostUsd is { } y ? x + y : null,
            CacheWriteInputTokens = Sum(a.CacheWriteInputTokens, b.CacheWriteInputTokens),
            CacheWrite5mInputTokens = Sum(a.CacheWrite5mInputTokens, b.CacheWrite5mInputTokens),
            CacheWrite1hInputTokens = Sum(a.CacheWrite1hInputTokens, b.CacheWrite1hInputTokens),
            CacheWrite30mInputTokens = Sum(a.CacheWrite30mInputTokens, b.CacheWrite30mInputTokens),
            CacheWrite12hInputTokens = Sum(a.CacheWrite12hInputTokens, b.CacheWrite12hInputTokens)
        };
    }
    private static void RecordCharge(Run run, PlaygroundProviderKind provider, string model, PlaygroundUsage? usage,
        DateTimeOffset at, PlaygroundChargeKind kind)
    {
        if (!PlaygroundPricing.IsValidUsage(usage))
        { run.Cost = PlaygroundPricing.Combine(run.Cost, PlaygroundPricing.Unknown("服务商用量结构无效，未推定为免费请求。")); return; }
        if (provider != PlaygroundProviderKind.Grok && usage?.ReportedCostUsd is not null) usage = usage with { ReportedCostUsd = null };
        run.Usage = AddUsage(run.Usage, usage);
        run.Cost = PlaygroundPricing.Combine(run.Cost, PlaygroundPricing.Calculate(provider, model, usage, at, kind));
    }
}

