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
        var source = detail is null || ViewModel.IsDetailsLoading ? null : GameVideoSource.From(detail.Id, detail.Metadata,
            entry?.Installations.Select(x => x.SteamAppId).OfType<string>());
        if (source is { Title.Length: 0 }) source = source with { Title = detail!.Personal.Name };
        gameVideos?.SetSource(source);
    }
    internal object? GameVideoEvidence => gameVideos?.Evidence;
    internal void SuspendGameVideos() => gameVideos?.Suspend();
}
