using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheOrder.Application;

namespace TheOrder.Infrastructure.Online;

public sealed class FeedbackDeviceCode(string userCode, string deviceCode, DateTimeOffset expires, TimeSpan interval)
{
    public string UserCode { get; } = userCode;
    internal string DeviceCode { get; } = deviceCode;
    internal DateTimeOffset Expires { get; } = expires;
    internal TimeSpan Interval { get; } = interval;
}

public sealed record FeedbackIssue(int Number)
{
    public Uri Link => new(GitHubFeedbackClient.IssuesPage, $"issues/{Number.ToString(CultureInfo.InvariantCulture)}");
}

public sealed class GitHubFeedbackClient(HttpClient http, IFeedbackCredentialStore store, string? clientId,
    TimeProvider? timeProvider = null) : IDisposable
{
    private const string RepositoryId = "1414122249";
    private static readonly Uri DeviceEndpoint = new("https://github.com/login/device/code");
    private static readonly Uri TokenEndpoint = new("https://github.com/login/oauth/access_token");
    private static readonly Uri UserEndpoint = new("https://api.github.com/user");
    private static readonly Uri IssuesEndpoint = new("https://api.github.com/repos/Ph-Rank/The-Order/issues");
    private readonly SemaphoreSlim credentialGate = new(1, 1);
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public static Uri VerificationPage { get; } = new("https://github.com/login/device");
    public static Uri IssuesPage { get; } = new("https://github.com/Ph-Rank/The-Order/");
    public static Uri IssuesList { get; } = new(IssuesPage, "issues");
    public static Uri ManualIssueForm { get; } = new(IssuesPage, "issues/new?blank=1");
    public bool IsConfigured => FeedbackCredential.ValidClientId(clientId);

    public async Task<FeedbackDeviceCode> StartAuthorizationAsync(CancellationToken cancellation)
    {
        RequireConfiguration();
        using var json = await PostFormAsync(DeviceEndpoint, new()
        {
            ["client_id"] = clientId!, ["scope"] = "offline_access"
        }, cancellation).ConfigureAwait(false);
        var root = json.RootElement;
        var userCode = ReadString(root, "user_code");
        var deviceCode = ReadString(root, "device_code");
        var expires = ReadInt(root, "expires_in") ?? 0;
        var interval = ReadInt(root, "interval") ?? 0;
        if (userCode is not { Length: 9 } || userCode[4] != '-' ||
            !userCode.Where((_, index) => index != 4).All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)) ||
            deviceCode is not { Length: > 0 and <= 128 } || !deviceCode.All(char.IsAsciiLetterOrDigit) ||
            ReadString(root, "verification_uri") != VerificationPage.AbsoluteUri ||
            expires is < 1 or > 1800 || interval is < 1 or > 60)
            throw new FeedbackException(ReadString(root, "error") is null
                ? FeedbackFailure.ServiceUnavailable : FeedbackFailure.NotConfigured);
        return new(userCode, deviceCode, time.GetUtcNow().AddSeconds(expires), TimeSpan.FromSeconds(Math.Max(5, interval)));
    }

    public async Task<string> CompleteAuthorizationAsync(FeedbackDeviceCode code, CancellationToken cancellation)
    {
        RequireConfiguration();
        var interval = code.Interval;
        while (time.GetUtcNow() + interval < code.Expires)
        {
            await Task.Delay(interval, time, cancellation).ConfigureAwait(false);
            if (time.GetUtcNow() >= code.Expires) break;
            using var json = await PostFormAsync(TokenEndpoint, new()
            {
                ["client_id"] = clientId!, ["device_code"] = code.DeviceCode, ["repository_id"] = RepositoryId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, cancellation).ConfigureAwait(false);
            switch (ReadString(json.RootElement, "error"))
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "expired_token" or "token_expired" or "incorrect_device_code":
                    throw new FeedbackException(FeedbackFailure.AuthorizationExpired);
                case "access_denied" or "unverified_user_email":
                    throw new FeedbackException(FeedbackFailure.AuthorizationDenied);
                case "device_flow_disabled" or "incorrect_client_credentials":
                    throw new FeedbackException(FeedbackFailure.NotConfigured);
            }

            // Validate the token's shape before using it as a request header.
            var provisional = ReadCredential(json.RootElement, "pending");
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
            userRequest.Headers.Authorization = new("Bearer", provisional.AccessToken);
            using var user = await SendAsync(userRequest, cancellation).ConfigureAwait(false);
            var login = ReadString(user.RootElement, "login");
            if (!FeedbackCredential.ValidLogin(login)) throw new FeedbackException(FeedbackFailure.ServiceUnavailable);
            var credential = ReadCredential(json.RootElement, login!);
            await credentialGate.WaitAsync(cancellation).ConfigureAwait(false);
            try { await store.SaveAsync(credential, cancellation).ConfigureAwait(false); }
            finally { credentialGate.Release(); }
            return credential.Login;
        }
        throw new FeedbackException(FeedbackFailure.AuthorizationExpired);
    }

    public async Task<string?> ConnectedAccountAsync(CancellationToken cancellation)
    {
        if (!IsConfigured) return null;
        await credentialGate.WaitAsync(cancellation).ConfigureAwait(false);
        try { return (await LoadCurrentAsync(cancellation).ConfigureAwait(false))?.Login; }
        finally { credentialGate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellation)
    {
        await credentialGate.WaitAsync(cancellation).ConfigureAwait(false);
        try { await store.ClearAsync(cancellation).ConfigureAwait(false); }
        finally { credentialGate.Release(); }
    }

    public async Task<FeedbackIssue> SubmitAsync(FeedbackReport report, string account, CancellationToken cancellation)
    {
        RequireConfiguration();
        if (report.Title.Length is 0 or > FeedbackReport.TitleLimit || report.Title.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(report.Body) || report.Body.Length > 8500)
            throw new FeedbackException(FeedbackFailure.InvalidReport);
        await credentialGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var credential = await LoadCurrentAsync(cancellation).ConfigureAwait(false);
            if (credential is null || !string.Equals(account, credential.Login, StringComparison.Ordinal))
                throw new FeedbackException(FeedbackFailure.SignInRequired);
            if (credential.AccessExpires <= time.GetUtcNow().AddMinutes(1))
                credential = await RefreshAsync(credential, cancellation).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, IssuesEndpoint)
            {
                Content = JsonContent.Create(new { title = report.Title, body = report.Body })
            };
            request.Headers.Authorization = new("Bearer", credential.AccessToken);
            using var result = await SendAsync(request, cancellation, creatingIssue: true).ConfigureAwait(false);
            var number = ReadInt(result.RootElement, "number") ?? 0;
            if (number <= 0) throw new FeedbackException(FeedbackFailure.SubmissionUnknown);
            return new(number);
        }
        finally { credentialGate.Release(); }
    }

    private async Task<FeedbackCredential?> LoadCurrentAsync(CancellationToken cancellation)
    {
        var value = await store.LoadAsync(cancellation).ConfigureAwait(false);
        return value is not null && value.ClientId == clientId && value.RefreshExpires > time.GetUtcNow() ? value : null;
    }

    private async Task<FeedbackCredential> RefreshAsync(FeedbackCredential credential, CancellationToken cancellation)
    {
        using var json = await PostFormAsync(TokenEndpoint, new()
        {
            ["client_id"] = clientId!, ["grant_type"] = "refresh_token", ["refresh_token"] = credential.RefreshToken,
            ["repository_id"] = RepositoryId
        }, cancellation).ConfigureAwait(false);
        if (ReadString(json.RootElement, "error") is "bad_refresh_token" or "invalid_grant" or "access_denied")
            throw new FeedbackException(FeedbackFailure.SignInRequired);
        var refreshed = ReadCredential(json.RootElement, credential.Login);
        await store.SaveAsync(refreshed, cancellation).ConfigureAwait(false);
        return refreshed;
    }

    private FeedbackCredential ReadCredential(JsonElement root, string login)
    {
        var expires = ReadInt(root, "expires_in") ?? 0;
        var refreshExpires = ReadInt(root, "refresh_token_expires_in") ?? 0;
        if (expires is < 1 or > 86400 || refreshExpires is < 1 or > 31622400 ||
            !string.Equals(ReadString(root, "token_type"), "bearer", StringComparison.OrdinalIgnoreCase))
            throw new FeedbackException(FeedbackFailure.ServiceUnavailable);
        var now = time.GetUtcNow();
        var result = new FeedbackCredential(clientId!, login, ReadString(root, "access_token") ?? "",
            ReadString(root, "refresh_token") ?? "", now.AddSeconds(expires), now.AddSeconds(refreshExpires));
        return result.IsValid() ? result : throw new FeedbackException(FeedbackFailure.ServiceUnavailable);
    }

    private async Task<JsonDocument> PostFormAsync(Uri endpoint, Dictionary<string, string> fields, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(fields) };
        return await SendAsync(request, cancellation).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellation, bool creatingIssue = false)
    {
        request.Headers.UserAgent.ParseAdd("The-Order-Feedback");
        var api = request.RequestUri!.Host == IssuesEndpoint.Host;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(api ? "application/vnd.github+json" : "application/json"));
        if (api) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var ambiguous = creatingIssue ? FeedbackFailure.SubmissionUnknown : FeedbackFailure.ServiceUnavailable;
        try
        {
            using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            if (response.StatusCode != (creatingIssue ? HttpStatusCode.Created : HttpStatusCode.OK))
            {
                var failure = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => FeedbackFailure.RateLimited,
                    HttpStatusCode.Forbidden when response.Headers.RetryAfter is not null ||
                        (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.Contains("0")) => FeedbackFailure.RateLimited,
                    HttpStatusCode.Unauthorized => FeedbackFailure.SignInRequired,
                    HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone => FeedbackFailure.RepositoryUnavailable,
                    HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when creatingIssue => FeedbackFailure.InvalidReport,
                    _ => ambiguous
                };
                throw new FeedbackException(failure);
            }
            await using var body = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 16 }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new FeedbackException(ambiguous); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        { throw new FeedbackException(ambiguous); }
    }

    private void RequireConfiguration()
    {
        if (!IsConfigured) throw new FeedbackException(FeedbackFailure.NotConfigured);
    }

    private static string? ReadString(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? ReadInt(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number) ? number : null;

    public void Dispose() => http.Dispose();
}
