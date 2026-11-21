using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application;

public enum CollectionSite { Vndb, Bangumi, NextMoe }

[Flags]
public enum CollectionFields { None = 0, Status = 1, Rating = 2, All = Status | Rating }

public sealed record CollectionEdit(LibraryEntry Entry, CollectionFields Fields)
{
    internal static CollectionEdit From(LibraryEntry entry, string previousStatus, double? previousRating)
    {
        var fields = CollectionFields.None;
        if (entry.Status != previousStatus) fields |= CollectionFields.Status;
        if (entry.PersonalRating != previousRating) fields |= CollectionFields.Rating;
        return new(entry, fields);
    }
}

public sealed record CollectionUpdate(CollectionSite Site, string WorkId, int? Status, int? Rating)
{
    public static CollectionUpdate? From(LibraryEntry entry, CollectionSite site, CollectionFields fields = CollectionFields.All)
    {
        int? status = (fields.HasFlag(CollectionFields.Status) ? entry.Status : null) switch
        {
            "playing" => site == CollectionSite.Vndb ? 1 : 3,
            "completed" => 2,
            "paused" => site == CollectionSite.Vndb ? 3 : 4,
            "dropped" => site == CollectionSite.Vndb ? 4 : 5,
            "wish" => site == CollectionSite.Vndb ? 5 : 1,
            _ => null
        };
        int? rating = fields.HasFlag(CollectionFields.Rating) && entry.PersonalRating is > 0 and <= 10 and var score
            ? Math.Max(site == CollectionSite.Vndb ? 10 : 1,
                (int)Math.Round(score * (site == CollectionSite.Vndb ? 10 : 1), MidpointRounding.AwayFromZero))
            : null;
        if (status is null && rating is null) return null;
        var id = WorkIdFor(entry, site);
        return id is null ? null : new(site, id, status, rating);
    }

    public static string? WorkIdFor(LibraryEntry entry, CollectionSite site)
    {
        var work = entry.MetadataMix is { } mix ? mix.Base : entry.Metadata;
        return site switch
        {
            CollectionSite.Vndb => entry.CollectionBindings?.Vndb ?? SourceWorkId(entry, work, "vndb"),
            CollectionSite.Bangumi => entry.CollectionBindings?.Bangumi ?? SourceWorkId(entry, work, "bangumi"),
            CollectionSite.NextMoe => entry.CollectionBindings?.NextMoe
                ?? (work is not null && MetadataMixing.IsNextMoeWorkId(work.Id) ? work.Id : null),
            _ => null
        };
    }

    public CollectionUpdate? RetainCurrentFields(LibraryEntry current)
    {
        var latest = From(current, Site);
        if (latest?.WorkId != WorkId) return null;
        var status = Status == latest.Status ? Status : null;
        var rating = Rating == latest.Rating ? Rating : null;
        return status is null && rating is null ? null : this with { Status = status, Rating = rating };
    }

    public string Summary(string localStatus, double? localRating)
    {
        var site = SiteName(Site);
        var status = Status.HasValue ? GameProgress.Label(localStatus) : "沿用网站状态";
        var score = Rating is { } value ? $"{(Site == CollectionSite.Vndb ? value / 10d : value).ToString("0.#", CultureInfo.InvariantCulture)} / 10"
            : localRating == 0 ? "网站不支持 0 分，沿用网站评分" : "沿用网站评分";
        return $"{site}（{WorkId}） · {status} · {score}";
    }

    public static string SiteName(CollectionSite site) => site switch
    {
        CollectionSite.Vndb => "VNDB",
        CollectionSite.Bangumi => "番组计划",
        _ => "NextMoe"
    };

    public static bool ValidVndbId(string? value, char prefix = 'v') => value is { Length: >= 2 and <= 13 }
        && value[0] == prefix && value[1] is >= '1' and <= '9' && value.Skip(1).All(char.IsAsciiDigit);

    private static string? SourceWorkId(LibraryEntry entry, WorkMetadata? work, string source)
    {
        var prefix = source + ":";
        string? id = null;
        if (work is not null)
        {
            var direct = work.Id.StartsWith(prefix, StringComparison.Ordinal);
            if ((direct || MetadataMixing.IsNextMoeWorkId(work.Id)) && !TryReadSourceWorkId(work, source, out id)) return null;
            if (direct && (id is null || work.Id != prefix + id)) return null;
        }

        if (entry.MetadataMix is { } mix && !mix.Layers.IsDefaultOrEmpty)
        {
            foreach (var layer in mix.Layers.Where(layer => layer.Source == source))
            {
                if (!TryReadSourceWorkId(layer.Work, source, out var layerId) || layerId is null
                    || layerId != layer.SourceId || layer.Work.Id != prefix + layerId
                    || id is not null && id != layerId) return null;
                id = layerId;
            }
        }
        return id;
    }

    internal static bool TryReadSourceWorkId(WorkMetadata? work, string source, out string? workId)
    {
        workId = null;
        if (work is null || work.References.IsDefaultOrEmpty) return true;
        foreach (var reference in work.References)
        {
            if (!string.Equals(reference?.Source, source, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = ReferenceId(source, reference!.Id);
            if (candidate is null || !string.IsNullOrEmpty(reference.Url) && ReferenceId(source, reference.Url, url: true) != candidate)
                return false;

            // NextMoe can list releases alongside the VNDB work. User lists are keyed by work ID.
            if (source == "vndb" && candidate[0] == 'r') continue;
            if (workId is not null && candidate != workId) return false;
            workId = candidate;
        }
        return true;
    }

    private static string? ReferenceId(string source, string value, bool url = false)
    {
        if (source == "bangumi")
            return BangumiRelationIdentityResolver.TryReferenceId(value, out var id) ? id : null;
        if (source != "vndb") return null;
        return !url && (ValidVndbId(value) || ValidVndbId(value, 'r')) ? value : VndbLinkId(value);
    }

    private static string? VndbLinkId(string value)
    {
        const string prefix = "https://vndb.org/";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var id = value[prefix.Length..];
        if (id.EndsWith('/')) id = id[..^1];
        return ValidVndbId(id) || ValidVndbId(id, 'r') ? id : null;
    }
}

public interface IBangumiCollectionWriter
{
    Task WriteAsync(CollectionUpdate update, BangumiAccessSnapshot access, CancellationToken cancellation);
}

public sealed record NextMoeCollectionResult(bool StateSynced, bool RatingSynced, string? Error, string? MergedInto = null);
public interface INextMoeCollectionWriter
{
    Task<NextMoeCollectionResult> WriteAsync(CollectionUpdate update, OnlineAuthSnapshot expected, CancellationToken cancellation);
}

public enum CollectionSyncFailure { InvalidToken, MissingPermission, Unauthorized, Offline, Timeout, RateLimited, Rejected, InvalidResponse, Storage, Changed, UnsupportedMediaType }

public sealed class CollectionSyncException(CollectionSyncFailure failure, int? httpStatus = null) : Exception(failure switch
{
    CollectionSyncFailure.InvalidToken => "请输入 VNDB 生成的访问令牌。",
    CollectionSyncFailure.MissingPermission => "授权缺少列表写入权限，请在网站检查令牌权限。",
    CollectionSyncFailure.Unauthorized => "授权无效或已过期，请重新连接。",
    CollectionSyncFailure.Offline => "网络连接中断，请在网站核对同步结果。",
    CollectionSyncFailure.Timeout => "请求超时，请在网站核对同步结果。",
    CollectionSyncFailure.RateLimited => "网站请求过于频繁，请稍后重试。",
    CollectionSyncFailure.Rejected => "网站拒绝了请求，请检查作品关联和账号权限。",
    CollectionSyncFailure.UnsupportedMediaType => "网站拒绝了提交格式（HTTP 415）。",
    CollectionSyncFailure.Storage => "无法读取或保存本机的 VNDB 授权。",
    CollectionSyncFailure.Changed => "授权已变化，请重新确认同步目标。",
    CollectionSyncFailure.InvalidResponse when httpStatus.HasValue => $"网站返回了无法确认的结果（HTTP {httpStatus}）。",
    _ => "网站返回了无法确认的结果，请稍后重试。"
})
{
    public CollectionSyncFailure Failure { get; } = failure;
}
