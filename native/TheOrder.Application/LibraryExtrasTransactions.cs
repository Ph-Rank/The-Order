using System.Collections.Immutable;
using System.Globalization;
using TheOrder.Domain;

namespace TheOrder.Application;

public sealed record LibraryExtrasToken(Guid Epoch, string GameId, Guid Generation, LibraryEntry Original);
public sealed record LibraryExtrasDetails(LibraryEntry Entry, LibraryExtrasToken Token);
public sealed partial class TransactionalLibrary
{
    public Task<LibraryExtrasDetails> ExtrasAsync(string id, CancellationToken cancellation = default) => Enqueue(() =>
    {
        var e = Find(current.Entries, id); return Task.FromResult(new LibraryExtrasDetails(e, new(epoch, id, lifecycles[id].Generation, e)));
    }, cancellation);
    private void AssertExtras(LibraryExtrasToken token, LibraryEntry e)
    {
        if (token.Epoch != epoch || token.Generation != lifecycles[e.Id].Generation || !e.ManualPlaytime.SequenceEqual(token.Original.ManualPlaytime)
            || !e.GuidePaths.SequenceEqual(token.Original.GuidePaths) || !SameExtraMetadata(e.Metadata, token.Original.Metadata) || !SameExtraMetadata(e.MetadataMix, token.Original.MetadataMix))
            throw new StaleLibraryRequestException();
    }
    private static bool SameExtraMetadata<T>(T left, T right) => EqualityComparer<T>.Default.Equals(left, right)
        || System.Text.Json.JsonSerializer.Serialize(left) == System.Text.Json.JsonSerializer.Serialize(right);
    public Task<CommitResult<LibraryEntry>> AddManualTimeAsync(LibraryExtrasToken token, DateOnly date, long seconds, string note, TimeZoneInfo zone,
        bool replaceDay = false, CancellationToken cancellation = default) => Mutate(games =>
    {
        var e = Find(games, token.GameId); AssertExtras(token, e);
        if (e.Sessions.Any(s => s.Native is { IsTerminal: false })) throw new SessionConflictException("请先结束或恢复本作品的计时，再修正时长。");
        if (replaceDay && e.TotalPlaySeconds != token.Original.TotalPlaySeconds) throw new StaleLibraryRequestException();
        var existing = PlaytimeStatistics.Build([e], zone).Days.Where(x => x.Date == date).Sum(x => x.Seconds);
        long delta = replaceDay ? checked(seconds - existing) : seconds;
        if (Math.Abs((decimal)delta) > 366L * 86400 || existing + delta < 0 || delta == 0 || date.Year < 1900 || date > DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime))
            throw new InvalidDataException("日期或修正时长无效；当天与累计时长不能为负数，也不能补记未来日期。");
        var changed = e with { ManualPlaytime = e.ManualPlaytime.Add(new(newId(), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), delta, note, now())) };
        LibraryExtras.Validate(changed); return (games.Replace(e, changed), changed);
    }, cancellation);
    public Task<CommitResult<LibraryEntry>> UndoManualTimeAsync(LibraryExtrasToken token, string recordId, CancellationToken cancellation = default) => Mutate(games =>
    {
        var e = Find(games, token.GameId); AssertExtras(token, e);
        if (e.Sessions.Any(s => s.Native is { IsTerminal: false })) throw new SessionConflictException("请先结束或恢复本作品的计时。");
        var record = e.ManualPlaytime.FirstOrDefault(x => x.Id == recordId && !x.Undone) ?? throw new InvalidDataException("这条修正已撤回或不存在。");
        var changed = e with { ManualPlaytime = e.ManualPlaytime.Replace(record, record with { Undone = true }) };
        if (PlaytimeStatistics.Build([changed], TimeZoneInfo.Local).Days.Any(x => x.Seconds < 0)) throw new InvalidDataException("后续修正依赖这条记录，请先撤回后续修正。");
        LibraryExtras.Validate(changed); return (games.Replace(e, changed), changed);
    }, cancellation);
    public Task<CommitResult<LibraryEntry>> SaveGuideAsync(LibraryExtrasToken token, string? path, CancellationToken cancellation = default, string? remove = null) => Mutate(games =>
    {
        var e = Find(games, token.GameId); AssertExtras(token, e); LibraryExtras.Path(path);
        if (path is not null && !LocalDocuments.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new InvalidDataException("请选择攻略文档。");
        var changed = e with { GuidePaths = remove is not null ? e.GuidePaths.Remove(remove) : path is null ? [] : [.. e.GuidePaths.Append(path).Distinct(StringComparer.OrdinalIgnoreCase)] };
        LibraryExtras.Validate(changed); return (games.Replace(e, changed), changed);
    }, cancellation);
    public Task<CommitResult<LibraryEntry>> ReplaceGuideAsync(LibraryExtrasToken token, string oldPath, string newPath, CancellationToken cancellation = default) => Mutate(games =>
    {
        var entry = Find(games, token.GameId); AssertExtras(token, entry);
        if (!entry.GuidePaths.Contains(oldPath)) throw new StaleLibraryRequestException();
        LibraryExtras.Path(newPath); if (!LocalDocuments.Extensions.Contains(Path.GetExtension(newPath).ToLowerInvariant())) throw new InvalidDataException("请选择攻略文档。");
        var changed = entry with { GuidePaths = [.. entry.GuidePaths.Select(path => path == oldPath ? newPath : path).Distinct(StringComparer.OrdinalIgnoreCase)] };
        LibraryExtras.Validate(changed); return (games.Replace(entry, changed), changed);
    }, cancellation);
}
public static class LocalDocuments
{
    public static readonly ImmutableArray<string> Extensions = [".txt", ".md", ".pdf", ".doc", ".docx", ".rtf", ".png", ".jpg", ".jpeg", ".xls", ".xlsx"];
}
public sealed class LibraryExtrasClient(OfflineLibraryClient library)
{
    public LibrarySnapshot? Snapshot => library.CurrentSnapshot;
    public Task<LibraryExtrasDetails> LoadAsync(string id, CancellationToken cancellation) => library.Required().ExtrasAsync(id, cancellation);
    public Task<ClientResult> TimeAsync(LibraryExtrasDetails details, DateOnly date, long seconds, string note, bool replace, CancellationToken cancellation)
        => Result(() => library.Required().AddManualTimeAsync(details.Token, date, seconds, note, TimeZoneInfo.Local, replace, cancellation));
    public Task<ClientResult> UndoAsync(LibraryExtrasDetails details, string id, CancellationToken cancellation)
        => Result(() => library.Required().UndoManualTimeAsync(details.Token, id, cancellation));
    public Task<ClientResult> GuideAsync(LibraryExtrasDetails details, string? path, CancellationToken cancellation, string? remove = null)
        => Result(() => library.Required().SaveGuideAsync(details.Token, path, cancellation, remove));
    public Task<ClientResult> ReplaceGuideAsync(LibraryExtrasDetails details, string oldPath, string newPath, CancellationToken cancellation)
        => Result(() => library.Required().ReplaceGuideAsync(details.Token, oldPath, newPath, cancellation));
    internal static async Task<ClientResult> Result(Func<Task<CommitResult<LibraryEntry>>> action)
    { try { return OfflineLibraryClient.Present(await action().ConfigureAwait(false)); } catch (Exception e) when (OfflineLibraryClient.Expected(e) || e is SessionConflictException) { return new(ClientOutcome.ValidationFailure, e is SessionConflictException ? e.Message : OfflineLibraryClient.Failure(e).Message); } }
}
