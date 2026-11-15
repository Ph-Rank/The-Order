using TheOrder.Application;
using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record PersonalValues(string Name, string Status, double? Rating, bool Favorite,
    string Notes, ImmutableArray<string> Categories, ImmutableArray<string> Tags)
{
    public static PersonalValues From(LibraryEntry e) => new(e.Name, e.Status, e.PersonalRating, e.Favorite, e.Notes, e.Categories, e.CustomTags);
    public bool Matches(PersonalValues other) => Name == other.Name && Status == other.Status && Rating == other.Rating && Favorite == other.Favorite && Notes == other.Notes
        && Categories.SequenceEqual(other.Categories) && Tags.SequenceEqual(other.Tags);
}
public sealed record PersonalEditToken(Guid Epoch, string Id, Guid Generation, PersonalValues Original);
public sealed record GameDetails(string Id, PersonalValues Personal, string MetadataTitle, string OriginalTitle, string Relationship,
    string Installations, string Playtime, string CoverDescription, PersonalEditToken Token)
{
    public string? CoverKey { get; init; }
    public CoverEditToken? CoverToken { get; init; }
    public string? WorkId { get; init; }
    // Read-only detail projection. These values never participate in a personal edit patch.
    public WorkMetadata? Metadata { get; init; }
    public string? BackdropKey { get; init; }
    public string? BackdropDescription { get; init; }
    public string? LastPlayedAt { get; init; }
    public bool HasPlayHistory { get; init; }
}
public sealed record CoverEditToken(Guid Epoch, string Id, Guid Generation, LocalCover? Original, CoverChoice? Choice, string? WorkId = null);
public sealed record BulkPersonalPatch
{
    public Change<string> Status { get; init; }
    public Change<bool> Favorite { get; init; }
    public ImmutableArray<string> AddCategories { get; init; } = [];
    public ImmutableArray<string> RemoveCategories { get; init; } = [];
    public ImmutableArray<string> AddTags { get; init; } = [];
    public ImmutableArray<string> RemoveTags { get; init; } = [];
}

public sealed partial class TransactionalLibrary
{
    public Task<GameDetails> DetailsAsync(string id, CancellationToken cancellation = default) => Enqueue(() =>
    {
        var e = Find(current.Entries, id); var personal = PersonalValues.From(e);
        int recordedCount = e.RecordedSessions.Length;
        return Task.FromResult(new GameDetails(e.Id, personal, e.Metadata?.Title ?? UiText.Get("未关联作品资料"), e.Metadata?.OriginalTitle ?? "",
            e.Metadata is null ? UiText.Get("尚未关联目录作品") : UiText.Format($"目录作品 {e.Metadata.Id} · {string.Join(" / ", e.Metadata.Developers)}"),
            e.Installations.IsEmpty ? UiText.Get("愿望条目 · 尚无安装") : string.Join("\n", e.Installations.Select(i => $"{(i.Id == e.PreferredInstallationId ? UiText.Get("首选 · ") : "")}{i.Folder}\n{i.Executable ?? UiText.Get("尚未设置启动程序")}")),
            UiText.Format($"累计 {e.EffectivePlaySeconds / 3600} 小时 {e.EffectivePlaySeconds % 3600 / 60} 分钟 · {e.PlayCount} 次 · {recordedCount} 条历史记录"),
            e.LocalCover is not null ? UiText.Get("个人本地封面 · 保存在书架资料中") : e.CoverChoice?.WorkId == e.Metadata?.Id && e.CoverChoice is not null ? UiText.Get("个人选择的作品封面") : e.Metadata?.CoverUrl is not null ? UiText.Get("自动推荐封面") : UiText.Get("暂无封面"),
            new(epoch, id, lifecycles[id].Generation, personal)) { WorkId = e.Metadata?.Id, Metadata = e.Metadata,
                CoverKey = ImageSourceKeys.For(e), BackdropKey = ArtworkSources.Key(e), BackdropDescription = ArtworkSources.Description(e),
                LastPlayedAt = e.LastPlayedAt, HasPlayHistory = recordedCount > 0 || e.EffectivePlaySeconds > 0 || e.PlayCount > 0,
                CoverToken = new(epoch, id, lifecycles[id].Generation, e.LocalCover, e.CoverChoice, e.Metadata?.Id) });
    }, cancellation);

    public Task<CommitResult<LibraryEntry>> SaveLocalCoverAsync(CoverEditToken token, LocalCover? cover, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            var entry = Find(games, token.Id);
            ValidateCoverToken(token, entry);
            var changed = entry with { LocalCover = cover, CoverChoice = null };
            return (games.Replace(entry, changed), changed);
        }, cancellation);

    private void ValidateCoverToken(CoverEditToken token, LibraryEntry entry)
    {
        if (token.Epoch != epoch || token.Generation != lifecycles[entry.Id].Generation || token.Original != entry.LocalCover
            || token.Choice != entry.CoverChoice || token.WorkId != entry.Metadata?.Id) throw new StaleLibraryRequestException();
    }

    // The caller supplies revalidated catalog data, never a URL taken from a View.
    // Personal fields and accounting remain untouched; only the cover part of metadata is refreshed.
    public Task<CommitResult<LibraryEntry>> SaveRemoteCoverAsync(CoverEditToken token, WorkMetadata work, string? coverId,
        CancellationToken cancellation = default, Action? precommit = null) => Mutate(games =>
        {
            var entry = Find(games, token.Id); ValidateCoverToken(token, entry);
            if (entry.Metadata is null || work.Id != token.WorkId) throw new StaleLibraryRequestException();
            work = codec.NormalizeWork(work);
            var selected = coverId is null ? null : work.Covers?.FirstOrDefault(c => c.Id == coverId)
                ?? throw new InvalidDataException(UiText.Get("这张封面已不可用，请重新打开封面选择。"));
            var changed = entry with { LocalCover = null, CoverChoice = selected is null ? null : new(work.Id, selected),
                Metadata = entry.Metadata with { CoverUrl = work.CoverUrl, Covers = work.Covers, FetchedAt = work.FetchedAt } };
            return (games.Replace(entry, changed), changed);
        }, cancellation, precommit: precommit);

    public Task<CommitResult<LibraryEntry>> SavePersonalAsync(PersonalEditToken token, PersonalPatch patch, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            if (token.Epoch != epoch || !lifecycles.TryGetValue(token.Id, out var lifecycle) || token.Generation != lifecycle.Generation) throw new StaleLibraryRequestException();
            var e = Find(games, token.Id);
            if (!token.Original.Matches(PersonalValues.From(e))) throw new StaleLibraryRequestException();
            var changed = e with
            {
                Name = patch.Name.IsSpecified ? patch.Name.Value : e.Name, NameIsCustom = patch.Name.IsSpecified ? true : e.NameIsCustom,
                Status = patch.Status.IsSpecified ? patch.Status.Value : e.Status, PersonalRating = patch.Rating.IsSpecified ? patch.Rating.Value : e.PersonalRating,
                Favorite = patch.Favorite.IsSpecified ? patch.Favorite.Value : e.Favorite, Notes = patch.Notes.IsSpecified ? patch.Notes.Value : e.Notes,
                Categories = patch.Categories.IsSpecified ? patch.Categories.Value : e.Categories, CustomTags = patch.Tags.IsSpecified ? patch.Tags.Value : e.CustomTags
            };
            changed = codec.Decode(codec.Encode([changed], now()))[0];
            return (games.Replace(e, changed), changed);
        }, cancellation);

    // One immutable candidate and one durable transaction, even for 1000 selected games.
    public Task<CommitResult<int>> UpdateBatchAsync(ImmutableArray<string> ids, BulkPersonalPatch patch, CancellationToken cancellation = default)
        => Mutate(games =>
        {
            if (ids.Length is < 1 or > 1000 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new InvalidDataException(UiText.Get("批量修改请选择 1 至 1000 个不重复条目。"));
            var template = new LibraryEntry("add", "labels", false, null, [], null, "planned", null, false, patch.AddCategories, patch.AddTags, "", 0, 0, [], now(), null, null, null, new(false, null));
            var normalized = codec.Decode(codec.Encode([template, template with { Id = "remove", Categories = patch.RemoveCategories, CustomTags = patch.RemoveTags }], now()));
            var add = normalized[0]; var remove = normalized[1];
            if (add.Categories.Intersect(remove.Categories).Any() || add.CustomTags.Intersect(remove.CustomTags).Any()) throw new InvalidDataException(UiText.Get("同一分类或标签不能同时添加和移除。"));
            if (!patch.Status.IsSpecified && !patch.Favorite.IsSpecified && add.Categories.IsEmpty && remove.Categories.IsEmpty && add.CustomTags.IsEmpty && remove.CustomTags.IsEmpty) throw new InvalidDataException(UiText.Get("请选择至少一项批量修改。"));
            var selected = ids.ToHashSet(StringComparer.Ordinal);
            if (games.Count(e => selected.Contains(e.Id)) != ids.Length) throw new StaleLibraryRequestException();
            ImmutableArray<string> Labels(ImmutableArray<string> original, ImmutableArray<string> additions, ImmutableArray<string> removals)
                => [.. original.Where(s => !removals.Contains(s)).Concat(additions).Distinct(StringComparer.Ordinal)];
            var candidate = games.Select(e => !selected.Contains(e.Id) ? e : e with
            {
                Status = patch.Status.IsSpecified ? patch.Status.Value : e.Status,
                Favorite = patch.Favorite.IsSpecified ? patch.Favorite.Value : e.Favorite,
                Categories = Labels(e.Categories, add.Categories, remove.Categories), CustomTags = Labels(e.CustomTags, add.CustomTags, remove.CustomTags)
            }).ToImmutableArray();
            return (candidate, ids.Length);
        }, cancellation);
}
