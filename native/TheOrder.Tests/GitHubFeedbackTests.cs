using System.Net;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

[Collection("Native profile resource")]
public sealed class GitHubFeedbackTests
{
    private const string ClientId = "synthetic-feedback";
    private const string TokenResponse = """
        {"access_token":"ghu_synthetic_access","refresh_token":"ghr_synthetic_refresh","token_type":"bearer","expires_in":28800,"refresh_token_expires_in":15811200}
        """;
    private static readonly FeedbackEnvironment Environment = new("1.3.0-beta.2", "10.0.26100", 200);
    private static readonly FeedbackReport Report = FeedbackReport.Create(FeedbackKind.Problem, "合成标题", "合成复现步骤", Environment, null);

    [Fact]
    public async Task AuthorizingAndPreviewingNeverPostsBeforeExplicitSubmission()
    {
        var store = new Store();
        var polls = 0;
        var issuePosts = 0;
        using var client = Client(store, async request =>
        {
            switch (request.RequestUri!.AbsoluteUri)
            {
                case "https://github.com/login/device/code":
                    Assert.Equal("client_id=synthetic-feedback&scope=offline_access", await request.Content!.ReadAsStringAsync());
                    return Json("""
                        {"device_code":"123abc","user_code":"TEST-1234","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}
                        """);
                case "https://github.com/login/oauth/access_token":
                    Assert.Null(request.Headers.Authorization);
                    Assert.Contains("repository_id=1414122249", await request.Content!.ReadAsStringAsync());
                    return Json(++polls == 1 ? """{"error":"authorization_pending"}""" : TokenResponse);
                case "https://api.github.com/user":
                    Assert.Equal("ghu_synthetic_access", request.Headers.Authorization?.Parameter);
                    return Json("""{"login":"test-player"}""");
                case "https://api.github.com/repos/Ph-Rank/The-Order/issues":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("ghu_synthetic_access", request.Headers.Authorization?.Parameter);
                    using (var content = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()))
                    {
                        Assert.Equal(Report.Title, content.RootElement.GetProperty("title").GetString());
                        Assert.Equal(Report.Body, content.RootElement.GetProperty("body").GetString());
                    }
                    issuePosts++;
                    return Json("""{"number":42,"html_url":"https://example.invalid/untrusted"}""", HttpStatusCode.Created);
                default: throw new InvalidOperationException();
            }
        });
        Assert.Null(await client.ConnectedAccountAsync(default));
        Assert.Equal("TEST-1234", (await client.StartAuthorizationAsync(default)).UserCode);
        var pollingCode = new FeedbackDeviceCode("TEST-1234", "123abc", DateTimeOffset.UtcNow.AddSeconds(20), TimeSpan.FromMilliseconds(1));
        Assert.Equal("test-player", await client.CompleteAuthorizationAsync(pollingCode, default));
        Assert.Equal("test-player", await client.ConnectedAccountAsync(default));
        Assert.Equal(0, issuePosts);
        var wrongUser = await Assert.ThrowsAsync<FeedbackException>(() => client.SubmitAsync(Report, "other", default));
        Assert.Equal(FeedbackFailure.SignInRequired, wrongUser.Failure);
        Assert.Equal(0, issuePosts);
        Assert.Equal("https://github.com/Ph-Rank/The-Order/issues/42", (await client.SubmitAsync(Report, "test-player", default)).Link.AbsoluteUri);
        Assert.Equal(1, issuePosts);
        await client.DisconnectAsync(default);
        Assert.Null(await client.ConnectedAccountAsync(default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, "{}", FeedbackFailure.SubmissionUnknown)]
    [InlineData(HttpStatusCode.Created, "{", FeedbackFailure.SubmissionUnknown)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", FeedbackFailure.SubmissionUnknown)]
    [InlineData(HttpStatusCode.TemporaryRedirect, "{}", FeedbackFailure.SubmissionUnknown)]
    [InlineData(HttpStatusCode.Unauthorized, "{}", FeedbackFailure.SignInRequired)]
    [InlineData(HttpStatusCode.Forbidden, "{}", FeedbackFailure.RepositoryUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", FeedbackFailure.RateLimited)]
    public async Task FailedOrUnconfirmedCreationDoesNotRetry(HttpStatusCode status, string body, FeedbackFailure expected)
    {
        var calls = 0;
        using var client = Client(new Store { Value = Credential() }, _ =>
        {
            calls++;
            return Task.FromResult(Json(body, status));
        });
        var failure = await Assert.ThrowsAsync<FeedbackException>(() => client.SubmitAsync(Report, "test-player", default));
        Assert.Equal(expected, failure.Failure);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RefreshRotationIsPersistedBeforeSendingIssue()
    {
        var store = new Store { Value = Credential(expired: true) };
        using var client = Client(store, async request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                Assert.Contains("refresh_token=ghr_old_refresh", await request.Content!.ReadAsStringAsync());
                return Json(TokenResponse);
            }
            Assert.Equal("ghu_synthetic_access", request.Headers.Authorization?.Parameter);
            Assert.Equal("ghr_synthetic_refresh", store.Value!.RefreshToken);
            return Json("""{"number":9}""", HttpStatusCode.Created);
        });
        Assert.Equal(9, (await client.SubmitAsync(Report, "test-player", default)).Number);
    }

    [Fact]
    public async Task MissingAppRegistrationKeepsPreviewAvailableWithoutNetwork()
    {
        using var client = Client(new Store(), _ => throw new InvalidOperationException(), clientId: null);
        Assert.False(client.IsConfigured);
        Assert.Null(await client.ConnectedAccountAsync(default));
        Assert.Equal(FeedbackFailure.NotConfigured, (await Assert.ThrowsAsync<FeedbackException>(() => client.StartAuthorizationAsync(default))).Failure);
        Assert.Equal("https://github.com/Ph-Rank/The-Order/issues/new?blank=1", GitHubFeedbackClient.ManualIssueForm.AbsoluteUri);
    }

    [Fact]
    public async Task UntrustedVerificationUrlCannotBeOpened()
    {
        using var client = Client(new Store(), _ => Task.FromResult(Json("""
            {"device_code":"123abc","user_code":"TEST-1234","verification_uri":"https://example.invalid/","expires_in":900,"interval":5}
            """)));
        Assert.Equal(FeedbackFailure.ServiceUnavailable, (await Assert.ThrowsAsync<FeedbackException>(() => client.StartAuthorizationAsync(default))).Failure);
    }

    [Fact]
    public void PreviewHasOnlyUserTextEnvironmentAndOptedInDiagnostics()
    {
        Assert.DoesNotContain("诊断", Report.Body);
        Assert.Contains("200%", Report.Body);
        var withDiagnostics = FeedbackReport.Create(FeedbackKind.Suggestion, " 标题 ", " 正文 ", Environment, "Warning Lifecycle");
        Assert.Equal("标题", withDiagnostics.Title);
        Assert.Contains("Warning Lifecycle", withDiagnostics.Body);
        Assert.Throws<FeedbackException>(() => FeedbackReport.Create(FeedbackKind.Problem, "标题\n第二行", "正文", Environment, null));
        Assert.Throws<FeedbackException>(() => FeedbackReport.Create(FeedbackKind.Problem, "标题", "  ", Environment, null));
    }

    [Fact]
    public async Task DpapiBindingRejectsCredentialsCopiedAcrossProfilesAndDisconnectClearsLocalToken()
    {
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var testDirectory = Path.Combine(temp, "the-order-feedback-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = NativeProfileStore.Open(new(NativeProfileKind.Test, Path.Combine(testDirectory, "first")), new NativeLibraryCodec());
            using var second = NativeProfileStore.Open(new(NativeProfileKind.Test, Path.Combine(testDirectory, "second")), new NativeLibraryCodec());
            var store = new WindowsFeedbackCredentials(first.Configuration());
            var existing = File.ReadAllBytes(Path.Combine(first.Root, "library.json"));
            Assert.Null(await store.LoadAsync(default));
            Assert.Empty(Directory.GetFiles(first.Root, "native-feedback-*"));
            await store.SaveAsync(Credential(), default);
            var original = Assert.Single(Directory.GetFiles(first.Root, "native-feedback-*"));
            Assert.DoesNotContain("ghr_old_refresh", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(original)));
            Assert.Equal("test-player", (await new WindowsFeedbackCredentials(first.Configuration()).LoadAsync(default))?.Login);
            File.Copy(original, Path.Combine(second.Root, Path.GetFileName(original)));
            var other = new WindowsFeedbackCredentials(second.Configuration());
            Assert.Equal(FeedbackFailure.Storage, (await Assert.ThrowsAsync<FeedbackException>(() => other.LoadAsync(default))).Failure);
            await store.ClearAsync(default);
            Assert.Null(await store.LoadAsync(default));
            Assert.Equal(existing, File.ReadAllBytes(Path.Combine(first.Root, "library.json")));
        }
        finally
        {
            var path = Path.GetFullPath(testDirectory);
            if (path.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).StartsWith("the-order-feedback-test-", StringComparison.Ordinal) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private static FeedbackCredential Credential(bool expired = false) => new(ClientId, "test-player", "ghu_old_access", "ghr_old_refresh",
        DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1), DateTimeOffset.UtcNow.AddDays(1));
    private static GitHubFeedbackClient Client(Store store, Func<HttpRequestMessage, Task<HttpResponseMessage>> send,
        string? clientId = ClientId) => new(new HttpClient(new Handler(send)), store, clientId);
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }

    private sealed class Store : IFeedbackCredentialStore
    {
        internal FeedbackCredential? Value { get; set; }
        public Task<FeedbackCredential?> LoadAsync(CancellationToken cancellation) => Task.FromResult(Value);
        public Task SaveAsync(FeedbackCredential credential, CancellationToken cancellation) { Value = credential; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellation) { Value = null; return Task.CompletedTask; }
    }
}
