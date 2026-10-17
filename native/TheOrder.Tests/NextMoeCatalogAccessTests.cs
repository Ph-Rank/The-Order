using System.Net;
using TheOrder.Application;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Online;
using TheOrder.Platform.Windows;
using TheOrder.Presentation;
using Xunit;

namespace TheOrder.Tests;

public sealed class NextMoeCatalogAccessTests
{
    private sealed class Lab : IAsyncDisposable
    {
        public readonly NextMoeAuthTests.Fixture Identity = new();
        private readonly TransactionProfile profile = new();
        private readonly HttpClient http;
        private readonly OfflineLibraryClient offline;
        private readonly WorkspaceViewModel workspace;
        public readonly OnlineViewModel Online;
        public int Requests;
        public bool Offline;
        public Lab()
        {
            var configuration = new NativeConfiguration(profile.Root); var rules = new LegacyClientTextRules();
            offline = new(new IsolatedLibraryFactory(new NativeLibraryCodec(), configuration), rules);
            workspace = new(offline, new IsolatedUiPreferencesStore(configuration), configuration, rules);
            http = new(new OnlineTransportTests.Handler((_, _) =>
            {
                Requests++;
                if (Offline) throw new HttpRequestException("synthetic offline");
                return Task.FromResult(OnlineTransportTests.Json("{\"object\":\"list\",\"items\":[]}"));
            }));
            var catalog = new NextMoeCatalog(new CatalogTransport(http, Identity.Auth, new("https://api.nextmoe.dev/v2/")));
            Online = new(catalog, Identity.Auth, new RemoteCoverSource(http), new(offline, catalog), workspace);
        }
        public async ValueTask DisposeAsync()
        { await Online.DisposeAsync(); await workspace.DisposeAsync(); await offline.DisposeAsync(); http.Dispose(); profile.Dispose(); }
    }

    [Fact]
    public async Task UncheckedStartupDoesNotAskForAnApiKey()
    {
        await using var lab = new Lab();
        Assert.False(lab.Online.CanAccessCatalog); Assert.Null(lab.Online.CatalogReminder);
        Assert.Null(lab.Identity.Auth.CurrentStatus); Assert.Equal(0, lab.Requests);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AccountWithoutApiKeyWorksOnLoginAndAfterRestart(bool restarted)
    {
        await using var lab = new Lab();
        if (restarted) lab.Identity.Storage.Record = new(1, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-refresh", [.. NextMoeOAuthIdentity.CatalogScopes]);
        else await lab.Identity.SignIn();
        await lab.Online.CheckCredentialAsync();
        Assert.False(lab.Online.HasCredential); Assert.True(lab.Online.CanAccessCatalog); Assert.Null(lab.Online.CatalogReminder);
        Assert.Equal(0, lab.Identity.Protocol.Refreshes); Assert.Equal(0, lab.Requests); // Presence check must not contact OAuth/catalog.
        await lab.Online.TestConnectionAsync();
        Assert.Equal(OnlineState.Loaded, lab.Online.ConnectionState); Assert.Null(lab.Online.CatalogReminder);
        Assert.Equal(NextMoeAccessMode.Account, lab.Identity.Storage.Record!.Mode);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AnonymousAccessAndLogoutDoNotRequireAnApiKey(bool logout)
    {
        await using var lab = new Lab();
        if (logout) { await lab.Identity.SignIn(); await lab.Identity.Auth.SignOutAsync(default); }
        await lab.Online.CheckCredentialAsync();
        Assert.False(lab.Online.HasCredential); Assert.True(lab.Online.CanAccessCatalog); Assert.Null(lab.Online.CatalogReminder);
        await lab.Online.TestConnectionAsync(); Assert.Equal(OnlineState.Loaded, lab.Online.ConnectionState);
        Assert.Equal(NextMoeAccessMode.Anonymous, lab.Identity.Storage.Record!.Mode);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InterruptedAccountRefreshAsksForLoginInsteadOfAnApiKey(bool hasDormantKey)
    {
        await using var lab = new Lab();
        if (hasDormantKey) lab.Identity.Keys.Value = "synthetic-key";
        lab.Identity.Storage.Record = new(1, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-refresh", [.. NextMoeOAuthIdentity.CatalogScopes], true);
        await lab.Online.CheckCredentialAsync();
        Assert.False(lab.Online.CanAccessCatalog);
        Assert.Equal("NextMoe 登录需要恢复", lab.Online.CatalogReminder?.Title);
        Assert.DoesNotContain("密钥", lab.Online.CatalogReminder!.Message);
        Assert.Equal(0, lab.Identity.Protocol.Refreshes); Assert.Equal(0, lab.Requests);
        await lab.Online.TestConnectionAsync(); Assert.Equal(OnlineState.Unauthorized, lab.Online.ConnectionState);
        Assert.Equal(NextMoeAccessMode.Account, lab.Identity.Storage.Record.Mode);
        await lab.Identity.SignIn(); lab.Online.RefreshAccessState();
        Assert.True(lab.Online.CanAccessCatalog); Assert.Null(lab.Online.CatalogReminder);
        Assert.Equal(hasDormantKey ? "synthetic-key" : null, lab.Identity.Keys.Value);
    }

    [Fact]
    public async Task BackgroundRefreshFailurePublishesAccessPropertiesAndReloginClearsNotice()
    {
        await using var lab = new Lab(); await lab.Identity.SignIn(); await lab.Online.CheckCredentialAsync();
        var properties = new List<string?>(); lab.Online.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        lab.Identity.Auth.Changed += (_, _) => lab.Online.RefreshAccessState();
        lab.Identity.Protocol.Fail = true; lab.Identity.Time.Now = lab.Identity.Time.Now.AddMinutes(20);
        await lab.Online.TestConnectionAsync();
        Assert.Equal(OnlineState.Unauthorized, lab.Online.ConnectionState);
        Assert.Contains(nameof(OnlineViewModel.CatalogReminder), properties);
        Assert.Contains(nameof(OnlineViewModel.CanAccessCatalog), properties);
        Assert.Equal("NextMoe 登录需要恢复", lab.Online.CatalogReminder?.Title);
        Assert.False(lab.Online.HasCredential);
        await lab.Identity.SignIn(); Assert.Null(lab.Online.CatalogReminder); Assert.True(lab.Online.CanAccessCatalog);
        Assert.Equal(OnlineState.Idle, lab.Online.ConnectionState);
        Assert.DoesNotContain("登录已失效", lab.Online.ConnectionMessage);
    }

    [Fact]
    public async Task LostApiKeyKeepsApiKeyRecoverySeparateFromAccountRecovery()
    {
        await using var lab = new Lab();
        await lab.Identity.Auth.SaveAsync("synthetic-key", default); await lab.Online.CheckCredentialAsync();
        Assert.True(lab.Online.CanAccessCatalog); Assert.Null(lab.Online.CatalogReminder);
        lab.Identity.Keys.Value = null; await lab.Online.CheckCredentialAsync();
        Assert.False(lab.Online.CanAccessCatalog); Assert.Equal("请检查当前 API Key", lab.Online.CatalogReminder?.Title);
        Assert.Equal(NextMoeAccessMode.ApiKey, lab.Identity.Storage.Record!.Mode);
    }

    [Fact]
    public async Task CorruptLoginIsNotMisrepresentedAsMissingApiKeyAndIsNotReset()
    {
        await using var lab = new Lab();
        lab.Identity.Storage.Record = new(99, NextMoeAccessMode.Account, new("42", "合成用户"), "synthetic-refresh", []);
        var original = lab.Identity.Storage.Record; await lab.Online.CheckCredentialAsync();
        Assert.False(lab.Online.CanAccessCatalog);
        Assert.Equal("NextMoe 访问状态暂不可用", lab.Online.CatalogReminder?.Title);
        Assert.Equal(OnlineState.CredentialCorrupt, lab.Online.ConnectionState);
        Assert.Same(original, lab.Identity.Storage.Record); Assert.Equal(0, lab.Identity.Storage.Writes);
    }

    [Fact]
    public async Task NetworkFailureWithValidLoginDoesNotAskForCredentials()
    {
        await using var lab = new Lab(); await lab.Identity.SignIn(); await lab.Online.CheckCredentialAsync();
        lab.Offline = true; await lab.Online.TestConnectionAsync();
        Assert.Equal(OnlineState.Offline, lab.Online.ConnectionState);
        Assert.True(lab.Online.CanAccessCatalog); Assert.Null(lab.Online.CatalogReminder);
        Assert.Equal(NextMoeAccessMode.Account, lab.Identity.Storage.Record!.Mode);
    }

    [Fact]
    public async Task StatusSnapshotCannotMutateProtectedScopes()
    {
        var lab = new NextMoeAuthTests.Fixture(); await lab.SignIn();
        lab.Auth.CurrentStatus!.Scopes[0] = "synthetic-altered";
        Assert.Contains("openid", (await lab.Auth.StatusAsync(default)).Scopes);
    }

    [Fact]
    public async Task SavedAccountNameMustNotMaskLoginRecoveryAfterRefreshFailure()
    {
        var lab = new PersonalSyncTests.Fixture();
        using var settings = new NextMoeSettingsViewModel(lab.Identity.Auth, lab.Sync, lab.Library);
        await lab.Login(); await settings.InitializeAsync();
        Assert.False(settings.Status!.NeedsLogin);
        lab.Identity.Protocol.Fail = true; lab.Identity.Time.Now = lab.Identity.Time.Now.AddMinutes(20);
        await Assert.ThrowsAsync<OnlineException>(() => lab.Identity.Auth.ReadAsync(default));
        Assert.True(settings.Status.NeedsLogin);
        Assert.Contains("重新登录", settings.AccessMessage);
    }
}
