using System.Net.Http.Headers;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

// Only the fixed official read-only account endpoint. Never cookie auth, query tokens,
// arbitrary URLs, redirects, mutations, raw response errors, or NextMoe credentials.
public sealed class BangumiAccountProvider(HttpClient client, TimeSpan? operationTimeout = null) : IBangumiAccountProvider
{
    public const string UserAgent = "TheOrder/1.1.0 (Ph-Rank; Windows; https://github.com/Ph-Rank/The-Order)";
    public async Task<BangumiAccount> ValidateAsync(string input, CancellationToken cancellation)
    {
        var token = BangumiAuthorization.NormalizeToken(input);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(operationTimeout ?? TimeSpan.FromSeconds(15));
        try
        {
            if (client.DefaultRequestHeaders.Authorization is not null || client.DefaultRequestHeaders.Contains("Cookie")) throw Error();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bgm.tv/v0/me");
            request.Headers.UserAgent.ParseAdd(UserAgent); request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status == 401) throw Error(BangumiAuthorizationFailure.Unauthorized);
            if (status == 403) throw Error(BangumiAuthorizationFailure.Forbidden);
            if (status == 429) throw Error(BangumiAuthorizationFailure.RateLimited);
            if (status >= 500) throw Error(BangumiAuthorizationFailure.Server);
            if (status != 200 || response.Content.Headers.ContentLength > 65536
                || response.Content.Headers.ContentType?.MediaType is not { } type
                || !type.Equals("application/json", StringComparison.OrdinalIgnoreCase)) throw Error();
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var body = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 65537 - (int)body.Length)), deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                body.Write(buffer, 0, count); if (body.Length > 65536) throw Error();
            }
            using var document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0
                || !root.TryGetProperty("username", out var user) || user.ValueKind != JsonValueKind.String) throw Error();
            var username = user.GetString()!;
            var name = root.TryGetProperty("nickname", out var nickname) && nickname.ValueKind == JsonValueKind.String ? nickname.GetString()! : username;
            if (username.Length is < 1 or > 200 || string.IsNullOrWhiteSpace(username) || name.Length > 200 || username.Any(char.IsControl) || name.Any(char.IsControl)) throw Error();
            deadline.Token.ThrowIfCancellationRequested();
            return new(number.ToString(System.Globalization.CultureInfo.InvariantCulture), username, name.Length == 0 ? username : name);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw Error(BangumiAuthorizationFailure.Timeout); }
        catch (HttpRequestException) { throw Error(BangumiAuthorizationFailure.Offline); }
        catch (Exception e) when (e is IOException or JsonException) { throw Error(); }
    }
    private static BangumiAuthorizationException Error(BangumiAuthorizationFailure failure = BangumiAuthorizationFailure.InvalidResponse) => new(failure);
}
