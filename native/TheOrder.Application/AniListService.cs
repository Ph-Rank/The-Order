using System.Security.Cryptography;
using System.Text;

namespace TheOrder.Application;

public sealed class AniListService(IAniListProvider provider, IAniListCache? disk = null,
    Func<DateTimeOffset>? clock = null, IMediaIdentityDecisions? identityDecisions = null) : IAniListReader, IMediaUserLinkReader
{
    private readonly IMediaIdentityDecisions decisions = identityDecisions ?? (disk as IMediaIdentityDecisionOwner)?.IdentityDecisions ?? new SessionMediaIdentityDecisions();
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(24), SearchFreshFor = TimeSpan.FromHours(6), MaximumStaleAge = TimeSpan.FromDays(14);
    private readonly object gate = new(); private readonly SemaphoreSlim commits = new(1);
    private readonly Dictionary<string, AniListCacheValue> memory = new();
    private readonly Dictionary<string, long> latest = new(); private long sequence;
    private long cacheMutation;
    private DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();
    public Task<AniListMatch> MatchAsync(AniListSource source, string? query, bool refresh, CancellationToken cancellation)
        => MatchCoreAsync(source, query, refresh, true, cancellation);
    public Task<AniListMatch> FindCandidatesAsync(AniListSource source, bool refresh, CancellationToken cancellation)
        => MatchCoreAsync(source, null, refresh, false, cancellation);
    private async Task<AniListMatch> MatchCoreAsync(AniListSource source, string? query, bool refresh, bool allowBinding, CancellationToken cancellation)
    {
        if (!source.IsValid) throw new InvalidDataException("Bangumi 作品身份无效。");
        var context = MediaIdentityContext.From(source);
        var user = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        AniListMatch Selected(MediaIdentityDecisionDocument document, AniListMedia[] candidates)
        {
            var d = document.Confirmed!;
            return new(candidates, new(source.Fingerprint, int.Parse(d.Target.Id, System.Globalization.CultureInfo.InvariantCulture), source.Kind, d.Evidence)
            { NeedsReview = source.HasAmbiguousExternalIdentity || MediaIdentityDecisionRules.NeedsReview(context, d), ConfirmedFormat = d.TargetFormat, IsUserDecision = true },
                "使用独立保存的用户关联；资料变化不会自动改绑。") { RejectedIds = Rejected(document) };
        }
        static int[] Rejected(MediaIdentityDecisionDocument document) => document.Decisions.Where(x => x.Kind == MediaIdentityDecisionKind.Rejected)
            .Select(x => int.Parse(x.Target.Id, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        async Task<AniListMatch> Finish(AniListMatch result)
        {
            // Provider/cache awaits may finish after a manual decision. Always
            // re-read the user component before returning an automatic result.
            var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
            if (allowBinding && query is null && current.Confirmed is not null) return Selected(current, result.Candidates);
            if (result.Binding is { } b && (current.HasLegacyConflict || current.Revision != user.Revision
                || current.Rejects(new("AniList", source.Kind == AniListMediaKind.Anime ? "Anime" : "Manga", b.MediaId.ToString(System.Globalization.CultureInfo.InvariantCulture)))))
                result = result with { Binding = null, Notice = "已保留你的关联决定；请核对候选，不会重新自动接受已排除的条目。" };
            return result with { RejectedIds = Rejected(current) };
        }
        if (allowBinding && query is null && user.Confirmed is not null) return Selected(user, []);
        // Automatic proofs are disposable hints. Reuse the candidate/details
        // caches below with their own TTL/completeness checks, never a permanent
        // binding shortcut that could bypass a refresh or newer partial search.
        if (allowBinding && query is null && !user.HasLegacyConflict && !source.HasAmbiguousExternalIdentity && source.ExternalLinks.Length > 0)
        {
            var references = source.ExternalLinks.Select(x => { AniListReference.TryParse(x, out var r); return r!; }).Distinct().ToArray();
            var values = new List<AniListMedia>();
            foreach (var reference in references.Take(2)) values.Add(await provider.ResolveAsync(reference, cancellation).ConfigureAwait(false));
            if (values.Select(x => x.Id).Distinct().Count() == 1 && !AniListMatching.Assess(source, values[0]).Conflict)
                return await Finish(new(values.ToArray(), new(source.Fingerprint, values[0].Id, source.Kind, "Bangumi 明确外部编号"))).ConfigureAwait(false);
            return await Finish(new(values.DistinctBy(x => x.Id).ToArray(), null, "外部编号证据存在差异，请核对候选。")).ConfigureAwait(false);
        }
        var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query ?? "")))[..16];
        var key = "search:" + source.Fingerprint + ":" + queryHash;
        var result = await LoadCacheAsync(key, refresh, SearchFreshFor, async token =>
        {
            var candidates = await provider.SearchCandidatesAsync(source, query, token).ConfigureAwait(false);
            return new AniListCacheValue(key, Now, Search: candidates.Items) { SearchComplete = candidates.Complete };
        }, cancellation).ConfigureAwait(false);
        if (allowBinding && query is null && !user.HasLegacyConflict && !result.Stale && result.Value.SearchComplete
            && AniListMatching.AutomaticCandidate(source, result.Value.Search!) is { } winner && !user.Rejects(MediaIdentityDecisionRules.Target(winner)))
        {
            try
            {
                var details = await LoadAsync(winner.Id, winner.Kind, false, 1, cancellation).ConfigureAwait(false);
                if (!details.IsStale && AniListMatching.StrongEvidence(source, details.Details.Media))
                {
                    var proof = new AniListBinding(source.Fingerprint, winner.Id, source.Kind,
                        "自动关联 · 唯一候选的标题、发行形式及首播日期一致"
                        + (source.Episodes is { } eps && details.Details.Media.Episodes is { } count && eps != count ? "（集数口径相差1集）" : "")) { AutomaticPolicyVersion = 1 };
                    proof = await SaveAutomaticProofAsync(proof, cancellation).ConfigureAwait(false);
                    return await Finish(new(result.Value.Search!, proof, result.Warning)).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is AniListException or IOException or UnauthorizedAccessException)
            { return await Finish(new(result.Value.Search!, null, "暂时无法核实或保存自动关联，请核对候选；Bangumi 资料仍可浏览。")).ConfigureAwait(false); }
        }
        return await Finish(new(result.Value.Search!, null, user.HasLegacyConflict ? "历史关联证据不完整或冲突，原记录已独立保留，请手动核对。" : source.HasAmbiguousExternalIdentity
            ? "来源外部编号不完整或有歧义，未自动关联；请先核对来源记录。"
            : result.Warning ?? (!result.Value.SearchComplete && result.Value.Search!.Length > 0 ? "候选检索可能不完整，请核对后选择。" : null))).ConfigureAwait(false);
    }
    private async Task<MediaIdentityDecisionDocument> ReadDecisions(MediaIdentityContext context, CancellationToken cancellation)
    {
        var value = await decisions.ReadAsync(context, cancellation).ConfigureAwait(false);
        if (!MediaIdentityDecisionRules.Valid(value) || value.Anchor != context.Anchor) throw new IOException("已保存的关联身份不可验证，未自动匹配。");
        return value;
    }
    public async Task<AniListResult> LoadAsync(int id, AniListMediaKind kind, bool refresh, int peoplePage, CancellationToken cancellation)
    {
        if (id <= 0 || !Enum.IsDefined(kind) || peoplePage is < 1 or > 10) throw new InvalidDataException("AniList 作品身份无效。");
        var key = $"media:{(int)kind}:{id}:{peoplePage}";
        var result = await LoadCacheAsync(key, refresh, FreshFor, async token => new(key, Now,
            Details: await provider.FetchAsync(id, kind, peoplePage, token).ConfigureAwait(false)), cancellation).ConfigureAwait(false);
        var details = result.Value.Details!;
        if (details.Media.Id != id || details.Media.Kind != kind || details.PeoplePage != peoplePage) throw new AniListException(AniListFailure.InvalidResponse, "AniList 缓存身份不一致。");
        return new(details, result.Cached, result.Stale, result.Warning);
    }
    public async Task ConfirmAsync(AniListSource source, AniListDetails details, CancellationToken cancellation)
    {
        if (!source.IsValid || !AniListValidation.Details(details) || !AniListMatching.ManualCompatible(source, details.Media))
            throw new InvalidDataException("作品类型或发行证据不一致，未保存此关联。");
        var context = MediaIdentityContext.From(source);
        var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        await decisions.ApplyAsync(new(MediaIdentityDecisionRules.Target(details.Media), MediaIdentityDecisionKind.Confirmed, context,
            details.Media.Format, null, Now, MediaIdentityDecisionOrigin.User, "用户核对确认 · Bangumi → AniList（手动选择，非自动匹配）"), current.Revision, cancellation).ConfigureAwait(false);
    }
    public async Task RejectAsync(AniListSource source, AniListMedia media, CancellationToken cancellation)
    {
        if (!source.IsValid || !AniListValidation.Media(media) || source.Kind != media.Kind) throw new InvalidDataException("排除条目的媒体身份无效。");
        var context = MediaIdentityContext.From(source); var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        await decisions.ApplyAsync(new(MediaIdentityDecisionRules.Target(media), MediaIdentityDecisionKind.Rejected, context, media.Format,
            null, Now, MediaIdentityDecisionOrigin.User, "用户排除此条目 · 不再自动关联"), current.Revision, cancellation).ConfigureAwait(false);
    }
    public async Task RevokeAsync(AniListSource source, int expectedMediaId, CancellationToken cancellation)
    {
        if (!source.IsValid || expectedMediaId <= 0) throw new InvalidDataException("待解除的关联身份无效。");
        var context = MediaIdentityContext.From(source); var current = await ReadDecisions(context, cancellation).ConfigureAwait(false);
        if (current.Confirmed is not { } selected || selected.Target.Id != expectedMediaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new IOException("已保存的关联已经改变，请重新读取后再解除。");
        await decisions.ApplyAsync(selected with { Kind = MediaIdentityDecisionKind.Rejected, Context = context, DecidedAtUtc = Now,
            Origin = MediaIdentityDecisionOrigin.User, Evidence = "用户解除已保存关联 · 不再自动接受" }, current.Revision, cancellation).ConfigureAwait(false);
    }
    public Task<MediaUserLinks> ReadUserLinksAsync(MediaIdentityContext source, CancellationToken cancellation)
        => decisions.ReadLinksAsync(source, cancellation);
    public Task<MediaUserLinks> ChangeUserLinkAsync(MediaIdentityContext source, MediaUserLink? link, MediaIdentityReference target,
        long expectedRevision, CancellationToken cancellation) => decisions.ChangeLinkAsync(source, link, target, expectedRevision, cancellation);
    public async Task RestoreAutomaticAsync(MediaIdentityContext source, CancellationToken cancellation)
    {
        var current = await ReadDecisions(source, cancellation).ConfigureAwait(false);
        await decisions.ResetAsync(source, current.Revision, cancellation).ConfigureAwait(false);
    }
    private async Task<AniListBinding> SaveAutomaticProofAsync(AniListBinding binding, CancellationToken cancellation)
    {
        await commits.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var value = new AniListCacheValue("binding:" + binding.SourceFingerprint, Now, Binding: binding);
            if (disk is not null) await disk.WriteAsync(value, cancellation).ConfigureAwait(false);
            // A completed atomic replacement is the committed state, even if
            // cancellation arrived after the namespace switch.
            lock (gate) { Remember(value); cacheMutation++; }
            return binding;
        }
        finally { commits.Release(); }
    }
    private async Task<(AniListCacheValue Value, bool Cached, bool Stale, string? Warning)> LoadCacheAsync(string key, bool refresh,
        TimeSpan fresh, Func<CancellationToken, Task<AniListCacheValue>> fetch, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); var previous = await ReadAsync(key, cancellation).ConfigureAwait(false);
        var needsPlayback = previous?.Details is { Media.Kind: AniListMediaKind.Anime, HasPlaybackMetadata: false };
        if (!refresh && previous is not null && !needsPlayback && Now - previous.CachedAtUtc < fresh) return (previous, true, false, null);
        long version; lock (gate) latest[key] = version = ++sequence;
        try
        {
            var value = await fetch(cancellation).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested();
            if (value.Key != key || !AniListValidation.Cache(value) || value.CachedAtUtc > Now.AddMinutes(5))
                throw new AniListException(AniListFailure.InvalidResponse, "AniList 资料格式不一致。");
            await SaveIfCurrentAsync(key, version, value, cancellation).ConfigureAwait(false); return (value, false, false, null);
        }
        catch (AniListException e) when (previous is not null && e.Failure is AniListFailure.Offline or AniListFailure.Timeout or AniListFailure.Server or AniListFailure.RateLimited)
        {
            cancellation.ThrowIfCancellationRequested(); return (previous, true, needsPlayback || Now - previous.CachedAtUtc >= fresh, "暂时无法更新，显示之前读取的 AniList 资料。");
        }
        catch (AniListException)
        {
            await SaveIfCurrentAsync(key, version, new(key, Now, Rejected: true), cancellation).ConfigureAwait(false); throw;
        }
        finally { lock (gate) { if (latest.GetValueOrDefault(key) == version) latest.Remove(key); } }
    }
    private async Task SaveIfCurrentAsync(string key, long version, AniListCacheValue value, CancellationToken cancellation)
    {
        await commits.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            bool owns; lock (gate) owns = latest.GetValueOrDefault(key) == version;
            if (!owns) return;
            var persisted = false;
            if (disk is not null) try { await disk.WriteAsync(value, cancellation).ConfigureAwait(false); persisted = true; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (!persisted) cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (persisted || latest.GetValueOrDefault(key) == version) { Remember(value); cacheMutation++; }
            }
        }
        finally { commits.Release(); }
    }
    private async Task<AniListCacheValue?> ReadAsync(string key, CancellationToken cancellation)
    {
        bool Valid(AniListCacheValue? value) => value is not null && value.Key == key && AniListValidation.Cache(value) && !value.Rejected
            && value.CachedAtUtc <= Now.AddMinutes(5) && (value.Binding is not null || Now - value.CachedAtUtc <= MaximumStaleAge);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested(); long observedMutation;
            lock (gate)
            {
                if (memory.TryGetValue(key, out var current))
                { if (!Valid(current)) return null; Remember(current); return current; }
                observedMutation = cacheMutation;
            }
            if (disk is null) return null;
            AniListCacheValue? value = null;
            try { value = await disk.ReadAsync(key, cancellation).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                // A single bounded epoch also covers a newer value that was
                // committed and then evicted while this disk read was pending.
                if (cacheMutation != observedMutation) continue;
                if (!Valid(value)) return null;
                Remember(value!); return value;
            }
        }
    }
    private void Remember(AniListCacheValue value)
    { memory.Remove(value.Key); memory[value.Key] = value; while (memory.Count > 64) memory.Remove(memory.Keys.First()); }
}
