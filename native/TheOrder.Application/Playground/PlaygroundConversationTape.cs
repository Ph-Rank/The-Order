using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private const int MaximumTapeCharacters = 2 * 1024 * 1024;
    // One most-recent chain is enough for active discussion. Weak references do
    // not pin an old library or an entire abandoned message graph in memory.
    private NativeTapeCache? nativeTape;
    private sealed record NativeTapeCache(Guid ConversationId, PlaygroundConversationOptions Options,
        PlaygroundSettings Settings, WeakReference<PlaygroundState> State, WeakReference<LibrarySnapshot>? Library,
        bool LibraryWasNull, PlaygroundActiveSession? ActiveSession, ImmutableArray<Guid> MessageIds, PlaygroundConversationTape Tape);

    private bool TapeEligible(Run run, PlaygroundState snapshot) => run.Conversation.Options.Provider == PlaygroundProviderKind.Gemini
        && run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.Full
        && run.Conversation.Options.Search != PlaygroundSearchMode.Off
        && snapshot.Messages.Where(m => m.ConversationId == run.Conversation.Id).Take(513).Count() <= 512
        && !run.Attachments.Any(IsImageAttachment)
        && !snapshot.Messages.Where(m => m.ConversationId == run.Conversation.Id).Any(m => m.Attachments.Any(IsImageAttachment));
    private static bool SameActivity(PlaygroundActiveSession? a, PlaygroundActiveSession? b)
        => a?.GameId == b?.GameId && a?.Status == b?.Status && a?.ConfirmedSeconds == b?.ConfirmedSeconds;
    private bool LibraryCurrent(Run run)
    {
        if (!run.SavedContext || !run.Settings.Permissions.Library) return true;
        var current = library();
        if (ReferenceEquals(current, run.LibrarySnapshot)) return true;
        if (run.PlaytimeRankingStamp is not null && run.PlaytimeRankingStamp != PlaytimeRankingIdentity(current)) return false;
        if (run.PlaytimeRankingStamp is not null && run.Conversation.Options.Provider != PlaygroundProviderKind.Gemini
            && run.Conversation.Options.Spoiler != PlaygroundSpoilerMode.LibraryStatus) return true;
        return run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.LibraryStatus && run.LibraryCaptured
            && run.SpoilerLibraryStamp == SpoilerLibraryIdentity(current);
    }
    private void PrepareConversationTape(Run run, PlaygroundState captured, bool retry)
    {
        if (run.Conversation.Options.Provider != PlaygroundProviderKind.Gemini) return;
        run.ConversationBinding = string.Join(':', captured.Identity.ProfileId, captured.Identity.StoreId, run.Conversation.Id,
            run.Conversation.Options.Provider, Endpoint(run.Conversation.Options.Provider, run.Conversation.Options.Endpoint),
            run.Conversation.Options.Model, Guid.NewGuid().ToString("N"));
        NativeTapeCache? cached;
        lock (gate) { cached = nativeTape; nativeTape = null; }
        if (run.SavedContext && run.Settings.Permissions.Library)
        { _ = ReadLibrary(run); run.TapeActivity = activeSession(); }
        if (retry || cached is null || !TapeEligible(run, captured) || cached.ConversationId != run.Conversation.Id
            || cached.Options != run.Conversation.Options || cached.Settings != run.Settings
            || !cached.State.TryGetTarget(out var prior) || !ReferenceEquals(prior, captured) || !ReferenceEquals(Snapshot, captured)) return;
        if (run.SavedContext && run.Settings.Permissions.Library)
        {
            if (cached.LibraryWasNull ? run.LibrarySnapshot is not null
                : cached.Library is null || !cached.Library.TryGetTarget(out var priorLibrary) || !ReferenceEquals(priorLibrary, run.LibrarySnapshot)) return;
            if (!SameActivity(cached.ActiveSession, run.TapeActivity)) return;
        }
        run.ConversationBinding = cached.Tape.Binding;
        run.ConversationTape = cached.Tape;
        run.TapeContextMessageIds = cached.MessageIds;
    }
    private void EnsureConversationScope(Run run)
    {
        if (run.Conversation.Options.Provider != PlaygroundProviderKind.Gemini && run.Conversation.Options.Spoiler != PlaygroundSpoilerMode.LibraryStatus
            && run.PlaytimeRankingStamp is null) return;
        if (!LibraryCurrent(run))
        {
            lock (gate) nativeTape = null;
            run.PendingTape = null;
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported,
                "书架资料已变化，本轮已停止；可重新提问以读取当前记录，程序不会自动重发。"));
        }
    }
    private void SaveConversationTape(Run run)
    {
        var snapshot = Snapshot;
        if (run.PendingTape is not { } tape || tape.Data.Length > MaximumTapeCharacters || !TapeEligible(run, snapshot)
            || run.Retention != PlaygroundRetention.Portable || !LibraryCurrent(run)) return;
        if (run.SavedContext && run.Settings.Permissions.Library && !SameActivity(run.TapeActivity, activeSession())) return;
        var covered = run.ContextMessageIds.Append(run.UserId).Append(run.AssistantId).Distinct().ToImmutableArray();
        if (covered.Length > 1024) return;
        lock (gate)
        {
            if (disposed || closing || run.Cancel.IsCancellationRequested || generation != run.Generation || active != run
                || !ReferenceEquals(state, snapshot) || state.Settings != run.Settings
                || state.Conversations.FirstOrDefault(c => c.Id == run.Conversation.Id)?.Options != run.Conversation.Options
                || state.Messages.FirstOrDefault(m => m.Id == run.AssistantId)?.Status != PlaygroundMessageStatus.Complete) return;
            nativeTape = new(run.Conversation.Id, run.Conversation.Options, run.Settings, new(snapshot),
                run.LibrarySnapshot is null ? null : new(run.LibrarySnapshot), run.LibrarySnapshot is null, run.TapeActivity, covered, tape);
        }
    }
    private void AdmitNativeDependencies(Run run)
    {
        if (run.ConversationTape is null) return;
        var snapshot = Snapshot; var admission = new MessageAdmission(this, run, snapshot);
        foreach (var id in run.TapeContextMessageIds)
        {
            var message = snapshot.Messages.FirstOrDefault(m => m.Id == id && m.ConversationId == run.Conversation.Id);
            if (message is null || !admission.Allows(message))
            { run.ConversationTape = null; run.ConversationBinding = Guid.NewGuid().ToString("N"); return; }
            DependOn(run, message.Id);
        }
    }
    private ImmutableArray<PlaygroundWireMessage> DiscussionMessages(Run run, bool imageInput)
    {
        AdmitNativeDependencies(run);
        var messages = PortableMessages(run, imageInput, run.ConversationTape is null);
        if (run.ConversationTape is { } tape)
        {
            // The opaque tape keeps its separate hard size bound. A protocol
            // signature is not user-visible prose, nor are JSON escape bytes.
            long characters = NativeEnvelopeCharacters(tape.Data) + Instructions(run).Length + messages.Sum(m => (long)m.Text.Length)
                + Definitions(run).Sum(t => (long)t.Name.Length + t.Description.Length + t.ParametersJson.Length);
            if (characters > run.Settings.MaximumContextCharacters)
            {
                run.ConversationTape = null; run.ConversationBinding = Guid.NewGuid().ToString("N");
                Activity(run, "本轮改用可移植的历史引用，重新读取当前资料。");
                messages = PortableMessages(run, imageInput);
            }
        }
        return messages;
    }
}
