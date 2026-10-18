using System.Globalization;

namespace TheOrder.Application;

// Lightweight, identity-bound link projection. Sources provide plain records;
// the renderer does not need to know which metadata provider supplied them.
public sealed record AnimeWatchIdentity(int? AniListId, string? BangumiId)
{
    public bool IsValid => (AniListId is > 0 || BangumiId is not null) && AniListId is null or > 0
        && (BangumiId is null || BangumiRelationIdentityResolver.ValidSubjectId(BangumiId));
    public bool Matches(AnimeWatchIdentity other) => IsValid && other.IsValid
        && (AniListId is null || other.AniListId is null || AniListId == other.AniListId)
        && (BangumiId is null || other.BangumiId is null || BangumiId == other.BangumiId)
        && (AniListId is not null && AniListId == other.AniListId || BangumiId is not null && BangumiId == other.BangumiId);
}
public enum WatchLinkKind { Playback, Information, Trailer, Store, Other }
public enum WatchLinkVerification { ProviderListed, PageIdentityChecked, UserSupplied }
public enum WatchLinkSort { BilibiliFirst, SourceOrder, PlatformName }
public sealed record WatchLinkEvidence(string Source, string Url, string Note, DateTimeOffset? ReviewedAtUtc = null, string? DataVersion = null);
public sealed record WatchLink(AnimeWatchIdentity Identity, string Platform, string Url, WatchLinkKind Kind,
    string[] Regions, WatchLinkVerification Verification, WatchLinkEvidence[] Sources, string? Notes = null);
public sealed record WatchLinkPreferenceRead(WatchLinkSort Sort = WatchLinkSort.BilibiliFirst, string? Warning = null, bool ReadOnly = false);
public interface IWatchLinkPreferencesStore
{
    Task<WatchLinkPreferenceRead> LoadAsync(CancellationToken cancellation = default);
    Task SaveAsync(WatchLinkSort sort, CancellationToken cancellation = default);
}
public static class WatchLinks
{
    // Shared display ordering; preferences never remove valid links.
    public static IEnumerable<T> OrderPlatforms<T>(IEnumerable<T> input, Func<T,string> platform, string[] preferred, bool alphabetic)
        => alphabetic ? input.OrderBy(platform, StringComparer.CurrentCulture)
            : preferred.Length > 0 ? input.OrderBy(x => { var n = Array.IndexOf(preferred, platform(x)); return n < 0 ? preferred.Length : n; }) : input;
    public static bool SafeUrl(string? value)
    {
        if (value is not { Length: > 0 and <= 2048 } || value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns) return false;
        var host = uri.IdnHost;
        return host.Contains('.') && !host.EndsWith('.') && !new[] { ".local", ".localhost", ".internal", ".invalid", ".test" }.Any(host.EndsWith)
            && !value.Contains('\\');
    }
    public static bool Valid(WatchLink? link) => link is not null && link.Identity is not null && link.Identity.IsValid
        && AniListValidation.Text(link.Platform, 100, false) && SafeUrl(link.Url) && Enum.IsDefined(link.Kind) && Enum.IsDefined(link.Verification)
        && link.Regions is { Length: <= 64 } && link.Regions.All(x => x is { Length: 2 } && x.All(c => char.IsAsciiLetterUpper(c)))
        && AniListValidation.OptionalText(link.Notes, 1000) && link.Sources is { Length: > 0 and <= 8 }
        && link.Sources.All(x => x is not null && AniListValidation.Text(x.Source, 100, false) && SafeUrl(x.Url) && AniListValidation.Text(x.Note, 2000) && AniListValidation.OptionalText(x.DataVersion, 160));
    public static string PlatformKey(WatchLink link)
    {
        var host = new Uri(link.Url).IdnHost;
        return host is "bilibili.com" or "www.bilibili.com" ? "bilibili" : link.Platform.ToLowerInvariant();
    }
    public static WatchLinkKind Classify(string url, WatchLinkKind declared)
    {
        if (!SafeUrl(url)) return WatchLinkKind.Other;
        var uri = new Uri(url);
        if (uri.Host is "bilibili.com" or "www.bilibili.com")
        {
            if (uri.AbsolutePath.StartsWith("/bangumi/media/", StringComparison.Ordinal)) return WatchLinkKind.Information;
            if (uri.AbsolutePath.StartsWith("/bangumi/play/", StringComparison.Ordinal)) return declared == WatchLinkKind.Playback ? declared : WatchLinkKind.Other;
            // A user-uploaded video is not automatically a licensed full episode.
            if (declared == WatchLinkKind.Playback) return WatchLinkKind.Other;
        }
        return declared;
    }
    public static string ResourceKey(WatchLink link)
    {
        var uri = new Uri(link.Url); var kind = Classify(link.Url, link.Kind);
        if (uri.Host is "bilibili.com" or "www.bilibili.com")
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments is ["bangumi", "play", var token] && token.Length > 2 && token[..2] is "ss" or "ep"
                && long.TryParse(token[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 && token[2..] == number.ToString(CultureInfo.InvariantCulture))
                return kind + ":bilibili:" + token;
        }
        // Only tracking fields are ignored; unknown query parameters, paths and
        // episode IDs remain distinct. Never deduplicate by title or site name.
        var query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(x =>
        { var key = x.Split('=')[0]; return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && key is not ("spm_id_from" or "spm_from" or "tag"); }));
        return kind + ":" + uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + (query.Length > 0 ? "?" + query : "");
    }
    public static WatchLink[] Merge(AnimeWatchIdentity identity, IEnumerable<WatchLink> input, WatchLinkSort sort = WatchLinkSort.BilibiliFirst)
    {
        if (!identity.IsValid || !Enum.IsDefined(sort)) return [];
        var output = new Dictionary<string, WatchLink>(StringComparer.Ordinal);
        foreach (var raw in input.Take(128))
        {
            if (!Valid(raw) || !identity.Matches(raw.Identity)) continue;
            var link = raw with { Kind = Classify(raw.Url, raw.Kind) }; var key = ResourceKey(link);
            if (!output.TryGetValue(key, out var old)) { if (output.Count < 64) output.Add(key, link); continue; }
            // Page correspondence is stronger evidence than a provider listing,
            // but neither state means that current playback rights were checked.
            var stronger = link.Verification == WatchLinkVerification.PageIdentityChecked ? link : old;
            output[key] = stronger with { Sources = old.Sources.Concat(link.Sources).DistinctBy(x => (x.Source, x.Url, x.Note, x.DataVersion)).Take(8).ToArray(), Regions = old.Regions.Concat(link.Regions).Distinct().Take(64).ToArray() };
        }
        var items = output.Values.AsEnumerable();
        return sort switch { WatchLinkSort.BilibiliFirst => OrderPlatforms(items, PlatformKey, ["bilibili"], false).ToArray(),
            WatchLinkSort.PlatformName => OrderPlatforms(items, x => x.Platform, [], true).ToArray(), _ => items.ToArray() };
    }
}
