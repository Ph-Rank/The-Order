using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Only registered API/library artwork is eligible. Arbitrary hosts use checked public DNS and sockets.
// Canonical paths on known artwork hosts can use the separate anonymous system route.
public sealed partial class RemoteCoverSource(HttpClient http, IRemoteArtworkCache? disk = null, HttpClient? originalArtwork = null) : IImageSourceProvider, IRemoteCoverRegistry, IRemoteArtworkPreparation
{
    private readonly object gate = new();
    private readonly Dictionary<string, Uri> sources = [];
    private readonly Dictionary<string, (byte[] Bytes, DateTimeOffset Until)> cache = [];
    private const int MaximumBytes = 20 * 1024 * 1024;
    public static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsoluteUri.Length <= 8192 && uri.HostNameType == UriHostNameType.Dns && uri.Host.Contains('.')
        && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        && !uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    [GeneratedRegex(@"\A/([0-9a-f]{2})/([0-9a-f]{2})/\1\2[0-9a-f]{60}(?:_(?:mini|[0-9]{2,4}))?\.webp\z")]
    private static partial Regex KunGalImagePath();
    public string? Register(string? url, string revision)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Allowed(uri)) return null;
        var key = ImageSourceKeys.Remote(url, revision)!;
        lock (gate) { if (sources.Count >= 2048 && !sources.ContainsKey(key)) sources.Remove(sources.Keys.First()); sources[key] = uri; }
        return key;
    }
    public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellation)
    {
        Uri? uri;
        lock (gate) { if (cache.TryGetValue(key, out var hit) && hit.Until > DateTimeOffset.UtcNow) return hit.Bytes; sources.TryGetValue(key, out uri); }
        if (uri is null) return null;
        // URL-addressed originals survive process restarts and layout/DPI changes.
        // The disposable decoded thumbnail budget cannot evict these originals.
        var diskKey=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        if(disk is not null)
        {
            try { if(await disk.ReadAsync(diskKey,cancellation).ConfigureAwait(false) is { } saved) return saved; }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or DataIsolationException) { }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var kunGalImage = uri.Host == "image.kungal.iloveren.link" && uri.Query.Length == 0 && KunGalImagePath().IsMatch(uri.AbsolutePath);
            var transport = originalArtwork is not null && (kunGalImage || VndbArtworkSource.OriginalImageUri(uri) || SteamArtworkSource.OriginalImageUri(uri)
                || BangumiRelationsService.ValidCoverUrl(uri.AbsoluteUri) || MusicBrainzValidation.CoverUri(uri.AbsoluteUri)
                || AniListValidation.Image(uri.AbsoluteUri)) ? originalArtwork : http;
            using var response = await ReadResponseAsync(transport, uri, kunGalImage, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png" or "image/webp")) return null;
            var bytes = await CatalogTransport.ReadBoundedAsync(response.Content, MaximumBytes, timeout.Token).ConfigureAwait(false);
            if(disk is not null)
            {
                try { await disk.WriteAsync(diskKey,bytes,timeout.Token).ConfigureAwait(false); }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or DataIsolationException) { }
            }
            // Decoder validates format and pixel count before allocation. Encoded cache is a separate,
            // disposable 32 MiB / 64-entry memory cache; personal covers never enter it.
            lock (gate)
            {
                cache.Remove(key);
                while (cache.Count > 0 && (cache.Count >= 64 || cache.Values.Sum(v => (long)v.Bytes.Length) + bytes.Length > 32 * 1024 * 1024)) cache.Remove(cache.OrderBy(k => k.Value.Until).First().Key);
                cache[key] = (bytes, DateTimeOffset.UtcNow.AddMinutes(10));
            }
            return bytes;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
        catch (Exception e) when (e is HttpRequestException or OnlineException or IOException) { return null; }
    }
    private static async Task<HttpResponseMessage> ReadResponseAsync(HttpClient transport, Uri original, bool kunGalImage, CancellationToken cancellation)
    {
        var uri = original;
        // Only CAA edition-addressed artwork may redirect. Other image sources retain the no-redirect policy.
        string? releaseId = null;
        if (MusicBrainzValidation.CoverUri(original.AbsoluteUri))
        {
            var match = Regex.Match(original.AbsolutePath, @"(?:/release/|/mbid-)([a-f0-9-]{36})/");
            if (match.Success) releaseId = match.Groups[1].Value;
        }
        for (int hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (kunGalImage) request.Headers.UserAgent.ParseAdd(CatalogTransport.UserAgent);
            else if (releaseId is not null) request.Headers.UserAgent.ParseAdd(MusicBrainzProvider.UserAgent);
            var response = await transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } returned && returned != uri)
            { response.Dispose(); throw new HttpRequestException("Unexpected artwork redirect."); }
            if (releaseId is null || response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var next = response.Headers.Location; response.Dispose();
            if (hop >= 3 || next is null || !next.IsAbsoluteUri || !MusicBrainzValidation.CoverUri(next.AbsoluteUri, releaseId))
                throw new HttpRequestException("Invalid CAA artwork redirect.");
            uri = next;
        }
    }
    public async Task<bool> EnsureCachedAsync(string key,CancellationToken cancellation)
    {
        var bytes=await ReadAsync(key,cancellation).ConfigureAwait(false);if(bytes is null)return false;
        if(disk is null)return true;
        Uri? uri;lock(gate)sources.TryGetValue(key,out uri);if(uri is null)return false;
        var diskKey=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        try {return await disk.ReadAsync(diskKey,cancellation).ConfigureAwait(false) is not null || await disk.WriteAsync(diskKey,bytes,cancellation).ConfigureAwait(false);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or DataIsolationException){return false;}
    }
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (b.Length == 4) return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && b[1] is 0 or 168)
            && !(b[0] == 100 && b[1] is >= 64 and <= 127) && !(b[0] == 198 && b[1] is 18 or 19)
            && !(b[0] == 192 && b[1] == 0 && b[2] == 2) && !(b[0] == 198 && b[1] == 51 && b[2] == 100) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Global unicast only; reject local, multicast, transition and documentation prefixes.
        return b.Length == 16 && (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x02)
            && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8));
    }
    public static HttpClient CreateClient()
    {
        return new(CreateHandler(Dns.GetHostAddressesAsync, ConnectSocket)) { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal static SocketsHttpHandler CreateHandler(Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress[], int, CancellationToken, ValueTask<Stream>> connect)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(10), AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli };
        handler.ConnectCallback = async (context, cancellation) =>
        {
            var addresses = await resolve(context.DnsEndPoint.Host, cancellation).ConfigureAwait(false);
            if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a))) throw new HttpRequestException("Image source is not public.");
            return await connect(addresses, context.DnsEndPoint.Port, cancellation).ConfigureAwait(false);
        };
        return handler;
    }
    private static async ValueTask<Stream> ConnectSocket(IPAddress[] addresses, int port, CancellationToken cancellation)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try { await socket.ConnectAsync(addresses, port, cancellation).ConfigureAwait(false); return new NetworkStream(socket, true); }
        catch { socket.Dispose(); throw; }
    }
}
