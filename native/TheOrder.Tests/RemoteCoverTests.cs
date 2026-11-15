using System.Net;
using System.Net.Http.Headers;
using TheOrder.Application;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class RemoteCoverTests
{
    [Theory][InlineData("file:///C:/secret")][InlineData("http://images.example/a")][InlineData("https://localhost/a")][InlineData("https://127.0.0.1/a")][InlineData("https://[::1]/a")][InlineData("https://a.local/a")][InlineData("https://a.internal/a")][InlineData("https://images.example:8080/a")][InlineData("https://secret@images.example/a")]
    public void SourcePolicyRejectsUnsafeUrls(string url) { using var http = new HttpClient(); Assert.Null(new RemoteCoverSource(http).Register(url, "r1")); }
    [Theory][InlineData("127.0.0.1")][InlineData("10.1.2.3")][InlineData("172.16.1.1")][InlineData("192.168.1.1")][InlineData("169.254.169.254")][InlineData("100.64.0.1")][InlineData("::1")][InlineData("fd00::1")][InlineData("fe80::1")][InlineData("::ffff:127.0.0.1")]
    public void ActualConnectionPolicyRejectsPrivateDnsAddresses(string ip) => Assert.False(RemoteCoverSource.IsPublic(IPAddress.Parse(ip)));

    private const string KunGalCoverPath = "/ab/cd/abcd0123456789abcdef0123456789abcdef0123456789abcdef0123456789ab";

    [Theory]
    [InlineData("image.kungal.iloveren.link", ".webp", true)]
    [InlineData("image.kungal.iloveren.link", "_mini.webp", true)]
    [InlineData("image.kungal.iloveren.link", "_128.webp", true)]
    [InlineData("image.kungal.iloveren.link", ".webp?variant=full", false)]
    [InlineData("image.kungal.iloveren.link", "/other.webp", false)]
    [InlineData("image.kungal.iloveren.link.example.org", ".webp", false)]
    public async Task OnlyCanonicalKunGalImagesUseThePublicRouteWithAnIdentifiedClient(string host, string suffix, bool publicRoute)
    {
        var url = $"https://{host}{KunGalCoverPath}{suffix}";
        int publicRequests = 0, checkedRequests = 0;
        using var checkedHttp = new HttpClient(new OnlineTransportTests.Handler((_, _) =>
        {
            checkedRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }));
        using var publicHttp = new HttpClient(new OnlineTransportTests.Handler((request, _) =>
        {
            publicRequests++;
            Assert.Equal(url, request.RequestUri!.AbsoluteUri);
            Assert.Contains($"TheOrder/{AppReleaseIdentity.Version}", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Headers.Referrer);
            Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/webp") } }
            });
        }));
        var source = new RemoteCoverSource(checkedHttp, originalArtwork: publicHttp);
        var bytes = await source.ReadAsync(source.Register(url, "revision")!, default);
        Assert.Equal(publicRoute, bytes is { Length: 3 });
        Assert.Equal(publicRoute ? 1 : 0, publicRequests);
        Assert.Equal(publicRoute ? 0 : 1, checkedRequests);
    }

    [Theory][InlineData("png")][InlineData("jpeg")][InlineData("malformed")][InlineData("wrong-type")][InlineData("oversized")][InlineData("huge")][InlineData("redirect")]
    public async Task RemoteBytesUseExistingBoundedDecoderAndFallback(string kind)
    {
        using var profile = new TransactionProfile(); var codec = new WindowsRasterCodec(); var bytes = await ImagePipelineTests.Png(40, 60);
        if (kind == "jpeg") { var file = Path.Combine(profile.Root, "synthetic.png"); File.WriteAllBytes(file, bytes); bytes = Convert.FromBase64String((await codec.NormalizePhotoAsync(file, default)).DataUrl.Split(',')[1]); }
        if (kind == "malformed") bytes = [1, 2, 3];
        if (kind == "huge") { bytes = bytes.ToArray(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 15000); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), 15000); }
        int calls = 0;
        using var http = new HttpClient(new OnlineTransportTests.Handler((request, _) =>
        {
            calls++; Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            var response = new HttpResponseMessage(kind == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(kind == "wrong-type" ? "text/html" : kind == "jpeg" ? "image/jpeg" : "image/png");
            if (kind == "oversized") response.Content.Headers.ContentLength = 20 * 1024 * 1024 + 1;
            return Task.FromResult(response);
        }));
        var source = new RemoteCoverSource(http); var key = source.Register("https://images.example/cover?private=synthetic", "r1")!;
        Assert.Matches("^[A-F0-9]{64}$", key);
        await using var pipeline = new ImagePipeline(source, codec, new IsolatedThumbnailCache(new(profile.Root)), new());
        var result = await pipeline.LoadAsync(key, 128, default);
        if (kind is "png" or "jpeg") { Assert.NotNull(result.Pixels); Assert.Equal(60, result.Pixels!.Height); }
        else Assert.Null(result.Pixels);
        Assert.Equal(1, calls);
        Assert.All(Directory.EnumerateFiles(profile.Root, "*", SearchOption.AllDirectories), file => Assert.DoesNotContain("private", file));
    }
    [Fact] public async Task CacheCorruptionRefetchAndVisualReuseKeepNewSource()
    {
        using var profile = new TransactionProfile(); var codec = new WindowsRasterCodec(); var bytes = await ImagePipelineTests.Png(40, 60); int calls = 0;
        using var http = new HttpClient(new OnlineTransportTests.Handler((_, _) => { calls++; var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; r.Content.Headers.ContentType = new("image/png"); return Task.FromResult(r); }));
        var source = new RemoteCoverSource(http); var key = source.Register("https://images.example/a", "r1")!; var newer = source.Register("https://images.example/a", "r2")!; Assert.NotEqual(key, newer);
        var disk = new IsolatedThumbnailCache(new(profile.Root));
        await using (var pipeline = new ImagePipeline(source, codec, disk, new())) Assert.NotNull((await pipeline.LoadAsync(key, 128, default)).Pixels);
        File.WriteAllBytes(Path.Combine(profile.Root, "native-thumbnails-v1", key + "-128.thumb"), [1, 2, 3]);
        await using (var pipeline = new ImagePipeline(source, codec, disk, new())) Assert.NotNull((await pipeline.LoadAsync(key, 128, default)).Pixels);
        Assert.Equal(1, calls); // The independent encoded cache repairs only the derived thumbnail.
        var slot = new ImageRequestSlot(); var first = slot.Begin(key); var second = slot.Begin(newer);
        Assert.False(slot.IsCurrent(key, first.Generation)); Assert.True(slot.IsCurrent(newer, second.Generation)); Assert.True(first.Cancellation.IsCancellationRequested); slot.Clear();
    }
    [Fact] public async Task RemoteCancellationDisposesRequestAndDoesNotBecomeError()
    {
        using var http = new HttpClient(new OnlineTransportTests.Handler(async (_, c) => { await Task.Delay(Timeout.Infinite, c); return new(HttpStatusCode.OK); }));
        var source = new RemoteCoverSource(http); var key = source.Register("https://images.example/a", "r")!; using var cancel = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(key, cancel.Token));
    }
}
