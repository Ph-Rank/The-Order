using System.ComponentModel;
using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed record GameVideoItem(string VideoId, string Name, IgdbVideo[] Relations, bool UserProvided)
{
    public string Classification => UserProvided ? "用户提供 · 未核验" : GameVideos.Classification(Name);
    public string Url => "https://www.youtube.com/watch?v=" + VideoId;
}
public sealed class GameVideoViewModel(GameVideoReader reader, IMediaIdentityDecisions decisions) : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public GameVideoSource? Source { get; private set; }
    public IgdbGame? Game { get; private set; }
    public IgdbGame[] Candidates { get; private set; } = [];
    public GameVideoItem[] Videos { get; private set; } = [];
    public string Message { get; private set; } = "";
    public bool Busy { get; private set; }
    public bool Complete { get; private set; }
    public long Version { get; private set; }
    public string? DefaultVideo => videoChoices?.Confirmed?.Target.Id;
    private MediaIdentityDecisionDocument? mapping, videoChoices;
    private MediaUserLinks? userLinks;
    private CancellationTokenSource? stop;
    private bool loaded, disposed;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    public void SetSource(GameVideoSource? source)
    {
        if (Source?.Fingerprint == source?.Fingerprint) { Source = source; return; }
        Cancel(); Source = source; Game = null; Candidates = []; Videos = []; mapping = videoChoices = null; userLinks = null;
        Message = ""; Complete = loaded = false; Changed();
    }
    public async Task LoadAsync(bool refresh = false, string? search = null)
    {
        if (disposed || Source is not { } source || Busy || loaded && !refresh && search is null) return;
        Cancel(); var run = Version; var token = (stop = new()).Token; Busy = true; Message = "正在读取游戏影像…"; Changed();
        try
        {
            var choices = await decisions.ReadAsync(source.Context, token);
            var manual = await decisions.ReadLinksAsync(source.Context, token);
            if (!Current(run, source)) return;
            mapping = choices; userLinks = manual;
            var personalVideos = await decisions.ReadAsync(source.VideoContext(0), token);
            if (!Current(run, source)) return;
            videoChoices = personalVideos;
            var choice = choices.Confirmed;
            int? id = choice is not null ? int.Parse(choice.Target.Id) : source.ExplicitIds.Length == 1 ? source.ExplicitIds[0] : null;
            if (source.ExplicitIds.Length > 1 && choice is null && search is null) { Message = "已有多个互相冲突的 IGDB 编号，请手动核对对应游戏。"; Rebuild(); return; }
            var query = new GameVideoQuery(search ?? source.Title, search is null ? source.SteamId : null, search is null ? id : null);
            var result = await reader.QueryAsync(query, refresh, token);
            // A missing Steam mapping may still have a title entry. Try at most
            // three bounded queries; never fall back from a confirmed/exact ID.
            if (search is null && id is null)
            {
                var alternatives = (source.SteamId is null ? source.Aliases : new[] { source.Title }.Concat(source.Aliases))
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 200 && (source.SteamId is not null || x != source.Title))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(2);
                foreach (var title in alternatives)
                {
                    if (result.Result.Games.Length > 0 || !result.Result.Complete || !Current(run, source)) break;
                    result = await reader.QueryAsync(new(title), refresh, token);
                }
            }
            if (!Current(run, source)) return;
            var latest = await decisions.ReadAsync(source.Context, token);
            if (!Current(run, source)) return;
            if (latest.Revision != choices.Revision)
            {
                mapping = latest; Game = null; Candidates = []; Rebuild();
                Message = "游戏对应已被另一项操作修改，请刷新影像后重试。"; return;
            }
            Candidates = result.Result.Games; Complete = result.Result.Complete;
            // A title search, even a complete single result, never confirms identity.
            var exact = search is null && id is { } expected && Complete ? Candidates.SingleOrDefault(x => x.Id == expected) : null;
            if (exact is not null && !choices.Rejects(new("IGDB", "Game", exact.Id.ToString()))
                && (choice is not null || !choices.Decisions.Any(x => x.Target.Id == exact.Id.ToString()))
                && (choice is null || !MediaIdentityDecisionRules.NeedsReview(source.Context, choice)
                    && choice.TargetFormat == exact.Kind && choice.TargetParentId == exact.VersionParent?.ToString()))
            {
                Game = exact;
                var selectedVideos = await decisions.ReadAsync(source.VideoContext(exact.Id), token);
                if (!Current(run, source)) return;
                videoChoices = selectedVideos;
                Message = result.Stale ? "当前使用已缓存的游戏影像；在线读取失败，可重试。" : "作品级影像，不代表你本地的具体发行版本。";
            }
            else { Game = null; videoChoices = personalVideos; Message = Candidates.Length == 0 ? "尚未找到对应的 IGDB 游戏，可更换标题或手动添加视频。" : Complete ? "请选择并确认对应游戏；请核对原版、重制版、移植版与合集。" : "候选未完整读取，请手动核对，未自动确认。"; }
            if (!Current(run, source)) return;
            loaded = true; Rebuild();
            if (Game is not null && Videos.Length == 0 && !result.Stale) Message = "IGDB 当前未收录此游戏的可用视频。";
        }
        catch (OperationCanceledException) { if (Current(run, source)) Message = "已取消读取，可以重试。"; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        { if (Current(run, source)) { Message = e is GameVideoException issue ? issue.UserMessage : "游戏影像资料暂不可用，可以重试。"; Rebuild(); } }
        finally { if (Current(run, source)) { Busy = false; Changed(); } }
    }
    public async Task ConfirmAsync(IgdbGame candidate)
    {
        if (Source is not { } source || mapping is null || !Candidates.Any(x => x.Id == candidate.Id) || Busy) return;
        var committed = false;
        await Mutate(async token =>
        {
            var saved = await decisions.ApplyAsync(new(new("IGDB", "Game", candidate.Id.ToString()), MediaIdentityDecisionKind.Confirmed,
                source.Context, candidate.Kind, candidate.VersionParent?.ToString(), DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User,
                "用户确认 IGDB 游戏对应；作品级，未确认本地发行版本"), mapping.Revision, token);
            if (Source?.Fingerprint == source.Fingerprint && !token.IsCancellationRequested) { mapping = saved; committed = true; }
        });
        if (committed && Source?.Fingerprint == source.Fingerprint) { loaded = false; await LoadAsync(); }
    }
    public async Task RejectGameAsync(IgdbGame candidate)
    {
        if (Source is not { } source || mapping is null || Busy) return;
        await Mutate(async token => { var saved = await decisions.ApplyAsync(new(new("IGDB", "Game", candidate.Id.ToString()), MediaIdentityDecisionKind.Rejected, source.Context, candidate.Kind, candidate.VersionParent?.ToString(), DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User, "用户排除 IGDB 游戏对应"), mapping.Revision, token); if (Source?.Fingerprint != source.Fingerprint || token.IsCancellationRequested) return; mapping = saved; Game = null; videoChoices = null; Rebuild(); });
    }
    public async Task WithdrawAsync()
    {
        if (Source is not { } source || mapping is null || Game is not { } game || Busy) return;
        var chosen = mapping.Confirmed ?? new(new("IGDB", "Game", game.Id.ToString()), MediaIdentityDecisionKind.Confirmed,
            source.Context, game.Kind, game.VersionParent?.ToString(), DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User, "用户撤销已有 IGDB 编号对应");
        await Mutate(async token => { var saved = await decisions.ApplyAsync(chosen with { Kind = MediaIdentityDecisionKind.Withdrawn, Context = source.Context, DecidedAtUtc = DateTimeOffset.UtcNow }, mapping.Revision, token); if (Source?.Fingerprint != source.Fingerprint || token.IsCancellationRequested) return; mapping = saved; Game = null; videoChoices = null; loaded = false; Rebuild(); });
    }
    public async Task ChooseVideoAsync(GameVideoItem item, bool reject)
    {
        if (Source is not { } source || !Videos.Contains(item) || Busy) return;
        await Mutate(async token =>
        {
            var context = source.VideoContext(Game?.Id ?? 0);
            var current = videoChoices ?? await decisions.ReadAsync(context, token);
            var saved = await decisions.ApplyAsync(new(new("YouTube", "Video", item.VideoId), reject ? MediaIdentityDecisionKind.Rejected : MediaIdentityDecisionKind.Confirmed,
                context, item.UserProvided ? "UserProvided" : "IGDB", null, DateTimeOffset.UtcNow, MediaIdentityDecisionOrigin.User,
                (item.UserProvided ? "用户提供；未核验：" : "IGDB video records: " + string.Join(",", item.Relations.Select(x => x.Id)) + "; ") + item.Name), current.Revision, token);
            if (Source?.Fingerprint != source.Fingerprint || token.IsCancellationRequested) return;
            videoChoices = saved;
            Rebuild();
        });
    }
    public async Task AddManualAsync(string url)
    {
        if (Source is not { } source || Busy) return;
        if (!GameVideos.TryYouTube(url, out var id)) { Message = "请输入有效的 HTTPS YouTube 视频地址。"; Changed(); return; }
        await Mutate(async token =>
        {
            var current = userLinks ?? await decisions.ReadLinksAsync(source.Context, token);
            var target = new MediaIdentityReference("YouTube", "Video", id);
            var saved = await decisions.ChangeLinkAsync(source.Context, new(source.Context, target, MediaUserLinkState.Related, "用户提供的视频", "YouTube", "RELATED", "用户提供；未核验", DateTimeOffset.UtcNow), target, current.Revision, token);
            if (Source?.Fingerprint != source.Fingerprint || token.IsCancellationRequested) return;
            userLinks = saved;
            Rebuild();
        });
    }
    public async Task RemoveManualAsync(GameVideoItem item)
    {
        if (Source is not { } source || !item.UserProvided || userLinks is null || Busy) return;
        await Mutate(async token => { var saved = await decisions.ChangeLinkAsync(source.Context, null, new("YouTube", "Video", item.VideoId), userLinks.Revision, token); if (Source?.Fingerprint != source.Fingerprint || token.IsCancellationRequested) return; userLinks = saved; Rebuild(); });
    }
    public async Task ResetVideoChoicesAsync()
    {
        if (Source is not { } source || Busy || videoChoices is null) return;
        var context = source.VideoContext(Game?.Id ?? 0); var revision = videoChoices.Revision;
        await Mutate(async token => { await decisions.ResetAsync(context, revision, token); var saved = await decisions.ReadAsync(context, token); if (Source?.Fingerprint != source.Fingerprint || token.IsCancellationRequested) return; videoChoices = saved; Rebuild(); });
    }
    private async Task Mutate(Func<CancellationToken, Task> action)
    {
        var source = Source; Cancel(); var run = Version; var token = (stop = new()).Token; Busy = true; Changed();
        try { await action(token); if (Current(run, source)) Message = "已保存你的选择。"; }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException) { if (Current(run, source)) Message = "保存失败或选择已改变，请刷新后重试；原记录保留。"; }
        finally { if (Current(run, source)) { Busy = false; Changed(); } }
    }
    private void Rebuild()
    {
        var data = (Game?.Videos ?? []).GroupBy(x => x.VideoId).Select(g => new GameVideoItem(g.Key, g.First().Name, g.ToArray(), false));
        var manual = (userLinks?.Items ?? []).Where(x => x.Source.Fingerprint == Source?.Fingerprint).Select(x => new GameVideoItem(x.Target.Id, x.Title, [], true));
        Videos = data.Concat(manual).GroupBy(x => x.VideoId).Select(x => x.First()).Where(x => videoChoices?.Rejects(new("YouTube", "Video", x.VideoId)) != true)
            .OrderByDescending(x => x.VideoId == DefaultVideo).ThenBy(x => x.Classification.StartsWith("宣传片") ? 0 : x.Classification.StartsWith("OP") ? 1 : 2).ThenBy(x => x.Name).ToArray();
    }
    private bool Current(long run, GameVideoSource? source) => !disposed && Version == run && Source?.Fingerprint == source?.Fingerprint;
    public void Cancel() { var busy = Busy; Version++; stop?.Cancel(); stop?.Dispose(); stop = null; Busy = false; if (busy) { loaded = false; Message = "已取消读取，可以重试。"; Changed(); } }
    public void Dispose() { if (disposed) return; Cancel(); disposed = true; Source = null; }
}
