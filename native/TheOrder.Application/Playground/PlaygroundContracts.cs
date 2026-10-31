using System.Collections.Immutable;

namespace TheOrder.Application.Playground;

public enum PlaygroundProviderKind { OpenAI, Gemini, Claude, Grok, DeepSeek }
public enum PlaygroundSearchMode { Off, Smart, Deep }
public enum PlaygroundSpoilerMode { Strict, Progress, Full, LibraryStatus }
public enum PlaygroundPhase { Discussion, PublicResearch }
public enum PlaygroundMessageStatus { Generating, Complete, Failed, Cancelled, Interrupted }
public enum PlaygroundSourceKind { LocalRecord, ChatMessage, Memory, Web, Attachment }
public enum PlaygroundRetention { Portable, DisplayOnly, SessionOnly }
public enum PlaygroundProviderEventKind { Text, ToolCall, Source, Usage, Continuation, Activity, Completed, Refusal, Error, ConversationTape }
public enum PlaygroundFailureKind { Authentication, AccessDenied, RateLimited, Quota, ModelUnavailable, Unsupported, Network, Timeout, InvalidResponse, Refused, Cancelled, Unknown }

public sealed record PlaygroundIdentity(Guid ProfileId, Guid StoreId);
public sealed record PlaygroundPermissions(bool Library = false, bool History = false, bool Memory = false, bool AutoMemory = false);
public sealed record PlaygroundConnection(PlaygroundProviderKind Provider, string Endpoint, string Model = "", bool Consented = false);
public sealed record PlaygroundSettings
{
    // Legacy storage compatibility; no longer an independent sending gate.
    public bool Enabled { get; init; }
    public bool? DiscussionConsent { get; init; }
    public PlaygroundPermissions Permissions { get; init; } = new();
    public ImmutableArray<PlaygroundConnection> Connections { get; init; } = [];
    // Legacy persisted values remain readable but no longer impose invocation counts.
    public int MaximumToolRounds { get; init; } = 0;
    public int MaximumSearches { get; init; } = 0;
    public int DeepMaximumSearches { get; init; } = 0;
    public int MaximumOutputTokens { get; init; } = 65536;
    public int MaximumContextCharacters { get; init; } = 256000;
    public int TimeoutSeconds { get; init; } = 900;
}
public sealed record PlaygroundConversationOptions
{
    public PlaygroundProviderKind Provider { get; init; }
    public string Endpoint { get; init; } = "https://api.openai.com/v1";
    public string Model { get; init; } = "";
    public PlaygroundSearchMode Search { get; init; } = PlaygroundSearchMode.Smart;
    public PlaygroundSpoilerMode Spoiler { get; init; } = PlaygroundSpoilerMode.LibraryStatus;
    public string? ReasoningEffort { get; init; } = "high";
    public bool UseSavedContext { get; init; } = true;
    public string SystemInstructions { get; init; } = "";
    public ImmutableDictionary<string, string> WorkProgress { get; init; } = ImmutableDictionary<string, string>.Empty;
}
public sealed record PlaygroundConversation(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    PlaygroundConversationOptions Options, bool Temporary = false);
public sealed record PlaygroundUsage(long? InputTokens = null, long? OutputTokens = null, long? ReasoningTokens = null,
    long? CachedInputTokens = null, int? Searches = null, int? Fetches = null)
{
    // InputTokens includes uncached, cache reads and cache writes. OutputTokens
    // includes reasoning; ReasoningTokens is a subset, never an extra charge.
    public string? BillingModel { get; init; }
    public string? ServiceTier { get; init; }
    public decimal? ReportedCostUsd { get; init; }
    public long? CacheWriteInputTokens { get; init; }
    public long? CacheWrite5mInputTokens { get; init; }
    public long? CacheWrite1hInputTokens { get; init; }
    public long? CacheWrite30mInputTokens { get; init; }
    public long? CacheWrite12hInputTokens { get; init; }
}
public sealed record PlaygroundSource(string Id, PlaygroundSourceKind Kind, string Title)
{
    public string? Url { get; init; }
    public string? TargetId { get; init; }
    public string? MessageId { get; init; }
    public string? WorkKey { get; init; }
    public DateTimeOffset? At { get; init; }
    public string? Quote { get; init; }
    public int? Start { get; init; }
    public int? End { get; init; }
    public bool HasExactRange { get; init; }
    public PlaygroundRetention Retention { get; init; }
}
// An explicit, read-only text snapshot. No path or filesystem capability crosses this boundary.
public sealed record PlaygroundAttachment(string Name, string Text)
{
    public string? MediaType { get; init; }
    public string? ImageBase64 { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
}
public sealed record PlaygroundMessage(Guid Id, Guid ConversationId, string Role, string Text, DateTimeOffset CreatedAt,
    PlaygroundMessageStatus Status, Guid? TurnId = null)
{
    public PlaygroundProviderKind? Provider { get; init; }
    public string? Model { get; init; }
    public ImmutableArray<PlaygroundSource> Sources { get; init; } = [];
    public ImmutableArray<PlaygroundAttachment> Attachments { get; init; } = [];
    // Host-recorded dependencies, independent of whether the model prints a citation.
    public ImmutableArray<Guid> ContextMessageIds { get; init; } = [];
    public ImmutableArray<string> Activities { get; init; } = [];
    public PlaygroundUsage? Usage { get; init; }
    public PlaygroundCostEstimate? Cost { get; init; }
    public string? Error { get; init; }
    public PlaygroundRetention Retention { get; init; }
    public string? SearchSuggestionsHtml { get; init; }
}
public sealed record PlaygroundMemory(Guid Id, string Text, Guid ConversationId, Guid MessageId, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string Kind = "explicit", string? WorkKey = null)
{
    public string? OriginalQuote { get; init; }
    public bool Disabled { get; init; }
    public Guid? SupersededBy { get; init; }
}
public sealed record PlaygroundSuppression(Guid Id, Guid MemoryId, string Text, ImmutableArray<Guid> MessageIds, DateTimeOffset At, string? WorkKey = null);
public sealed record PlaygroundState
{
    public int Version { get; init; } = 1;
    public required PlaygroundIdentity Identity { get; init; }
    public long Revision { get; init; }
    public PlaygroundSettings Settings { get; init; } = new();
    public ImmutableArray<PlaygroundConversation> Conversations { get; init; } = [];
    public ImmutableArray<PlaygroundMessage> Messages { get; init; } = [];
    public ImmutableArray<PlaygroundMemory> Memories { get; init; } = [];
    public ImmutableArray<PlaygroundSuppression> Suppressions { get; init; } = [];
}
public sealed record PlaygroundActiveSession(string GameId, string Status, long ConfirmedSeconds, DateTimeOffset ObservedAt);
public sealed record PlaygroundChange(Guid? ConversationId, Guid? TurnId, string Kind);

public interface IPlaygroundStore
{
    PlaygroundIdentity Identity { get; }
    Task<PlaygroundState?> LoadAsync(CancellationToken cancellation);
    Task SaveAsync(PlaygroundState state, long expectedRevision, CancellationToken cancellation);
}
public interface IPlaygroundCredentials
{
    Task<bool> HasAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation);
    Task<string?> ReadAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation);
    Task SaveAsync(PlaygroundProviderKind provider, string endpoint, string key, CancellationToken cancellation);
    Task ClearAsync(PlaygroundProviderKind provider, string endpoint, CancellationToken cancellation);
}

public sealed record PlaygroundModel(string Id, string Name, PlaygroundCapabilities Capabilities)
{
    // Catalog metadata is not proof that this Key may generate with the model.
    public string? Version { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public ImmutableArray<string> GenerationMethods { get; init; } = [];
    public bool GenerationMethodsReported { get; init; }
    public string? Lifecycle { get; init; }
}
public sealed record PlaygroundCapabilities(bool Verified = false, bool Streaming = true, bool Tools = false, bool Search = false)
{
    public bool ImageInput { get; init; }
    public ImmutableArray<string> ReasoningEfforts { get; init; } = [];
    public string? Notice { get; init; }
    public long? ContextTokens { get; init; }
}
public sealed record PlaygroundToolDefinition(string Name, string Description, string ParametersJson);
public sealed record PlaygroundToolCall(string Id, string Name, string ArgumentsJson);
public sealed record PlaygroundWireMessage(string Role, string Text, string? ToolCallId = null, string? ToolName = null)
{
    public ImmutableArray<PlaygroundAttachment> Attachments { get; init; } = [];
}
// Private provider state is short-lived and never exported, indexed or shown as reasoning.
public sealed class PlaygroundContinuation(string binding, string data)
{
    public string Binding { get; } = binding;
    public string Data { get; } = data;
    public override string ToString() => "[provider-private-state]";
}
// Only a same-process host cache may own this stateless native conversation tape.
// It is never part of PlaygroundState, an export, a source, or displayed reasoning.
public sealed class PlaygroundConversationTape(string binding, string data)
{
    public string Binding { get; } = binding;
    public string Data { get; } = data;
    public override string ToString() => "[provider-private-conversation-state]";
}
public sealed record PlaygroundProviderRequest(Guid ConversationId, Guid TurnId, PlaygroundProviderKind Provider,
    string Endpoint, string Model, string Instructions, ImmutableArray<PlaygroundWireMessage> Messages)
{
    public PlaygroundPhase Phase { get; init; }
    public ImmutableArray<PlaygroundToolDefinition> Tools { get; init; } = [];
    public ImmutableArray<PlaygroundWireMessage> ToolResults { get; init; } = [];
    public PlaygroundContinuation? Continuation { get; init; }
    public PlaygroundConversationTape? ConversationTape { get; init; }
    public string Binding { get; init; } = "";
    public string ConversationBinding { get; init; } = "";
    public string? ReasoningEffort { get; init; } = "high";
    public int MaximumOutputTokens { get; init; } = 65536;
    public int MaximumToolCalls { get; init; } = 0;
}
public sealed record PlaygroundFailure(PlaygroundFailureKind Kind, string Message, bool Retryable = false, int? HttpStatus = null, int? RetryAfterSeconds = null);
public sealed record PlaygroundProviderEvent(PlaygroundProviderEventKind Kind)
{
    public string? Text { get; init; }
    public PlaygroundToolCall? ToolCall { get; init; }
    public PlaygroundSource? Source { get; init; }
    public PlaygroundUsage? Usage { get; init; }
    public PlaygroundContinuation? Continuation { get; init; }
    public PlaygroundConversationTape? ConversationTape { get; init; }
    public PlaygroundFailure? Failure { get; init; }
    public string? StopReason { get; init; }
}
public sealed record PlaygroundPublicSearchRequest(Guid ConversationId, Guid TurnId, PlaygroundProviderKind Provider,
    string Endpoint, string Model, string Query, int MaximumSearches = 0, int MaximumOutputTokens = 65536)
{
    public string? ReasoningEffort { get; init; } = "high";
}
public sealed record PlaygroundPublicFetchRequest(Guid ConversationId, Guid TurnId, PlaygroundProviderKind Provider,
    string Endpoint, string Model, string Url);
public sealed record PlaygroundResearchResult(string Text, ImmutableArray<PlaygroundSource> Sources, PlaygroundUsage? Usage = null)
{
    public PlaygroundRetention Retention { get; init; }
    public string? SearchSuggestionsHtml { get; init; }
}
public interface IPlaygroundProvider
{
    PlaygroundProviderKind Kind { get; }
    string DefaultEndpoint { get; }
    PlaygroundCapabilities Capabilities(string model, string endpoint);
    Task<ImmutableArray<PlaygroundModel>> ListModelsAsync(string endpoint, CancellationToken cancellation);
    IAsyncEnumerable<PlaygroundProviderEvent> SendAsync(PlaygroundProviderRequest request, CancellationToken cancellation);
    Task<PlaygroundResearchResult> SearchAsync(PlaygroundPublicSearchRequest request, CancellationToken cancellation);
    Task<PlaygroundResearchResult> FetchAsync(PlaygroundPublicFetchRequest request, CancellationToken cancellation)
        => Task.FromException<PlaygroundResearchResult>(new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported,
            "此服务商或型号尚未核实网页读取能力。")));
}
public sealed class PlaygroundProviderException(PlaygroundFailure failure) : Exception(failure.Message)
{
    public PlaygroundFailure Failure { get; } = failure;
}
