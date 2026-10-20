using TheOrder.Application;

namespace TheOrder.WinUI.Views;

public sealed partial class DetailsPage
{
    private GameVideoView? gameVideos;
    public void ConfigureGameVideos(GameVideoReader reader, IMediaIdentityDecisions decisions)
    { gameVideos = new(); gameVideos.Configure(reader, decisions); GameVideosHost.Content = gameVideos; }
    private void SynchronizeGameVideos(GameDetails? detail)
    {
        var entry = bangumiLibrary?.Invoke()?.Entries.FirstOrDefault(x => x.Id == detail?.Id);
        var source = detail is null || ViewModel.IsDetailsLoading ? null : entry is null
            ? GameVideoSource.From(detail.Id, detail.Metadata) : GameVideoSource.FromEntry(entry, detail.Metadata);
        if (source is { Title.Length: 0 }) source = source.SteamId is not null || source.ExplicitIds.Length > 0 ? source with { Title = "游戏影像" } : null;
        gameVideos?.SetSource(source);
    }
    internal Task ProbeGameVideoForFixtureAsync() => gameVideos?.ProbeForFixtureAsync() ?? Task.CompletedTask;
    internal object? GameVideoEvidence => gameVideos?.Evidence;
    internal void SuspendGameVideos() => gameVideos?.Suspend();
}
