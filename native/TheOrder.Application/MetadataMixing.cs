using System.Collections.Immutable;
using TheOrder.Domain;

namespace TheOrder.Application;

public static class MetadataMixing
{
    public static bool IsNextMoeWorkId(string id) => id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit);
    public static bool HasNextMoeIdentity(LibraryEntry entry) => entry.Metadata is { } work
        && IsNextMoeWorkId(work.Id) && entry.MetadataMix is not { Base: null };
    public static WorkMetadata? Compose(MetadataMix mix)
    {
        var result = mix.Base;
        foreach (var layer in mix.Layers)
        {
            var w = layer.Work;
            result ??= w with { Description = "", ReleaseDate = null, ReleaseDatePrecision = null,
                Developers = [], Tags = [], Ratings = [], Characters = [], CoverUrl = null, Covers = [], BannerUrl = null };
            foreach (var field in layer.Fields)
                result = field switch
                {
                    "description" when !layer.FillOnly || string.IsNullOrWhiteSpace(result.Description) => result with { Description = w.Description },
                    "releaseDate" when !layer.FillOnly || string.IsNullOrWhiteSpace(result.ReleaseDate) => result with { ReleaseDate = w.ReleaseDate, ReleaseDatePrecision = w.ReleaseDatePrecision },
                    "developers" when !layer.FillOnly || result.Developers.IsEmpty => result with { Developers = w.Developers },
                    "tags" when !layer.FillOnly || result.Tags.IsEmpty => result with { Tags = w.Tags },
                    "ratings" => result with { Ratings = layer.FillOnly ? [.. result.Ratings.Concat(w.Ratings.Where(r => !result.Ratings.Any(s => s.Source == r.Source)))]
                        : [.. result.Ratings.Where(r => !w.Ratings.Any(s => s.Source == r.Source)).Concat(w.Ratings)] },
                    "characters" when !layer.FillOnly || result.Characters.IsEmpty => result with { Characters = w.Characters },
                    "cover" when !layer.FillOnly || string.IsNullOrWhiteSpace(result.CoverUrl) => result with { CoverUrl = w.CoverUrl, Covers = w.Covers },
                    _ => result
                };
            result = result with { References = [.. result.References.Concat(w.References).DistinctBy(r => (r.Source, r.Id))] };
        }
        return result;
    }
    public static ImmutableArray<ManualPlaytime> MergeLedger(ImmutableArray<ManualPlaytime> left, ImmutableArray<ManualPlaytime> right)
    {
        var result = left.ToBuilder();
        foreach (var item in right)
        {
            var previous = result.FirstOrDefault(x => x.Id == item.Id);
            if (previous is null) result.Add(item);
            else if (previous != item) throw new InvalidDataException("人工记录编号冲突，未合并。");
        }
        return result.ToImmutable();
    }
    public static MetadataMix? Merge(WorkMetadata work, MetadataMix? left, MetadataMix? right)
    {
        if (left is null && right is null) return null;
        ValidateMergeIdentity(work, left);
        ValidateMergeIdentity(work, right);
        var layers = (left?.Layers ?? []).ToBuilder();
        foreach (var layer in right?.Layers ?? [])
        {
            var existing = layers.FirstOrDefault(x => x.Source == layer.Source);
            if (existing is null) layers.Add(layer);
            else if (existing.SourceId != layer.SourceId) throw new InvalidDataException("直接资料源编号冲突，请先核对资料源再合并。");
            else if (System.Text.Json.JsonSerializer.Serialize(existing) != System.Text.Json.JsonSerializer.Serialize(layer))
                throw new InvalidDataException("直接资料源的字段或内容不同，请先统一资料源设置再合并。");
        }
        return new(work, layers.ToImmutable());
    }
    private static void ValidateMergeIdentity(WorkMetadata work, MetadataMix? mix)
    {
        foreach (var layer in mix?.Layers ?? [])
        {
            // A layer can cross catalog IDs only when the new work explicitly identifies
            // that same source record. Release references do not identify a separate VNDB work.
            if (!CollectionUpdate.TryReadSourceWorkId(work, layer.Source, out var reference)
                || reference is not null && reference != layer.SourceId
                || mix!.Base?.Id != work.Id && layer.Work.Id != work.Id && reference is null)
                throw new InvalidDataException("补充资料与要关联的作品不一致或无法确认。请先在作品的「补充资料」中撤回相关来源，再重新关联；原书架条目已保留。");
        }
    }
}
public sealed partial class TransactionalLibrary
{
    public Task<CommitResult<LibraryEntry>> ApplySourceAsync(LibraryExtrasToken token, MetadataSourceLayer layer, CancellationToken cancellation = default) => Mutate(games =>
    {
        var e = Find(games, token.GameId); AssertExtras(token, e);
        layer = layer with { Work = codec.NormalizeWork(layer.Work) };
        if (!CollectionUpdate.TryReadSourceWorkId(e.Metadata, layer.Source, out var reference)
            || reference is not null && reference != layer.SourceId)
            throw new InvalidDataException("资料源编号与当前作品不同，请先核对作品和版本。");
        var mix = e.MetadataMix ?? new(e.Metadata, []);
        mix = mix with { Layers = mix.Layers.RemoveAll(x => x.Source == layer.Source).Add(layer) };
        var changed = e with { MetadataMix = mix, Metadata = MetadataMixing.Compose(mix) };
        LibraryExtras.Validate(changed); return (games.Replace(e, changed), changed);
    }, cancellation);
    public Task<CommitResult<LibraryEntry>> RemoveSourceAsync(LibraryExtrasToken token, string source, CancellationToken cancellation = default) => Mutate(games =>
    {
        var e = Find(games, token.GameId); AssertExtras(token, e);
        var mix = e.MetadataMix ?? throw new InvalidDataException("没有可撤回的直接资料。");
        mix = mix with { Layers = mix.Layers.RemoveAll(x => x.Source == source) };
        var metadata = MetadataMixing.Compose(mix);
        var changed = e with { Metadata = metadata, MetadataMix = mix.Layers.IsEmpty ? null : mix,
            CoverChoice = e.CoverChoice?.WorkId == metadata?.Id ? e.CoverChoice : null };
        return (games.Replace(e, changed), changed);
    }, cancellation);
}
