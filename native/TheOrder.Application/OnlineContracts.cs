using TheOrder.Application;
using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public enum OnlineFailure { NoCredential, InvalidCredential, Unauthorized, Forbidden, NotFound, RateLimited, Server, Offline, Timeout, InvalidResponse, ResponseTooLarge, InvalidRequest, RedirectBlocked, MergeLoop, MergeLimit, CredentialStorage, CredentialStorageCorrupt, LoginRequired, UserIdentityRequired, ScopeRequired, QuotaExceeded, Conflict }
public sealed class OnlineException(OnlineFailure failure, TimeSpan? retryAfter = null) : Exception(MessageFor(failure))
{
    public OnlineFailure Failure { get; } = failure;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public static string MessageFor(OnlineFailure failure) => failure switch
    {
        OnlineFailure.LoginRequired => "NextMoe 登录已失效或刷新未完成，请重新登录；当前访问方式未改变。",
        OnlineFailure.UserIdentityRequired => "个人数据需要登录 NextMoe；匿名访问和 API 密钥只能读取公开目录。",
        OnlineFailure.ScopeRequired => "这项操作需要额外授权。请在同步设置中明确选择后重新授权。",
        OnlineFailure.QuotaExceeded => "今天的在线额度已用完，已暂停请求。可稍后再试；本地书架仍可使用。",
        OnlineFailure.Conflict => "资料已在另一侧发生变化，请重新预览并选择要保留的结果。",
        OnlineFailure.NoCredential => UiText.Get("请在设置中检查当前资料访问方式。"),
        OnlineFailure.InvalidCredential => UiText.Get("API 密钥格式不正确或已失效，请重新填写。"),
        OnlineFailure.Unauthorized => UiText.Get("当前访问未获授权，请在设置中检查登录状态或 API Key。"),
        OnlineFailure.Forbidden => UiText.Get("当前账号或 API Key 无权访问此内容，可检查账号权限；本地资料已保留。"),
        OnlineFailure.NotFound => UiText.Get("当前目录中没有可访问的这条资料。"),
        OnlineFailure.RateLimited => UiText.Get("请求暂时受限，请稍后重试。"),
        OnlineFailure.Server => UiText.Get("在线服务暂时不可用，请稍后重试。"),
        OnlineFailure.Offline => UiText.Get("无法连接在线服务。离线书架仍可使用。"),
        OnlineFailure.Timeout => UiText.Get("在线请求超时，请重试。"),
        OnlineFailure.ResponseTooLarge => UiText.Get("在线响应超过大小限制，已停止读取。"),
        OnlineFailure.InvalidRequest => UiText.Get("请求参数无效，请重新搜索。"),
        OnlineFailure.RedirectBlocked => UiText.Get("服务返回了不允许的跳转，已停止请求。"),
        OnlineFailure.MergeLoop or OnlineFailure.MergeLimit => UiText.Get("作品合并信息异常，已停止跟随，请稍后重试。"),
        OnlineFailure.CredentialStorage => UiText.Get("隔离凭据无法读取或保存，请检查目录和 Windows 账户保护。"),
        OnlineFailure.CredentialStorageCorrupt => UiText.Get("凭据存储损坏或无法由当前 Windows 账户解密。原文件已保留，请重新保存或明确移除。"),
        _ => UiText.Get("服务返回了不完整或无效的数据。")
    };
}
public sealed record OnlinePage(ImmutableArray<WorkMetadata> Items, string? NextCursor);
public interface IOnlineCatalog
{
    Task<OnlinePage> SearchAsync(string query, string? cursor, CancellationToken cancellation);
    Task<WorkMetadata> DetailsAsync(string id, bool refresh, CancellationToken cancellation);
    Task TestConnectionAsync(CancellationToken cancellation);
    void ClearCache();
}
// Secret retrieval is an infrastructure concern; Presentation receives only ICredentialSettings.
public interface ICredentialSettings
{
    Task<bool> HasCredentialAsync(CancellationToken cancellation);
    Task SaveAsync(string secret, CancellationToken cancellation);
    Task ClearAsync(CancellationToken cancellation);
}
public interface ICredentialProvider { Task<string?> ReadAsync(CancellationToken cancellation); }
public interface IRemoteCoverRegistry
{
    string? Register(string? url, string revision);
}
// Display-only batch artwork lookup. Never supplies recognition identity or linked metadata.
public interface ICatalogCoverSource
{
    Task<ImmutableDictionary<string,string?>> CoversAsync(ImmutableArray<string> ids, CancellationToken cancellation);
}
public interface IRemoteArtworkCache
{
    Task<byte[]?> ReadAsync(string key, CancellationToken cancellation);
    Task<bool> WriteAsync(string key, byte[] encoded, CancellationToken cancellation);
}
public interface IRemoteArtworkPreparation
{
    Task<bool> EnsureCachedAsync(string key,CancellationToken cancellation);
}
public static class ImageSourceKeys
{
    public static string? For(LibraryEntry entry) => entry.LocalCover is { } local ? LocalCoverSources.Key(local)
        : Remote(CoverSources.Selected(entry), entry.Metadata?.FetchedAt ?? "");
    public static string? Remote(string? url, string revision) => string.IsNullOrWhiteSpace(url) ? null :
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("remote\n" + url + "\n" + revision)));
}
