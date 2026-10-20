using System.Net;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public static class IgdbServiceEndpoint
{
    // Independent IGDB Worker, never the Bangumi OAuth service. The first public
    // deployment still requires operator approval; no application secret ships.
    public const string PublicServiceUrl = "https://the-order-igdb-game-videos.zyx2251721565.workers.dev/";

    public static Uri? Resolve(NativeProfileKind? profile, string? configured)
    {
        if (profile is null) return null;
        if (string.IsNullOrWhiteSpace(configured))
            return profile is NativeProfileKind.Production or NativeProfileKind.Beta ? new(PublicServiceUrl) : null;
        return Uri.TryCreate(configured, UriKind.Absolute, out var uri) && Valid(uri, profile == NativeProfileKind.Test) ? uri : null;
    }

    public static bool Valid(Uri? service, bool allowTestLoopback = false) => service is { IsAbsoluteUri: true }
        && service.UserInfo.Length == 0 && service.Query.Length == 0 && service.Fragment.Length == 0 && service.AbsolutePath == "/"
        && (service.Scheme == "https" && service.IsDefaultPort && !IPAddress.TryParse(service.Host, out _) && !service.IsLoopback
            && service.Host.Contains('.') && !service.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !service.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || allowTestLoopback && service.Scheme == "http" && service.IsLoopback);
}
