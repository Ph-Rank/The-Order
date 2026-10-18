namespace TheOrder.Application;

public sealed partial class MusicBrainzService
{
    public async Task<MusicOutletResult> LoadOutletsAsync(MusicBrainzReference reference, bool refresh, CancellationToken cancellation)
    {
        if (!MusicOutlets.Reference(reference)) throw new InvalidDataException("收听入口仅适用于具体发行版或录音。");
        if (provider is not IMusicOutletProvider outlets) throw new NotSupportedException("当前资料源不支持收听入口。");
        var key = $"outlets:{(int)reference.Kind}:{reference.Id}";
        var result = await LoadAsync(key, refresh, AlbumFreshFor, async token =>
            new MusicBrainzCacheValue(key, Now) { Outlets = await outlets.FetchOutletsAsync(reference, token).ConfigureAwait(false) }, cancellation).ConfigureAwait(false);
        return new(result.Value.Outlets!, result.Cached, result.Stale, result.Warning);
    }
    public async Task<MusicDigitalOutletResult> LoadDigitalOutletsAsync(MusicOutletSnapshot current, CancellationToken cancellation)
    {
        if (!MusicOutlets.Valid(current) || current.Entity.Kind != MusicBrainzEntityKind.Release || current.GroupId is null)
            return new([], false, "此条目没有可核对的专辑组，未猜测其他发行版本。");
        var editions = await EditionsAsync(current.GroupId, cancellation).ConfigureAwait(false);
        var eligible = editions.Candidates.Where(x => x.Id != current.Entity.Id && x.ReleaseGroupId == current.GroupId
            && x.Status == "Official" && x.MediaFormats.Length > 0 && x.MediaFormats.All(f => f == "Digital Media"))
            .OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var values = new List<MusicOutletResult>(); var warnings = new List<string>();
        if (editions.Candidates.Any(x => x.MediaFormats.Length == 0)) warnings.Add("部分发行物没有提供介质，未推测为数字版。");
        // One bounded group page and at most two exact-release lookups. Never promote
        // a unique partial page into an equivalent release, or change the album choice.
        foreach (var edition in eligible.Take(2))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var result = await LoadOutletsAsync(new(MusicBrainzEntityKind.Release, edition.Id), false, cancellation).ConfigureAwait(false);
                if (result.Snapshot.GroupId != current.GroupId || result.Snapshot.ReleaseStatus != "Official" || result.Snapshot.Formats.Length == 0 || result.Snapshot.Formats.Any(f => f != "Digital Media"))
                { warnings.Add("一条发行记录的专辑组、发行状态或介质已变化，未混入当前入口。"); continue; }
                values.Add(result);
            }
            catch (MusicBrainzException) { warnings.Add("部分数字发行资料暂时不可用，可稍后重试。"); }
        }
        return new(values.ToArray(), editions.Truncated || eligible.Length > 2 || warnings.Count > 0,
            string.Join(" ", warnings.Distinct().Append(editions.Truncated || eligible.Length > 2 ? "仅检查有界结果，不代表全部数字版本。" : "")).Trim() is { Length: > 0 } message ? message : null);
    }
    public async Task<AppleCatalogResult> SearchAppleAsync(MusicOutletSnapshot current, string country, bool refresh, CancellationToken cancellation)
    {
        if (!MusicOutlets.Valid(current) || current.Entity.Kind != MusicBrainzEntityKind.Release || !MusicOutlets.Country(country)) throw new InvalidDataException("请选择具体发行版及支持的商店地区。");
        if (apple is null) throw new NotSupportedException("当前构建未启用 Apple 官方目录查询。");
        string? explicitId = null;
        foreach (var link in current.Links)
            if (AppleAlbumCandidate.TryCatalogAlbumId(link.Url, out var id)) { explicitId = id; break; }
        var query = new AppleCatalogQuery(current.Entity, current.Title, current.Artist, current.Barcode, country, explicitId);
        var result = await LoadAsync(query.Key, refresh, AlbumFreshFor, async token =>
            new MusicBrainzCacheValue(query.Key, Now) { Apple = await apple.SearchAsync(query, token).ConfigureAwait(false) }, cancellation).ConfigureAwait(false);
        return result.Value.Apple! with { FromCache = result.Cached, IsStale = result.Stale, Warning = result.Warning };
    }
    public Task<MediaUserLinks> ReadAppleChoicesAsync(MusicBrainzReference release, CancellationToken cancellation)
    {
        if (!MusicOutlets.Reference(release) || release.Kind != MusicBrainzEntityKind.Release) throw new InvalidDataException("商店选择必须属于具体发行版。");
        return decisions.ReadLinksAsync(MediaIdentityContext.ForMusicOutlet(release), cancellation);
    }
    public Task<MediaUserLinks> ChangeAppleChoiceAsync(MusicBrainzReference release, AppleAlbumCandidate? candidate,
        MediaIdentityReference target, long revision, CancellationToken cancellation)
    {
        if (!MusicOutlets.Reference(release) || release.Kind != MusicBrainzEntityKind.Release || target.Provider != "iTunes"
            || !target.IsValid || candidate is not null && (!candidate.IsValid || candidate.Target != target || candidate.Url.Length > 1000))
            throw new InvalidDataException("商店页面与具体发行版的选择无法验证。");
        var context = MediaIdentityContext.ForMusicOutlet(release);
        var link = candidate is null ? null : new MediaUserLink(context, target, MediaUserLinkState.Related,
            candidate.Title, "Album", "RELATED", candidate.Url, Now);
        return decisions.ChangeLinkAsync(context, link, target, revision, cancellation);
    }
}
