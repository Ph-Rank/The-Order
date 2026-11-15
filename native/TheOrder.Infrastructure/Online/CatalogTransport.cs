using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed record NetworkEvent(string Method, string Host, int? Status, long ElapsedMs, int RetryCount, Guid RequestId);
internal enum OnlineAcceptanceStage { BodyComplete, Parsed, Mapped, BeforeCache, AfterCache, BeforeReturn }
public sealed class RequestBudget : IDisposable
{
    private readonly CancellationTokenSource timeout;
    private readonly CancellationTokenSource linked;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly TimeSpan duration;
    private CancellationTokenRegistration authCancellation;
    internal OnlineAuthSnapshot Auth { get; private set; }
    internal bool HasAuth { get; private set; }
    internal Action<OnlineAcceptanceStage>? Probe { get; set; }
    internal Func<TimeSpan>? Elapsed { get; set; }
    private Func<Action?>? admission;
    public CancellationToken Token => linked.Token;
    public TimeSpan Remaining => duration - (Elapsed?.Invoke() ?? elapsed.Elapsed);
    public int Retries { get; set; }
    public RequestBudget(TimeSpan duration, CancellationToken cancellation)
    { this.duration = duration; timeout = new(duration); linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token); }
    internal void Attach(OnlineAuthSnapshot auth) { Auth = auth; HasAuth = true; authCancellation = auth.Changed.Register(() => linked.Cancel()); }
    public void Check() { Token.ThrowIfCancellationRequested(); if (Remaining <= TimeSpan.Zero) throw new OnlineException(OnlineFailure.Timeout); }
    internal void Checkpoint(OnlineAcceptanceStage stage) { Probe?.Invoke(stage); Check(); }
    internal void DeferCache(Func<Action?> commit) => admission = commit;
    internal T Complete<T>(T value)
    {
        Action? rollback = null;
        try
        {
            Checkpoint(OnlineAcceptanceStage.BeforeCache);
            rollback = admission?.Invoke();
            Checkpoint(OnlineAcceptanceStage.AfterCache);
            Checkpoint(OnlineAcceptanceStage.BeforeReturn);
            return value;
        }
        catch { rollback?.Invoke(); throw; }
    }
    public void Dispose() { authCancellation.Dispose(); linked.Dispose(); timeout.Dispose(); }
}
public sealed class CatalogTransport(HttpClient client, ICredentialProvider credentials, Uri endpoint, Action<NetworkEvent>? log = null,
    TimeSpan? timeout = null, Func<TimeSpan, CancellationToken, Task>? delay = null, NextMoeRateCoordinator? rate = null)
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    internal static readonly string UserAgent = $"TheOrder/{AppReleaseIdentity.Version} (+https://github.com/Ph-Rank/The-Order)";
    private readonly OnlineAuthContext auth = (credentials as IOnlineAuthContextSource)?.AuthContext ?? new();
    internal Action<OnlineAcceptanceStage>? AcceptanceProbe { get; set; }
    internal Func<TimeSpan>? OperationElapsed { get; set; }
    // Cache reads use the same auth lock as admission/rollback, so provisional entries cannot escape.
    internal T ReadCurrent<T>(RequestBudget budget, Func<T> read) => auth.Accept(budget.Auth, () => { budget.Check(); return read(); });
    public async Task<T> Operation<T>(Func<RequestBudget, Task<T>> action, CancellationToken cancellation)
    {
        using var budget = new RequestBudget(timeout ?? TimeSpan.FromSeconds(45), cancellation) { Probe = AcceptanceProbe, Elapsed = OperationElapsed };
        try
        {
            budget.Attach(await auth.CaptureAsync(budget.Token).ConfigureAwait(false));
            var value = await action(budget).ConfigureAwait(false);
            return auth.Accept(budget.Auth, () => budget.Complete(value));
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && (!budget.HasAuth || !budget.Auth.Changed.IsCancellationRequested && budget.Auth.Generation == auth.Generation)) { throw new OnlineException(OnlineFailure.Timeout); }
    }
    public async Task<JsonDocument> GetAsync(string path, IReadOnlyDictionary<string, string> query, RequestBudget budget)
    {
        // The public news list is anonymous, even when the catalog uses a saved key or account.
        var news = path == "news";
        if ((!news && !path.StartsWith("catalog/", StringComparison.Ordinal)) || path.Contains("..", StringComparison.Ordinal)
            || path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Contains(':')) throw new OnlineException(OnlineFailure.InvalidRequest);
        var access = news ? null : credentials as INextMoeAccess;
        var secret = news ? null : await credentials.ReadAsync(budget.Token).ConfigureAwait(false);
        if (!news && string.IsNullOrEmpty(secret) && access is null) throw new OnlineException(OnlineFailure.NoCredential);
        if (secret is not null && (secret.Length > (access is null ? 4096 : 16384) || secret.Any(c => c < 0x21 || c > 0x7e))) throw new OnlineException(OnlineFailure.InvalidCredential);
        var mode = news ? NextMoeAccessMode.Anonymous : access is null ? NextMoeAccessMode.ApiKey : (await access.StatusAsync(budget.Token).ConfigureAwait(false)).Mode;
        bool refreshed = false;
        var uri = new Uri(endpoint, path + "?" + string.Join("&", query.Select(k => Uri.EscapeDataString(k.Key) + "=" + Uri.EscapeDataString(k.Value))));
        while (true)
        {
            budget.Check();
            if (rate is not null) await rate.WaitAsync(budget.Auth.Generation, mode, budget.Token).ConfigureAwait(false);
            budget.Check();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (secret is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new("application/json"));
            var elapsed = Stopwatch.StartNew(); var requestId = Guid.NewGuid();
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false); }
            catch (HttpRequestException) { Emit(null); throw new OnlineException(OnlineFailure.Offline); }
            using (response)
            {
                int status = (int)response.StatusCode; Emit(status);
                if (status is >= 300 and < 400) throw new OnlineException(OnlineFailure.RedirectBlocked);
                var type = response.Content.Headers.ContentType?.MediaType ?? "";
                JsonDocument? body = null;
                if (response.IsSuccessStatusCode || type.Equals("application/json", StringComparison.OrdinalIgnoreCase) || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await ReadBoundedAsync(response.Content, MaximumBytes, budget.Token).ConfigureAwait(false);
                    budget.Checkpoint(OnlineAcceptanceStage.BodyComplete);
                    try { _ = new System.Text.UTF8Encoding(false, true).GetCharCount(bytes); body = JsonDocument.Parse(bytes); }
                    catch (Exception e) when (e is JsonException or System.Text.DecoderFallbackException) { throw new OnlineException(OnlineFailure.InvalidResponse); }
                    try { budget.Checkpoint(OnlineAcceptanceStage.Parsed); } catch { body.Dispose(); throw; }
                }
                if (response.IsSuccessStatusCode) { rate?.Observe(budget.Auth.Generation, response.Headers, null); return body ?? throw new OnlineException(OnlineFailure.InvalidResponse); }
                using (body)
                {
                    var root = body?.RootElement ?? default;
                    var code = RemoteWorkDto.Text(root, "code");
                    rate?.Observe(budget.Auth.Generation, response.Headers, code);
                    if (status == 401 && !refreshed && access is not null && mode == NextMoeAccessMode.Account && secret is not null)
                    {
                        refreshed = true;
                        if (await access.RefreshRejectedAsync(secret, budget.Token).ConfigureAwait(false)) { secret = await access.ReadAsync(budget.Token).ConfigureAwait(false); continue; }
                    }
                    if (status == 404 && code == "ENTITY_MERGED")
                    {
                        var id = RemoteWorkDto.Text(root, "current_id");
                        var family = path.Split('/');
                        var expected = family.Length == 3 && family[0] == "catalog" ? family[1] switch { "works" => "work", "characters" => "character", "companies" => "company", "credit-names" => "credit_name", "persons" => "person", "tags" => "tag", "engines" => "engine", "series" => "series", "traits" => "trait", _ => "" } : "";
                        if (expected.Length == 0 || RemoteWorkDto.Text(root, "object") != expected || !RemoteWorkDto.ValidId(id)) throw new OnlineException(OnlineFailure.InvalidResponse);
                        throw new MergedWorkException(id, expected);
                    }
                    var failure = status switch { 400 => OnlineFailure.InvalidRequest, 401 => code == "INVALID_API_KEY" ? OnlineFailure.InvalidCredential : OnlineFailure.Unauthorized,
                        403 => OnlineFailure.Forbidden, 404 => OnlineFailure.NotFound, 408 => OnlineFailure.Timeout, 429 => code == "QUOTA_EXCEEDED" ? OnlineFailure.QuotaExceeded : OnlineFailure.RateLimited, >= 500 => OnlineFailure.Server, _ => OnlineFailure.InvalidResponse };
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                    if (retryAfter is null && response.Headers.TryGetValues("Retry-After", out var values)
                        && double.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                        && double.IsFinite(seconds) && seconds >= 0) retryAfter = TimeSpan.FromSeconds(Math.Min(seconds, 86400));
                    var wait = TimeSpan.FromMilliseconds(500 * Math.Pow(2, budget.Retries));
                    if (retryAfter > wait) wait = retryAfter.Value;
                    bool retry = status is 502 or 503 or 504 || status == 429 && code == "RATE_LIMITED";
                    if (!retry || budget.Retries >= 2 || wait >= budget.Remaining) throw new OnlineException(failure, retryAfter);
                    budget.Retries++;
                    await (delay ?? Task.Delay)(wait, budget.Token).ConfigureAwait(false);
                }
            }
            void Emit(int? status) { try { log?.Invoke(new("GET", endpoint.Host, status, elapsed.ElapsedMilliseconds, budget.Retries, requestId)); } catch { /* Telemetry must never change a request result. */ } }
        }
    }
    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > maximum) throw new OnlineException(OnlineFailure.ResponseTooLarge);
        try
        {
            await using var input = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = new byte[16384];
            while (true)
            {
                int count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)output.Length)), cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (count == 0) return output.ToArray();
                if (output.Length + count > maximum) throw new OnlineException(OnlineFailure.ResponseTooLarge);
                output.Write(buffer, 0, count);
            }
        }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw new OnlineException(OnlineFailure.InvalidResponse); }
    }
    public static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(10), MaxConnectionsPerServer = 4
    }) { Timeout = Timeout.InfiniteTimeSpan };
}
public sealed class MergedWorkException(string canonicalId, string entityObject = "work") : Exception("ENTITY_MERGED") { public string CanonicalId { get; } = canonicalId; public string EntityObject { get; } = entityObject; }
