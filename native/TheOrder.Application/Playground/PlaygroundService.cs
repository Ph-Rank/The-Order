using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application.Playground;

// Application-owned lifetime. Views only observe immutable snapshots; providers
// and tools never receive the profile store or a mutable library object.
public sealed partial class PlaygroundService : IAsyncDisposable
{
    private readonly IPlaygroundStore store;
    private readonly IPlaygroundCredentials credentials;
    private readonly Dictionary<PlaygroundProviderKind, IPlaygroundProvider> providers;
    private readonly Func<LibrarySnapshot?> library;
    private readonly Func<PlaygroundActiveSession?> activeSession;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private PlaygroundState state;
    private long generation;
    private bool initialized, closing, disposed, configurationUncertain;
    private Run? active;
    public event Action<PlaygroundChange>? Changed;
    public PlaygroundState Snapshot { get { lock (gate) return state; } }
    public bool IsGenerating { get { lock (gate) return active is { } r && !r.Cancel.IsCancellationRequested; } }
    public Guid? ActiveConversationId { get { lock (gate) return active?.Conversation.Id; } }

    public PlaygroundService(IPlaygroundStore store, IPlaygroundCredentials credentials, IEnumerable<IPlaygroundProvider> providers,
        Func<LibrarySnapshot?> library, Func<PlaygroundActiveSession?>? activeSession = null, Func<DateTimeOffset>? clock = null)
    {
        this.store = store; this.credentials = credentials; this.providers = providers.ToDictionary(p => p.Kind);
        this.library = library; this.activeSession = activeSession ?? (() => null); this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        state = new() { Identity = store.Identity };
    }
    public async Task InitializeAsync(CancellationToken cancellation = default)
    {
        await writer.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (gate) { ThrowClosed(); if (initialized) return; }
            var loaded = await store.LoadAsync(cancellation).ConfigureAwait(false) ?? new() { Identity = store.Identity };
            Validate(loaded);
            var recovered = loaded with { Conversations = loaded.Conversations.Select(c => c.Options.Search == PlaygroundSearchMode.Deep
                ? c with { Options = c.Options with { Search = PlaygroundSearchMode.Smart } } : c).ToImmutableArray(), Messages = loaded.Messages.Select(m => m.Status == PlaygroundMessageStatus.Generating
                ? m with { Status = PlaygroundMessageStatus.Interrupted, Error = "上次回复已中断；可手动重试，程序不会自动重新发送。" } : m).ToImmutableArray() };
            if (!recovered.Messages.SequenceEqual(loaded.Messages) || !recovered.Conversations.SequenceEqual(loaded.Conversations))
            { recovered = recovered with { Revision = checked(loaded.Revision + 1) }; await store.SaveAsync(recovered, loaded.Revision, cancellation).ConfigureAwait(false); }
            lock (gate) { state = recovered; initialized = true; }
        }
        finally { writer.Release(); }
        Notify(null, null, "initialized");
    }
    public async Task<Guid> CreateConversationAsync(bool temporary = false, CancellationToken cancellation = default)
    {
        await InitializeAsync(cancellation).ConfigureAwait(false);
        var id = Guid.NewGuid(); var now = clock();
        await Mutate(s =>
        {
            if (s.Conversations.Length >= 500) throw new InvalidOperationException("会话数量已达上限，请先整理现有会话。");
            var connection = s.Settings.Connections.FirstOrDefault();
            var options = new PlaygroundConversationOptions { UseSavedContext = !temporary };
            if (connection is not null) options = options with { Provider = connection.Provider, Endpoint = connection.Endpoint, Model = connection.Model };
            return s with { Conversations = s.Conversations.Add(new(id, temporary ? "临时会话" : "新会话", now, now, options, temporary)) };
        }, cancellation, !temporary).ConfigureAwait(false);
        Notify(id, null, "conversation"); return id;
    }
    public Task RenameConversationAsync(Guid id, string title, CancellationToken cancellation = default)
    {
        title = title.Trim(); if (title.Length is < 1 or > 120 || title.Any(char.IsControl)) throw new ArgumentException("会话名称应为 1–120 个字符。");
        return ChangeConversation(id, c => c with { Title = title, UpdatedAt = clock() }, cancellation);
    }
    public Task UpdateConversationAsync(Guid id, PlaygroundConversationOptions options, CancellationToken cancellation = default)
    {
        options = options with { Endpoint = Endpoint(options.Provider, options.Endpoint),
            Search = options.Search == PlaygroundSearchMode.Deep ? PlaygroundSearchMode.Smart : options.Search };
        Validate(options);
        var current = Snapshot.Conversations.FirstOrDefault(c => c.Id == id);
        if (current is not null && (current.Options.UseSavedContext && !options.UseSavedContext || current.Options.Spoiler != options.Spoiler
            || current.Options.Search != options.Search || current.Options.SystemInstructions != options.SystemInstructions
            || current.Options.WorkProgress.Count != options.WorkProgress.Count
            || current.Options.WorkProgress.Any(p => !options.WorkProgress.TryGetValue(p.Key, out var value) || value != p.Value))) Invalidate();
        return ChangeConversation(id, c => c with { Options = options, UpdatedAt = clock() }, cancellation);
    }
    private async Task ChangeConversation(Guid id, Func<PlaygroundConversation, PlaygroundConversation> change, CancellationToken cancellation)
    {
        await InitializeAsync(cancellation).ConfigureAwait(false); var temporary = Conversation(id).Temporary;
        await Mutate(s => s with { Conversations = s.Conversations.Select(c => c.Id == id ? change(c) : c).ToImmutableArray() }, cancellation, !temporary).ConfigureAwait(false);
        Notify(id, null, "conversation");
    }
    public async Task DeleteConversationAsync(Guid id, CancellationToken cancellation = default)
    {
        Invalidate(); await InitializeAsync(cancellation).ConfigureAwait(false); var temporary = Conversation(id).Temporary;
        await Mutate(s =>
        {
            var removed = s.Messages.Where(m => m.ConversationId == id).Select(m => m.Id).ToHashSet();
            return s with { Conversations = s.Conversations.Where(c => c.Id != id).ToImmutableArray(),
                Messages = s.Messages.Where(m => m.ConversationId != id).ToImmutableArray(),
                Memories = s.Memories.Where(m => m.ConversationId != id && !removed.Contains(m.MessageId)).ToImmutableArray(),
                Suppressions = s.Suppressions.Where(x => !x.MessageIds.All(removed.Contains)).ToImmutableArray() };
        }, cancellation, !temporary).ConfigureAwait(false);
        Notify(id, null, "deleted");
    }
    public async Task ConfigureAsync(PlaygroundSettings settings, CancellationToken cancellation = default)
    {
        settings = settings with { Connections = settings.Connections.Select(c => c with { Endpoint = Endpoint(c.Provider, c.Endpoint) }).ToImmutableArray() };
        Validate(settings);
        // Capture the user's revocation intent before waiting for a writer that
        // may already be publishing the last model response atomically.
        Invalidate(); lock (gate) configurationUncertain = true;
        await InitializeAsync(cancellation).ConfigureAwait(false);
        // A failed revocation save pauses sending for this process. It must not
        // silently continue under the old disk grant after the user's intent.
        await Mutate(s => s with { Settings = settings }, cancellation).ConfigureAwait(false);
        lock (gate) configurationUncertain = false;
        Notify(null, null, "settings");
    }
    public IReadOnlyList<PlaygroundConversation> SearchConversations(string query)
    {
        var s = Snapshot; return s.Conversations.Where(c => !c.Temporary).Select(c => (Conversation: c,
            Score: Match(c.Title, query) + s.Messages.Where(m => m.ConversationId == c.Id && m.Retention == PlaygroundRetention.Portable)
                .Select(m => Match(m.Text, query)).DefaultIfEmpty(0).Max())).Where(x => string.IsNullOrWhiteSpace(query) || x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Conversation.UpdatedAt).Take(100).Select(x => x.Conversation).ToArray();
    }
    public IReadOnlyList<PlaygroundMemory> SearchMemories(string query) => Snapshot.Memories
        .Where(m => string.IsNullOrWhiteSpace(query) || Match(m.Text, query) > 0).OrderByDescending(m => m.UpdatedAt).Take(200).ToArray();
    public async Task EditMemoryAsync(Guid id, string text, CancellationToken cancellation = default)
    {
        text = text.Trim(); if (text.Length is < 1 or > 2000) throw new ArgumentException("记忆内容应为 1–2000 个字符。");
        Invalidate(); await Mutate(s => s with { Memories = s.Memories.Select(m => m.Id == id
            ? m with { Text = text, Kind = "user-edited", UpdatedAt = clock(), SupersededBy = null } : m).ToImmutableArray() }, cancellation).ConfigureAwait(false);
        Notify(null, null, "memory");
    }
    public async Task ForgetMemoryAsync(Guid id, CancellationToken cancellation = default)
    {
        Invalidate(); await Mutate(s =>
        {
            var memory = s.Memories.FirstOrDefault(m => m.Id == id) ?? throw new ArgumentException("记忆已不存在。");
            var users = s.Messages.Where(m => m.Role == "user" && (m.Id == memory.MessageId ||
                Normalize(m.Text).Contains(Normalize(memory.OriginalQuote ?? memory.Text), StringComparison.Ordinal))).Select(m => m.Id).ToImmutableArray();
            var direct = s.Messages.Where(m => m.Role == "assistant" && m.Sources.Any(x => x.TargetId == id.ToString("N"))).Select(m => m.Id);
            var messages = SuppressedMessageIds(s, users.Concat(direct)).ToImmutableArray();
            return s with { Memories = s.Memories.Where(m => m.Id != id && !messages.Contains(m.MessageId)).ToImmutableArray(),
                Suppressions = s.Suppressions.Add(new(Guid.NewGuid(), id, Hash(memory.OriginalQuote ?? memory.Text), messages, clock(), memory.WorkKey)),
                Messages = s.Messages.Select(m => messages.Contains(m.Id) && m.Role == "assistant" ? m with { Sources = [] } : m).ToImmutableArray() };
        }, cancellation).ConfigureAwait(false);
        Notify(null, null, "forgotten");
    }
    public async Task SetMemoryDisabledAsync(Guid id, bool disabled, CancellationToken cancellation = default)
    {
        Invalidate(); await Mutate(s => s with { Memories = s.Memories.Select(m => m.Id == id ? m with { Disabled = disabled, UpdatedAt = clock() } : m).ToImmutableArray() }, cancellation).ConfigureAwait(false);
        Notify(null, null, "memory");
    }
    public Task<bool> HasCredentialAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation = default)
        => credentials.HasAsync(provider, Endpoint(provider, endpoint), cancellation);
    public async Task SaveCredentialAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation = default)
    { Invalidate(); await credentials.SaveAsync(provider, Endpoint(provider, endpoint), key, cancellation).ConfigureAwait(false); Notify(null, null, "credentials"); }
    public async Task ClearCredentialAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation = default)
    { Invalidate(); await credentials.ClearAsync(provider, Endpoint(provider, endpoint), cancellation).ConfigureAwait(false); Notify(null, null, "credentials"); }
    public async Task<ImmutableArray<PlaygroundModel>> ListModelsAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation = default)
    {
        var s = Snapshot; endpoint = Endpoint(provider, endpoint);
        if (!PlaygroundAuthorizationPolicy.CanSend(s.Settings))
            throw new InvalidOperationException("请先启用对话功能并确认全局讨论授权。");
        lock (gate) if (configurationUncertain) throw new InvalidOperationException("授权设置尚未安全保存，请先重新保存设置。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(s.Settings.TimeoutSeconds));
        try { return await Provider(provider).ListModelsAsync(endpoint, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new PlaygroundProviderException(new(PlaygroundFailureKind.Timeout, "模型列表读取超时；可手动重试，或直接填写模型 ID。")); }
    }
    public PlaygroundCapabilities Capabilities(PlaygroundProviderKind provider, string endpoint, string model)
        => Provider(provider).Capabilities(model, Endpoint(provider, endpoint));
    private IPlaygroundProvider Provider(PlaygroundProviderKind kind) => providers.GetValueOrDefault(kind)
        ?? throw new InvalidOperationException("此服务商暂不可用。");
    private PlaygroundConversation Conversation(Guid id) => Snapshot.Conversations.FirstOrDefault(c => c.Id == id)
        ?? throw new ArgumentException("会话已不存在。");
    private async Task Mutate(Func<PlaygroundState, PlaygroundState> change, CancellationToken cancellation, bool persist = true, bool allowClosing = false, Run? finalAdmission = null)
    {
        if (!initialized) await InitializeAsync(cancellation).ConfigureAwait(false);
        await writer.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            PlaygroundState before; lock (gate) { if (disposed || closing && !allowClosing) throw new ObjectDisposedException(nameof(PlaygroundService)); before = state; }
            cancellation.ThrowIfCancellationRequested(); var next = change(before);
            if (persist)
            {
                next = next with { Revision = checked(before.Revision + 1) };
                var durable = Durable(next); Validate(durable);
                await store.SaveAsync(durable, before.Revision, cancellation).ConfigureAwait(false);
            }
            if (finalAdmission is { } run)
            {
                bool libraryCurrent = run.Conversation.Options.Spoiler != PlaygroundSpoilerMode.LibraryStatus && run.PlaytimeRankingStamp is null || LibraryCurrent(run);
                bool current; lock (gate) current = libraryCurrent && active == run && generation == run.Generation && !run.Cancel.IsCancellationRequested && !closing && !disposed;
                if (!current)
                {
                    // A store may already have entered its indivisible publication
                    // when revocation arrives. Preserve the acknowledged revision,
                    // then atomically remove late output/derived preferences before
                    // exposing any new snapshot or admitting a subsequent request.
                    var cancelled = next with { Messages = next.Messages.Select(m => m.Id == run.AssistantId ? m with {
                        Text = "", Status = PlaygroundMessageStatus.Cancelled, Error = "状态或授权已变化，迟到内容未接纳。", Sources = [], SearchSuggestionsHtml = null } : m).ToImmutableArray(),
                        Memories = before.Memories };
                    if (persist)
                    {
                        cancelled = cancelled with { Revision = checked(next.Revision + 1) };
                        await store.SaveAsync(Durable(cancelled), next.Revision, CancellationToken.None).ConfigureAwait(false);
                    }
                    next = cancelled;
                }
            }
            // Do not turn a completed atomic save into a memory/disk mismatch
            // by throwing cancellation after the store has acknowledged it.
            lock (gate)
            {
                if (active is { } running && finalAdmission is null && next.Messages.Any(m => m.Id == running.AssistantId && m.Status == PlaygroundMessageStatus.Generating)
                    && state.Messages.FirstOrDefault(m => m.Id == running.AssistantId) is { Status: PlaygroundMessageStatus.Generating } preview)
                    next = next with { Messages = next.Messages.Select(m => m.Id == preview.Id ? m with { Text = preview.Text, Activities = preview.Activities } : m).ToImmutableArray() };
                state = next;
            }
        }
        finally { writer.Release(); }
    }
    private static PlaygroundState Durable(PlaygroundState s)
    {
        var conversations = s.Conversations.Where(c => !c.Temporary).ToImmutableArray(); var ids = conversations.Select(c => c.Id).ToHashSet();
        var messages = s.Messages.Where(m => ids.Contains(m.ConversationId)).Select(m => m.Retention != PlaygroundRetention.Portable
            ? m with { Text = "本条公开检索答复仅保留在本次会话中，未写入长期上下文。", Attachments = [], Sources = [], SearchSuggestionsHtml = null }
            : m with { SearchSuggestionsHtml = null }).ToImmutableArray();
        var messageIds = messages.Select(m => m.Id).ToHashSet();
        return s with { Conversations = conversations, Messages = messages,
            Memories = s.Memories.Where(m => ids.Contains(m.ConversationId) && messageIds.Contains(m.MessageId)).ToImmutableArray(),
            Suppressions = s.Suppressions.Select(x => x with { MessageIds = x.MessageIds.Where(messageIds.Contains).ToImmutableArray() }).ToImmutableArray() };
    }
    private void Notify(Guid? conversation, Guid? turn, string kind)
    { foreach (Action<PlaygroundChange> listener in Changed?.GetInvocationList() ?? []) try { listener(new(conversation, turn, kind)); } catch { /* An observer cannot own the request lifecycle. */ } }
    private void ThrowClosed() { if (disposed || closing) throw new ObjectDisposedException(nameof(PlaygroundService)); }
    private void Invalidate(Guid? conversation = null)
    {
        Run? cancel; lock (gate) { if (conversation is not null && active?.Conversation.Id != conversation) return; generation++; nativeTape = null; cancel = active; if (cancel is not null) cancel.Continuation = null; }
        cancel?.Cancel.Cancel();
        if (cancel is not null) Notify(cancel.Conversation.Id, cancel.Id, "stopping");
    }
    public void Stop() { Invalidate(); }
    public async ValueTask DisposeAsync()
    {
        Run? run; lock (gate) { if (disposed || closing) return; closing = true; generation++; nativeTape = null; run = active; }
        run?.Cancel.Cancel();
        if (run is not null) try { await run.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
        lock (gate) { disposed = true; active = null; }
    }
}

