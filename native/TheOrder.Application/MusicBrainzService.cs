namespace TheOrder.Application;

// Public metadata remains disposable. User edition decisions use a stable typed
// Bangumi source in a separate Native Profile component.
public sealed partial class MusicBrainzService(IMusicBrainzProvider provider, IMusicBrainzCache? disk = null,
    Func<DateTimeOffset>? clock = null, IMediaIdentityDecisions? identityDecisions = null, IAppleCatalogProvider? apple = null) : IMusicBrainzReader, IMediaUserLinkReader, IMusicListeningReader
{
    private readonly IMediaIdentityDecisions decisions = identityDecisions ?? (disk as IMediaIdentityDecisionOwner)?.IdentityDecisions ?? new SessionMediaIdentityDecisions();
    public static readonly TimeSpan SearchFreshFor = TimeSpan.FromHours(1), AlbumFreshFor = TimeSpan.FromDays(1), MaximumStaleAge = TimeSpan.FromDays(7);
    private readonly object gate = new();
    private readonly SemaphoreSlim commits = new(1);
    private readonly Dictionary<string, MusicBrainzCacheValue> memory = [];
    private readonly Dictionary<string, long> latest = [];
    private long sequence;
    private long cacheMutation;
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    public async Task<MusicBrainzMatchResult> MatchAsync(MusicAlbumSource source, bool refresh, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("音乐匹配依据无效。");
        var context = MediaIdentityContext.From(source);
        var user = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        MusicBrainzMatchResult Select(MediaIdentityDecisionDocument current, MusicBrainzSearchResult search, string? automatic, bool cached, bool stale, string? warning)
        {
            var selected = current.Confirmed;
            return new(search, selected is null && !current.HasLegacyConflict && automatic is { } id && !current.Rejects(MediaIdentityDecisionRules.Target(id)) ? id : null,
                selected?.Target.Id, cached, stale, current.HasLegacyConflict ? "历史发行版关联存在冲突，原记录保留，请重新核对。" : warning)
            { NeedsReview = selected is not null && MediaIdentityDecisionRules.NeedsReview(context, selected), ConfirmedReleaseGroupId = selected?.TargetParentId,
                RejectedIds = current.Decisions.Where(d => d.Kind == MediaIdentityDecisionKind.Rejected).Select(d => d.Target.Id).ToArray() };
        }
        if (!refresh && user.Confirmed is not null) return Select(user, new([], 0, false, null, null), null, true, false, null);
        // Retrieval revisions invalidate negative/partial search caches without invalidating confirmed identities.
        // Keep legacy source fingerprints/user choices stable. Aliases affect
        // retrieval only, and are included separately in the new cache generation.
        var hints = System.Text.Json.JsonSerializer.Serialize(source.Facts.TitleAliases);
        var key = "search:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source.Fingerprint + ":retrieval-5:" + hints)));
        var result = await LoadAsync(key, refresh, SearchFreshFor, async token =>
            new(key, Now, Search: await provider.SearchAsync(source, token).ConfigureAwait(false)), cancellation).ConfigureAwait(false);
        var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        return Select(current, result.Value.Search!, current.Revision == user.Revision ? MusicBrainzMatching.Automatic(source, result.Value.Search!) : null,
            result.Cached, result.Stale, result.Warning);
    }
    public async Task<MusicBrainzAlbumResult> LoadAlbumAsync(string releaseId, bool refresh, CancellationToken cancellation)
    {
        if (!MusicBrainzReference.ValidId(releaseId)) throw new InvalidDataException("MusicBrainz 发行版编号无效。");
        var key = "release:" + releaseId;
        var result = await LoadAsync(key, refresh, AlbumFreshFor, async token =>
            new(key, Now, Album: await provider.FetchAlbumAsync(releaseId, token).ConfigureAwait(false)), cancellation).ConfigureAwait(false);
        return new(result.Value.Album!, result.Cached, result.Stale, result.Warning);
    }
    public async Task<MusicBrainzSearchResult> EditionsAsync(string releaseGroupId, CancellationToken cancellation)
    {
        if (!MusicBrainzReference.ValidId(releaseGroupId)) throw new InvalidDataException("MusicBrainz 专辑组编号无效。");
        var key = "group:" + releaseGroupId;
        var result = await LoadAsync(key, false, SearchFreshFor, async token => new(key, Now,
            Search: await provider.EditionsAsync(releaseGroupId, token).ConfigureAwait(false)), cancellation).ConfigureAwait(false);
        return result.Value.Search! with { Notice = result.Warning ?? result.Value.Search!.Notice };
    }
    public async Task ConfirmAsync(MusicAlbumSource source, MusicBrainzAlbum album, CancellationToken cancellation)
    {
        if (!source.IsValid || !MusicBrainzValidation.Album(album) || MusicBrainzMatching.Assess(source, album.Release).HasIdentityConflict)
            throw new InvalidDataException("音乐发行版或专辑组的明确编号存在冲突，未保存此关联。");
        var context = MediaIdentityContext.From(source); var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        await decisions.ApplyAsync(new(MediaIdentityDecisionRules.Target(album.Release.Id), MediaIdentityDecisionKind.Confirmed, context,
            "Release", album.Release.ReleaseGroupId, Now, MediaIdentityDecisionOrigin.User, "用户核对发行版 · Bangumi → MusicBrainz"), current.Revision, cancellation).ConfigureAwait(false);
    }
    public async Task RejectAsync(MusicAlbumSource source, MusicBrainzReleaseCandidate release, CancellationToken cancellation)
    {
        if (!source.IsValid || !MusicBrainzValidation.Candidate(release)) throw new InvalidDataException("排除发行版的身份无效。");
        var context = MediaIdentityContext.From(source); var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        await decisions.ApplyAsync(new(MediaIdentityDecisionRules.Target(release.Id), MediaIdentityDecisionKind.Rejected, context,
            "Release", release.ReleaseGroupId, Now, MediaIdentityDecisionOrigin.User, "用户排除此发行版 · 不再自动关联"), current.Revision, cancellation).ConfigureAwait(false);
    }
    public Task<MediaUserLinks> ReadUserLinksAsync(MediaIdentityContext source, CancellationToken cancellation) => decisions.ReadLinksAsync(source, cancellation);
    public Task<MediaUserLinks> ChangeUserLinkAsync(MediaIdentityContext source, MediaUserLink? link, MediaIdentityReference target,
        long expectedRevision, CancellationToken cancellation) => decisions.ChangeLinkAsync(source, link, target, expectedRevision, cancellation);
    public async Task RestoreAutomaticAsync(MediaIdentityContext source, CancellationToken cancellation)
    {
        var current = await ReadDecisions(source, cancellation).ConfigureAwait(false);
        await decisions.ResetAsync(source, current.Revision, cancellation).ConfigureAwait(false);
    }
    private async Task<MediaIdentityDecisionDocument> ReadDecisions(MediaIdentityContext context, CancellationToken cancellation)
    {
        var current = await decisions.ReadAsync(context, cancellation).ConfigureAwait(false);
        if (!MediaIdentityDecisionRules.Valid(current) || current.Anchor != context.Anchor) throw new IOException("已保存的发行版关联身份不可验证，未自动匹配。");
        return current;
    }
    public async Task RevokeAsync(MusicAlbumSource source, string expectedReleaseId, CancellationToken cancellation)
    {
        if (!source.IsValid || !MusicBrainzReference.ValidId(expectedReleaseId)) throw new InvalidDataException("待解除的发行版身份无效。");
        var context = MediaIdentityContext.From(source); var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        if (current.Confirmed is not { } selected || selected.Target.Id != expectedReleaseId) throw new IOException("已保存的发行版已改变，请重新读取后再解除。");
        await decisions.ApplyAsync(selected with { Kind = MediaIdentityDecisionKind.Rejected, Context = context, DecidedAtUtc = Now,
            Origin = MediaIdentityDecisionOrigin.User, Evidence = "用户解除发行版关联 · 不再自动接受" }, current.Revision, cancellation).ConfigureAwait(false);
    }
    private async Task<(MusicBrainzCacheValue Value, bool Cached, bool Stale, string? Warning)> LoadAsync(string key, bool refresh,
        TimeSpan fresh, Func<CancellationToken, Task<MusicBrainzCacheValue>> fetch, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var prior = await ReadAsync(key, cancellation).ConfigureAwait(false);
        if (!refresh && prior is not null && Now - prior.CachedAtUtc < fresh) return (prior, true, false, null);
        long version; lock (gate) latest[key] = version = ++sequence;
        try
        {
            var value = await fetch(cancellation).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
            if (value.Key != key || !MusicBrainzValidation.Cache(value) || value.CachedAtUtc > Now.AddMinutes(5))
                throw new MusicBrainzException(MusicBrainzFailure.InvalidResponse, "MusicBrainz 资料身份或格式不一致。");
            value = Snapshot(value);
            await commits.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                bool owns; lock (gate) owns = latest.GetValueOrDefault(key) == version;
                if (owns)
                {
                    var persisted = false;
                    if (disk is not null)
                        try { await disk.WriteAsync(Snapshot(value), cancellation).ConfigureAwait(false); persisted = true; }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    if (!persisted) cancellation.ThrowIfCancellationRequested();
                    lock (gate)
                    {
                        if (persisted || latest.GetValueOrDefault(key) == version) { Remember(value); cacheMutation++; }
                    }
                }
            }
            finally { commits.Release(); }
            return (Snapshot(value), false, false, null);
        }
        catch (MusicBrainzException e) when (prior is not null && e.Failure is MusicBrainzFailure.Offline or MusicBrainzFailure.Timeout
            or MusicBrainzFailure.Server or MusicBrainzFailure.RateLimited)
        {
            cancellation.ThrowIfCancellationRequested();
            return (prior, true, Now - prior.CachedAtUtc >= fresh, "暂时无法更新，显示已缓存的音乐资料。" + e.Message);
        }
        catch (MusicBrainzException)
        {
            // A removed/invalid release must not reappear from disk on the next visit or process restart.
            await commits.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                bool owns; lock (gate) owns = latest.GetValueOrDefault(key) == version;
                if (owns)
                {
                    var rejected = new MusicBrainzCacheValue(key, Now, Rejected: true);
                    if (disk is not null)
                        try { await disk.WriteAsync(rejected, cancellation).ConfigureAwait(false); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    lock (gate) { Remember(rejected); cacheMutation++; }
                }
            }
            finally { commits.Release(); }
            throw;
        }
        finally { lock (gate) { if (latest.GetValueOrDefault(key) == version) latest.Remove(key); } }
    }
    private async Task<MusicBrainzCacheValue?> ReadAsync(string key, CancellationToken cancellation)
    {
        bool Valid(MusicBrainzCacheValue? value) => value is not null && value.Key == key && MusicBrainzValidation.Cache(value) && !value.Rejected
            && value.CachedAtUtc <= Now.AddMinutes(5) && (value.Binding is not null || Now - value.CachedAtUtc <= MaximumStaleAge);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested(); long observedMutation;
            lock (gate)
            {
                if (memory.TryGetValue(key, out var current))
                { if (!Valid(current)) return null; Remember(current); return Snapshot(current); }
                observedMutation = cacheMutation;
            }
            if (disk is null) return null;
            MusicBrainzCacheValue? value = null;
            try { value = await disk.ReadAsync(key, cancellation).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (cacheMutation != observedMutation) continue;
                if (!Valid(value)) return null;
                var owned = Snapshot(value!); Remember(owned); return Snapshot(owned);
            }
        }
    }
    private void Remember(MusicBrainzCacheValue value)
    {
        memory.Remove(value.Key); memory[value.Key] = value;
        while (memory.Count > 64) memory.Remove(memory.Keys.First());
    }
    // Arrays remain serialization-compatible, but neither a provider, disk
    // adapter nor a UI consumer may retain the cache's mutable ownership.
    private static MusicBrainzCacheValue Snapshot(MusicBrainzCacheValue value)
    {
        static MusicBrainzReleaseCandidate Release(MusicBrainzReleaseCandidate release) => release with
        { Labels = release.Labels.ToArray(), MediaFormats = release.MediaFormats.ToArray(),
            ReleaseEvents = release.ReleaseEvents.ToArray(), ArtistCredits = release.ArtistCredits.ToArray() };
        return value with
        {
            Outlets = value.Outlets is { } outlets ? MusicOutlets.Copy(outlets) : null,
            Apple = value.Apple is { } catalog ? catalog with { Candidates = catalog.Candidates.ToArray() } : null,
            Search = value.Search is { } search ? search with { Candidates = search.Candidates.Select(Release).ToArray() } : null,
            Album = value.Album is { } album ? album with
            {
                Release = Release(album.Release), SecondaryTypes = album.SecondaryTypes.ToArray(), Credits = album.Credits.ToArray(),
                Discs = album.Discs.Select(disc => disc with { Tracks = disc.Tracks.Select(track => track with
                {
                    Credits = track.Credits.ToArray(), ArtistCredits = track.ArtistCredits.ToArray(),
                    Works = track.Works.Select(work => work with { Languages = work.Languages.ToArray(), Credits = work.Credits.ToArray() }).ToArray()
                }).ToArray() }).ToArray()
            } : null
        };
    }
}
