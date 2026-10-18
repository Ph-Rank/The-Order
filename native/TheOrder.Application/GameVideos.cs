using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record IgdbExternal(string Source, string Id);
public sealed record IgdbVideo(int Id, int GameId, string VideoId, string Name);
public sealed record IgdbGame(int Id, string Name, string[] Aliases, string[] Developers, string[] Platforms,
    string? ReleaseDate, string Kind, int? VersionParent, int[] Remakes, int[] Remasters, IgdbExternal[] ExternalIds, IgdbVideo[] Videos);
public sealed record IgdbResult(IgdbGame[] Games, bool Complete, DateTimeOffset FetchedAtUtc);
public sealed record GameVideoQuery(string Title, string? SteamId = null, int? Id = null);
public sealed record GameVideoSource(string GameId, string? WorkId, string Title, string[] Aliases, string[] Developers,
    string? ReleaseDate, int[] ExplicitIds, string? SteamId)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { GameId, WorkId, ExplicitIds, SteamId })));
    public MediaIdentityContext Context => new(new(new("Native", "LibraryEntry", GameId), MediaIdentityPurpose.GameVideo, "Game"), Fingerprint, "Work", "GAME");
    public MediaIdentityContext VideoContext(int id) => new(new(new("Native", "LibraryEntry", GameId), MediaIdentityPurpose.GameVideoSelection, id > 0 ? "IGDB:" + id : "Manual"), Fingerprint, "WorkVideo", "YouTube");
    public static GameVideoSource From(string id, WorkMetadata? work, IEnumerable<string>? installedSteamIds = null)
    {
        var refs = work?.References ?? [];
        var ids = refs.Where(x => x.Source.Equals("igdb", StringComparison.OrdinalIgnoreCase) && GameVideos.ValidId(x.Id)).Select(x => int.Parse(x.Id)).Distinct().Order().ToArray();
        var steam = refs.Where(x => x.Source.Equals("steam", StringComparison.OrdinalIgnoreCase) && GameVideos.ValidId(x.Id)).Select(x => x.Id)
            .Concat((installedSteamIds ?? []).Where(GameVideos.ValidId)).Distinct().ToArray();
        return new(id, work?.Id, work?.OriginalTitle is { Length: > 0 } original ? original : work?.Title ?? "", work?.Aliases.ToArray() ?? [], work?.Developers.ToArray() ?? [], work?.ReleaseDate, ids, steam.Length == 1 ? steam[0] : null);
    }
}
public sealed class GameVideoException(string code) : IOException(code)
{
    public string Code { get; } = code;
    public string UserMessage => Code switch
    {
        "unconfigured" => "游戏影像服务尚未配置；这不代表此游戏没有影像。",
        "authentication" => "游戏影像服务的开发者认证暂不可用，请稍后重试，无需登录 Twitch。",
        "rate_limited" => "游戏影像服务暂时繁忙，请稍后重试。",
        "timeout" => "读取游戏影像超时，请重试。",
        _ => "暂时无法读取游戏影像，请重试；本地游戏与资料仍可使用。"
    };
}
public interface IGameVideoProvider { Task<IgdbResult> QueryAsync(GameVideoQuery query, CancellationToken cancellation); }
public interface IGameVideoCache
{
    Task<IgdbResult?> ReadAsync(string key, CancellationToken cancellation);
    Task WriteAsync(string key, IgdbResult value, CancellationToken cancellation);
}
public static class GameVideos
{
    public static bool ValidId(string? id) => int.TryParse(id, out var n) && n > 0 && id == n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static bool Valid(IgdbGame? game) => game is { Id: > 0, Aliases.Length: <= 32, Developers.Length: <= 32, Platforms.Length: <= 32, Remakes.Length: <= 32, Remasters.Length: <= 32, ExternalIds.Length: <= 64, Videos.Length: <= 32 }
        && AniListValidation.Text(game.Name, 500, false) && AniListValidation.Text(game.Kind, 100, false)
        && game.Aliases.Concat(game.Developers).Concat(game.Platforms).All(x => AniListValidation.Text(x, 500, false))
        && (game.ReleaseDate is null || DateOnly.TryParseExact(game.ReleaseDate, "yyyy-MM-dd", out _))
        && (game.VersionParent is null or > 0) && game.Remakes.Concat(game.Remasters).All(x => x > 0)
        && game.ExternalIds.All(x => x is not null && AniListValidation.Text(x.Source, 100, false) && AniListValidation.Text(x.Id, 100, false))
        && game.Videos.All(x => x is { Id: > 0 } && x.GameId == game.Id && AniListPlayback.ValidTrailer(new("youtube", x.VideoId)) && AniListValidation.Text(x.Name, 500, false));
    public static bool Valid(IgdbResult? result) => result is { Games.Length: <= 20 } && result.FetchedAtUtc != default
        && result.Games.All(Valid) && result.Games.Select(x => x.Id).Distinct().Count() == result.Games.Length;
    public static string Key(GameVideoQuery query) => "igdb-v1:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(query)));
    public static string Classification(string name)
    {
        var text = name.ToLowerInvariant();
        if (text.Contains("trailer") || text.Contains("ティザー") || text.Contains("プロモーション")) return "宣传片 · 按视频名称";
        if (text.Contains("opening") || text.Contains("オープニング") || text == "op" || text.StartsWith("op ")) return "OP · 按视频名称";
        if (text.Contains("gameplay") || text.Contains("プレイ動画")) return "实机影像 · 按视频名称";
        return "宣传影像";
    }
    public static bool TryYouTube(string? input, out string id)
    {
        id = "";
        if (input is not { Length: > 0 and <= 2048 } || !Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host == "youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" && uri.AbsolutePath == "/watch")
        {
            var ids = uri.Query.TrimStart('?').Split('&').Where(x => x.StartsWith("v=", StringComparison.Ordinal)).Select(x => x[2..]).ToArray();
            if (ids.Length == 1) id = ids[0];
        }
        return AniListPlayback.ValidTrailer(new("youtube", id));
    }
}
public sealed class GameVideoReader(IGameVideoProvider provider, IGameVideoCache? cache = null, Func<DateTimeOffset>? clock = null)
{
    public async Task<(IgdbResult Result, bool Stale)> QueryAsync(GameVideoQuery query, bool refresh, CancellationToken cancellation)
    {
        var now = (clock ?? (() => DateTimeOffset.UtcNow))(); var key = GameVideos.Key(query);
        var prior = cache is null ? null : await cache.ReadAsync(key, cancellation);
        if (prior is not null && (!GameVideos.Valid(prior) || query.Id is { } expected && (prior.Games.Length > 1 || prior.Games.Any(x => x.Id != expected))
            || now - prior.FetchedAtUtc < TimeSpan.Zero || now - prior.FetchedAtUtc > TimeSpan.FromDays(7))) prior = null;
        if (!refresh && prior is not null && now - prior.FetchedAtUtc < TimeSpan.FromHours(12)) return (prior, false);
        try
        {
            var value = await provider.QueryAsync(query, cancellation); cancellation.ThrowIfCancellationRequested();
            if (!GameVideos.Valid(value) || query.Id is { } id && (value.Games.Length > 1 || value.Games.Any(x => x.Id != id))) throw new GameVideoException("invalid_response");
            if (cache is not null) try { await cache.WriteAsync(key, value, cancellation); } catch (IOException) { }
            return (value, false);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) when (prior is not null) { cancellation.ThrowIfCancellationRequested(); return (prior, true); }
    }
}
