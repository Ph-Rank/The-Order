namespace TheOrder.Application;

public sealed partial class SessionMediaIdentityDecisions
{
    private readonly Dictionary<string, MediaUserLinks> links = [];
    public Task<MediaUserLinks> ReadLinksAsync(MediaIdentityContext source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); if (!source.IsValid) throw new InvalidDataException();
        lock (gate) return Task.FromResult(Copy(links.GetValueOrDefault(source.Anchor.Key) ?? MediaUserLinks.Empty(source.Anchor)));
    }
    public Task<MediaUserLinks> ChangeLinkAsync(MediaIdentityContext source, MediaUserLink? link,
        MediaIdentityReference target, long expectedRevision, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); if (!source.IsValid) throw new InvalidDataException();
        lock (gate)
        {
            var next = MediaUserLinkRules.Apply(links.GetValueOrDefault(source.Anchor.Key) ?? MediaUserLinks.Empty(source.Anchor), link, target, expectedRevision);
            links[source.Anchor.Key] = next; return Task.FromResult(Copy(next));
        }
    }
    public Task ResetAsync(MediaIdentityContext source, long expectedRevision, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); if (!source.IsValid) throw new InvalidDataException();
        lock (gate)
        {
            var current = documents.GetValueOrDefault(source.Anchor.Key) ?? MediaIdentityDecisionDocument.Empty(source.Anchor);
            if (current.Revision != expectedRevision || current.Revision == long.MaxValue) throw new IOException("关联已改变，请重新读取。");
            documents[source.Anchor.Key] = current with { Revision = current.Revision + 1, HasLegacyConflict = false,
                Decisions = current.Decisions.Select(x => x with { Kind = MediaIdentityDecisionKind.Withdrawn }).ToArray() };
            return Task.CompletedTask;
        }
    }
    private static MediaUserLinks Copy(MediaUserLinks value) => value with { Items = [.. value.Items] };
}
