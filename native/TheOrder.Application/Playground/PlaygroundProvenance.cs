using System.Collections.Immutable;

namespace TheOrder.Application.Playground;

public sealed partial class PlaygroundService
{
    private static void DependOn(Run run, Guid id)
    {
        if (id == Guid.Empty || id == run.AssistantId) return;
        if (run.ContextMessageIds.Count >= 1024 && !run.ContextMessageIds.Contains(id))
            throw new PlaygroundProviderException(new(PlaygroundFailureKind.Unsupported, "本轮上下文依赖达到上限，请缩小问题后重试。"));
        run.ContextMessageIds.Add(id);
    }
    private static HashSet<Guid> SuppressedMessageIds(PlaygroundState state, IEnumerable<Guid>? roots = null)
    {
        var blocked = state.Suppressions.SelectMany(x => x.MessageIds).ToHashSet();
        if (roots is not null) blocked.UnionWith(roots);
        // Use a reverse dependency graph, rather than a citation printed by the
        // model. A paraphrase of a paraphrase remains derived from the original.
        var dependents = new Dictionary<Guid, List<Guid>>();
        foreach (var m in state.Messages)
        {
            var ids = m.ContextMessageIds.Concat(m.Sources.Select(x => Guid.TryParse(x.MessageId, out var id) ? id : Guid.Empty));
            foreach (var id in ids.Where(x => x != Guid.Empty).Distinct())
            {
                if (!dependents.TryGetValue(id, out var rows)) dependents[id] = rows = [];
                rows.Add(m.Id);
            }
        }
        var pending = new Queue<Guid>(blocked);
        while (pending.TryDequeue(out var id)) if (dependents.TryGetValue(id, out var rows))
            foreach (var child in rows) if (blocked.Add(child)) pending.Enqueue(child);
        return blocked;
    }
    private static bool Suppressed(PlaygroundState state, PlaygroundMessage message) => SuppressedMessageIds(state).Contains(message.Id);

    // This checks the full host-recorded ancestry each time a saved answer is
    // admitted. Revoking a tool does not make its earlier output public data.
    private sealed class MessageAdmission
    {
        private readonly PlaygroundService owner;
        private readonly Run run;
        private readonly PlaygroundState state;
        private readonly Dictionary<Guid, PlaygroundMessage> messages;
        private readonly Dictionary<Guid, PlaygroundMemory> memories;
        private readonly HashSet<Guid> suppressed;
        private readonly Dictionary<Guid, bool> decided = [];
        private HashSet<string>? workKeys;
        public MessageAdmission(PlaygroundService owner, Run run, PlaygroundState state)
        {
            this.owner = owner; this.run = run; this.state = state;
            messages = state.Messages.ToDictionary(m => m.Id); memories = state.Memories.ToDictionary(m => m.Id);
            suppressed = SuppressedMessageIds(state);
        }
        private bool WorkIsCurrent(string? key)
        {
            if (key is null) return true;
            if (!owner.Can(run, "library")) return false;
            workKeys ??= owner.ReadLibrary(run)?.Entries.Select(WorkKeyFor).ToHashSet(StringComparer.Ordinal) ?? [];
            return workKeys.Contains(key);
        }
        public bool Allows(PlaygroundMessage message)
        {
            if (decided.TryGetValue(message.Id, out var value)) return value;
            var visiting = new HashSet<Guid>(); var pending = new Stack<(PlaygroundMessage Message, bool Exit)>();
            pending.Push((message, false));
            while (pending.TryPop(out var frame))
            {
                var current = frame.Message;
                if (decided.ContainsKey(current.Id)) { if (frame.Exit) visiting.Remove(current.Id); continue; }
                var dependencies = current.ContextMessageIds.Concat(current.Sources.Select(s => Guid.TryParse(s.MessageId, out var id) ? id : Guid.Empty))
                    .Where(id => id != Guid.Empty).Distinct().ToArray();
                if (frame.Exit)
                {
                    decided[current.Id] = dependencies.All(id => decided.GetValueOrDefault(id)); visiting.Remove(current.Id); continue;
                }
                if (!Check(current) || dependencies.Any(id => !messages.ContainsKey(id) || visiting.Contains(id)))
                { decided[current.Id] = false; continue; }
                visiting.Add(current.Id); pending.Push((current, true));
                foreach (var id in dependencies) if (!decided.ContainsKey(id)) pending.Push((messages[id], false));
            }
            return decided.GetValueOrDefault(message.Id);
        }
        private bool Check(PlaygroundMessage message)
        {
            if (message.Status != PlaygroundMessageStatus.Complete || message.Retention != PlaygroundRetention.Portable || suppressed.Contains(message.Id)) return false;
            if (run.Conversation.Options.Search == PlaygroundSearchMode.Off && (message.Sources.Any(source => source.Kind == PlaygroundSourceKind.Web)
                || message.Usage?.Searches is > 0 || message.Usage?.Fetches is > 0)) return false;
            if (run.Conversation.Options.Spoiler == PlaygroundSpoilerMode.LibraryStatus && !owner.NarrativeHistoryAllowed(run, message)
                && (message.Attachments.Length != 0 || SafePreferenceFor(new(Guid.Empty, message.Text, message.ConversationId, message.Id,
                    message.CreatedAt, message.CreatedAt)) is null)) return false;
            if (message.ConversationId != run.Conversation.Id && (!owner.Can(run, "history")
                || state.Conversations.Any(c => c.Id == message.ConversationId && c.Temporary))) return false;
            foreach (var source in message.Sources)
            {
                if (!WorkIsCurrent(source.WorkKey)) return false;
                switch (source.Kind)
                {
                    case PlaygroundSourceKind.LocalRecord:
                        if (!owner.Can(run, "library")) return false;
                        break;
                    case PlaygroundSourceKind.Memory:
                        if (!owner.Can(run, "memory") || !Guid.TryParse(source.TargetId, out var memoryId)
                            || !memories.TryGetValue(memoryId, out var memory) || memory.Disabled || memory.SupersededBy is not null
                            || source.At != memory.UpdatedAt || source.Quote != Redacted(memory.Text, 1000)
                            || source.MessageId != memory.MessageId.ToString("N") || !WorkIsCurrent(memory.WorkKey)) return false;
                        break;
                    case PlaygroundSourceKind.ChatMessage:
                        if (!Guid.TryParse(source.MessageId, out var id) || !messages.TryGetValue(id, out var original)) return false;
                        // The current conversation's user messages are ordinary
                        // conversation context; cross-conversation excerpts need History.
                        if (original.ConversationId != run.Conversation.Id && !owner.Can(run, "history")) return false;
                        break;
                    case PlaygroundSourceKind.Attachment:
                        if (!AttachmentOrigin(messages, source, out var attachmentMessage)
                            || attachmentMessage.ConversationId != run.Conversation.Id && !owner.Can(run, "history")) return false;
                        break;
                    case PlaygroundSourceKind.Web:
                        if (run.Conversation.Options.Search == PlaygroundSearchMode.Off || source.Retention != PlaygroundRetention.Portable || !SafeWebUrl(source.Url)) return false;
                        break;
                    default: return false;
                }
            }
            return true;
        }
    }
}
