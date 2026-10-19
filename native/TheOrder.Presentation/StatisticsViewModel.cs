using System.Collections.Immutable;
using TheOrder.Application;

namespace TheOrder.Presentation;

public sealed record StatisticsGameOption(string? Id, string Name);
public sealed class StatisticsViewModel : ObservableState, IDisposable
{
    private readonly Func<StatisticsRequest, CancellationToken, Task<StatisticsDashboard>> build;
    private readonly Func<DateOnly> today;
    private readonly TimeZoneInfo zone;
    private CancellationTokenSource? pending;
    private long generation;
    private bool disposed;
    private LibrarySnapshot? source;
    private DateOnly? sourceDay;
    public StatisticsViewModel(Func<StatisticsRequest, CancellationToken, Task<StatisticsDashboard>>? build = null,
        Func<DateOnly>? today = null, TimeZoneInfo? zone = null)
    {
        this.build = build ?? ((request, token) => Task.Run(() => StatisticsDashboardBuilder.Build(request, token), token));
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today)); this.zone = zone ?? TimeZoneInfo.Local;
    }
    public StatisticsPeriod Period { get; private set; } = StatisticsPeriod.Last30Days;
    public string? GameId { get; private set; }
    public ImmutableArray<StatisticsGameOption> GameOptions { get; private set; } = [new(null, "全部作品")];
    public StatisticsDashboard? Data { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public async Task RefreshAsync(LibrarySnapshot? snapshot, bool force = false)
    {
        if (disposed) return;
        var day = today();
        if (!force && ReferenceEquals(source, snapshot) && sourceDay == day && (Busy || Data is not null) && Error is null) return;
        source = snapshot; sourceDay = day;
        if (GameId is not null && snapshot?.Entries.Any(x => x.Id == GameId) != true) { GameId = null; Changed(nameof(GameId)); }
        GameOptions = [new(null, "全部作品"), .. (snapshot?.Entries ?? []).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => new StatisticsGameOption(x.Id, x.Name))];
        Changed(nameof(GameOptions));
        pending?.Cancel(); pending?.Dispose(); var cancellation = pending = new(); var current = ++generation;
        if (snapshot is null) { Data = null; Error = "书架尚未读取完成。"; Busy = false; Notify(); return; }
        Busy = true; Error = null; Changed(nameof(Busy)); Changed(nameof(Error));
        try
        {
            var result = await build(new(snapshot.Entries, Period, day, zone, GameId), cancellation.Token);
            if (disposed || current != generation || cancellation.IsCancellationRequested) return;
            Data = result; Error = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed && current == generation) Error = "统计暂时无法读取，已有结果已保留。请重试。"; }
        finally { if (!disposed && current == generation) { Busy = false; Notify(); } }
    }
    public Task SelectPeriodAsync(StatisticsPeriod period)
    {
        if (!Enum.IsDefined(period)) throw new ArgumentOutOfRangeException(nameof(period));
        if (Period == period) return Task.CompletedTask;
        Period = period; Changed(nameof(Period)); return RefreshAsync(source, true);
    }
    public Task SelectGameAsync(string? id)
    {
        if (id is not null && source?.Entries.Any(x => x.Id == id) != true) return Task.CompletedTask;
        if (GameId == id) return Task.CompletedTask;
        GameId = id; Changed(nameof(GameId)); return RefreshAsync(source, true);
    }
    public void Leave() { if (disposed) return; generation++; pending?.Cancel(); Busy = false; sourceDay = null; Changed(nameof(Busy)); }
    private void Notify() { Changed(nameof(Data)); Changed(nameof(Busy)); Changed(nameof(Error)); }
    public void Dispose() { if (disposed) return; Leave(); disposed = true; pending?.Dispose(); pending = null; }
}
