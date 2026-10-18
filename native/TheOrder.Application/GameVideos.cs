using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record IgdbExternal(string Source, string Id);
public sealed record IgdbVideo(int Id, int GameId, string VideoId, string Name);
public sealed record IgdbGame(int Id, string Name, string[] Aliases, string[] Developers, string[] Platforms,
    string? ReleaseDate, string Kind, int? VersionParent, int[] Remakes, int[] Remasters, IgdbExternal[] ExternalIds, IgdbVideo[] Videos)
{
    public int? ParentGame { get; init; }
}
public sealed record IgdbResult(IgdbGame[] Games, bool Complete, DateTimeOffset FetchedAtUtc);
public sealed record GameVideoQuery(string Title, string? SteamId = null, int? Id = null);
public sealed record GameVideoSource(string GameId, string? WorkId, string Title, string[] Aliases, string[] Developers,
    string? ReleaseDate, int[] ExplicitIds, string? SteamId)
{
    public string? InstallationSteamId { get; init; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { GameId, WorkId, ExplicitIds, SteamId })));
    public MediaIdentityContext Context => new(new(new("Native", "LibraryEntry", GameId), MediaIdentityPurpose.GameVideo, "Game"), Fingerprint, "Work", "GAME");
    public MediaIdentityContext VideoContext(int id) => new(new(new("Native", "LibraryEntry", GameId), MediaIdentityPurpose.GameVideoSelection, id > 0 ? "IGDB:" + id : "Manual"), Fingerprint, "WorkVideo", "YouTube");
    public static GameVideoSource FromEntry(LibraryEntry entry, WorkMetadata? work)
    {
        var selected = entry.Installations.FirstOrDefault(x => x.Id == entry.PreferredInstallationId)
            ?? (entry.Installations.Length == 1 ? entry.Installations[0] : null);
        return From(entry.Id, work, selected?.SteamAppId is { } app ? [app] : []);
    }
    public static GameVideoSource From(string id, WorkMetadata? work, IEnumerable<string>? installedSteamIds = null)
    {
        var refs = work?.References ?? [];
        var ids = refs.Where(x => x.Source.Equals("igdb", StringComparison.OrdinalIgnoreCase) && GameVideos.ValidId(x.Id)).Select(x => int.Parse(x.Id)).Distinct().Order().ToArray();
        var installed = (installedSteamIds ?? []).Where(GameVideos.ValidId).Distinct().ToArray();
        var linked = refs.Where(x => x.Source.Equals("steam", StringComparison.OrdinalIgnoreCase) && GameVideos.ValidId(x.Id)).Select(x => x.Id).Distinct().ToArray();
        var steam = installed.Length == 1 ? installed[0] : installed.Length == 0 && linked.Length == 1 ? linked[0] : null;
        var original = work?.OriginalTitle is { Length: > 0 } name ? name : work?.Title ?? "";
        var evidence = (work?.TitleEvidence ?? []).Where(x => !x.IsMachine && x.Kind is "official" or "alias" or "latin").ToArray();
        var excluded = (work?.TitleEvidence ?? []).Where(x => x.IsMachine || x.Kind == "abbreviation").Select(x => x.Text).Except(evidence.Select(x => x.Text)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var english = evidence.Where(x => x.Language is { } lang && (lang.Equals("en", StringComparison.OrdinalIgnoreCase) || lang.StartsWith("en-", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Kind == "official" ? 0 : 1).Select(x => x.Text);
        var aliases = (work?.Aliases ?? []).Where(x => !excluded.Contains(x)).ToArray();
        bool Latin(string x) => x.Count(c => char.IsLetter(c) && c <= '\u024F') >= 4 && !x.Any(c => char.IsLetter(c) && c > '\u024F');
        var first = english.FirstOrDefault(x => x.Length is > 0 and <= 200)
            ?? (Latin(original) ? original : aliases.FirstOrDefault(Latin)) ?? original;
        var names = new[] { first, original }.Concat(english).Concat(evidence.Where(x => x.Language is "ja" or "ja-JP").Select(x => x.Text))
            .Concat(aliases).Concat([work?.Title ?? ""]).Where(x => x.Length is > 0 and <= 200).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();
        return new(id, work?.Id, names.FirstOrDefault() ?? "", names.Skip(1).ToArray(), work?.Developers.ToArray() ?? [], work?.ReleaseDate, ids, steam)
        { InstallationSteamId = installed.Length == 1 ? installed[0] : null };
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
        && (game.VersionParent is null or > 0) && (game.ParentGame is null or > 0) && game.ParentGame != game.Id && game.Remakes.Concat(game.Remasters).All(x => x > 0)
        && game.ExternalIds.All(x => x is not null && AniListValidation.Text(x.Source, 100, false) && AniListValidation.Text(x.Id, 100, false))
        && game.Videos.All(x => x is { Id: > 0 } && x.GameId == game.Id && AniListPlayback.ValidTrailer(new("youtube", x.VideoId)) && AniListValidation.Text(x.Name, 500, false));
    public static bool Valid(IgdbResult? result) => result is { Games.Length: <= 20 } && result.FetchedAtUtc != default
        && result.Games.All(Valid) && result.Games.Select(x => x.Id).Distinct().Count() == result.Games.Length;
    public static string Key(GameVideoQuery query) => "igdb-v1:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(query)));
    public static string IdentityFormat(IgdbGame game) => game.Kind + (game.ParentGame is { } id ? "; parent_game=" + id : "");
    // A presentation shortlist, never an identity decision. Keep the complete
    // result available for manual review, including unknown platform/type data.
    public static bool PrimaryCandidate(GameVideoSource source, IgdbGame game) =>
        game.Platforms.Any(x => x.Equals("PC (Microsoft Windows)", StringComparison.OrdinalIgnoreCase) || x.Equals("Windows", StringComparison.OrdinalIgnoreCase) || x is "PC-98" or "PC-8800 Series" or "DOS")
        && (SteamMatches(source, game) || game.Kind is "Main Game" or "Remake" or "Remaster" or "Expanded Game"
            && new[] { source.Title }.Concat(source.Aliases).Any(x => TitleKey(x) is { Length: > 0 } key
                && (key == TitleKey(game.Name) || game.Aliases.Any(alias => key == TitleKey(alias)))));
    public static int CandidateOrder(GameVideoSource source, IgdbGame game) => SteamMatches(source, game) ? 0
        : source.ReleaseDate is { Length: > 0 } date && game.ReleaseDate == date ? 1
        : source.Developers.Any(x => game.Developers.Any(y => TitleKey(x) == TitleKey(y))) ? 2 : 3;
    public static string CandidateKind(IgdbGame game) => game.Kind switch
    {
        "Main Game" => "游戏本篇", "Remake" => "重制版", "Remaster" => "高清重制版", "Port" => "移植版",
        "Bundle" => "合集", "Mod" => "Mod", "Expanded Game" => "扩展版", "Standalone Expansion" => "独立扩展",
        "Episode" => "分章作品", "Expansion" => "扩展内容", "DLC Add-on" => "追加内容", _ => game.Kind
    };
    public static bool SteamMatches(GameVideoSource source, IgdbGame game) => source.SteamId is { } steam
        && game.ExternalIds.Any(x => x.Source.Equals("Steam", StringComparison.OrdinalIgnoreCase) && x.Id == steam);
    public static bool InstalledEditionMatches(GameVideoSource source, IgdbGame game) => source.InstallationSteamId is { } installed
        && installed == source.SteamId && SteamMatches(source, game)
        && game.Kind is "Main Game" or "Remake" or "Remaster" or "Port" or "Expanded Game"
        && game.Platforms.Any(x => x.Equals("PC (Microsoft Windows)", StringComparison.OrdinalIgnoreCase) || x.Equals("Windows", StringComparison.OrdinalIgnoreCase));
    private static string TitleKey(string value)
    {
        try { return string.Concat(value.Normalize(NormalizationForm.FormKC).EnumerateRunes().Where(Rune.IsLetterOrDigit).Select(x => Rune.ToLowerInvariant(x).ToString())); }
        catch (ArgumentException) { return ""; }
    }
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
