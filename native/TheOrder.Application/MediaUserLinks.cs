namespace TheOrder.Application;

// Bookmarks and directed relationships are user data, never identity proofs.
// They share the identity component's profile ownership and atomic writer, but
// use a separate envelope so old clients can still read their identity choices.
public enum MediaUserLinkState { Pending, Related }
public sealed record MediaUserLink(MediaIdentityContext Source, MediaIdentityReference Target,
    MediaUserLinkState State, string Title, string TargetFormat, string Relation, string Note, DateTimeOffset SavedAtUtc);
public sealed record MediaUserLinks(MediaIdentityAnchor Anchor, long Revision, MediaUserLink[] Items)
{
    public static MediaUserLinks Empty(MediaIdentityAnchor anchor) => new(anchor, 0, []);
}
public static class MediaUserLinkRules
{
    public static bool Valid(MediaUserLink? link) => link is { Source.IsValid: true, Target.IsValid: true }
        && Enum.IsDefined(link.State) && AniListValidation.Text(link.Title, 2000, false)
        && AniListValidation.Text(link.TargetFormat, 100) && AniListValidation.Text(link.Note, 1000)
        && link.SavedAtUtc != default && link.Relation is "RELATED" or "OTHER_EDITION"
        && (link.State != MediaUserLinkState.Pending || link.Relation == "RELATED")
        && (link.Source.Anchor.Purpose == MediaIdentityPurpose.AniListMedia && link.Target is { Provider: "AniList" or "MyAnimeList", Resource: "Anime" or "Manga" }
            || link.Source.Anchor.Purpose == MediaIdentityPurpose.MusicRelease && link.Target is { Provider: "MusicBrainz", Resource: "Release" or "ReleaseGroup" }
            || link.Source.Anchor.Purpose == MediaIdentityPurpose.CatalogWork && link.Target is { Provider: "NextMoe", Resource: "Work" } && link.State == MediaUserLinkState.Pending)
        && (link.Relation != "OTHER_EDITION" || link.Source.Anchor.Purpose == MediaIdentityPurpose.MusicRelease && link.Target.Resource == "Release");
    public static bool Valid(MediaUserLinks? value) => value is { Anchor.IsValid: true, Revision: >= 0, Items.Length: <= 256 }
        && value.Items.All(x => Valid(x) && x.Source.Anchor == value.Anchor)
        && value.Items.Select(x => x.Target).Distinct().Count() == value.Items.Length;
    public static MediaUserLinks Apply(MediaUserLinks current, MediaUserLink? link, MediaIdentityReference target, long revision)
    {
        if (!Valid(current) || !target.IsValid || link is not null && (!Valid(link) || link.Source.Anchor != current.Anchor || link.Target != target))
            throw new InvalidDataException("相关作品的来源或目标身份无效。");
        if (current.Revision != revision || revision == long.MaxValue) throw new IOException("相关作品已被其他操作更新，请重读后再保存。");
        var rows = current.Items.Where(x => x.Target != target);
        var result = new MediaUserLinks(current.Anchor, revision + 1, (link is null ? rows : rows.Append(link)).ToArray());
        if (!Valid(result)) throw new IOException("相关作品数量已达保存边界，原记录保留。");
        return result;
    }
}
