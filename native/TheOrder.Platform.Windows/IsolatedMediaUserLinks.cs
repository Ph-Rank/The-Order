using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed partial class IsolatedMediaIdentityDecisions
{
    public Task<MediaUserLinks> ReadLinksAsync(MediaIdentityContext source, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("相关作品来源无效。");
        return Run((root, profile) => Task.FromResult(ReadLinks(root, profile, source)), cancellation);
    }
    private static MediaUserLinks ReadLinks(string root, NativeProfileStore profile, MediaIdentityContext source)
    {
        var key = "user-links-" + source.Anchor.Key;
        var value = ReadPayload<MediaUserLinks>(Path.Combine(root, key + ".json"), profile, "user-links", key)
            ?? MediaUserLinks.Empty(source.Anchor);
        if (!MediaUserLinkRules.Valid(value) || value.Anchor != source.Anchor) throw new IOException("相关作品记录无法验证，原文件保留。");
        return value;
    }
    public Task<MediaUserLinks> ChangeLinkAsync(MediaIdentityContext source, MediaUserLink? link,
        MediaIdentityReference target, long expectedRevision, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("相关作品来源无效。");
        return Run(async (root, profile) =>
        {
            var next = MediaUserLinkRules.Apply(ReadLinks(root, profile, source), link, target, expectedRevision);
            await Write(root, profile, "user-links-" + source.Anchor.Key, "user-links", next, cancellation).ConfigureAwait(false);
            return next;
        }, cancellation);
    }
    public Task ResetAsync(MediaIdentityContext source, long expectedRevision, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("跨站关联来源无效。");
        return Run(async (root, profile) =>
        {
            var current = ReadDocument(Path.Combine(root, source.Anchor.Key + ".json"), profile, source.Anchor)
                ?? MediaIdentityDecisionDocument.Empty(source.Anchor);
            if (current.Revision != expectedRevision || current.Revision == long.MaxValue) throw new IOException("关联已改变，请重新读取。");
            var next = current with { Revision = current.Revision + 1, HasLegacyConflict = false,
                Decisions = current.Decisions.Select(x => x with { Kind = MediaIdentityDecisionKind.Withdrawn }).ToArray() };
            await Write(root, profile, source.Anchor.Key, "decisions", next, cancellation).ConfigureAwait(false);
            return true;
        }, cancellation);
    }
}
