using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;
using TheOrder.Application.Playground;

namespace TheOrder.Platform.Windows;

// Profile-owned sidecar. It never opens another library writer or changes its schema.
public sealed class WindowsPlaygroundStore : IPlaygroundStore
{
    private readonly PlaygroundDisk disk;
    private readonly SemaphoreSlim serial = new(1, 1);
    public PlaygroundIdentity Identity => disk.Identity;
    public WindowsPlaygroundStore(NativeConfiguration configuration) => disk = new(configuration);
    public async Task<PlaygroundState?> LoadAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await disk.WithDirectory(false, folder => Task.FromResult(folder is null ? null : Read(folder, "state-v1.dpapi")), cancellation).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    public async Task SaveAsync(PlaygroundState state, long expectedRevision, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await disk.WithDirectory(true, async folder =>
            {
                var prior = Read(folder!, "state-v1.dpapi");
                if ((prior?.Revision ?? 0) != expectedRevision || state.Revision != expectedRevision + 1)
                    throw new InvalidOperationException("Playground 资料已变化，请重新读取后再保存。");
                var clean = PersistentProjection(state); Validate(clean);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(clean, PlaygroundDisk.Json);
                if (bytes.Length > PlaygroundDisk.MaximumBytes) throw new InvalidDataException("Playground 资料超过容量限制，请先整理对话。");
                cancellation.ThrowIfCancellationRequested();
                var protectedBytes = WindowsDataProtection.Protect(bytes, true);
                CryptographicOperations.ZeroMemory(bytes);
                try
                {
                    // Cancellation before the atomic commit; once begun, finish consistently.
                    cancellation.ThrowIfCancellationRequested();
                    var target = Path.Combine(folder!, "state-v1.dpapi");
                    if (prior is not null)
                    {
                        // A recovery point may roll back content, never privacy decisions.
                        var recovery = RecoveryProjection(prior, clean); Validate(recovery);
                        var recoveryPlain = JsonSerializer.SerializeToUtf8Bytes(recovery, PlaygroundDisk.Json);
                        if (recoveryPlain.Length > PlaygroundDisk.MaximumBytes)
                        { CryptographicOperations.ZeroMemory(recoveryPlain); throw new InvalidDataException("Playground 恢复资料超过容量限制，请先整理对话。"); }
                        byte[] recoveryCipher;
                        try { recoveryCipher = WindowsDataProtection.Protect(recoveryPlain, true); }
                        finally { CryptographicOperations.ZeroMemory(recoveryPlain); }
                        try { await WindowsAtomicFile.WriteAsync(Path.Combine(folder!, "state-v1.lastgood.dpapi"), recoveryCipher).ConfigureAwait(false); }
                        finally { CryptographicOperations.ZeroMemory(recoveryCipher); }
                    }
                    await WindowsAtomicFile.WriteAsync(target, protectedBytes).ConfigureAwait(false);
                }
                finally { CryptographicOperations.ZeroMemory(protectedBytes); }
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
    public async Task RestoreLastGoodAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await disk.WithDirectory(false, async folder =>
            {
                if (folder is null) throw new InvalidDataException("没有可恢复的 Playground 备份。");
                var valid = Read(folder, "state-v1.lastgood.dpapi") ?? throw new InvalidDataException("没有可恢复的 Playground 备份。");
                Validate(valid);
                // Never downgrade a readable future format. Unknown/corrupt bytes are preserved.
                var current = PlaygroundDisk.ReadBytes(Path.Combine(folder, "state-v1.dpapi"));
                if (current is not null)
                {
                    try
                    {
                        var plain = WindowsDataProtection.Protect(current, false);
                        try
                        {
                            using var document = JsonDocument.Parse(plain);
                            string[] known = ["Version", "Identity", "Revision", "Settings", "Conversations", "Messages", "Memories", "Suppressions"];
                            if (document.RootElement.ValueKind != JsonValueKind.Object
                                || document.RootElement.TryGetProperty("Version", out var version) && version.GetInt32() != 1
                                || document.RootElement.EnumerateObject().Any(p => !known.Contains(p.Name, StringComparer.Ordinal)))
                                throw new NotSupportedException("较新或未知格式的 Playground 资料不能由此版本回退。");
                        }
                        finally { CryptographicOperations.ZeroMemory(plain); }
                    }
                    catch (Exception error) when (error is CryptographicException or JsonException) { }
                    var preserved = Path.Combine(folder, "state-preserved-" + Guid.NewGuid().ToString("N") + ".dpapi");
                    await WindowsAtomicFile.WriteAsync(preserved, current).ConfigureAwait(false);
                }
                cancellation.ThrowIfCancellationRequested();
                await WindowsAtomicFile.WriteAsync(Path.Combine(folder, "state-v1.dpapi"), PlaygroundDisk.ReadBytes(Path.Combine(folder, "state-v1.lastgood.dpapi"))!).ConfigureAwait(false);
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        finally { serial.Release(); }
    }
    private PlaygroundState? Read(string folder, string name)
    {
        var encrypted = PlaygroundDisk.ReadBytes(Path.Combine(folder, name)); if (encrypted is null) return null;
        byte[] plain;
        try { plain = WindowsDataProtection.Protect(encrypted, false); }
        catch (CryptographicException) { throw new InvalidDataException("Playground 资料无法在当前 Windows 用户下解密，原文件已保留。"); }
        try
        {
            var state = JsonSerializer.Deserialize<PlaygroundState>(plain, PlaygroundDisk.Json) ?? throw new InvalidDataException("Playground 资料为空。");
            Validate(state); return state;
        }
        catch (JsonException) { throw new InvalidDataException("Playground 资料格式无法安全读取，原文件已保留。"); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private void Validate(PlaygroundState state)
    {
        if (state.Version != 1) throw new NotSupportedException("Playground 资料格式不受此版本支持，原文件已保留。");
        if (state.Identity != Identity || state.Revision < 0 || state.Revision > 9_007_199_254_740_991
            || state.Settings is null || state.Settings.Permissions is null
            || state.Conversations.IsDefault || state.Messages.IsDefault || state.Memories.IsDefault || state.Suppressions.IsDefault
            || state.Conversations.Length > 500 || state.Messages.Length > 20000 || state.Memories.Length > 5000 || state.Suppressions.Length > 20000)
            throw new InvalidDataException("Playground 资料身份、版本或数量不符合约定。");
        var conversations = state.Conversations.Select(x => x.Id).ToHashSet();
        if (conversations.Count != state.Conversations.Length || conversations.Contains(Guid.Empty)
            || state.Messages.Select(m => m.Id).Distinct().Count() != state.Messages.Length
            || state.Messages.Any(m => m.Id == Guid.Empty || !conversations.Contains(m.ConversationId) || m.Text is null || m.Text.Length > 1048576
                || !ValidAttachments(m) || !ValidAccounting(m)
                || m.Role is not ("user" or "assistant" or "research" or "system") || !Enum.IsDefined(m.Status) || !Enum.IsDefined(m.Retention)
                || m.ContextMessageIds.IsDefault || m.ContextMessageIds.Length > 1024 || m.ContextMessageIds.Contains(m.Id)
                || m.ContextMessageIds.Contains(Guid.Empty) || m.ContextMessageIds.Distinct().Count() != m.ContextMessageIds.Length)
            || state.Conversations.Any(c => c.Title is null || c.Title.Length > 256 || c.Options is null || c.Temporary)
            || state.Memories.Any(m => m.Text is null || m.Text.Length > 8000 || m.OriginalQuote?.Length > 8000)
            || state.Suppressions.Any(s => s.Text is null || s.Text.Length > 256))
            throw new InvalidDataException("Playground 对话或记忆结构无效，原文件已保留。");
    }
    private static bool ValidAttachments(PlaygroundMessage message)
    {
        if (message.Attachments.IsDefault || message.Role != "user" && !message.Attachments.IsEmpty) return false;
        try { PlaygroundAttachmentRules.Validate(message.Attachments); return true; }
        catch (ArgumentException) { return false; }
    }
    private static bool ValidAccounting(PlaygroundMessage message)
    {
        if (!PlaygroundPricing.IsValidStoredUsage(message) || !PlaygroundPricing.IsValidCost(message.Cost)) return false;
        if (message.Usage is null && message.Cost is null) return true; // Existing v1 records remain readable.
        if (message.Role is not ("assistant" or "research") || message.Provider is not { } provider || !Enum.IsDefined(provider)) return false;
        // A cost-shaped object alone is not evidence of a provider charge.
        // Only xAI's documented native usage amount is accepted as reported
        // actual, and the saved summary must match that same request aggregate.
        if (message.Usage?.ReportedCostUsd is not null && provider != PlaygroundProviderKind.Grok) return false;
        return message.Cost is not { IsActual: true } actual || provider == PlaygroundProviderKind.Grok
            && message.Usage?.ReportedCostUsd is { } reported && actual.Minimum == reported && actual.Maximum == reported;
    }
    public static PlaygroundState PersistentProjection(PlaygroundState state)
    {
        var conversations = state.Conversations.Where(c => !c.Temporary).ToImmutableArray();
        var ids = conversations.Select(c => c.Id).ToHashSet();
        var messages = state.Messages.Where(m => ids.Contains(m.ConversationId)).Select(m => m.Retention != PlaygroundRetention.Portable
            ? m with { Text = "该公开检索答复仅在当次会话中展示，未长期保存；可以重新检索。", Attachments = [], Sources = [], SearchSuggestionsHtml = null }
            : m with { SearchSuggestionsHtml = null }).ToImmutableArray();
        var users = messages.Where(m => m.Role == "user").Select(m => m.Id).ToHashSet();
        return state with { Conversations = conversations, Messages = messages,
            Memories = state.Memories.Where(m => ids.Contains(m.ConversationId) && users.Contains(m.MessageId)).ToImmutableArray() };
    }
    private static PlaygroundState RecoveryProjection(PlaygroundState prior, PlaygroundState current)
    {
        var currentConversations = current.Conversations.ToDictionary(c => c.Id);
        var currentMessages = current.Messages.ToDictionary(m => m.Id);
        var conversations = prior.Conversations.Where(c => currentConversations.ContainsKey(c.Id))
            .Select(c => c with { Options = currentConversations[c.Id].Options }).ToImmutableArray();
        var ids = conversations.Select(c => c.Id).ToHashSet();
        var messages = prior.Messages.Where(m => ids.Contains(m.ConversationId) && currentMessages.ContainsKey(m.Id))
            .Select(m => m with { Sources = m.Sources.Where(s => currentMessages[m.Id].Sources.Contains(s)).ToImmutableArray(),
                Attachments = currentMessages[m.Id].Attachments,
                Status = currentMessages[m.Id].Status == PlaygroundMessageStatus.Complete ? m.Status : currentMessages[m.Id].Status,
                Text = currentMessages[m.Id].Status == PlaygroundMessageStatus.Complete ? m.Text : currentMessages[m.Id].Text,
                Error = currentMessages[m.Id].Status == PlaygroundMessageStatus.Complete ? m.Error : currentMessages[m.Id].Error,
                Usage = currentMessages[m.Id].Usage, Cost = currentMessages[m.Id].Cost }).ToImmutableArray();
        var users = messages.Where(m => m.Role == "user").Select(m => m.Id).ToHashSet();
        return prior with { Settings = current.Settings, Conversations = conversations, Messages = messages,
            Memories = current.Memories.Where(m => ids.Contains(m.ConversationId) && users.Contains(m.MessageId)).ToImmutableArray(),
            // Keep suppressed dependency IDs even if a message has been deleted.
            Suppressions = current.Suppressions };
    }
}

internal sealed class PlaygroundDisk
{
    internal const int MaximumBytes = 32 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, MaxDepth = 32 };
    private readonly NativeConfiguration configuration;
    internal PlaygroundIdentity Identity { get; }
    internal PlaygroundDisk(NativeConfiguration configuration)
    {
        this.configuration = configuration;
        if (configuration.Profile is not NativeProfileStore profile) throw new InvalidOperationException("Playground 需要原生隔离资料环境。");
        Identity = new(profile.Manifest.ProfileId, profile.Manifest.StoreId);
    }
    internal Task<T> WithDirectory<T>(bool create, Func<string?, Task<T>> action, CancellationToken cancellation)
        // Image snapshots make serialization, DPAPI and checked file reads large
        // enough to stall native input. Keep the complete pinned transaction off
        // the UI thread; begun atomic writes retain their existing semantics.
        => Task.Run(() => WithDirectoryCore(create, action, cancellation), cancellation);
    private async Task<T> WithDirectoryCore<T>(bool create, Func<string?, Task<T>> action, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var root = NativeProfileAccess.Validate(configuration); var held = IsolatedLibraryPersistence.PinDirectoryChain(root);
        try
        {
            var folder = Path.Combine(root, "playground");
            if (!Directory.Exists(folder))
            {
                if (!create) return await action(null).ConfigureAwait(false);
                Directory.CreateDirectory(folder);
            }
            held.AddRange(IsolatedLibraryPersistence.PinDirectoryChain(folder));
            configuration.Profile!.ValidateAccess(); cancellation.ThrowIfCancellationRequested();
            return await action(folder).ConfigureAwait(false);
        }
        finally { foreach (var item in held) item.Dispose(); }
    }
    internal static byte[]? ReadBytes(string path)
    {
        try
        {
            using var input = IsolatedLibrarySource.OpenChecked(path);
            if (input.Length <= 0 || input.Length > MaximumBytes + 65536) throw new InvalidDataException("Playground 文件大小不符合约定。");
            var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); return bytes;
        }
        catch (FileNotFoundException) { return null; }
    }
}
