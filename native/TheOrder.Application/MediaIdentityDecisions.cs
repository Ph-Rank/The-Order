using System.Security.Cryptography;
using System.Text.Json;

namespace TheOrder.Application;

// User decisions, not an entity graph. Provider/resource namespaces are part of
// identity; titles and remote-cache revisions are deliberately absent from Key.
public enum MediaIdentityPurpose { AniListMedia, MusicRelease, CatalogWork, MusicListening, GameVideo, GameVideoSelection }
public enum MediaIdentityDecisionKind { Confirmed, Rejected, Withdrawn }
public enum MediaIdentityDecisionOrigin { User, LegacyConfirmation }
public sealed record MediaIdentityReference(string Provider, string Resource, string Id)
{
    public bool IsValid => (Provider, Resource) switch
    {
        ("Bangumi", "Subject") => BangumiRelationIdentityResolver.ValidSubjectId(Id),
        ("AniList", "Anime" or "Manga") => int.TryParse(Id, out var n) && n > 0 && Id == n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ("MusicBrainz", "Release") => MusicBrainzReference.ValidId(Id),
        ("MusicBrainz", "ReleaseGroup") => MusicBrainzReference.ValidId(Id),
        ("MyAnimeList", "Anime" or "Manga") => int.TryParse(Id, out var m) && m > 0 && Id == m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ("NextMoe", "Work") => Id is { Length: >= 1 and <= 20 } && Id[0] is >= '1' and <= '9' && Id.All(char.IsAsciiDigit),
        ("IGDB", "Game") => GameVideos.ValidId(Id),
        ("YouTube", "Video") => AniListPlayback.ValidTrailer(new("youtube", Id)),
        ("Native", "LibraryEntry") => Id is { Length: >= 1 and <= 256 } && !string.IsNullOrWhiteSpace(Id) && !Id.Any(char.IsControl),
        ("iTunes", "Album:JP" or "Album:CN" or "Album:US") => MusicOutlets.Digits(Id),
        _ => false
    };
}
public sealed record MediaIdentityAnchor(MediaIdentityReference Source, MediaIdentityPurpose Purpose, string Medium)
{
    public string Key => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Source, Purpose, Medium })));
    public bool IsValid => Source is { IsValid: true, Provider: "Native", Resource: "LibraryEntry" } && Purpose == MediaIdentityPurpose.CatalogWork && Medium == "Game"
        || Source is { IsValid: true, Provider: "Native", Resource: "LibraryEntry" }
            && (Purpose == MediaIdentityPurpose.GameVideo && Medium == "Game"
                || Purpose == MediaIdentityPurpose.GameVideoSelection && (Medium == "Manual" || Medium is { Length: > 5 } && Medium.StartsWith("IGDB:", StringComparison.Ordinal) && GameVideos.ValidId(Medium[5..])))
        || Source is { IsValid: true, Provider: "MusicBrainz", Resource: "Release" } && Purpose == MediaIdentityPurpose.MusicListening && Medium == "Music"
        || Source is { IsValid: true, Provider: "Bangumi", Resource: "Subject" }
        && (Purpose == MediaIdentityPurpose.AniListMedia && Medium is "Anime" or "Book"
            || Purpose == MediaIdentityPurpose.MusicRelease && Medium == "Music");
}
public sealed record MediaIdentityContext(MediaIdentityAnchor Anchor, string Fingerprint, string Granularity, string Format)
{
    public bool IsValid => Anchor is { IsValid: true } && Fingerprint is { Length: 64 } && Fingerprint.All(char.IsAsciiHexDigit)
        && AniListValidation.Text(Granularity, 100, false) && AniListValidation.Text(Format, 100);
    public static MediaIdentityContext From(AniListSource source) => new(
        new(new("Bangumi", "Subject", source.BangumiId), MediaIdentityPurpose.AniListMedia, source.Kind == AniListMediaKind.Anime ? "Anime" : "Book"),
        source.Fingerprint, source.Kind == AniListMediaKind.Anime ? "Work" : (source.BookLevel ?? BookEntityLevel.Unknown).ToString(), source.ExpectedFormat ?? "");
    public static MediaIdentityContext From(MusicAlbumSource source) => new(
        new(new("Bangumi", "Subject", source.BangumiId), MediaIdentityPurpose.MusicRelease, "Music"), source.Fingerprint, "Release", "");
    public static MediaIdentityContext ForLibraryEntry(string id) => new(new(new("Native", "LibraryEntry", id), MediaIdentityPurpose.CatalogWork, "Game"),
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("library-entry:" + id))), "Work", "GAME");
    public static MediaIdentityContext ForMusicOutlet(MusicBrainzReference release)
    {
        if (release is not { Kind: MusicBrainzEntityKind.Release } || !MusicBrainzReference.ValidId(release.Id)) throw new InvalidDataException("商店关联的来源必须是具体发行版。");
        return new(new(new("MusicBrainz", "Release", release.Id), MediaIdentityPurpose.MusicListening, "Music"),
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("music-outlets:" + release.Id))), "Release", "Release");
    }
}
public sealed record MediaIdentityDecision(MediaIdentityReference Target, MediaIdentityDecisionKind Kind,
    MediaIdentityContext Context, string TargetFormat, string? TargetParentId, DateTimeOffset DecidedAtUtc,
    MediaIdentityDecisionOrigin Origin, string Evidence);
public sealed record MediaIdentityDecisionDocument(MediaIdentityAnchor Anchor, long Revision, MediaIdentityDecision[] Decisions)
{
    public bool HasLegacyConflict { get; init; }
    public MediaIdentityDecision? Confirmed => Decisions.SingleOrDefault(d => d.Kind == MediaIdentityDecisionKind.Confirmed);
    public bool Rejects(MediaIdentityReference target) => Decisions.Any(d => d.Target == target && d.Kind == MediaIdentityDecisionKind.Rejected);
    public static MediaIdentityDecisionDocument Empty(MediaIdentityAnchor anchor) => new(anchor, 0, []);
}
// Legacy cache bindings lack a stable source ID. Preserve them independently,
// then attach only when the exact old source fingerprint is encountered.
public sealed record LegacyMediaIdentityChoice(MediaIdentityPurpose Purpose, string Fingerprint,
    MediaIdentityReference Target, DateTimeOffset ConfirmedAtUtc, string Evidence, bool Automatic = false, bool NeedsReview = false,
    MediaIdentityAnchor? SourceAnchor = null);
public interface IMediaIdentityDecisions
{
    Task<MediaIdentityDecisionDocument> ReadAsync(MediaIdentityContext source, CancellationToken cancellation);
    Task<MediaIdentityDecisionDocument> ApplyAsync(MediaIdentityDecision decision, long expectedRevision, CancellationToken cancellation);
    Task<MediaUserLinks> ReadLinksAsync(MediaIdentityContext source, CancellationToken cancellation)
        => Task.FromResult(MediaUserLinks.Empty(source.Anchor));
    Task<MediaUserLinks> ChangeLinkAsync(MediaIdentityContext source, MediaUserLink? link, MediaIdentityReference target, long expectedRevision, CancellationToken cancellation)
        => Task.FromException<MediaUserLinks>(new NotSupportedException("当前环境不能保存相关作品。"));
    Task ResetAsync(MediaIdentityContext source, long expectedRevision, CancellationToken cancellation)
        => Task.FromException(new NotSupportedException("当前环境不能恢复自动选择。"));
}
public interface IMediaIdentityDecisionOwner { IMediaIdentityDecisions IdentityDecisions { get; } }

public static class MediaIdentityDecisionRules
{
    public const int MaximumDecisions = 256;
    public static MediaIdentityReference Target(AniListMedia media) => new("AniList", media.Kind == AniListMediaKind.Anime ? "Anime" : "Manga", media.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static MediaIdentityReference Target(string releaseId) => new("MusicBrainz", "Release", releaseId);
    public static bool Compatible(MediaIdentityAnchor anchor, MediaIdentityReference target) => anchor.IsValid && target.IsValid
        && (anchor.Purpose == MediaIdentityPurpose.AniListMedia && target.Provider == "AniList"
            && target.Resource == (anchor.Medium == "Anime" ? "Anime" : "Manga")
            || anchor.Purpose == MediaIdentityPurpose.MusicRelease && target is { Provider: "MusicBrainz", Resource: "Release" }
            || anchor.Purpose == MediaIdentityPurpose.GameVideo && target is { Provider: "IGDB", Resource: "Game" }
            || anchor.Purpose == MediaIdentityPurpose.GameVideoSelection && target is { Provider: "YouTube", Resource: "Video" });
    public static bool Valid(MediaIdentityDecision? decision) => decision is { Context.IsValid: true, Target.IsValid: true }
        && Compatible(decision.Context.Anchor, decision.Target) && Enum.IsDefined(decision.Kind) && Enum.IsDefined(decision.Origin)
        && AniListValidation.Text(decision.TargetFormat, 100) && (decision.TargetParentId is null
            || decision.Context.Anchor.Purpose == MediaIdentityPurpose.MusicRelease && MusicBrainzReference.ValidId(decision.TargetParentId)
            || decision.Context.Anchor.Purpose == MediaIdentityPurpose.GameVideo && GameVideos.ValidId(decision.TargetParentId))
        && decision.DecidedAtUtc != default && AniListValidation.Text(decision.Evidence, 1000, false);
    public static bool Valid(MediaIdentityDecisionDocument? document) => document is { Anchor.IsValid: true, Revision: >= 0, Decisions.Length: <= MaximumDecisions }
        && document.Decisions.All(d => Valid(d) && d.Context.Anchor == document.Anchor)
        && document.Decisions.Select(d => d.Target).Distinct().Count() == document.Decisions.Length
        && document.Decisions.Count(d => d.Kind == MediaIdentityDecisionKind.Confirmed) <= 1;
    public static MediaIdentityDecisionDocument Apply(MediaIdentityDecisionDocument current, MediaIdentityDecision decision, long expectedRevision)
    {
        if (!Valid(current) || !Valid(decision) || current.Anchor != decision.Context.Anchor) throw new InvalidDataException("跨站关联的身份或类型无效。");
        if (current.Revision != expectedRevision) throw new IOException("关联选择已被其他操作更新，请重新读取后再确认。");
        // Replacing a choice is an explicit user action. Keep the former target
        // excluded from automatic matching; it can still be chosen manually.
        var rows = current.Decisions.Where(d => d.Target != decision.Target).Select(d =>
            decision.Kind == MediaIdentityDecisionKind.Confirmed && d.Kind == MediaIdentityDecisionKind.Confirmed
                ? d with { Kind = MediaIdentityDecisionKind.Rejected, DecidedAtUtc = decision.DecidedAtUtc } : d).Append(decision).ToArray();
        if (rows.Length > MaximumDecisions || current.Revision == long.MaxValue) throw new IOException("该作品的关联决定已达保存边界，原记录保留。");
        return new(current.Anchor, current.Revision + 1, rows);
    }
    public static bool NeedsReview(MediaIdentityContext current, MediaIdentityDecision decision) => current.Anchor != decision.Context.Anchor
        || current.Anchor.Purpose is MediaIdentityPurpose.GameVideo or MediaIdentityPurpose.GameVideoSelection && current.Fingerprint != decision.Context.Fingerprint
        || current.Granularity != decision.Context.Granularity
        || current.Format.Length > 0 && decision.Context.Format.Length > 0 && current.Format != decision.Context.Format;
}

// Used only by non-profile hosts and deterministic tests. The desktop composition
// always supplies the Native Profile implementation; this has no disk claims.
public sealed partial class SessionMediaIdentityDecisions : IMediaIdentityDecisions
{
    private readonly object gate = new();
    private readonly Dictionary<string, MediaIdentityDecisionDocument> documents = [];
    public Task<MediaIdentityDecisionDocument> ReadAsync(MediaIdentityContext source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); if (!source.IsValid) throw new InvalidDataException();
        lock (gate) return Task.FromResult(Clone(documents.GetValueOrDefault(source.Anchor.Key) ?? MediaIdentityDecisionDocument.Empty(source.Anchor)));
    }
    public Task<MediaIdentityDecisionDocument> ApplyAsync(MediaIdentityDecision decision, long expectedRevision, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); if (!MediaIdentityDecisionRules.Valid(decision)) throw new InvalidDataException();
        lock (gate)
        {
            var key = decision.Context.Anchor.Key;
            var next = MediaIdentityDecisionRules.Apply(documents.GetValueOrDefault(key) ?? MediaIdentityDecisionDocument.Empty(decision.Context.Anchor), decision, expectedRevision);
            documents[key] = next; return Task.FromResult(Clone(next));
        }
    }
    private static MediaIdentityDecisionDocument Clone(MediaIdentityDecisionDocument value) => value with { Decisions = [.. value.Decisions] };
}
