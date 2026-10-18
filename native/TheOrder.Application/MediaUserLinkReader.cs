namespace TheOrder.Application;

public interface IMediaUserLinkReader
{
    Task<MediaUserLinks> ReadUserLinksAsync(MediaIdentityContext source, CancellationToken cancellation);
    Task<MediaUserLinks> ChangeUserLinkAsync(MediaIdentityContext source, MediaUserLink? link, MediaIdentityReference target,
        long expectedRevision, CancellationToken cancellation);
    Task RestoreAutomaticAsync(MediaIdentityContext source, CancellationToken cancellation);
}
